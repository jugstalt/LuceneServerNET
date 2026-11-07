using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Engine.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace LuceneServerNET.Engine.Services
{
    public class LuceneSharedResourcesService : IDisposable
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private const int DefaultBackgroundCommitSeconds = 20;

        private readonly string _rootPath;
        private readonly ConcurrentDictionary<string, LuceneResources> _resources;
        private readonly ConcurrentDictionary<string, MappingResource> _mappings;

        private readonly Timer _timer;
        private int _timerRunning = 0;
        private int _disposed = 0;

        private readonly ILogger _logger;

        public LuceneSharedResourcesService(IOptionsMonitor<LuceneServiceOptions> options,
                                            ILogger<LuceneSharedResourcesService> logger = null)
        {
            _logger = (ILogger)logger ?? NullLogger.Instance;

            _rootPath = options.CurrentValue.RootPath.CreateDirectoryIfNotExists();

            _resources = new ConcurrentDictionary<string, LuceneResources>();
            _mappings = new ConcurrentDictionary<string, MappingResource>();

            var seconds = options.CurrentValue.BackgroundCommitSeconds;
            var backgroundInterval = TimeSpan.FromSeconds(seconds > 0 ? seconds : DefaultBackgroundCommitSeconds);
            _timer = new Timer(Timer_Elapsed, null, backgroundInterval, backgroundInterval);
        }

        private LuceneResources GetResources(string indexName)
        {
            if (_resources.TryGetValue(indexName, out var existing))
            {
                return existing;
            }

            CheckForUnloading(indexName);
            PathGuard.ValidateIndexName(indexName);

            // LuceneResources opens files lazily => a (possibly discarded) instance from GetOrAdd is cheap
            return _resources.GetOrAdd(indexName, name => new LuceneResources(_rootPath, name));
        }

        /// <summary>
        /// Acquires a reference counted searcher. The caller MUST dispose the lease (using)
        /// when the search is finished; until then the underlying reader stays open,
        /// even if a refresh replaces it in the meantime.
        /// </summary>
        public SearcherLease AcquireSearcher(string indexName)
        {
            return GetResources(indexName).AcquireSearcher();
        }

        public Analyzer GetAnalyzer(string indexName)
        {
            return GetResources(indexName).Analyzer;
        }

        public IndexWriter GetIndexWriter(string indexName)
        {
            return GetResources(indexName).DirectoryWriter;
        }

        public IndexMapping GetMapping(string indexName)
        {
            if (_mappings.TryGetValue(indexName, out var mapping))
            {
                return mapping.Mapping;
            }

            CheckForUnloading(indexName);

            return _mappings.GetOrAdd(indexName, name => new MappingResource(_rootPath, name)).Mapping;
        }

        public void RefreshMapping(string indexName)
        {
            if (_mappings.TryGetValue(indexName, out var mapping))
            {
                CheckForUnloading(indexName);

                mapping.RefreshMapping();
            }
        }

        /// <summary>
        /// Commits pending changes and makes them visible to new searches (blocking).
        /// </summary>
        public void RefreshResources(string indexName)
        {
            if (_resources.TryGetValue(indexName, out var resource))
            {
                resource.Commit();
                resource.Refresh(blocking: true);
            }
        }

        public void RemoveResources(string indexName)
        {
            if (_resources.TryRemove(indexName, out LuceneResources removed))
            {
                removed.Dispose();
            }
        }

        #region Timer

        private void Timer_Elapsed(object state)
        {
            // never run overlapping ticks (a commit on a large index can take longer than the period)
            if (Interlocked.CompareExchange(ref _timerRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                // enumerating a ConcurrentDictionary is safe even if entries are removed concurrently
                foreach (var kv in _resources)
                {
                    try
                    {
                        kv.Value.Refresh(blocking: false);
                        kv.Value.Commit();
                    }
                    catch (Exception ex)
                    {
                        // an unhandled exception in a timer callback would terminate the process
                        _logger.LogError(ex, "Index {IndexName}: background refresh/commit failed", kv.Key);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _timerRunning, 0);
            }
        }

        #endregion

        #region Unloading

        private readonly ConcurrentDictionary<string, bool> _unloadedIndices = new ConcurrentDictionary<string, bool>();
        public IDisposable UnloadIndex(string indexName)
        {
            var unloader = new Unloader(indexName, _unloadedIndices);

            try
            {
                RemoveResources(indexName);
                _mappings.TryRemove(indexName, out _);

                return unloader;
            }
            catch (Exception ex)
            {
                unloader.Dispose();
                throw new Exception($"Error on unloading index { indexName }", ex);
            }
        }

        public bool IsUnloading(string indexName)
        {
            return _unloadedIndices.TryGetValue(indexName, out bool unloading) && unloading;
        }

        private void CheckForUnloading(string indexName)
        {
            if (IsUnloading(indexName))
            {
                throw new Exception($"Index { indexName } is unloaded");
            }
        }

        #endregion

        /// <summary>
        /// Commits and closes all writers/readers. Indices are reopened lazily on the next request.
        /// </summary>
        public void ReleaseAllResources()
        {
            foreach (var key in _resources.Keys.ToArray())
            {
                try
                {
                    RemoveResources(key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Index {IndexName}: release failed", key);
                }
            }

            _mappings.Clear();
        }

        #region IDisposable

        /// <summary>
        /// Called by the DI container on host shutdown (after the server stopped processing requests):
        /// commits pending changes and closes the writers, so no write.lock is left behind.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            using (var waitHandle = new ManualResetEvent(false))
            {
                if (_timer.Dispose(waitHandle))
                {
                    waitHandle.WaitOne(TimeSpan.FromSeconds(30));  // let a running tick finish
                }
            }

            ReleaseAllResources();
        }

        #endregion

        #region Classes

        public sealed class SearcherLease : IDisposable
        {
            private SearcherManager _manager;

            internal SearcherLease(SearcherManager manager)
            {
                Searcher = manager.Acquire();
                _manager = manager;
            }

            public IndexSearcher Searcher { get; }

            public void Dispose()
            {
                var manager = Interlocked.Exchange(ref _manager, null);
                manager?.Release(Searcher);
            }
        }

        private class LuceneResources : IDisposable
        {
            private readonly string _rootPath, _indexName;
            private readonly object _lock = new object();   // per index instance

            private Lucene.Net.Store.Directory _directory;
            private volatile IndexWriter _directoryWriter;
            private volatile SearcherManager _searcherManager;
            private bool _disposed;

            public LuceneResources(string rootPath, string indexName)
            {
                _rootPath = rootPath;
                _indexName = PathGuard.ValidateIndexName(indexName);

                Analyzer = new StandardAnalyzer(AppLuceneVersion);
            }

            public Analyzer Analyzer { get; }

            private void EnsureOpen()
            {
                if (_searcherManager != null)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_disposed)
                    {
                        throw new ObjectDisposedException($"Index {_indexName} resources");
                    }

                    if (_searcherManager != null)
                    {
                        return;
                    }

                    var indexPath = PathGuard.Combine(_rootPath, _indexName);
                    if (new DirectoryInfo(indexPath).Exists == false)
                    {
                        throw new Exception("Index not exists");
                    }

                    _directory = FSDirectory.Open(indexPath);
                    _directoryWriter = new IndexWriter(_directory, new IndexWriterConfig(AppLuceneVersion, Analyzer));
                    // NRT searcher on top of the writer; reference counting keeps a reader
                    // open as long as a search still uses it
                    _searcherManager = new SearcherManager(_directoryWriter, applyAllDeletes: true, searcherFactory: null);
                }
            }

            public IndexWriter DirectoryWriter
            {
                get
                {
                    EnsureOpen();
                    return _directoryWriter;
                }
            }

            public SearcherLease AcquireSearcher()
            {
                EnsureOpen();
                return new SearcherLease(_searcherManager);
            }

            public void Refresh(bool blocking)
            {
                var manager = _searcherManager;
                if (manager == null)
                {
                    return;  // not opened yet => nothing to refresh
                }

                if (blocking)
                {
                    manager.MaybeRefreshBlocking();
                }
                else
                {
                    manager.MaybeRefresh();
                }
            }

            public void Commit()
            {
                var writer = _directoryWriter;
                if (writer != null && writer.HasUncommittedChanges())
                {
                    writer.Commit();
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    _disposed = true;

                    try
                    {
                        // searches still holding a lease keep their reader until they release it
                        _searcherManager?.Dispose();
                    }
                    finally
                    {
                        _searcherManager = null;
                        try
                        {
                            // IndexWriter.Dispose() commits pending changes and removes write.lock
                            _directoryWriter?.Dispose();
                        }
                        finally
                        {
                            _directoryWriter = null;
                            _directory?.Dispose();
                            _directory = null;
                            Analyzer.Dispose();
                        }
                    }
                }
            }
        }

        private class MappingResource
        {
            private volatile IndexMapping _mapping;
            private readonly string _rootPath, _indexName;

            public MappingResource(string rootPath, string indexName)
            {
                _rootPath = rootPath;
                _indexName = PathGuard.ValidateIndexName(indexName);

                RefreshMapping();
            }

            public IndexMapping Mapping => _mapping;

            public void RefreshMapping()
            {
                IndexMapping mapping = null;

                try
                {
                    var indexMetaPath = PathGuard.Combine(_rootPath, $".{ _indexName }", "mapping.json");

                    var metaFileInfo = new FileInfo(indexMetaPath);
                    if (metaFileInfo.Exists)
                    {
                        mapping = File.ReadAllText(metaFileInfo.FullName).DeserializeJson<IndexMapping>();
                    }
                }
                catch (Exception ex)
                {
                    // don't silently fall back to an empty mapping: documents would be indexed/returned wrong
                    throw new Exception($"Index {_indexName}: can't read mapping.json: {ex.Message}", ex);
                }

                _mapping = mapping ?? new IndexMapping();  // default (empty) mapping, if no mapping exists
            }
        }

        private class Unloader : IDisposable
        {
            private readonly string _indexName;
            private readonly ConcurrentDictionary<string, bool> _unloadedIndices;

            public Unloader(string indexName, ConcurrentDictionary<string, bool> unloadedIndices)
            {
                _indexName = PathGuard.ValidateIndexName(indexName);
                _unloadedIndices = unloadedIndices;
                _unloadedIndices[indexName] = true;
            }

            public void Dispose()
            {
                if (!_unloadedIndices.TryRemove(_indexName, out bool locked))
                {
                    _unloadedIndices[_indexName] = false;
                }
            }
        }

        #endregion
    }
}

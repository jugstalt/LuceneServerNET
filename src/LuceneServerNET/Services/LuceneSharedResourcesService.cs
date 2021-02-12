using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneServerNET.Core.Extensions;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace LuceneServerNET.Services
{
    public class LuceneSharedResourcesService : IDisposable
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly ConcurrentDictionary<string, LuceneResources> _resources;
        private readonly ConcurrentDictionary<string, MappingResource> _mappings;

        private System.Threading.Timer _timer;

        public LuceneSharedResourcesService(IOptionsMonitor<LuceneServiceOptions> options)
        {
            _rootPath = options.CurrentValue.RootPath.CreateDirectoryIfNotExists();

            _resources = new ConcurrentDictionary<string, LuceneResources>();
            _mappings = new ConcurrentDictionary<string, MappingResource>();

            _timer= new System.Threading.Timer(Timer_Elapsed, null, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        }

        private void InitResources(string indexName)
        {
            CheckForUnloading(indexName);

            var indexPath = Path.Combine(_rootPath, indexName);

            RemoveResources(indexName);

            var resource = new LuceneResources(_rootPath, indexName);
            _resources[indexName] = resource;
        }

        public IndexSearcher GetIndexSearcher(string indexName)
        {
            if (!_resources.ContainsKey(indexName))
            {
                InitResources(indexName);
            }

            return _resources[indexName].IndexSearcher;
        }

        public Analyzer GetAnalyzer(string indexName)
        {
            if (!_resources.ContainsKey(indexName))
            {
                InitResources(indexName);
            }

            return _resources[indexName].Analyzer;
        }

        public IndexWriter GetIndexWriter(string indexName)
        {
            if (!_resources.ContainsKey(indexName))
            {
                InitResources(indexName);
            }

            return _resources[indexName].DirectoryWriter;
        }

        public IndexMapping GetMapping(string indexName)
        {
            if (!_mappings.ContainsKey(indexName))
            {
                CheckForUnloading(indexName);

                _mappings[indexName] = new MappingResource(_rootPath, indexName);
            }

            return _mappings[indexName].Mapping;
        }

        public void RefreshMapping(string indexName)
        {
            if (_mappings.ContainsKey(indexName))
            {
                CheckForUnloading(indexName);

                _mappings[indexName]?.RefreshMapping();
            }
        }

        public void RefreshResources(string indexName)
        {

            if (_resources.ContainsKey(indexName))
            {
                _resources[indexName].DirectoryWriter.Commit();
                _resources[indexName].RefreshReaderSearcher();
            }
        }

        public void RemoveResources(string indexName)
        {
            if(_resources.ContainsKey(indexName))
            {
                if (_resources.TryRemove(indexName, out LuceneResources removed))
                {
                    removed.Dispose();
                }
                else
                {
                    throw new Exception("Can't remove lucene resources from dictionary");
                }
            }
        }

        #region Timer

        private void Timer_Elapsed(object sender)
        {
            foreach(var indexName in _resources.Keys)
            {
                _resources[indexName].RefreshReaderSearcher();
            }
        }

        #endregion

        #region Unloading

        private ConcurrentDictionary<string, bool> _unloadedIndices = new ConcurrentDictionary<string, bool>();
        public IDisposable UnloadIndex(string indexName)
        {
            var unloader = new Unloader(indexName, _unloadedIndices);

            try
            {
                RemoveResources(indexName);

                if(_mappings.ContainsKey(indexName))
                {
                    _mappings.TryRemove(indexName, out MappingResource mapping);
                }

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
            return _unloadedIndices.ContainsKey(indexName) &&
                   _unloadedIndices[indexName] == true;
        }

        private void CheckForUnloading(string indexName)
        {
            if (IsUnloading(indexName))
            {
                throw new Exception($"Index { indexName } is unloaded");
            }
        }

        #endregion

        public void ReleaseAllResources()
        {
            foreach (var key in _resources.Keys.ToArray())
            {
                var resources = _resources[key];
                if (resources != null)
                {
                    resources.Dispose();
                }
            }

            _resources.Clear();
            _mappings.Clear();
        }

        #region IDisposable

        public void Dispose()
        {
            
        }

        #endregion

        #region Classes

        private class LuceneResources : IDisposable
        {
            private readonly string _rootPath, _indexName;

            public LuceneResources(string rootPath, string indexName)
            {
                _rootPath = rootPath;
                _indexName = indexName;

                Analyzer = //new Lucene.Net.Analysis.De.GermanAnalyzer(AppLuceneVersion);
                           new StandardAnalyzer(AppLuceneVersion);
                           //new WhitespaceAnalyzer(AppLuceneVersion);
            }

            #region Lucene.NET Resources

            public Analyzer Analyzer { get; private set; }

            private void InitResources()
            {
                var indexPath = Path.Combine(_rootPath, _indexName);

                if (new DirectoryInfo(indexPath).Exists == false)
                    throw new Exception("Index not exists");

                _directory = FSDirectory.Open(indexPath);
                //Directory = new RAMDirectory(FSDirectory.Open(indexPath), IOContext.DEFAULT);

                var indexConfig = new IndexWriterConfig(AppLuceneVersion, this.Analyzer);
                _directoryWriter = new IndexWriter(_directory, indexConfig);
                _directoryReader = _directoryWriter.GetReader(false);
                _indexSearcher = new IndexSearcher(DirectoryReader);
            }

            private void ReleaseResources()
            {
                if (_indexSearcher != null)
                {
                    _indexSearcher = null;
                }

                if (Analyzer != null)
                {
                    Analyzer.Dispose();
                    Analyzer = null;
                }

                if (_directoryReader != null)
                {
                    _directoryReader.Dispose();
                    _directoryReader = null;
                }

                if(_directoryWriter!=null)
                {
                    _directoryWriter.Dispose();
                    _directoryWriter = null;
                }

                if (_directory != null)
                {
                    _directory.Dispose();
                    _directory = null;
                }
            }

            private Lucene.Net.Store.Directory _directory = null;
            private IndexWriter _directoryWriter = null;
            private DirectoryReader _directoryReader = null;
            private IndexSearcher _indexSearcher = null;

            public Lucene.Net.Store.Directory Directory
            {
                get
                {
                    if (_directory == null)
                    {
                        InitResources();
                    }

                    return _directory;
                }
            }
            public DirectoryReader DirectoryReader { get
                {
                    if (_directoryReader == null)
                    {
                        InitResources();
                    }

                    return _directoryReader;
                }
            }
            public IndexSearcher IndexSearcher
            {
                get
                {
                    if (_indexSearcher == null)
                    {
                        InitResources();
                    }

                    return _indexSearcher;
                }
            }

            public IndexWriter DirectoryWriter
            {
                get
                {
                    if (_directoryWriter == null)
                    {
                        InitResources();
                    }

                    return _directoryWriter;
                }
            }

            #endregion

            #region Refresh Reader/Searcher

            public void RefreshReaderSearcher()
            {
                if (_directoryReader == null || _directoryReader.IsCurrent())
                {
                    return;
                }

                var oldReader = _directoryReader;

                _directoryReader = _directoryWriter.GetReader(false);
                var newSearcher = new IndexSearcher(_directoryReader);
                Interlocked.Exchange(ref _indexSearcher, newSearcher);

                oldReader.Dispose();

                Console.WriteLine($"Index { _indexName }: Searcher updated");
            }

            #endregion

            #region IDisposable

            public void Dispose()
            {
                ReleaseResources();
            }

            #endregion
        }

        private class MappingResource
        {
            private IndexMapping _mapping;
            private readonly string _rootPath, _indexName;

            public MappingResource(string rootPath, string indexName)
            {
                _rootPath = rootPath;
                _indexName = indexName;

                RefreshMapping();
            }

            public IndexMapping Mapping => _mapping;

            public void RefreshMapping()
            {
                IndexMapping mapping = null;

                try
                {
                    var indexMetaPath = Path.Combine(_rootPath, $".{ _indexName }", "mapping.json");

                    var metaFileInfo = new FileInfo(indexMetaPath);
                    if (metaFileInfo.Exists)
                    {
                        mapping = File.ReadAllText(metaFileInfo.FullName).DeserializeJson<IndexMapping>();
                    }
                }
                catch { }

                _mapping = mapping ?? new IndexMapping();  // default (empty) mapping
            }
        }

        private class Unloader : IDisposable
        {
            private string _indexName;
            private readonly ConcurrentDictionary<string, bool> _unloadedIndices = new ConcurrentDictionary<string, bool>();

            public Unloader(string indexName, ConcurrentDictionary<string, bool> unloadedIndices)
            {
                _indexName = indexName;
                _unloadedIndices = unloadedIndices;
                _unloadedIndices[indexName] = true;
            }

            public void Dispose()
            {
                if(!_unloadedIndices.TryRemove(_indexName, out bool locked))
                {
                    _unloadedIndices[_indexName] = false;
                }
            }
        }

        #endregion
    }
}

using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Concurrent;
using System.IO;
using LuceneServerNET.Core.Extensions;

namespace LuceneServerNET.Services
{
    public class LuceneSharedResourcesService : IDisposable
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly ConcurrentDictionary<string, LuceneResources> _resources;
        private readonly ConcurrentDictionary<string, MappingResource> _mappings;

        public LuceneSharedResourcesService()
        {
            _rootPath = @"c:\temp\lucene.net\indices";
            _resources = new ConcurrentDictionary<string, LuceneResources>();
            _mappings = new ConcurrentDictionary<string, MappingResource>();
        }

        private void InitResources(string indexName)
        {
            CheckForUnloading(indexName);

            var indexPath = Path.Combine(_rootPath, indexName);

            var resource = new LuceneResources(_rootPath, indexName);
            _resources[indexName] = resource;
        }

        public IndexSearcher GetIIndexSearcher(string indexName)
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
            
            InitResources(indexName);
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
                throw new Exception($"Index { indexName } is unloaded");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            foreach(var key in _resources.Keys)
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

        #endregion

        #region Classes

        private class LuceneResources : IDisposable
        {
            private readonly string _rootPath, _indexName;

            public LuceneResources(string rootPath, string indexName)
            {
                _rootPath = rootPath;
                _indexName = indexName;

                var indexPath = Path.Combine(rootPath, indexName);

                Directory = FSDirectory.Open(indexPath);
                DirectoryReader = DirectoryReader.Open(Directory);
                Analyzer = new StandardAnalyzer(AppLuceneVersion);
                IndexSearcher = new IndexSearcher(DirectoryReader);
            }

            #region Lucene.NET Resources

            public Lucene.Net.Store.Directory Directory { get; set; }
            public DirectoryReader DirectoryReader { get; set; }
            public Analyzer Analyzer { get; set; }
            public IndexSearcher IndexSearcher { get; set; }

            #endregion

            #region IDisposable

            public void Dispose()
            {
                if (IndexSearcher != null)
                {
                    IndexSearcher = null;
                }

                if(Analyzer!=null)
                {
                    Analyzer.Dispose();
                    Analyzer = null;
                }

                if (DirectoryReader != null)
                {
                    DirectoryReader.Dispose();
                    DirectoryReader = null;
                }

                if (Directory != null)
                {
                    Directory.Dispose();
                    Directory = null;
                }
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

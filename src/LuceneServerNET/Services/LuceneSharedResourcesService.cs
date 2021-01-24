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
                _mappings[indexName] = new MappingResource(_rootPath, indexName);
            }

            return _mappings[indexName].Mapping;
        }

        public void RefreshMapping(string indexName)
        {
            if (_mappings.ContainsKey(indexName))
            {
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
                var resource = _resources[indexName];
                resource.Dispose();

                _resources.TryRemove(indexName, out LuceneResources removed);
            }
        }

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

        #endregion
    }
}

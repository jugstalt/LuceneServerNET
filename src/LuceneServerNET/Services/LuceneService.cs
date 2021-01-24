using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LuceneServerNET.Core.Models.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LuceneServerNET.Services
{
    public class LuceneService
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly LuceneSharedResourcesService _resources; 

        public LuceneService(LuceneSharedResourcesService resources)
        {
            _rootPath = @"c:\temp\lucene.net\indices";
            _resources = resources;
        }

        public bool IndexExists(string indexName)
        {
            var indexPath = Path.Combine(_rootPath, indexName);

            return new DirectoryInfo(indexPath).Exists;
        }

        #region Index Operations

        public bool CreateIndex(string indexName)
        {
            if (IndexExists(indexName))
            {
                throw new Exception("Index already exists");
            }

            var indexPath = Path.Combine(_rootPath, indexName);
            var indexMetaPath = Path.Combine(_rootPath, MetaIndexName(indexName));

            new DirectoryInfo(indexPath).Create();
            new DirectoryInfo(indexMetaPath).Create();

            return true;

            //using (var dir = FSDirectory.Open(indexPath))
            //{
            //    return true;
            //}
        }

        public bool RemoveIndex(string indexName)
        {
            if(!IndexExists(indexName))
            {
                return true;
            }

            var indexPath = Path.Combine(_rootPath, indexName);
            var indexMetaPath = Path.Combine(_rootPath, MetaIndexName(indexName));

            new DirectoryInfo(indexPath).Delete(true);
            new DirectoryInfo(indexMetaPath).Delete(true);

            return true;
        }

        #endregion

        #region Mapping

        public bool Map(string indexName, IndexMapping mapping)
        {
            if (!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            if(mapping==null)
            {
                throw new ArgumentException("Parameter mapping == null");
            }

            var filePath = Path.Combine(_rootPath, MetaIndexName(indexName), "mapping.json");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists)
            {
                fileInfo.Delete();
            }

            File.WriteAllText(
                filePath,
                JsonSerializer.Serialize(mapping)); ;

            _resources.RefreshMapping(indexName);

            return true;
        }

        public IndexMapping Mapping(string indexName)
        {
            return _resources.GetMapping(indexName);
        }

        #endregion

        #region Indexing

        public bool Index(string indexName, IEnumerable<IDictionary<string,object>> items)
        {
            if(!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            if (items == null || items.Count() == 0)
            {
                return true;
            }

            var mapping = _resources.GetMapping(indexName);

            List<Document> docs = new List<Document>();
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var itemType = item.GetType();
                var doc = new Document();

                foreach (var field in mapping.Fields)
                {
                    if (!item.ContainsKey(field.Name))
                    {
                        continue;
                    }

                    var value = item[field.Name];
                    if (value == null)
                    {
                        continue;
                    }

                    switch (field.FieldType)
                    {
                        case FieldTypes.StringType:
                            doc.Add(new StringField(
                                field.Name,
                                value.ToString(),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO));
                            break;
                        case FieldTypes.TextType:
                            doc.Add(new TextField(
                                field.Name,
                                value.ToString(),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO));
                            break;
                        case FieldTypes.Int32Type:
                            doc.Add(new Int32Field(
                                field.Name,
                                Convert.ToInt32(value),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO));
                            break;
                        case FieldTypes.DoubleType:
                            doc.Add(new DoubleField(
                                field.Name,
                                Convert.ToDouble(value),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO));
                            break;
                        case FieldTypes.SingleType:
                            doc.Add(new SingleField(
                                field.Name,
                                Convert.ToSingle(value),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO));
                            break;
                        case FieldTypes.NotIndexedString:
                            doc.Add(
                                new Field(field.Name,
                                value.ToString(),
                                field.Store == Store.YES ? Field.Store.YES : Field.Store.NO,
                                Field.Index.NO));
                            break;
                    }
                }
                docs.Add(doc);
            }

            using (var dir = GetIndexFSDirectory(indexName))
            using (var analyzer = new StandardAnalyzer(AppLuceneVersion)) // Create an analyzer to process the text
            {
                var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
                using (var writer = new IndexWriter(dir, indexConfig))
                {
                    writer.AddDocuments(docs);

                    writer.Flush(triggerMerge: false, applyAllDeletes: false);
                }
            }
            return true;
        }

        #endregion

        #region Search/Query

        public IEnumerable<object> Search(string indexName, string term)
        {
            var searcher = _resources.GetIIndexSearcher(indexName);

            //var phrase = new MultiPhraseQuery
            //{
            //    new Term("content", term)
            //};

            //var phrase = new WildcardQuery(new Term("content", $"{ term }*"));

            var mapping = _resources.GetMapping(indexName);

            var parser = new QueryParser(AppLuceneVersion, mapping.PrimaryField, _resources.GetAnalyzer(indexName));
            var query = parser.Parse(term);

            var hits = searcher.Search(query, 20 /* top 20 */).ScoreDocs;

            List<object> docs = new List<object>();
            if (hits.Length > 0)
            {
                foreach (var hit in hits)
                {
                    var foundDoc = searcher.Doc(hit.Doc);

                    if (foundDoc != null)
                    {
                        var doc = new Dictionary<string, object>();

                        doc.Add("_id", hit.Doc);
                        doc.Add("_score", hit.Score);

                        foreach(var field in mapping.Fields)
                        {
                            doc.Add(field.Name, foundDoc.Get(field.Name));
                        }

                        docs.Add(doc);
                    }
                }
            }

            return docs;
        }

        #endregion

        #region Helper

        private FSDirectory GetIndexFSDirectory(string indexName)
        {
            var indexPath = Path.Combine(_rootPath, indexName);

            return FSDirectory.Open(indexPath);
        }

        private string MetaIndexName(string indexName)
        {
            return $".{ indexName }";
        }

        #endregion
    }
}

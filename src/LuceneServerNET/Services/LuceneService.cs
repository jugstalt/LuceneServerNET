using Lucene.Net.Documents;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Grouping;
using Lucene.Net.Util;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Extensions;
using LuceneServerNET.Parse;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Services
{
    public class LuceneService
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly LuceneSharedResourcesService _resources; 

        public LuceneService(LuceneSharedResourcesService resources,
                             IOptionsMonitor<LuceneServiceOptions> options)
        {
            _rootPath = options.CurrentValue.RootPath;
            _resources = resources;
        }
        
        public bool IndexExists(string indexName)
        {
            var indexPath = Path.Combine(_rootPath, indexName);

            return new DirectoryInfo(indexPath).Exists;
        }

        public void ReleaseAll()
        {
            _resources.ReleaseAllResources();
        }

        #region Index Operations

        public bool CreateIndex(string indexName)
        {
            if (IndexExists(indexName))
            {
                throw new Exception("Index already exists");
            }
            if(_resources.IsUnloading(indexName))
            {
                throw new Exception("Index is unloaded");
            }

            var indexPath = Path.Combine(_rootPath, indexName);
            var indexMetaPath = Path.Combine(_rootPath, MetaIndexName(indexName));

            new DirectoryInfo(indexPath).Create();
            new DirectoryInfo(indexMetaPath).Create();

            return true;
        }

        public bool RemoveIndex(string indexName)
        {
            if(!IndexExists(indexName))
            {
                return true;
            }

            var indexPath = Path.Combine(_rootPath, indexName);
            var indexMetaPath = Path.Combine(_rootPath, MetaIndexName(indexName));

            using (var unloader = _resources.UnloadIndex(indexName))
            {
                var indexDirectory = new DirectoryInfo(indexPath);
                var indexMetaDirectory = new DirectoryInfo(indexMetaPath);

                if (indexDirectory.Exists)
                {
                    indexDirectory.Delete(true);
                }

                if (indexMetaDirectory.Exists)
                {
                    indexMetaDirectory.Delete(true);
                }
            }

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

        private static object _writeLocker = new object();

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
                    #region AutoSetValues

                    switch (field.FieldType)
                    {
                        case FieldTypes.GuidType:
                            doc.Add(new StringField(
                                    field.Name,
                                    Guid.NewGuid().ToString("N").ToLower(),
                                    Field.Store.YES));
                            continue;
                    }

                    #endregion

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
                            if (field.Index)
                            {
                                doc.Add(new StringField(
                                    field.Name,
                                    value.ToString(),
                                    field.Store ? Field.Store.YES : Field.Store.NO));
                            } 
                            else 
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToString()));
                            }
                            break;
                        case FieldTypes.TextType:
                            if (field.Index)
                            {
                                doc.Add(new TextField(
                                field.Name,
                                value.ToString(),
                                field.Store  ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToString()));
                            }
                            break;
                        case FieldTypes.Int32Type:
                            if (field.Index)
                            {
                                doc.Add(new Int32Field(
                                field.Name,
                                Convert.ToInt32(value),
                                field.Store  ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, Convert.ToInt32(value)));
                            }
                            break;
                        case FieldTypes.DoubleType:
                            if (field.Index)
                            {
                                doc.Add(new DoubleField(
                                field.Name,
                                Convert.ToDouble(value),
                                field.Store  ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, Convert.ToDouble(value)));
                            }
                            break;
                        case FieldTypes.SingleType:
                            if (field.Index)
                            {
                                doc.Add(new SingleField(
                                field.Name,
                                Convert.ToSingle(value),
                                field.Store  ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, Convert.ToSingle(value)));
                            }
                            break;
                        case FieldTypes.DateTimeType:
                            if (field.Index)
                            {
                                value = DateTools.DateToString(Convert.ToDateTime(value.ToString()).ToUniversalTime(), DateTools.Resolution.SECOND);
                                doc.Add(new StringField(
                                field.Name,
                                (string)value,
                                field.Store ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToString()));
                            }
                            break;
                    }
                }
                docs.Add(doc);
            }

            //lock (_writeLocker)
            {
                var writer = _resources.GetIndexWriter(indexName);
                writer.AddDocuments(docs);

                writer.Flush(triggerMerge: false, applyAllDeletes: false);
            }

            return true;
        }

        public bool RemoveDocuments(string indexName, string term, string termField = "id")
        {
            if (!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            var parser = new QueryParser(AppLuceneVersion, termField, _resources.GetAnalyzer(indexName));
            var query = parser.Parse(term);

            var writer = _resources.GetIndexWriter(indexName);
            writer.DeleteDocuments(query);
            writer.Commit();

            return true;
        }

        public void RefreshIndex(string indexName)
        {
            if (!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            _resources.RefreshResources(indexName);
        }

        #endregion

        #region Search/Query

        public IEnumerable<IDictionary<string, object>> Search(string indexName, 
                                                               string term, 
                                                               string outFieldNames = null, 
                                                               int size = 20,
                                                               string sortFieldName = null,
                                                               bool sortReverse = false)
        {
            var searcher = _resources.GetIndexSearcher(indexName);

            var mapping = _resources.GetMapping(indexName);

            Query query = null;
            if (String.IsNullOrEmpty(term))
            {
                query = new MatchAllDocsQuery();
            }
            else
            {
                // https://lucene.apache.org/core/2_9_4/queryparsersyntax.html
                //var parser = new QueryParser(AppLuceneVersion, mapping.PrimaryField, _resources.GetAnalyzer(indexName));
                var parser = new MultiFieldQueryParser(
                    AppLuceneVersion,
                    mapping.PrimaryFields.ToArray(),
                    _resources.GetAnalyzer(indexName));

                query = parser.Parse(term);
            }

            ScoreDoc[] hits = null;
            FieldMapping sortField = String.IsNullOrEmpty(sortFieldName) ?
                null :
                mapping.GetField(sortFieldName);

            if (sortField != null)
            {
                Sort sort = new Sort(new SortField(sortFieldName, sortField.GetSortFieldType(), sortReverse));
                hits = searcher.Search(query, size, sort).ScoreDocs;
            }
            else
            {
                hits = searcher.Search(query, size).ScoreDocs;
            }

            var outFields = new QueryOutFields(outFieldNames);
            
            List<IDictionary<string, object>> docs = new List<IDictionary<string, object>>();
            if (hits != null && hits.Length > 0)
            {
                foreach (var hit in hits)
                {
                    var foundDoc = searcher.Doc(hit.Doc);

                    if (foundDoc != null)
                    {
                        var doc = new Dictionary<string, object>();

                        //doc.Add("_id", hit.Doc);
                        if (!float.IsNaN(hit.Score))
                        {
                            doc.Add("_score", hit.Score);
                        }

                        foreach (var field in mapping.Fields)
                        {
                            if (outFields.Names.Count() > 0 &&
                                !outFields.Names.Contains("*") &&
                                !outFields.Names.Contains(field.Name))
                            {
                                continue;
                            }

                            object val = field.GetValue(foundDoc);

                            //if (outFieldExpressions.TryGetValue(field.Name, out string expression))
                            //{
                            //    if(!String.IsNullOrEmpty(expression))
                            //    {
                            //        val = expression.ParseExpression(val);
                            //    }
                            //}

                            doc.Add(field.Name, val);
                        }

                        docs.Add(doc);
                    }
                }
            }

            return docs;
        }

        #endregion

        #region Grouping

        public IEnumerable<IDictionary<string, object>> GroupBy(string indexName, string groupField, string term)
        {
            var groupingSearch = new GroupingSearch(groupField);
            //groupingSearch.SetGroupSort(groupSort);
            //groupingSearch.SetFillSortFields(fillFields);

            var searcher = _resources.GetIndexSearcher(indexName);

            var mapping = _resources.GetMapping(indexName);

            Query query = null;
            if (String.IsNullOrEmpty(term))
            {
                query = new MatchAllDocsQuery();
            }
            else
            {
                var parser = new MultiFieldQueryParser(
                    AppLuceneVersion, 
                    mapping.PrimaryFields.ToArray(),
                    _resources.GetAnalyzer(indexName));
                query = parser.Parse(term);
            }

            var topGroups = groupingSearch.Search(searcher, query, 0, 100000);

            return topGroups.Groups
                            .Select(g =>
                            {
                                var value = g.GroupValue;
                                if (g.GroupValue is BytesRef)
                                    value = Encoding.UTF8.GetString(((BytesRef)g.GroupValue).Bytes);

                                return new Dictionary<string, object>()
                                {
                                    { "value", value  },
                                    { "_hits", g.TotalHits },
                                    //{ "_score", g.Score }
                                };
                            });
        }

        #endregion

        #region Helper

        private string MetaIndexName(string indexName)
        {
            return $".{ indexName }";
        }

        #endregion
    }
}

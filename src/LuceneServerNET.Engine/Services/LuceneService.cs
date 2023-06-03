using Lucene.Net.Documents;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Grouping;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Lucene.Net.Util;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Spatial;
using LuceneServerNET.Engine.Extensions;
using LuceneServerNET.Engine.Models.Spatial;
using LuceneServerNET.Parse;
using Microsoft.Extensions.Options;
using Spatial4n.Core.Context;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LuceneServerNET.Core.Phonetics;

namespace LuceneServerNET.Engine.Services
{
    public class LuceneService
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly ArchiveService _archive;
        private readonly LuceneSharedResourcesService _resources;

        private SpatialContext _spatialContext = null;
        private SpatialPrefixTree _tree = null;

        public LuceneService(LuceneSharedResourcesService resources,
                             ArchiveService archive,
                             IOptionsMonitor<LuceneServiceOptions> options)
        {
            _rootPath = options.CurrentValue.RootPath;
            _archive = archive;
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
            if (_resources.IsUnloading(indexName))
            {
                throw new Exception("Index is unloaded");
            }

            var indexPath = Path.Combine(_rootPath, indexName);
            var indexMetaPath = Path.Combine(_rootPath, MetaIndexName(indexName));

            new DirectoryInfo(indexPath).Create();
            new DirectoryInfo(indexMetaPath).Create();

            _archive.CreateArchive(indexName);

            return true;
        }

        public bool RemoveIndex(string indexName)
        {
            _archive.RemoveArchive(indexName);

            if (!IndexExists(indexName))
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

        #region Metadata

        #region Mapping

        public bool Map(string indexName, IndexMapping mapping)
        {
            if (!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            if (mapping == null)
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
                JsonSerializer.Serialize(mapping));

            _archive.Map(indexName, mapping);

            _resources.RefreshMapping(indexName);

            return true;
        }

        public IndexMapping Mapping(string indexName)
        {
            return _resources.GetMapping(indexName);
        }

        #endregion

        #region Custom

        public bool AddCustomMetadata(string indexName, string name, string metaData)
        {
            if (!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            name = name?.Replace("\\", "/")
                        .Trim()
                        .ToLower();

            if (String.IsNullOrEmpty(name) ||
                name.EndsWith(".meta") ||
                name.Contains("/") ||
                new string[] { "mapping " }.Contains(name))
            {
                throw new Exception($"Invalid or reserved metadata name '{ name }'.");
            }

            var filePath = Path.Combine(_rootPath, MetaIndexName(indexName), $"{ name }.meta");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists)
            {
                fileInfo.Delete();
            }

            File.WriteAllText(
                filePath,
                metaData);

            _archive.AddCustomMetadata(indexName, name, metaData);

            return true;
        }

        async public Task<string> GetCustomMetadata(string indexName, string name)
        {
            name = name?.Trim().ToLower();

            if (String.IsNullOrEmpty(name))
            {
                return null;
            }

            var filePath = Path.Combine(_rootPath, MetaIndexName(indexName), $"{ name }.meta");

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists)
            {
                return await File.ReadAllTextAsync(fileInfo.FullName);
            }

            return null;
        }

        async public Task<Dictionary<string, string>> GetCustomMetadatas(string indexName)
        {
            try
            {
                Dictionary<string, string> customMetadatas = new Dictionary<string, string>();
                string path = Path.Combine(_rootPath, MetaIndexName(indexName));

                foreach (var fileInfo in new DirectoryInfo(path).GetFiles("*.meta"))
                {
                    var name = fileInfo.Name.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length);
                    customMetadatas.Add(name, await File.ReadAllTextAsync(fileInfo.FullName));
                }

                return customMetadatas;
            }
            catch { return null; }
        }

        public Task<IEnumerable<string>> GetCustomMetadataNames(string indexName)
        {
            try
            {
                List<string> customMetadataNames = new List<string>();
                string path = Path.Combine(_rootPath, MetaIndexName(indexName));

                foreach (var fileInfo in new DirectoryInfo(path).GetFiles("*.meta"))
                {
                    customMetadataNames.Add(fileInfo.Name.Substring(0, fileInfo.Name.Length - fileInfo.Extension.Length));
                }

                return Task.FromResult<IEnumerable<string>>(customMetadataNames);
            }
            catch { return null; }
        }

        #endregion

        #endregion

        #region Indexing

        private static object _writeLocker = new object();

        public bool Index(string indexName, IEnumerable<IDictionary<string, object>> items, bool archive = true)
        {
            if (!IndexExists(indexName))
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
                                    value.ToStringWithAsciiEncode(field, mapping),
                                    field.Store ? Field.Store.YES : Field.Store.NO));

                                if (mapping.PrimaryFieldsPhonetics != Algorithm.None &&
                                    field.IsPrimaryField(mapping))
                                {
                                    doc.Add(new StringField(
                                        field.ToPhoneticsFieldName(),
                                        value.ToString()
                                             .ToPhonetics(mapping.PrimaryFieldsPhonetics)
                                             .ToStringWithAsciiEncode(field, mapping),
                                        Field.Store.NO)); // should be NO in production
                                }
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
                                value.ToStringWithAsciiEncode(field, mapping),
                                field.Store ? Field.Store.YES : Field.Store.NO));

                                if (mapping.PrimaryFieldsPhonetics != Algorithm.None &&
                                    field.IsPrimaryField(mapping))
                                {
                                    doc.Add(new TextField(
                                        field.ToPhoneticsFieldName(),
                                        value.ToString()
                                             .ToPhonetics(mapping.PrimaryFieldsPhonetics)
                                             .ToStringWithAsciiEncode(field, mapping),
                                        Field.Store.NO)); // should be NO in production
                                }
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
                                value.ToInt32(),
                                field.Store ? Field.Store.YES : Field.Store.NO));
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
                                value.ToDouble(),
                                field.Store ? Field.Store.YES : Field.Store.NO));
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
                                value.ToSingle(),
                                field.Store ? Field.Store.YES : Field.Store.NO));
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
                        case FieldTypes.GeoType:
                            InitSpatial();

                            GeoType geoValue = GeoType.Parse(value);

                            if (geoValue is GeoPoint && geoValue.IsValid())
                            {
                                var geoPoint = (GeoPoint)geoValue;
                                var strategy = new RecursivePrefixTreeStrategy(_tree, field.Name);
                                var point = _spatialContext.MakePoint(geoPoint.Longidute, geoPoint.Latitude);
                                foreach (var f in strategy.CreateIndexableFields(point))
                                {
                                    doc.Add(f);
                                }
                                if (field.Store)
                                {
                                    doc.Add(new Lucene.Net.Documents.StoredField(field.Name, geoPoint.ToString()));
                                }
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

            if (archive)
            {
                _archive.Index(indexName, items);
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
                                                               bool sortReverse = false,
                                                               ISpatialFilter spatialFilter = null,
                                                               string[] primarySearchFields = null)
        {
            var searcher = _resources.GetIndexSearcher(indexName);

            var mapping = _resources.GetMapping(indexName);

            Query query = null;
            Filter filter = null;

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
                    primarySearchFields ?? mapping.PrimaryFields.ToArray(),
                    _resources.GetAnalyzer(indexName));

                query = parser.Parse(term.ParseSerachTerm(mapping));
            }

            // Spatial Filter
            if(spatialFilter != null)
            {
                InitSpatial();
                filter = spatialFilter.ToFilter(_spatialContext, _tree);
            }

            ScoreDoc[] hits = null;
            FieldMapping sortField = String.IsNullOrEmpty(sortFieldName) ?
                null :
                mapping.GetField(sortFieldName);

            if (sortField != null)
            {
                Sort sort = new Sort(new SortField(sortFieldName, sortField.GetSortFieldType(), sortReverse));
                hits = searcher.Search(query, filter, size, sort).ScoreDocs;
            }
            else
            {
                hits = searcher.Search(query, filter, size).ScoreDocs;
            }

            var outFields = new QueryOutFields(outFieldNames);
            var selectOutFields = outFields.SelectOutFields(mapping);

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

                        foreach (var selectOutField in selectOutFields)
                        {
                            var field = mapping.GetField(selectOutField.Name);
                            if (field == null)
                            {
                                continue;
                            }

                            object val = field.GetValue(foundDoc);

                            val = val.DecodeAsciiCharacters(field, mapping);

                            var fieldName = field.Name;

                            val = selectOutField.Invoke(val, ref fieldName);

                            doc.Add(fieldName, val);
                        }

                        //if(mapping.PrimaryFieldsPhonetics != Algorithm.None)
                        //{
                        //    foreach(var primaryField in mapping.PrimaryFields)
                        //    {
                        //        object val = foundDoc.Get($"_phonetics_{primaryField}");
                        //        doc.Add($"_phonetics_{primaryField}", val);
                        //    }
                        //}

                        docs.Add(doc);
                    }
                }
            }

            return docs;
        }

        public IEnumerable<IDictionary<string, object>> SearchPhonetic(string indexName,
                                                               string term,
                                                               string outFieldNames = null,
                                                               int size = 20,
                                                               string sortFieldName = null,
                                                               bool sortReverse = false,
                                                               ISpatialFilter spatialFilter = null)
        {
            var mapping = _resources.GetMapping(indexName);

            if (mapping.PrimaryFieldsPhonetics == Algorithm.None)
                return new IDictionary<string, object>[0];

            term = term.ToPhonetics(mapping.PrimaryFieldsPhonetics);

            var termParser = new Core.QueryBuilder();
            term = termParser.ParseTerm(term);

            return Search(indexName,
                          term,
                          outFieldNames,
                          size,
                          sortFieldName,
                          sortReverse,
                          spatialFilter,
                          mapping.PrimaryFields.Select(f => f.ToPhoneticsFieldName()).ToArray());
        }


        #endregion

        #region Grouping

        public IEnumerable<IDictionary<string, object>> GroupBy(string indexName, 
                                                                string groupField, 
                                                                string term)
        {
            var groupingSearch = new GroupingSearch(groupField);
            //groupingSearch.SetGroupSort(groupSort);
            //groupingSearch.SetFillSortFields(fillFields);

            var searcher = _resources.GetIndexSearcher(indexName);

            var mapping = _resources.GetMapping(indexName);
            var field = mapping?.Fields
                .Where(f => f.Name.Equals(groupField, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

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

                query = parser.Parse(term.ParseSerachTerm(mapping));
            }

            var topGroups = groupingSearch.Search(searcher, query, 0, 100000);

            return topGroups.Groups
                            .Select(g =>
                            {
                                var value = g.GroupValue;
                                if (g.GroupValue is BytesRef)
                                {
                                    value = Encoding.UTF8.GetString(((BytesRef)g.GroupValue).Bytes);
                                }

                                return new Dictionary<string, object>()
                                {
                                    { "value", value.DecodeAsciiCharacters(field, mapping)  },
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

        private void InitSpatial()
        {
            if (_spatialContext == null)
            {
                _spatialContext = SpatialContext.GEO;

                int maxLevels = 11;
                _tree = new GeohashPrefixTree(_spatialContext, maxLevels);
            }
        }

        #endregion
    }
}

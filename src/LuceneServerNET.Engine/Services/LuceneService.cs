using Lucene.Net.Documents;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Grouping;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Util;
using LuceneServerNET.Core.Models.Mapping;
using LuceneServerNET.Core.Models.Spatial;
using LuceneServerNET.Core.Phonetics;
using LuceneServerNET.Engine.Exceptions;
using LuceneServerNET.Engine.Extensions;
using LuceneServerNET.Engine.Models.Spatial;
using LuceneServerNET.Parse;
using Microsoft.Extensions.Options;
using Spatial4n.Context;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuceneServerNET.Engine.Services
{
    public class LuceneService
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;
        private readonly ArchiveService _archive;
        private readonly LuceneSharedResourcesService _resources;
        private readonly AliasService _aliases;

        private SpatialContext _spatialContext = null;
        private SpatialPrefixTree _tree = null;

        public LuceneService(LuceneSharedResourcesService resources,
                             ArchiveService archive,
                             AliasService aliases,
                             IOptionsMonitor<LuceneServiceOptions> options)
        {
            _rootPath = options.CurrentValue.RootPath;
            _archive = archive;
            _resources = resources;
            _aliases = aliases;
        }

        #region Aliases

        /// <summary>The index an alias points to, otherwise the name itself.</summary>
        public string ResolveAlias(string name) => _aliases.Resolve(name);

        public IReadOnlyDictionary<string, string> Aliases() => _aliases.GetAll();

        public void SetAlias(string alias, string indexName) => _aliases.Set(alias, indexName, IndexExists);

        public bool RemoveAlias(string alias) => _aliases.Remove(alias);

        #endregion

        public bool IndexExists(string indexName)
        {
            var indexPath = PathGuard.Combine(_rootPath, PathGuard.ValidateIndexName(indexName));

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
                throw new IndexConflictException("Index already exists");
            }
            if (_resources.IsUnloading(indexName))
            {
                throw new IndexConflictException("Index is unloaded");
            }
            if (_aliases.IsAlias(indexName))
            {
                throw new IndexConflictException($"'{indexName}' is an alias");
            }

            var indexPath = PathGuard.Combine(_rootPath, PathGuard.ValidateIndexName(indexName));
            var indexMetaPath = PathGuard.Combine(_rootPath, MetaIndexName(indexName));

            new DirectoryInfo(indexPath).Create();
            new DirectoryInfo(indexMetaPath).Create();

            _archive.CreateArchive(indexName);

            return true;
        }

        public bool RemoveIndex(string indexName)
        {
            if (_aliases.IsAlias(indexName))
            {
                throw new IndexConflictException($"'{indexName}' is an alias, not an index; remove the alias with DELETE api/v2/aliases/{indexName}");
            }

            var aliases = _aliases.AliasesOf(indexName).ToArray();
            if (aliases.Length > 0)
            {
                throw new IndexConflictException($"Index is used by the alias(es) {String.Join(", ", aliases)}; remove or switch them first");
            }

            _archive.RemoveArchive(indexName);

            if (!IndexExists(indexName))
            {
                return true;
            }

            var indexPath = PathGuard.Combine(_rootPath, PathGuard.ValidateIndexName(indexName));
            var indexMetaPath = PathGuard.Combine(_rootPath, MetaIndexName(indexName));

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
                throw new IndexNotFoundException();
            }

            if (mapping == null)
            {
                throw new ArgumentException("Parameter mapping == null");
            }

            var filePath = PathGuard.Combine(_rootPath, MetaIndexName(indexName), "mapping.json");

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
                throw new IndexNotFoundException();
            }

            name = name?.Trim().ToLower();

            if (!PathGuard.IsValidName(name) || name == "mapping")
            {
                throw new ArgumentException($"Invalid or reserved metadata name '{name}'. Allowed: a-z, 0-9, '_', '-' (max. 64 characters).");
            }

            var filePath = PathGuard.Combine(_rootPath, MetaIndexName(indexName), $"{name}.meta");

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

            PathGuard.ValidateMetadataName(name);

            var filePath = PathGuard.Combine(_rootPath, MetaIndexName(indexName), $"{name}.meta");

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
                string path = PathGuard.Combine(_rootPath, MetaIndexName(indexName));

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
                string path = PathGuard.Combine(_rootPath, MetaIndexName(indexName));

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


        public bool Index(string indexName, IEnumerable<IDictionary<string, object>> items, bool archive = true)
        {
            if (!IndexExists(indexName))
            {
                throw new IndexNotFoundException();
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
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToInt32()));
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
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToDouble()));
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
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToSingle()));
                            }
                            break;
                        case FieldTypes.DateTimeType:
                            if (field.Index)
                            {
                                value = DateTools.DateToString(value.ToUtcDateTime(), DateResolution.SECOND);
                                doc.Add(new StringField(
                                field.Name,
                                (string)value,
                                field.Store ? Field.Store.YES : Field.Store.NO));
                            }
                            else
                            {
                                // ISO 8601 UTC ("2024-01-31T12:00:00.0000000Z"), independent of the server culture
                                doc.Add(new Lucene.Net.Documents.StoredField(field.Name, value.ToUtcDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
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

            // IndexWriter is thread safe; changes become visible on refresh (or the background
            // timer, ~20s) and durable on commit (refresh, background timer, shutdown)
            _resources.GetIndexWriter(indexName).AddDocuments(docs);

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
                throw new IndexNotFoundException();
            }

            var parser = new QueryParser(AppLuceneVersion, termField, _resources.GetAnalyzer(indexName));
            var query = parser.Parse(term);

            var writer = _resources.GetIndexWriter(indexName);
            writer.DeleteDocuments(query);  // committed by refresh / background timer / shutdown

            return true;
        }

        public void RefreshIndex(string indexName)
        {
            if (!IndexExists(indexName))
            {
                throw new IndexNotFoundException();
            }

            _resources.RefreshResources(indexName);
        }

        #endregion

        #region Administration

        /// <summary>
        /// Names of all indices (directories in the root path, without the hidden ".name" meta directories).
        /// </summary>
        public IEnumerable<string> IndexNames()
        {
            var root = new DirectoryInfo(_rootPath);
            if (!root.Exists)
            {
                return Array.Empty<string>();
            }

            return root.GetDirectories()
                       .Select(d => d.Name)
                       .Where(n => !n.StartsWith(".") && PathGuard.IsValidName(n))
                       .OrderBy(n => n)
                       .ToArray();
        }

        public Core.Models.Admin.IndexStats IndexStats(string indexName)
        {
            if (!IndexExists(indexName))
            {
                throw new IndexNotFoundException();
            }

            var stats = new Core.Models.Admin.IndexStats() { Name = indexName };

            var indexDirectory = new DirectoryInfo(PathGuard.Combine(_rootPath, PathGuard.ValidateIndexName(indexName)));
            var files = indexDirectory.GetFiles("*", SearchOption.AllDirectories);
            stats.SizeInBytes = files.Sum(f => f.Length);
            stats.LastModifiedUtc = files.Length > 0 ? files.Max(f => f.LastWriteTimeUtc) : null;

            if (files.Any(f => f.Name.StartsWith("segments")))
            {
                using var searcherLease = _resources.AcquireSearcher(indexName);
                var reader = searcherLease.Searcher.IndexReader;
                stats.DocumentCount = reader.NumDocs;
                stats.DeletedDocumentCount = reader.NumDeletedDocs;
            }

            return stats;
        }

        /// <summary>
        /// All documents of the index (stored fields of the mapping, without _guid) from one consistent
        /// snapshot. Fields that are not stored can not be exported. The searcher is held until the
        /// enumeration ends (dispose the enumerator).
        /// </summary>
        public IEnumerable<IDictionary<string, object>> ExportDocuments(string indexName)
        {
            if (!IndexExists(indexName))
            {
                throw new IndexNotFoundException();
            }

            var mapping = _resources.GetMapping(indexName);
            var fields = mapping.Fields.Where(f => f.Store && f.FieldType != FieldTypes.GuidType).ToArray();

            return Enumerate();

            IEnumerable<IDictionary<string, object>> Enumerate()
            {
                using var searcherLease = _resources.AcquireSearcher(indexName);
                var reader = searcherLease.Searcher.IndexReader;
                var liveDocs = Lucene.Net.Index.MultiFields.GetLiveDocs(reader);

                for (int i = 0; i < reader.MaxDoc; i++)
                {
                    if (liveDocs != null && !liveDocs.Get(i))
                    {
                        continue;   // deleted
                    }

                    var doc = reader.Document(i);
                    var item = new Dictionary<string, object>();
                    foreach (var field in fields)
                    {
                        var value = field.GetValue(doc);
                        if (value != null)
                        {
                            item[field.Name] = value;
                        }
                    }
                    yield return item;
                }
            }
        }

        #endregion

        #region Search/Query

        /// <summary>
        /// Max. from + size for offset paging. Deeper pages: use searchAfter.
        /// </summary>
        public const int MaxResultWindow = 10000;

        public IEnumerable<IDictionary<string, object>> Search(string indexName,
                                                               string term,
                                                               string outFieldNames = null,
                                                               int size = 20,
                                                               string sortFieldName = null,
                                                               bool sortReverse = false,
                                                               ISpatialFilter spatialFilter = null,
                                                               string[] primarySearchFields = null)
            => SearchPage(indexName, term, outFieldNames, size, 0, null,
                          sortFieldName, sortReverse, spatialFilter, primarySearchFields).Hits;

        /// <summary>
        /// Paged search. Use either <paramref name="from"/> (offset, limited by <see cref="MaxResultWindow"/>)
        /// or <paramref name="searchAfter"/> (cursor from a previous page; relevance sort only).
        /// </summary>
        public SearchPage SearchPage(string indexName,
                                     string term,
                                     string outFieldNames = null,
                                     int size = 20,
                                     int from = 0,
                                     string searchAfter = null,
                                     string sortFieldName = null,
                                     bool sortReverse = false,
                                     ISpatialFilter spatialFilter = null,
                                     string[] primarySearchFields = null)
        {
            if (size < 0) throw new ArgumentException("size must be >= 0");
            if (from < 0) throw new ArgumentException("from must be >= 0");
            if ((long)from + size > MaxResultWindow)
            {
                throw new ArgumentException($"from + size must not exceed {MaxResultWindow}. Use searchAfter for deep paging.");
            }

            using var searcherLease = _resources.AcquireSearcher(indexName);
            var searcher = searcherLease.Searcher;

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

                query = parser.Parse(term.ParseSearchTerm(mapping));
            }

            // Spatial Filter
            if (spatialFilter != null)
            {
                InitSpatial();
                filter = spatialFilter.ToFilter(_spatialContext, _tree);
            }

            FieldMapping sortField = String.IsNullOrEmpty(sortFieldName) ?
                null :
                mapping.GetField(sortFieldName);

            TopDocs topDocs;
            int skip = 0;
            if (!String.IsNullOrEmpty(searchAfter))
            {
                if (sortField != null)
                {
                    throw new ArgumentException("searchAfter is only supported for relevance sorting (without sortField).");
                }
                if (from > 0)
                {
                    throw new ArgumentException("from and searchAfter can not be combined.");
                }

                topDocs = searcher.SearchAfter(SearchAfterCursor.Decode(searchAfter), query, filter, Math.Max(size, 1));
            }
            else if (sortField != null)
            {
                Sort sort = new Sort(new SortField(sortFieldName, sortField.GetSortFieldType(), sortReverse));
                topDocs = searcher.Search(query, filter, Math.Max(from + size, 1), sort);
                skip = from;
            }
            else
            {
                topDocs = searcher.Search(query, filter, Math.Max(from + size, 1));
                skip = from;
            }

            ScoreDoc[] hits = topDocs.ScoreDocs.Skip(skip).Take(size).ToArray();

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

            string nextSearchAfter = null;
            if (sortField == null && size > 0 && hits.Length == size)
            {
                nextSearchAfter = SearchAfterCursor.Encode(hits[hits.Length - 1]);
            }

            return new SearchPage(docs, topDocs.TotalHits, nextSearchAfter);
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
            {
                return new IDictionary<string, object>[0];
            }

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

        public SearchPage SearchPhoneticPage(string indexName,
                                             string term,
                                             string outFieldNames = null,
                                             int size = 20,
                                             int from = 0,
                                             string searchAfter = null,
                                             string sortFieldName = null,
                                             bool sortReverse = false,
                                             ISpatialFilter spatialFilter = null)
        {
            var mapping = _resources.GetMapping(indexName);

            if (mapping.PrimaryFieldsPhonetics == Algorithm.None)
            {
                return new SearchPage(new IDictionary<string, object>[0], 0, null);
            }

            term = term.ToPhonetics(mapping.PrimaryFieldsPhonetics);
            term = new Core.QueryBuilder().ParseTerm(term);

            return SearchPage(indexName, term, outFieldNames, size, from, searchAfter,
                              sortFieldName, sortReverse, spatialFilter,
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

            using var searcherLease = _resources.AcquireSearcher(indexName);
            var searcher = searcherLease.Searcher;

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

                query = parser.Parse(term.ParseSearchTerm(mapping));
            }

            var topGroups = groupingSearch.Search(searcher, query, 0, 100000);

            return topGroups.Groups
                            .Select(g =>
                            {
                                var value = g.GroupValue;
                                if (g.GroupValue is BytesRef)
                                {
                                    value = ((BytesRef)g.GroupValue).Utf8ToString();
                                }

                                return new Dictionary<string, object>()
                                {
                                    { "value", value.DecodeAsciiCharacters(field, mapping)  },
                                    { "_hits", g.TotalHits },
                                    //{ "_score", g.Score }
                                };
                            })
                            .ToList();  // materialize while the searcher lease is held
        }

        #endregion

        #region Helper

        private string MetaIndexName(string indexName)
        {
            return $".{PathGuard.ValidateIndexName(indexName)}";
        }

        private void InitSpatial()
        {
            if (_spatialContext == null)
            {
                _spatialContext = SpatialContext.Geo;

                int maxLevels = 11;
                _tree = new GeohashPrefixTree(_spatialContext, maxLevels);
            }
        }

        #endregion
    }
}

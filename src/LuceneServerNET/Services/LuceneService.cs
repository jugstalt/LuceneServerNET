using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using System;
using System.Collections.Generic;
using System.IO;

namespace LuceneServerNET.Services
{
    public class LuceneService
    {
        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        private readonly string _rootPath;

        public LuceneService()
        {
            _rootPath = @"c:\temp\lucene.net\indices";
        }

        public bool IndexExists(string indexName)
        {
            var indexPath = Path.Combine(_rootPath, indexName);

            return new DirectoryInfo(indexPath).Exists;
        }

        public bool CreateIndex(string indexName)
        {
            if (IndexExists(indexName))
            {
                throw new Exception("Index already exists");
            }

            var indexPath = Path.Combine(_rootPath, indexName);

            new DirectoryInfo(indexPath).Create();

            return true;

            //using (var dir = FSDirectory.Open(indexPath))
            //{
            //    return true;
            //}
        }

        //public bool Map(string indexName)
        //{
        //    var doc = new Document();
        //    doc.Add(new ("name")

        //    {
        //        // StringField indexes but doesn't tokenize
        //        new StringField("name",
        //            source.Name,
        //            Field.Store.Yes),
        //        new TextField("favoritePhrase",
        //            source.FavoritePhrase,
        //            Field.Store.YES)
        //    };

        //    return true;
        //}

        public bool Index(string indexName, string title, string content)
        {
            if(!IndexExists(indexName))
            {
                throw new Exception("Index not exists");
            }

            var doc = new Document()
            {
                // StringField indexes but doesn't tokenize
                new StringField("title",
                    title,
                    Field.Store.YES),
                new TextField("content",
                            content,
                            Field.Store.YES)
            };

            using (var dir = GetIndexFSDirectory(indexName))
            using(var analyzer = new StandardAnalyzer(AppLuceneVersion)) // Create an analyzer to process the text
            {
                

                // Create an index writer
                var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
                using (var writer = new IndexWriter(dir, indexConfig))
                {
                    writer.AddDocument(doc);
                    writer.Flush(triggerMerge: false, applyAllDeletes: false);
                }
            }

            return true;
        }

        public IEnumerable<object> Search(string indexName, string term)
        {
            using (var dir = GetIndexFSDirectory(indexName))
            using (var indexReader = DirectoryReader.Open(dir))
            using (var analyzer = new StandardAnalyzer(AppLuceneVersion))
            {
                var searcher = new IndexSearcher(indexReader);

                //var phrase = new MultiPhraseQuery
                //{
                //    new Term("content", term)
                //};

                //var phrase = new WildcardQuery(new Term("content", $"{ term }*"));

                var parser = new QueryParser(AppLuceneVersion, "content", analyzer);
                var query = parser.Parse(term);

                var hits = searcher.Search(query, 20 /* top 20 */).ScoreDocs;

                List<object> docs=new List<object>();
                foreach (var hit in hits)
                {
                    var foundDoc = searcher.Doc(hit.Doc);

                    if (foundDoc != null)
                    {
                        docs.Add(new
                        {
                            _id = hit.Doc,
                            _score = hit.Score,
                            title = foundDoc.Get("title"),
                            content = foundDoc.Get("content")
                        });
                    }
                }


                return docs;
            }
        }

        #region Helper

        private FSDirectory GetIndexFSDirectory(string indexName)
        {
            var indexPath = Path.Combine(_rootPath, indexName);

            return FSDirectory.Open(indexPath);
        }

        #endregion
    }
}

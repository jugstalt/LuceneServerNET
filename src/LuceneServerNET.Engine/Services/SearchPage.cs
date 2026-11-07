using Lucene.Net.Search;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuceneServerNET.Engine.Services
{
    public class SearchPage
    {
        public SearchPage(IEnumerable<IDictionary<string, object>> hits, long totalHits, string nextSearchAfter)
        {
            Hits = hits;
            TotalHits = totalHits;
            NextSearchAfter = nextSearchAfter;
        }

        public IEnumerable<IDictionary<string, object>> Hits { get; }

        /// <summary>
        /// Total number of matching documents (independent of from/size).
        /// </summary>
        public long TotalHits { get; }

        /// <summary>
        /// Cursor for the next page, or null.
        /// </summary>
        public string NextSearchAfter { get; }
    }

    /// <summary>
    /// Opaque cursor for searchAfter paging: base64url("doc:score").
    /// Lucene doc ids are only stable while the index is unchanged:
    /// after documents were added/removed a cursor may skip or repeat hits.
    /// </summary>
    internal static class SearchAfterCursor
    {
        public static string Encode(ScoreDoc doc)
        {
            var raw = $"{doc.Doc.ToString(CultureInfo.InvariantCulture)}:{doc.Score.ToString("R", CultureInfo.InvariantCulture)}";

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }

        public static ScoreDoc Decode(string cursor)
        {
            try
            {
                var b64 = cursor.Trim().Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split(':');

                return new ScoreDoc(int.Parse(parts[0], CultureInfo.InvariantCulture),
                                    float.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid searchAfter cursor.", ex);
            }
        }
    }
}

using System;

namespace LuceneServerNET.Core.Models.Admin
{
    /// <summary>
    /// Statistics of a single index (returned by GET api/v2/indices/{id}/stats).
    /// </summary>
    public class IndexStats
    {
        public string Name { get; set; }

        /// <summary>Number of (visible, not deleted) documents.</summary>
        public int DocumentCount { get; set; }

        /// <summary>Number of deleted documents that are not merged away yet.</summary>
        public int DeletedDocumentCount { get; set; }

        /// <summary>Size of the index files on disk (bytes).</summary>
        public long SizeInBytes { get; set; }

        /// <summary>Last write time (UTC) of an index file.</summary>
        public DateTime? LastModifiedUtc { get; set; }
    }
}

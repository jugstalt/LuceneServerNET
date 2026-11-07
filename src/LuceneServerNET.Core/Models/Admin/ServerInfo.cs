using System;

namespace LuceneServerNET.Core.Models.Admin
{
    /// <summary>
    /// Server information for administration (returned by GET api/v2/admin/info).
    /// </summary>
    public class ServerInfo
    {
        public string Version { get; set; }
        public string Platform { get; set; }
        public string Framework { get; set; }
        public DateTime StartedUtc { get; set; }
        public double UptimeSeconds { get; set; }
        public int IndexCount { get; set; }
        public long WorkingSetBytes { get; set; }
    }
}

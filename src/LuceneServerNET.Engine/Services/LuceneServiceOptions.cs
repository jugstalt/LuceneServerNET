namespace LuceneServerNET.Engine.Services;

public class LuceneServiceOptions
{
    public string RootPath { get; set; }
    public string ArchivePath { get; set; }

    /// <summary>
    /// Interval of the background refresh + commit of all open indices (default 20 seconds).
    /// </summary>
    public int BackgroundCommitSeconds { get; set; } = 20;
}

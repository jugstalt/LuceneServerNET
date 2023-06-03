namespace LuceneServerNET.Engine.Services;

public class RestoreServiceOptions
{
    public bool RestoreOnRestart { get; set; }
    public int RestoreOnRestartCount { get; set; }
    public int RestoreOnRestartSince { get; set; }

    public bool IsRestoreDesired()
        => RestoreOnRestart == true &&
           (RestoreOnRestartCount > 0 || RestoreOnRestartSince > 0);
}

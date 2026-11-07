using System.Globalization;

namespace LuceneServerNET.Admin.Components.Shared;

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
             : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
             : $"{span.Minutes}m {span.Seconds}s";
    }

    public static string Date(DateTime? utc) => utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "–";

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Value(object? value) => value switch
    {
        null => "",
        System.Text.Json.JsonElement e when e.ValueKind == System.Text.Json.JsonValueKind.String => e.GetString() ?? "",
        System.Text.Json.JsonElement e => e.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
}

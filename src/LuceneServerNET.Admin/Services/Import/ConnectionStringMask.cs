using System.Data.Common;

namespace LuceneServerNET.Admin.Services.Import;

/// <summary>
/// Shows connection strings without passwords ("Password=*****") and puts the stored password back when a
/// masked string is saved again.
/// </summary>
public static class ConnectionStringMask
{
    public const string Mask = "*****";

    private static readonly string[] PasswordKeys = ["password", "pwd"];

    private static bool IsPasswordKey(string key) => PasswordKeys.Contains(key.Trim().ToLowerInvariant());

    public static string Masked(string connectionString)
    {
        if (String.IsNullOrWhiteSpace(connectionString)) return "";
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (var key in builder.Keys.Cast<string>().Where(IsPasswordKey).ToList())
        {
            builder[key] = Mask;
        }
        return builder.ConnectionString;
    }

    public static bool ContainsMask(string? connectionString)
    {
        if (String.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return builder.Keys.Cast<string>().Any(k => IsPasswordKey(k) && builder[k]?.ToString() == Mask);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Replaces masked passwords of <paramref name="edited"/> with the password of <paramref name="stored"/>.</summary>
    public static string Unmask(string edited, string stored)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = edited };
        var storedBuilder = new DbConnectionStringBuilder { ConnectionString = stored };
        var storedPassword = storedBuilder.Keys.Cast<string>().Where(IsPasswordKey).Select(k => storedBuilder[k]?.ToString()).FirstOrDefault();

        foreach (var key in builder.Keys.Cast<string>().Where(k => IsPasswordKey(k) && builder[k]?.ToString() == Mask).ToList())
        {
            if (storedPassword is null) throw new ArgumentException("The stored connection string has no password: enter it");
            builder[key] = storedPassword;
        }
        return builder.ConnectionString;
    }
}

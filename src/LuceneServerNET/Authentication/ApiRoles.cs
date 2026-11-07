using System;
using System.Collections.Generic;

namespace LuceneServerNET.Authentication;

public static class ApiRoles
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Admin = "admin";

    /// <summary>
    /// Expands a role to itself plus all implied lower roles (admin => write => read).
    /// Unknown roles are passed through (lower case).
    /// </summary>
    public static IEnumerable<string> Expand(string role)
    {
        var r = role?.Trim().ToLowerInvariant();

        return r switch
        {
            Admin => [Admin, Write, Read],
            Write => [Write, Read],
            Read => [Read],
            null or "" => Array.Empty<string>(),
            _ => [r]
        };
    }
}

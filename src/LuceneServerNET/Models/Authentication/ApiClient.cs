using System.Collections.Generic;

namespace LuceneServerNET.Models.Authentication;

public class ApiClient
{
    public string Id { get; set; }

    /// <summary>
    /// Plain text secret (legacy, still supported). Prefer <see cref="SecretHash"/>.
    /// </summary>
    public string Secret { get; set; }

    /// <summary>
    /// Hashed secret in the format "pbkdf2-sha256$iterations$saltBase64$hashBase64".
    /// Create one with: LuceneServerNET --hash-secret &lt;secret&gt;
    /// </summary>
    public string SecretHash { get; set; }

    /// <summary>
    /// Roles: "read", "write", "admin". Higher roles include lower ones.
    /// If omitted, the client gets "admin" (backwards compatible).
    /// </summary>
    public IEnumerable<string> Roles { get; set; }
}

using LuceneServerNET.Admin.Services.Repository;
using LuceneServerNET.Client;
using LuceneServerNET.Core.Models.Admin;
using Microsoft.AspNetCore.DataProtection;

namespace LuceneServerNET.Admin.Services.Instances;

/// <summary>Instance from the configuration section Admin:Instances.</summary>
public class InstanceOptions
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public enum InstanceSource { Configuration, Database }

/// <summary>An administrated LuceneServer.NET instance. The secret never leaves the server (Blazor Server).</summary>
public class ServerInstance
{
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public InstanceSource Source { get; init; }
    public long? DatabaseId { get; init; }

    public bool IsEditable => Source == InstanceSource.Database;
}

/// <summary>Result of probing an instance (health, version, roles of the API client).</summary>
public class InstanceStatus
{
    public bool Online { get; set; }
    public string? Error { get; set; }
    public ServerInfo? Info { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    public bool CanRead => Roles.Contains("read");
    public bool CanWrite => Roles.Contains("write");
    public bool IsAdmin => Roles.Contains("admin");
}

/// <summary>
/// Merges the instances from the configuration (read only) and the admin database (editable)
/// and creates API clients for them.
/// </summary>
public class InstanceRegistry
{
    private readonly IConfiguration _config;
    private readonly IAdminRepositoryService _repository;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;

    public const string HttpClientName = "luceneserver";

    /// <summary>Raised after an instance was added, changed or removed (e.g. to refresh the navigation).</summary>
    public event Action? Changed;

    public InstanceRegistry(IConfiguration config,
                            IAdminRepositoryService repository,
                            IDataProtectionProvider dataProtection,
                            IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _repository = repository;
        _protector = dataProtection.CreateProtector("LuceneServerNET.Admin.InstanceSecrets");
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<ServerInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var fromConfig = (_config.GetSection("Admin:Instances").Get<InstanceOptions[]>() ?? [])
            .Where(i => !String.IsNullOrWhiteSpace(i.Name) && !String.IsNullOrWhiteSpace(i.Url))
            .Select(i => new ServerInstance()
            {
                Name = i.Name.Trim(),
                Url = i.Url.Trim(),
                ClientId = i.ClientId,
                ClientSecret = i.ClientSecret,
                Source = InstanceSource.Configuration
            });

        var fromDatabase = (await _repository.GetInstancesAsync(cancellationToken))
            .Select(r => new ServerInstance()
            {
                Name = r.Name,
                Url = r.Url,
                ClientId = r.ClientId,
                ClientSecret = Unprotect(r.ClientSecretProtected),
                Source = InstanceSource.Database,
                DatabaseId = r.Id
            });

        // configuration wins if a name exists twice
        return fromConfig.Concat(fromDatabase)
                         .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.First())
                         .ToList();
    }

    public async Task<ServerInstance?> GetAsync(string name, CancellationToken cancellationToken = default)
        => (await GetAllAsync(cancellationToken)).FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds or updates a database instance. An empty secret on update keeps the stored secret.</summary>
    public async Task SaveAsync(long? id, string name, string url, string? clientId, string? clientSecret, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        url = url.Trim().TrimEnd('/');

        if (String.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Url must be an absolute http(s) url");
        }

        var existing = await GetAsync(name, cancellationToken);
        if (existing != null && existing.DatabaseId != id)
        {
            throw new ArgumentException($"An instance with the name '{name}' already exists");
        }

        if (id.HasValue)
        {
            var record = await _repository.GetInstanceAsync(id.Value, cancellationToken)
                         ?? throw new ArgumentException("Instance not found");
            record.Name = name;
            record.Url = url;
            record.ClientId = clientId;
            if (!String.IsNullOrEmpty(clientSecret))
            {
                record.ClientSecretProtected = _protector.Protect(clientSecret);
            }
            if (String.IsNullOrEmpty(clientId))
            {
                record.ClientSecretProtected = null;
            }
            await _repository.UpdateInstanceAsync(record, cancellationToken);
        }
        else
        {
            await _repository.InsertInstanceAsync(new InstanceRecord()
            {
                Name = name,
                Url = url,
                ClientId = clientId,
                ClientSecretProtected = String.IsNullOrEmpty(clientSecret) ? null : _protector.Protect(clientSecret)
            }, cancellationToken);
        }

        Changed?.Invoke();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteInstanceAsync(id, cancellationToken);
        Changed?.Invoke();
    }

    public LuceneServerV2Client CreateClient(ServerInstance instance)
        => new(instance.Url, instance.ClientId, instance.ClientSecret, _httpClientFactory.CreateClient(HttpClientName));

    public LuceneServerV2Client CreateClient(string url, string? clientId, string? clientSecret)
        => new(url, clientId, clientSecret, _httpClientFactory.CreateClient(HttpClientName));

    public static async Task<InstanceStatus> ProbeAsync(LuceneServerV2Client client, CancellationToken cancellationToken = default)
    {
        var status = new InstanceStatus();
        try
        {
            var whoAmI = await client.WhoAmIAsync(cancellationToken);
            status.Roles = (whoAmI.Roles ?? []).ToArray();
            status.Online = true;

            if (status.IsAdmin)
            {
                status.Info = await client.GetServerInfoAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            status.Error = ex.Message;
        }
        return status;
    }

    private string? Unprotect(string? protectedSecret)
    {
        if (String.IsNullOrEmpty(protectedSecret)) return null;
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch
        {
            return null;  // keys lost => secret has to be entered again
        }
    }
}

using LuceneServerNET.Engine.Exceptions;
using LuceneServerNET.Engine.Extensions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LuceneServerNET.Engine.Services;

/// <summary>
/// Index aliases: an alias name points to exactly one index and can be used everywhere an index id is used
/// (except creating and removing indices). Switching an alias is atomic for readers, e.g. to rebuild an index
/// under a new name and switch without downtime.
/// Stored in {RootPath}/aliases.json, a copy is written to {ArchivePath}/aliases.json (if configured) and used
/// when the root file is missing (restore).
/// </summary>
public class AliasService
{
    private const string FileName = "aliases.json";

    private readonly string _rootPath, _archivePath;
    private readonly object _lock = new();
    private Dictionary<string, string> _aliases;   // alias => index (copy on write)

    public AliasService(IOptionsMonitor<LuceneServiceOptions> options)
    {
        _rootPath = options.CurrentValue.RootPath;
        _archivePath = options.CurrentValue.ArchivePath;
    }

    private string FilePath => Path.Combine(_rootPath, FileName);
    private string ArchiveFilePath => String.IsNullOrEmpty(_archivePath) ? null : Path.Combine(_archivePath, FileName);

    private Dictionary<string, string> Aliases
    {
        get
        {
            var aliases = _aliases;
            if (aliases != null)
            {
                return aliases;
            }

            lock (_lock)
            {
                return _aliases ??= Load();
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        foreach (var path in new[] { FilePath, ArchiveFilePath })
        {
            if (path != null && File.Exists(path))
            {
                var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
                return new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            }
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Alias => index, sorted by alias.</summary>
    public IReadOnlyDictionary<string, string> GetAll()
        => Aliases.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value);

    public bool IsAlias(string name) => name != null && Aliases.ContainsKey(name);

    /// <summary>The index of an alias, otherwise the name itself.</summary>
    public string Resolve(string name)
        => name != null && Aliases.TryGetValue(name, out var index) ? index : name;

    public IEnumerable<string> AliasesOf(string indexName)
        => Aliases.Where(a => a.Value == indexName).Select(a => a.Key).OrderBy(a => a, StringComparer.Ordinal);

    /// <summary>Creates or switches an alias.</summary>
    /// <param name="indexExists">checks if an index (directory) exists</param>
    public void Set(string alias, string indexName, Func<string, bool> indexExists)
    {
        PathGuard.ValidateIndexName(alias);
        PathGuard.ValidateIndexName(indexName);

        if (alias == indexName)
        {
            throw new ArgumentException("Alias and index must have different names");
        }
        if (indexExists(alias))
        {
            throw new IndexConflictException($"An index with the name '{alias}' exists, an alias can not have the same name");
        }
        if (IsAlias(indexName))
        {
            throw new ArgumentException($"'{indexName}' is an alias; an alias must point to an index");
        }
        if (!indexExists(indexName))
        {
            throw new IndexNotFoundException();
        }

        Update(aliases => aliases[alias] = indexName);
    }

    /// <summary>Removes an alias; returns false if it does not exist.</summary>
    public bool Remove(string alias)
    {
        if (!IsAlias(alias))
        {
            return false;
        }
        Update(aliases => aliases.Remove(alias));
        return true;
    }

    private void Update(Action<Dictionary<string, string>> change)
    {
        lock (_lock)
        {
            var aliases = new Dictionary<string, string>(_aliases ??= Load(), StringComparer.Ordinal);
            change(aliases);

            var json = JsonSerializer.Serialize(aliases.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value),
                                                new JsonSerializerOptions { WriteIndented = true });
            WriteAtomic(FilePath, json);
            if (ArchiveFilePath != null && Directory.Exists(_archivePath))
            {
                WriteAtomic(ArchiveFilePath, json);
            }

            _aliases = aliases;   // readers see the old or the new dictionary, never a partial one
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}

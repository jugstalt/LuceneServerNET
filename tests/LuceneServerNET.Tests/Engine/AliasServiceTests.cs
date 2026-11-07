using LuceneServerNET.Engine.Exceptions;
using LuceneServerNET.Engine.Services;
using Microsoft.Extensions.Options;

namespace LuceneServerNET.Tests.Engine;

public class AliasServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lucene-alias-{Guid.NewGuid():N}");
    private readonly string _archive;
    private readonly HashSet<string> _indices = ["orte_1", "orte_2"];

    public AliasServiceTests()
    {
        _archive = _root + "-archive";
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_archive);
    }

    private AliasService Create(string archive = null)
        => new(new StaticOptions(new LuceneServiceOptions { RootPath = _root, ArchivePath = archive ?? _archive }));

    private bool Exists(string name) => _indices.Contains(name);

    [Fact]
    public void Set_Resolve_Switch_Remove()
    {
        var aliases = Create();
        Assert.Equal("orte", aliases.Resolve("orte"));   // no alias => name itself

        aliases.Set("orte", "orte_1", Exists);
        Assert.Equal("orte_1", aliases.Resolve("orte"));
        Assert.Equal(["orte"], aliases.AliasesOf("orte_1"));

        aliases.Set("orte", "orte_2", Exists);
        Assert.Equal("orte_2", aliases.Resolve("orte"));
        Assert.Empty(aliases.AliasesOf("orte_1"));

        Assert.True(aliases.Remove("orte"));
        Assert.False(aliases.Remove("orte"));
        Assert.Equal("orte", aliases.Resolve("orte"));
    }

    [Fact]
    public void IsPersisted_AndRestoredFromArchive()
    {
        Create().Set("orte", "orte_1", Exists);

        Assert.Equal("orte_1", Create().Resolve("orte"));                 // root file
        Assert.True(File.Exists(Path.Combine(_archive, "aliases.json")));

        File.Delete(Path.Combine(_root, "aliases.json"));
        Assert.Equal("orte_1", Create().Resolve("orte"));                 // archive copy
    }

    [Fact]
    public void WithoutArchivePath_OnlyRoot()
    {
        var aliases = Create(archive: "");
        aliases.Set("orte", "orte_1", Exists);
        Assert.True(File.Exists(Path.Combine(_root, "aliases.json")));
        Assert.False(File.Exists(Path.Combine(_archive, "aliases.json")));
    }

    [Fact]
    public void Rules()
    {
        var aliases = Create();
        Assert.Throws<IndexConflictException>(() => aliases.Set("orte_2", "orte_1", Exists));   // name of an index
        Assert.Throws<IndexNotFoundException>(() => aliases.Set("orte", "missing", Exists));
        Assert.Throws<ArgumentException>(() => aliases.Set("orte_1", "orte_1", Exists));
        Assert.Throws<ArgumentException>(() => aliases.Set("Bad Name", "orte_1", Exists));

        aliases.Set("orte", "orte_1", Exists);
        Assert.Throws<ArgumentException>(() => aliases.Set("orte2", "orte", Exists));          // alias to an alias
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_archive, true); } catch { }
    }

    private class StaticOptions(LuceneServiceOptions value) : IOptionsMonitor<LuceneServiceOptions>
    {
        public LuceneServiceOptions CurrentValue => value;
        public LuceneServiceOptions Get(string name) => value;
        public IDisposable OnChange(Action<LuceneServiceOptions, string> listener) => null;
    }
}

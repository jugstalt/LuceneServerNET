#nullable enable
using LuceneServerNET.Admin.Services.Import;

namespace LuceneServerNET.Tests.Admin;

public class ConnectionStringMaskTests
{
    [Theory]
    [InlineData("Host=gisdb;Database=gis;Username=reader;Password=geheim;Port=5432", "geheim")]
    [InlineData("Server=sql1;Database=gis;User Id=reader;Pwd=\"se;cret\";TrustServerCertificate=True", "se;cret")]
    public void Mask_And_Unmask(string stored, string password)
    {
        var masked = ConnectionStringMask.Masked(stored);
        Assert.DoesNotContain(password, masked);
        Assert.Contains("*****", masked);
        Assert.True(ConnectionStringMask.ContainsMask(masked));

        // edit the host, keep the masked password
        var edited = masked.Replace("gisdb", "gisdb2").Replace("sql1", "sql2");
        var restored = ConnectionStringMask.Unmask(edited, stored);
        var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = restored };
        Assert.Equal(password, builder.Keys.Cast<string>().Where(k => k is "password" or "pwd").Select(k => builder[k].ToString()).Single());
        Assert.True(restored.Contains("gisdb2") || restored.Contains("sql2"));
    }

    [Fact]
    public void NoPassword()
    {
        Assert.Equal("data source=a.sqlite", ConnectionStringMask.Masked("Data Source=a.sqlite"));
        Assert.False(ConnectionStringMask.ContainsMask("Data Source=a.sqlite"));
        Assert.Throws<ArgumentException>(() => ConnectionStringMask.Unmask("Host=a;Password=*****", "Host=a"));
    }
}

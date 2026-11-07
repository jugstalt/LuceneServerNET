using LuceneServerNET.Engine.Extensions;
using System.Globalization;
using System.Text.Json;

namespace LuceneServerNET.Tests.Engine;

public class DateTimeConvertTests
{
    [Theory]
    [InlineData("2024-01-31", "2024-01-31T00:00:00.0000000Z")]               // no time zone => UTC (not server local time)
    [InlineData("2024-01-31T12:30:00", "2024-01-31T12:30:00.0000000Z")]
    [InlineData("2024-01-31T12:30:00Z", "2024-01-31T12:30:00.0000000Z")]
    [InlineData("2024-06-01T12:00:00+02:00", "2024-06-01T10:00:00.0000000Z")] // offset => converted
    [InlineData("1128-01-01", "1128-01-01T00:00:00.0000000Z")]
    public void ToUtcDateTime_FromString(string input, string expected)
    {
        Assert.Equal(expected, input.ToUtcDateTime().ToString("O", CultureInfo.InvariantCulture));
        Assert.Equal(expected, JsonDocument.Parse($"\"{input}\"").RootElement.ToUtcDateTime().ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToUtcDateTime_FromDateTime()
    {
        var unspecified = new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Utc), unspecified.ToUtcDateTime());
        Assert.Equal(DateTimeKind.Utc, unspecified.ToUtcDateTime().Kind);

        var local = new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime(), local.ToUtcDateTime());

        var offset = new DateTimeOffset(2024, 1, 31, 12, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTime(2024, 1, 31, 10, 0, 0, DateTimeKind.Utc), offset.ToUtcDateTime());
    }

    [Fact]
    public void ToUtcDateTime_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            // "01/02/2024" would be Jan 2 (en-US) or Feb 1 (de) with culture parsing; invariant = MM/dd
            Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), "01/02/2024".ToUtcDateTime());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void StoredString_IsoAndLegacyCulture()
    {
        Assert.Equal(new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Utc), ConvertExtensions.StoredStringToUtcDateTime("2024-01-31T12:00:00.0000000Z"));

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-AT");
        try
        {
            // stored-only values of older indices: server culture format
            Assert.Equal(new DateTime(2024, 1, 31, 12, 0, 0), ConvertExtensions.StoredStringToUtcDateTime("31.01.2024 12:00:00"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Null(ConvertExtensions.StoredStringToUtcDateTime(" "));
        Assert.Null(ConvertExtensions.StoredStringToUtcDateTime("no date"));
    }

    [Fact]
    public void ToUtcDateTime_Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => "no date".ToUtcDateTime());
        Assert.Throws<FormatException>(() => ((object)null).ToUtcDateTime());
    }
}

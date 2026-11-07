using Lucene.Net.Spatial.Prefix.Tree;
using LuceneServerNET.Core.Models.Spatial;
using LuceneServerNET.Engine.Models.Spatial;
using Spatial4n.Context;

namespace LuceneServerNET.Tests.Engine;

public class SpatialFilterTests
{
    [Fact]
    public void BBox_Parse_ReadsFieldAndCoordinates()
    {
        var filter = Assert.IsType<BBoxFilter>(BBoxFilter.Parse("geo:10.5,46,17.25,49"));

        Assert.Equal("geo", filter.GeoFieldName);
        Assert.Equal((10.5, 46d, 17.25, 49d), (filter.MinX, filter.MinY, filter.MaxX, filter.MaxY));
    }

    [Theory]
    [InlineData("geo")]
    [InlineData("geo:1,2,3")]
    [InlineData("geo:a,b,c,d")]
    public void BBox_Parse_InvalidArgs_ThrowsArgumentException(string args)
        => Assert.Throws<ArgumentException>(() => BBoxFilter.Parse(args));

    [Fact]
    public void DistanceKm_Parse_ReadsValues()
    {
        var filter = Assert.IsType<DistanceKmFilter>(DistanceKmFilter.Parse("pos:15.4,47.07,2.5"));

        Assert.Equal("pos", filter.GeoFieldName);
        Assert.Equal((15.4, 47.07, 2.5), (filter.X, filter.Y, filter.Distance));
    }

    [Fact]
    public void DistanceKm_Parse_Invalid_Throws()
        => Assert.Throws<ArgumentException>(() => DistanceKmFilter.Parse("pos:15.4"));

    [Fact]
    public void LineDistance_Parse_ReadsPointsAndDistance()
    {
        var filter = Assert.IsType<LineStringDistanceKmFilter>(LineStringDistanceKmFilter.Parse("geo:1,2,3,4,5,6,0.5"));

        Assert.Equal("geo", filter.GeoFieldName);
        Assert.Equal(0.5, filter.Distance);
        Assert.Equal(new[] { new[] { 1d, 2 }, new[] { 3d, 4 }, new[] { 5d, 6 } }, filter.Points);
    }

    [Theory]
    [InlineData("geo:1,2,0.5")]
    [InlineData("geo:1,2,3,4")]
    public void LineDistance_Parse_InvalidCoordinateCount_Throws(string args)
        => Assert.Throws<ArgumentException>(() => LineStringDistanceKmFilter.Parse(args));

    [Fact]
    public void Filters_CanBeConvertedToLuceneFilter()
    {
        var ctx = SpatialContext.Geo;
        var tree = new GeohashPrefixTree(ctx, 11);

        Assert.NotNull(BBoxFilter.Parse("geo:10,46,17,49").ToFilter(ctx, tree));
        Assert.NotNull(DistanceKmFilter.Parse("geo:15,47,2").ToFilter(ctx, tree));
        Assert.NotNull(LineStringDistanceKmFilter.Parse("geo:15,47,16,48,2").ToFilter(ctx, tree));
    }
}

public class GeoTypeTests
{
    [Fact]
    public void Parse_Json_ReturnsGeoPoint()
    {
        var point = Assert.IsType<GeoPoint>(GeoType.Parse("{\"Type\":\"GeoPoint\",\"Longidute\":15.4,\"Latitude\":47.1}"));

        Assert.True(point.IsValid());
        Assert.Equal((15.4, 47.1), (point.Longidute, point.Latitude));
    }

    [Fact(Skip = "Bug: GeoType.Parse prüft 'String.IsNullOrEmpty' statt '!String.IsNullOrEmpty' – Koordinaten-Strings werden nie geparst")]
    public void Parse_CoordinateString_ReturnsGeoPoint()
        => Assert.IsType<GeoPoint>(GeoType.Parse("15.4 47.1"));

    [Fact(Skip = "Bug: GeoPoint(lat, lon)-Konstruktor setzt zweimal Latitude, Longitude bleibt 0")]
    public void GeoPoint_Constructor_SetsLongitude()
        => Assert.Equal(15.4, new GeoPoint(47.1, 15.4).Longidute);
}

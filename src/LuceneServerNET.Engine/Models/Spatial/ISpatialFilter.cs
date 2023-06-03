using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix.Tree;
using Spatial4n.Core.Context;

namespace LuceneServerNET.Engine.Models.Spatial
{
    public interface ISpatialFilter
    {
        string GeoFieldName { get; }
        Filter ToFilter(SpatialContext spatialContext, SpatialPrefixTree tree);
    }
}

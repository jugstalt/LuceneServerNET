using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Spatial4n.Context;
using Spatial4n.Distance;
using System;
using System.Globalization;
using System.Linq;

namespace LuceneServerNET.Engine.Models.Spatial
{
    public class DistanceKmFilter : ISpatialFilter
    {
        private DistanceKmFilter(string fieldName, double x, double y, double distance)
        {
            this.GeoFieldName = fieldName;

            this.X = x;
            this.Y = y;
            this.Distance = distance;
        }

        public double X { get; set; }
        public double Y { get; set; }
        public double Distance { get; set; }

        #region ISpatialFilter

        public string GeoFieldName { get; }

        public Filter ToFilter(SpatialContext spatialContext, SpatialPrefixTree tree)
        {
            var strategy = new RecursivePrefixTreeStrategy(tree, GeoFieldName);

            var spatialArgs = new SpatialArgs(SpatialOperation.Intersects,
                spatialContext.MakeCircle(X, Y, DistanceUtils.Dist2Degrees(this.Distance, DistanceUtils.EarthMeanRadiusKilometers)));

            return strategy.MakeFilter(spatialArgs);
        }

        #endregion

        //&filter=dist_km(....)
        //fieldname:x,y,distance [km]
        public static ISpatialFilter Parse(string args)
        {
            try
            {
                int pos = args.IndexOf(":");

                string fieldName = args.Substring(0, pos);
                var coords = args.Substring(pos + 1).Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();

                return new DistanceKmFilter(fieldName, coords[0], coords[1], coords[2]);
            }
            catch
            {
                throw new ArgumentException("bbox args - usage:fieldname:x,y,distance in km");
            }
        }
    }
}

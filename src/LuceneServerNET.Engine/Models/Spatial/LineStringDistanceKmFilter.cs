using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Spatial4n.Core.Context;
using Spatial4n.Core.Distance;
using Spatial4n.Core.Shapes;
using Spatial4n.Core.Shapes.Impl;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LuceneServerNET.Engine.Models.Spatial
{
    public class LineStringDistanceKmFilter : ISpatialFilter
    {
        private LineStringDistanceKmFilter(string fieldName, IEnumerable<double[]> points, double distance)
        {
            this.GeoFieldName = fieldName;

            this.Points = points;
            this.Distance = distance;
        }

        public IEnumerable<double[]> Points { get; }
        public double Distance { get; }

        #region ISpatialFilter

        public string GeoFieldName { get; }

        public Filter ToFilter(SpatialContext spatialContext, SpatialPrefixTree tree)
        {
            var strategy = new RecursivePrefixTreeStrategy(tree, GeoFieldName);

            var spatialArgs = new SpatialArgs(SpatialOperation.Intersects,
                spatialContext.MakeBufferedLineString(
                    this.Points.Select(p => (IPoint)new Point(p[0], p[1], spatialContext)).ToList(),
                    DistanceUtils.Dist2Degrees(this.Distance, DistanceUtils.EARTH_MEAN_RADIUS_KM)));

            return strategy.MakeFilter(spatialArgs);
        }

        #endregion

        //&filter=linedist_km(....)
        //fieldname:x1,y1,x2,y2,...,distance in km
        public static ISpatialFilter Parse(string args)
        {
            try
            {
                int pos = args.IndexOf(":");

                string fieldName = args.Substring(0, pos);
                var coords = args.Substring(pos + 1).Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();

                if (coords.Length < 5 || coords.Length % 2 != 1)
                {
                    throw new ArgumentException();
                }

                List<double[]> points = new List<double[]>();
                for (int i = 0, to = coords.Length - 2; i < to; i+=2)
                {
                    points.Add(new double[] { coords[i], coords[i + 1] });
                }

                return new LineStringDistanceKmFilter(fieldName, points, coords[coords.Length - 1]);
            }
            catch
            {
                throw new ArgumentException("bbox args - usage:fieldname:x1,y1,x2,y2,...,distance in km");
            }
        }
    }
}

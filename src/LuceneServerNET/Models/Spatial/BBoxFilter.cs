using Lucene.Net.Search;
using Lucene.Net.Spatial.Prefix;
using Lucene.Net.Spatial.Prefix.Tree;
using Lucene.Net.Spatial.Queries;
using Spatial4n.Core.Context;
using System;
using System.Globalization;
using System.Linq;

namespace LuceneServerNET.Models.Spatial
{
    public class BBoxFilter : ISpatialFilter
    {
        private BBoxFilter(string fieldName, double minX, double minY, double maxX, double maxY)
        {
            this.GeoFieldName = fieldName;

            this.MinX = minX;
            this.MinY = minY;
            this.MaxX = maxX;
            this.MaxY = maxY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        #region ISpatialFilter

        public string GeoFieldName { get; }

        public Filter ToFilter(SpatialContext spatialContext, SpatialPrefixTree tree)
        {
            var strategy = new RecursivePrefixTreeStrategy(tree, GeoFieldName);

            var spatialArgs = new SpatialArgs(SpatialOperation.Intersects,
                spatialContext.MakeRectangle(this.MinX, this.MaxX, this.MinY, this.MaxY));

            return strategy.MakeFilter(spatialArgs);
        }

        #endregion

        //&filter=bbox(....)
        //fieldname:minX,minyY,maxX,maxY
        public static ISpatialFilter Parse(string args)
        {
            try
            {
                int pos = args.IndexOf(":");

                string fieldName = args.Substring(0, pos);
                var coords = args.Substring(pos + 1).Split(',').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();

                return new BBoxFilter(fieldName, coords[0], coords[1], coords[2], coords[3]);
            }
            catch
            {
                throw new ArgumentException("bbox args - usage:fieldname:minX,minyY,maxX,maxY");
            }
        }
    }
}
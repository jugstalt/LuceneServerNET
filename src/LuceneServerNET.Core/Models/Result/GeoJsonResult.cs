using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LuceneServerNET.Core.Models.Result
{
    public class GeoJsonResult : ApiResult
    {
        public GeoJsonResult(string geofieldName, IEnumerable<IDictionary<string, object>> hits)
        {
            this.features = new List<Feature>();

            foreach (var hit in hits)
            {
                var feature = new Feature();

                var geo = hit.ContainsKey(geofieldName) ?
                    hit[geofieldName]?.ToString() : null;

                if(!String.IsNullOrEmpty(geo))
                {
                    try
                    {
                        var coords = geo.Split(' ').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();
                        feature.geometry = new
                        {
                            type = "Point",
                            coordinates = coords
                        };
                    } catch { }
                }

                feature.properties = hit;

                this.features.Add(feature);
            }
        }

        public ICollection<Feature> features { get; }

        public string type => "FeatureCollection";

        #region Classes

        public class Feature
        {
            public string type => "Feature";
            public object geometry { get; set; }
            public IDictionary<string, object> properties { get; set; }
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LuceneServerNET.Core.Models.Spatial
{
    public abstract class GeoType
    {
        public abstract string Type { get; set; }
        public abstract bool IsValid();

        static public GeoType Parse(object value)
        {
            GeoType geoType = null;

            if (value is GeoType)
            {
                return (GeoType)value;
            }

            var stringValue = value?.ToString().Trim() ?? String.Empty;
            if (stringValue.StartsWith("{"))
            {
                geoType = JsonSerializer.Deserialize<GeoPoint>(value.ToString());
            }
            else if (!String.IsNullOrEmpty(stringValue))
            {
                // "lon lat" or "lon,lat" (same order as GeoPoint.ToString(), like GeoJSON)
                var parts = stringValue.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
                {
                    geoType = new GeoPoint(lat, lon);
                }
            }

            return geoType;
        }
    }
}

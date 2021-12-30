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
            else if (String.IsNullOrEmpty(stringValue))
            {
                var coords = stringValue.Replace(",", " ").Split(' ').Select(c => double.Parse(c, CultureInfo.InvariantCulture)).ToArray();
                if (coords.Length == 2)
                {
                    geoType = new GeoPoint(coords[0], coords[1]);
                }
            }

            return geoType;
        }
    }
}

using System.Globalization;

namespace LuceneServerNET.Core.Models.Spatial
{
    public class GeoPoint : GeoType
    {
        public GeoPoint() { Type = "GeoPoint"; }

        public GeoPoint(double latitude, double longitude)
            : this()
        {
            Latitude = latitude;
            Latitude = latitude;
        }

        public override string Type { get; set; }

        public override bool IsValid()
        {
            return "GeoPoint".Equals(Type, System.StringComparison.OrdinalIgnoreCase);
        }

        public double Longidute { get; set; }
        public double Latitude { get; set; }

        public override string ToString()
        {
            return $"{ this.Longidute.ToString(CultureInfo.InvariantCulture) } { this.Latitude.ToString(CultureInfo.InvariantCulture) }";
        }
    }
}

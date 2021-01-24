using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleClient.Rss
{
    static public class Extensions
    {
        static public NumberFormatInfo Nhi = System.Globalization.CultureInfo.InvariantCulture.NumberFormat;
        static public NumberFormatInfo Cnf = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;

        static public bool IsLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        static public bool IsWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        static public bool IsOSX = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        static public string Platform
        {
            get
            {
                if (IsLinux)
                    return OSPlatform.Linux.ToString();

                if (IsOSX)
                    return OSPlatform.OSX.ToString();

                if (IsWindows)
                    return OSPlatform.Windows.ToString();

                return "Unknown";
            }
        }

        static public double ToDouble(this string value)
        {
            if (IsWindows)
                return double.Parse(value.Replace(",", "."), Nhi);

            return double.Parse(value.Replace(",", Cnf.NumberDecimalSeparator));
        }

        static public float ToFloat(this string value)
        {
            if (IsWindows)
                return float.Parse(value.Replace(",", "."), Nhi);

            return float.Parse(value.Replace(",", Cnf.NumberDecimalSeparator));
        }
    }
}

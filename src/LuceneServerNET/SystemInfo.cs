using System.Globalization;
using System.Runtime.InteropServices;

namespace LuceneServerNET
{
    public class SystemInfo
    {
        static public NumberFormatInfo Nhi = CultureInfo.InvariantCulture.NumberFormat;
        static public NumberFormatInfo Cnf = CultureInfo.CurrentCulture.NumberFormat;

        static public bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        static public bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        static public bool IsOSX = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

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
    }
}

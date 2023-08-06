using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EkipConnect.Helpers
{
    internal static class VersionExtensions
    {
        public static string ToVersionStringShort(this Version version)
        {
            var shortVersionString = $"{version.Major:00}.{version.Minor:00}";
            return shortVersionString;
        }
        public static string ToVersionString(this Version version)
        {
            if (version == null) { return "00.00"; }
            var versionString = $"{version.Major:00}.{version.Minor:00}";
            if (version.Build > 0 && version.Revision > 0)
            {
                versionString += $".{version.Build:00}.{version.Revision:00}";
            }
            else if (version.Build > 0)
            {
                versionString += $".{version.Build:00}";
            }
            else if (version.Revision > 0)
            {
                versionString += $".{version.Build:00}.{version.Revision:00}";
            }

            return versionString;
        }
    }
}

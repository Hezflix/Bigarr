// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Test.Common/StringExtensions.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: OsInfo.IsNotWindows replaced by OperatingSystem.IsWindows() (upstream
// OsInfo lives in NzbDrone.Common.EnvironmentInfo, which is not vendored). See VENDORED.md

using System;
using System.IO;

namespace Bigarr.Parsing.Tests
{
    /// <summary>
    /// Turns the Windows-shaped paths used by the upstream fixtures into paths valid on the host OS,
    /// so the vendored test data can stay unchanged.
    /// </summary>
    internal static class TestPathExtensions
    {
        public static string AsOsAgnostic(this string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                if (path.Length > 2 && path[1] == ':')
                {
                    path = path.Replace(":", "");
                    path = Path.DirectorySeparatorChar + path;
                }

                path = path.Replace("\\", Path.DirectorySeparatorChar.ToString());
            }

            return path;
        }
    }
}

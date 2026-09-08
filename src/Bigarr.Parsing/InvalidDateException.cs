// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/InvalidDateException.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System;

namespace Bigarr.Parsing
{
    public class InvalidDateException : Exception
    {
        public InvalidDateException(string message, params object[] args)
            : base(string.Format(message, args))
        {
        }

        public InvalidDateException(string message)
            : base(message)
        {
        }
    }
}

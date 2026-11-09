// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/InvalidSeasonException.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")


using System;

namespace Bigarr.Parsing.Episodes
{
    public class InvalidSeasonException : Exception
    {
        public InvalidSeasonException(string message, params object[] args)
            : base(string.Format(message, args))
        {
        }

        public InvalidSeasonException(string message)
            : base(message)
        {
        }
    }
}

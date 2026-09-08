// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Organizer/FileNameBuilder.cs (the Iso639BTMap field only) — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modification: extracted the single static map out of FileNameBuilder (which pulls in the whole
// Core dependency graph) into this internal holder. See VENDORED.md.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Bigarr.Parsing.Internal
{
    internal static class Iso639BTMap
    {
        // generated from https://www.loc.gov/standards/iso639-2/ISO-639-2_utf-8.txt
        public static readonly ImmutableDictionary<string, string> Map = new Dictionary<string, string>
        {
            { "alb", "sqi" },
            { "arm", "hye" },
            { "baq", "eus" },
            { "bur", "mya" },
            { "chi", "zho" },
            { "cze", "ces" },
            { "dut", "nld" },
            { "fre", "fra" },
            { "geo", "kat" },
            { "ger", "deu" },
            { "gre", "ell" },
            { "gsw", "deu" },
            { "ice", "isl" },
            { "mac", "mkd" },
            { "mao", "mri" },
            { "may", "msa" },
            { "per", "fas" },
            { "rum", "ron" },
            { "slo", "slk" },
            { "tib", "bod" },
            { "wel", "cym" },
            { "khk", "mon" },
            { "mvf", "mon" }
        }.ToImmutableDictionary();
    }
}

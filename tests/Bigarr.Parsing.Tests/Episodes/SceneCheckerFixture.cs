// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/SceneCheckerFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class SceneCheckerFixture
    {
        [Theory]
        [InlineData("Series.Title.S04E13.Helen.Keller.The.Musical.720p.WEBRip.AAC2.0.H.264-GC")]
        [InlineData("Series.Title.S07E02.720p.WEB-DL.DD5.1.H.264-pcsyndicate")]
        [InlineData("Series.Title.2009.S05E06.Baby.Shower.720p.WEB-DL.DD5.1.H.264-iT00NZ")]
        [InlineData("Series.Title.S04E17.720p.HDTV.X264-DIMENSION")]
        [InlineData("Series.Title.S04.720p.HDTV.X264-DIMENSION")]
        public void should_return_true_for_scene_names(string title)
        {
            SceneChecker.IsSceneTitle(title).Should().BeTrue();
        }

        [Theory]
        [InlineData("S08E05 - Virtual In-Stanity [WEBDL-720p]")]
        [InlineData("S08E05 - Virtual In-Stanity.With.Dots [WEBDL-720p]")]
        [InlineData("Something")]
        [InlineData("86de66b7ef385e2fa56a3e41b98481ea1658bfab")]
        [InlineData("Series.Title.S04E17.720p.HDTV.X264")] // no group
        [InlineData("S04E17.720p.HDTV.X264-DIMENSION")] // no series title
        [InlineData("Series.Title.S04E17-DIMENSION")] // no quality
        [InlineData("Series.Title.720p.HDTV.X264-DIMENSION")] // no episode
        public void should_return_false_for_non_scene_names(string title)
        {
            SceneChecker.IsSceneTitle(title).Should().BeFalse();
        }
    }
}

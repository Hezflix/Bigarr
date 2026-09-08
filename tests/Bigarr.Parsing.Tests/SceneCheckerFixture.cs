// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/SceneCheckerFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Tests
{
    public class SceneCheckerFixture
    {
        [Theory]
        [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")]
        public void should_return_true_for_scene_names(string title)
        {
            SceneChecker.IsSceneTitle(title).Should().BeTrue();
        }

        [Theory]
        [InlineData("S08E05 - Virtual In-Stanity [WEBDL-720p]")]
        [InlineData("S08E05 - Virtual In-Stanity.With.Dots [WEBDL-720p]")]
        [InlineData("Something")]
        [InlineData("86de66b7ef385e2fa56a3e41b98481ea1658bfab")]
        [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-")] // "no group"
        [InlineData("Some.Movie.2..2013.German.DTS.DL-Pate")] // "no quality"
        [InlineData("2013.German.DTS.DL.BluRay.x264-Pate")] // "no movietitle"
        public void should_return_false_for_non_scene_names(string title)
        {
            SceneChecker.IsSceneTitle(title).Should().BeFalse();
        }

        [Theory]
        [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate_", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")] // "underscore at the end"
        [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate.mkv", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")] // "file extension"
        [InlineData("Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate.nzb", "Some.Movie.2..2013.German.DTS.DL.720p.BluRay.x264-Pate")] // "file extension"
        [InlineData("Some.Movie.2..2013.German.DTS.DL.【720p】.BluRay.x264-Pate.nzb", "Some.Movie.2..2013.German.DTS.DL.[720p].BluRay.x264-Pate")] // "brackets"
        public void should_correctly_parse_scene_names(string title, string result)
        {
            SceneChecker.GetSceneTitle(title).Should().Be(result);
        }
    }
}

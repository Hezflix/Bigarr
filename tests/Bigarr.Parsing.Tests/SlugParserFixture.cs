// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/SlugParserFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;



namespace Bigarr.Parsing.Tests
{
    public class SlugParserFixture
    {
        [Theory]
        [InlineData("tèst", "test")]
        [InlineData("têst", "test")]
        [InlineData("tëst", "test")]
        [InlineData("tËst", "test")]
        [InlineData("áccent", "accent")]
        [InlineData("àccent", "accent")]
        [InlineData("âccent", "accent")]
        [InlineData("Äccent", "accent")]
        [InlineData("åccent", "accent")]
        [InlineData("acceñt", "accent")]
        [InlineData("ßtest", "test")]
        [InlineData("œtest", "test")]
        [InlineData("Œtest", "test")]
        [InlineData("Øtest", "test")]
        public void should_replace_accents(string input, string result)
        {
            Parser.ToUrlSlug(input).Should().Be(result);
        }

        [Theory]
        [InlineData("Test'Result")]
        [InlineData("Test$Result")]
        [InlineData("Test(Result")]
        [InlineData("Test)Result")]
        [InlineData("Test*Result")]
        [InlineData("Test?Result")]
        [InlineData("Test/Result")]
        [InlineData("Test=Result")]
        [InlineData("Test\\Result")]
        public void should_replace_special_characters(string input)
        {
            Parser.ToUrlSlug(input).Should().Be("testresult");
        }

        [Theory]
        [InlineData("ThIS IS A MiXeD CaSe SensItIvE ValUe")]
        public void should_lowercase_capitals(string input)
        {
            Parser.ToUrlSlug(input).Should().Be("this-is-a-mixed-case-sensitive-value");
        }

        [Theory]
        [InlineData("test----")]
        [InlineData("test____")]
        [InlineData("test-_--_")]
        public void should_trim_trailing_dashes_and_underscores(string input)
        {
            Parser.ToUrlSlug(input).Should().Be("test");
        }

        [Theory]
        [InlineData("test result")]
        [InlineData("test     result")]
        public void should_replace_spaces_with_dash(string input)
        {
            Parser.ToUrlSlug(input).Should().Be("test-result");
        }

        [Theory]
        [InlineData("test     result", "test-result")]
        [InlineData("test-----result", "test-result")]
        [InlineData("test_____result", "test_result")]
        public void should_replace_double_occurence(string input, string result)
        {
            Parser.ToUrlSlug(input).Should().Be(result);
        }

        [Theory]
        [InlineData("Test'Result")]
        [InlineData("Test$Result")]
        [InlineData("Test(Result")]
        [InlineData("Test)Result")]
        [InlineData("Test*Result")]
        [InlineData("Test?Result")]
        [InlineData("Test/Result")]
        [InlineData("Test=Result")]
        [InlineData("Test\\Result")]
        public void should_replace_special_characters_with_dash_when_enabled(string input)
        {
            Parser.ToUrlSlug(input, true).Should().Be("test-result");
        }

        [Theory]
        [InlineData("Test'Result")]
        [InlineData("Test$Result")]
        [InlineData("Test(Result")]
        [InlineData("Test)Result")]
        [InlineData("Test*Result")]
        [InlineData("Test?Result")]
        [InlineData("Test/Result")]
        [InlineData("Test=Result")]
        [InlineData("Test\\Result")]
        public void should__not_replace_special_characters_with_dash_when_disabled(string input)
        {
            Parser.ToUrlSlug(input, false).Should().Be("testresult");
        }

        [Theory]
        [InlineData("test----", "-_", "test")]
        [InlineData("test____", "-_", "test")]
        [InlineData("test-_-_", "-_", "test")]
        [InlineData("test----", "-", "test")]
        [InlineData("test____", "-", "test____")]
        [InlineData("test-_-_", "-", "test-_-_")]
        [InlineData("test----", "_", "test----")]
        [InlineData("test____", "_", "test")]
        [InlineData("test-_-_", "_", "test-_-")]
        [InlineData("test----", "", "test----")]
        [InlineData("test____", "", "test____")]
        [InlineData("test-_-_", "", "test-_-_")]
        public void should_trim_trailing_dashes_and_underscores_based_on_list(string input, string trimList, string result)
        {
            Parser.ToUrlSlug(input, false, trimList, "").Should().Be(result);
        }

        [Theory]
        [InlineData("test----result", "-_", "test-result")]
        [InlineData("test____result", "-_", "test_result")]
        [InlineData("test_-_-result", "-_", "test-result")]
        [InlineData("test-_-_result", "-_", "test_result")]
        [InlineData("test----result", "-", "test-result")]
        [InlineData("test____result", "-", "test____result")]
        [InlineData("test-_-_result", "-", "test-_-_result")]
        [InlineData("test----result", "_", "test----result")]
        [InlineData("test____result", "_", "test_result")]
        [InlineData("test-_-_result", "_", "test-_-_result")]
        [InlineData("test----result", "", "test----result")]
        [InlineData("test____result", "", "test____result")]
        [InlineData("test-_-_result", "", "test-_-_result")]
        public void should_replace_duplicate_characters_based_on_list(string input, string deduplicateChars, string result)
        {
            Parser.ToUrlSlug(input, false, "", deduplicateChars).Should().Be(result);
        }

        [Fact]
        public void should_handle_null_trim_parameters()
        {
            Parser.ToUrlSlug("test", false, null, "-_").Should().Be("test");
        }

        [Fact]
        public void should_handle_null_dedupe_parameters()
        {
            Parser.ToUrlSlug("test", false, "-_", null).Should().Be("test");
        }

        [Fact]
        public void should_handle_empty_trim_parameters()
        {
            Parser.ToUrlSlug("test", false, "", "-_").Should().Be("test");
        }

        [Fact]
        public void should_handle_empty_dedupe_parameters()
        {
            Parser.ToUrlSlug("test", false, "-_", "").Should().Be("test");
        }
    }
}

// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/NormalizeSeriesTitleFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class NormalizeSeriesTitleFixture
    {
        [Theory]
        [InlineData("Series", "series")]
        [InlineData("Series (2009)", "series2009")]
        [InlineData("Series.2010", "series2010")]
        [InlineData("Series_and_Title_Sonarr", "seriestitlesonarr")]
        public void should_normalize_series_title(string parsedSeriesName, string seriesName)
        {
            var result = parsedSeriesName.CleanSeriesTitle();
            result.Should().Be(seriesName);
        }

        [Theory]
        [InlineData("CaPitAl", "capital")]
        [InlineData("peri.od", "period")]
        [InlineData("this.^&%^**$%@#$!That", "thisthat")]
        [InlineData("test/test", "testtest")]
        [InlineData("90210", "90210")]
        [InlineData("24", "24")]
        [InlineData("Test: Something à Deux", "testsomethingdeux")]
        [InlineData("Parler à", "parlera")]
        [InlineData("Ríkið", "rikid")]
        public void should_remove_special_characters_and_casing(string dirty, string clean)
        {
            var result = dirty.CleanSeriesTitle();
            result.Should().Be(clean);
        }

        [Theory]
        [InlineData("the")]
        [InlineData("and")]
        [InlineData("or")]
        [InlineData("an")]
        [InlineData("of")]
        public void should_remove_common_words_from_middle_of_title(string word)
        {
            var dirtyFormat = new[]
                            {
                                "word.{0}.word",
                                "word {0} word",
                                "word-{0}-word"
                            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, word);
                dirty.CleanSeriesTitle().Should().Be("wordword");
            }
        }

        [Theory]
        [InlineData("the")]
        [InlineData("and")]
        [InlineData("or")]
        [InlineData("an")]
        [InlineData("of")]
        public void should_not_remove_common_words_from_end_of_title(string word)
        {
            var dirtyFormat = new[]
                              {
                                  "word.word.{0}",
                                  "word-word-{0}",
                                  "word-word {0}"
                              };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, word);
                dirty.CleanSeriesTitle().Should().Be("wordword" + word.ToLower());
            }
        }

        [Fact]
        public void should_remove_a_from_middle_of_title()
        {
            var dirtyFormat = new[]
                            {
                                "word.{0}.word",
                                "word {0} word",
                                "word-{0}-word",
                            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, "a");
                dirty.CleanSeriesTitle().Should().Be("wordword");
            }
        }

        [Theory]
        [InlineData("the")]
        [InlineData("and")]
        [InlineData("or")]
        [InlineData("a")]
        [InlineData("an")]
        [InlineData("of")]
        public void should_not_remove_common_words_in_the_middle_of_word(string word)
        {
            var dirtyFormat = new[]
                            {
                                "word.{0}word",
                                "word {0}word",
                                "word-{0}word",
                                "word{0}.word",
                                "word{0}-word",
                                "word{0}-word",
                            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, word);
                dirty.CleanSeriesTitle().Should().Be("word" + word.ToLower() + "word");
            }
        }

        [Theory]
        [InlineData("The Series", "theseries")]
        [InlineData("The Series Show With Sonarr Dev", "theseriesshowwithsonarrdev")]
        [InlineData("The.Series.Show", "theseriesshow")]
        public void should_not_remove_from_the_beginning_of_the_title(string parsedSeriesName, string seriesName)
        {
            var result = parsedSeriesName.CleanSeriesTitle();
            result.Should().Be(seriesName);
        }

        [Theory]
        [InlineData("the")]
        [InlineData("and")]
        [InlineData("or")]
        [InlineData("a")]
        [InlineData("an")]
        [InlineData("of")]
        public void should_not_clean_word_from_beginning_of_string(string word)
        {
            var dirtyFormat = new[]
                            {
                                "{0}.word.word",
                                "{0}-word-word",
                                "{0} word word"
                            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, word);
                dirty.CleanSeriesTitle().Should().Be(word + "wordword");
            }
        }

        [Fact]
        public void should_not_clean_trailing_a()
        {
            "Series Title A".CleanSeriesTitle().Should().Be("seriestitlea");
        }

        [Theory]
        [InlineData("3%", "3percent")]
        [InlineData("Series Top & 100% Coding Developers", "seriestop100percentcodingdevelopers")]
        [InlineData("Series Title What's Your F@%king Deal?!", "seriestitlewhatsyourfkingdeal")]
        public void should_replace_percent_sign_with_percent_following_numbers(string input, string expected)
        {
            input.CleanSeriesTitle().Should().Be(expected);
        }
    }
}

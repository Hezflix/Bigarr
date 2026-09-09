// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/NormalizeTitleFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Tests
{
    public class NormalizeTitleFixture
    {
        [Theory]
        [InlineData("Conan", "conan")]
        [InlineData("Castle (2009)", "castle2009")]
        [InlineData("Parenthood.2010", "parenthood2010")]
        [InlineData("Law_and_Order_SVU", "lawordersvu")]
        public void should_normalize_series_title(string parsedSeriesName, string seriesName)
        {
            var result = parsedSeriesName.CleanMovieTitle();
            result.Should().Be(seriesName);
        }

        [Theory]
        [InlineData("CaPitAl", "capital")]
        [InlineData("peri.od", "period")]
        [InlineData("this.^&%^**$%@#$!That", "thisthat")]
        [InlineData("test/test", "testtest")]
        [InlineData("90210", "90210")]
        [InlineData("24", "24")]
        [InlineData("I'm a cyborg, but that's OK", "imcyborgbutthatsok")]
        [InlineData("Im a cyborg, but thats ok", "imcyborgbutthatsok")]
        [InlineData("Test: Something à Deux", "testsomethingdeux")]
        [InlineData("Parler à", "parlera")]
        public void should_remove_special_characters_and_casing(string dirty, string clean)
        {
            var result = dirty.CleanMovieTitle();
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
                dirty.CleanMovieTitle().Should().Be("wordword");
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
                dirty.CleanMovieTitle().Should().Be("wordword" + word.ToLower());
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
                dirty.CleanMovieTitle().Should().Be("wordword");
            }
        }

        [Fact]
        public void should_not_remove_a_when_at_start_of_acronym()
        {
            var dirtyFormat = new[]
            {
                "word.{0}.N.K.L.E.word",
                "word {0} N K L E word",
                "word-{0}-N-K-L-E-word",
            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, "a");
                dirty.CleanMovieTitle().Should().Be("wordankleword");
            }
        }

        [Fact]
        public void should_not_remove_a_when_at_end_of_acronym()
        {
            var dirtyFormat = new[]
            {
                "word.N.K.L.E.{0}.word",
                "word N K L E {0} word",
                "word-N-K-L-E-{0}-word",
            };

            foreach (var s in dirtyFormat)
            {
                var dirty = string.Format(s, "a");
                dirty.CleanMovieTitle().Should().Be("wordnkleaword");
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
                dirty.CleanMovieTitle().Should().Be("word" + word.ToLower() + "word");
            }
        }

        [Theory]
        [InlineData("The Office", "theoffice")]
        [InlineData("The Tonight Show With Jay Leno", "thetonightshowwithjayleno")]
        [InlineData("The.Daily.Show", "thedailyshow")]
        public void should_not_remove_from_the_beginning_of_the_title(string parsedSeriesName, string seriesName)
        {
            var result = parsedSeriesName.CleanMovieTitle();
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
                dirty.CleanMovieTitle().Should().Be(word + "wordword");
            }
        }

        [Fact]
        public void should_not_clean_trailing_a()
        {
            "Tokyo Ghoul A".CleanMovieTitle().Should().Be("tokyoghoula");
        }
    }
}

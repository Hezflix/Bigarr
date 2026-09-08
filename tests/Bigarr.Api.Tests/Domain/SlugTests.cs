using Bigarr.Domain.Common;

namespace Bigarr.Api.Tests.Domain;

public sealed class SlugTests
{
    [Theory]
    [InlineData("1080p", "1080p")]
    [InlineData("HD - 1080p", "hd-1080p")]
    [InlineData("Director's Cut", "director-s-cut")]
    [InlineData("  Ultra   HD  ", "ultra-hd")]
    [InlineData("Édition Spéciale", "edition-speciale")]
    public void Normalizes_free_text_into_a_slug(string input, string expected)
    {
        var slug = Slug.Normalize(input);

        Assert.Equal(expected, slug);
        Assert.True(Slug.IsValid(slug));
    }

    [Theory]
    [InlineData("4k", true)]
    [InlineData("directors-cut", true)]
    [InlineData("4K", false)]
    [InlineData("-4k", false)]
    [InlineData("a--b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_slugs(string? input, bool expected) => Assert.Equal(expected, Slug.IsValid(input));
}

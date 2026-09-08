using Bigarr.Domain.Auth;

namespace Bigarr.Api.Tests.Domain;

public sealed class CompatViewTests
{
    [Theory]
    [InlineData(null, CompatViewKind.Default, null)]
    [InlineData("", CompatViewKind.Default, null)]
    [InlineData("default", CompatViewKind.Default, null)]
    [InlineData("ALL", CompatViewKind.All, null)]
    [InlineData("version:4k", CompatViewKind.Version, "4k")]
    [InlineData(" version:directors-cut ", CompatViewKind.Version, "directors-cut")]
    public void Parses_valid_values(string? input, CompatViewKind kind, string? versionName)
    {
        Assert.True(CompatView.TryParse(input, out var view));
        Assert.Equal(kind, view.Kind);
        Assert.Equal(versionName, view.VersionName);
    }

    [Theory]
    [InlineData("version:")]
    [InlineData("version:4K")]
    [InlineData("version:Not A Slug")]
    [InlineData("movie")]
    public void Rejects_invalid_values(string input)
    {
        Assert.False(CompatView.TryParse(input, out _));
        Assert.Throws<FormatException>(() => CompatView.Parse(input));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("all")]
    [InlineData("version:4k")]
    public void Round_trips_through_its_string_form(string value)
    {
        Assert.Equal(value, CompatView.Parse(value).ToString());
        Assert.Equal(CompatView.Parse(value), CompatView.Parse(value));
    }
}

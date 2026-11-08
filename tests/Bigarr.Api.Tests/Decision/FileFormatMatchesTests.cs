using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;

namespace Bigarr.Api.Tests.Decision;

/// <summary>A remembered match is reused only for the same file, the same inputs and the same custom formats.</summary>
public sealed class FileFormatMatchesTests
{
    private static readonly QualityDefinition Bluray = new() { Id = 7, Name = "Bluray-1080p", Source = QualitySource.Bluray, Resolution = 1080 };

    private static readonly CustomFormatCalculator Calculator = new(CustomFormatJson.ParseMany("""
        [
          { "name": "Tier", "specifications": [ { "name": "Groups", "implementation": "ReleaseGroupSpecification", "fields": { "value": "^(FLUX)$" } } ] },
          { "name": "x265", "specifications": [ { "name": "x265", "implementation": "ReleaseTitleSpecification", "fields": { "value": "[xh][ ._-]?265" } } ] },
          { "name": "Recent", "specifications": [ { "name": "Recent", "implementation": "YearSpecification", "fields": { "min": 2015, "max": 2030 } } ] }
        ]
        """).Select((d, i) => d with { Id = i + 1 }));

    [Fact]
    public void The_same_inputs_reuse_the_match_and_any_change_matches_again()
    {
        var matches = new FileFormatMatches();
        var input = Input("Heat.1995.1080p.BluRay.x264-FLUX", "FLUX", 1995);

        var first = matches.Match(1, input, Bluray, Calculator, "formats-a");
        Assert.Equal([1], first);
        // Equal inputs read again from the database are new instances: still a hit.
        Assert.Same(first, matches.Match(1, Input("Heat.1995.1080p.BluRay.x264-FLUX", "FLUX", 1995), Bluray, Calculator, "formats-a"));

        // Another release name, another year, other formats, another file: matched again.
        Assert.Equal([2], matches.Match(1, Input("Heat.1995.1080p.BluRay.x265-GRP", "GRP", 1995), Bluray, Calculator, "formats-a"));
        Assert.Equal([2, 3], matches.Match(1, Input("Heat.1995.1080p.BluRay.x265-GRP", "GRP", 2016), Bluray, Calculator, "formats-a"));
        var again = matches.Match(1, Input("Heat.1995.1080p.BluRay.x265-GRP", "GRP", 2016), Bluray, Calculator, "formats-b");
        Assert.Equal([2, 3], again);
        Assert.NotSame(again, matches.Match(1, Input("Heat.1995.1080p.BluRay.x265-GRP", "GRP", 2016) with { Languages = ["French"] }, Bluray, Calculator, "formats-b"));
        Assert.Equal([1], matches.Match(2, input, Bluray, Calculator, "formats-b"));
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Retain_forgets_the_files_no_longer_judged()
    {
        var matches = new FileFormatMatches();
        foreach (var id in new[] { 1, 2, 3 })
        {
            matches.Match(id, Input($"Film.{id}.2020.1080p.BluRay.x264-FLUX", "FLUX", 2020), Bluray, Calculator, "formats");
        }

        matches.Retain(new HashSet<int> { 2 });

        Assert.Equal(1, matches.Count);
    }

    private static FileFormatInput Input(string release, string group, int year) =>
        new(release, release + ".mkv", 10_000_000_000, IndexerFlags.None, Bluray.Id, ["English"], null, group, year, "en");
}

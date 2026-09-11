using System.Reflection;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Parsing;
using Bigarr.Parsing.Languages;

namespace Bigarr.Api.Tests.Decision;

/// <summary>The TRaSH guide custom formats embedded in the test assembly, compiled once, with ids in file order.</summary>
public static class TrashFixtures
{
    public static readonly IReadOnlyList<(string File, CustomFormatDefinition Definition)> All = Load();

    public static readonly CustomFormatCalculator Calculator = new(All.Select(f => f.Definition));

    public static CustomFormatDefinition Get(string name) => All.Single(f => f.Definition.Name == name).Definition;

    public static string Json(string file)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Trash.{file}.json") ?? throw new InvalidOperationException($"Fixture {file} not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Formats matched by a release title, for a film in <paramref name="originalLanguage"/> (ISO 639-1).</summary>
    public static IReadOnlyList<string> Match(string title, long size = 0, IndexerFlags flags = IndexerFlags.None, int? year = 1999, string originalLanguage = "en")
    {
        var parsed = Parser.ParseMovieTitle(title);
        var input = CustomFormatInput.FromParsed(parsed, null, size, flags, year, CustomFormatInput.LanguageFromIsoCode(originalLanguage));
        return Calculator.Match(input).Select(d => d.Name).ToList();
    }

    private static List<(string, CustomFormatDefinition)> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var result = new List<(string, CustomFormatDefinition)>();
        var id = 0;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Trash.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            var file = resource["Trash.".Length..^".json".Length];
            var definition = CustomFormatJson.ParseMany(Json(file)).Single() with { Id = ++id };
            result.Add((file, definition));
        }

        return result;
    }
}

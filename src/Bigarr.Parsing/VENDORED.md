# Code vendored from Radarr — `Bigarr.Parsing`

This document is the **sync contract** between Bigarr and Radarr. It lists exactly what was copied,
from which version, and which modifications were applied. Every quarterly comparison with upstream
must start from this file.

## Source

| | |
|---|---|
| Upstream project | [Radarr/Radarr](https://github.com/Radarr/Radarr) |
| License | GPL-3.0 (Bigarr is GPL-3.0 as well — compliant reuse) |
| Tag | `v6.3.0.10514` (latest stable, non-prerelease) |
| Commit SHA | `7827e5368947f158ad06f757334f5cde6c406411` |
| Commit date | 2026-07-12 |
| Vendoring date | 2026-09-08 |

Checkout command for the next diff:

```bash
git clone --depth 1 --branch v6.3.0.10514 https://github.com/Radarr/Radarr.git /tmp/radarr
```

## Scope

Only the **release-name parser** is vendored: turning a release title or a file path into a
`ParsedMovieInfo` (title, year, edition, group, quality, languages, IMDb/TMDb id, hardcoded
subtitles). Anything that assumes a database, a movie repository or the rest of `NzbDrone.Core`
is excluded (see "Exclusions").

## Public entry points

| API | Role |
|---|---|
| `Parser.ParseMovieTitle(string title, bool isDir = false)` | parses a release name → `ParsedMovieInfo` (`null` when not parsable) |
| `Parser.ParseMoviePath(string path)` | parses a file path (falls back to the folder name) |
| `Parser.ParseEdition`, `ParseImdbId`, `ParseTmdbId`, `ParseHardcodeSubs`, `HasMultipleLanguages` | single-purpose extractions |
| `Parser.NormalizeImdbId`, `ToUrlSlug`, `NormalizeMovieTitle`, `NormalizeTitle`, `ReplaceGermanUmlauts` | normalizations |
| `Parser.CleanMovieTitle()`, `SimplifyReleaseTitle()` | extension methods on `string` |
| `ReleaseGroupParser.ParseReleaseGroup(string title)` | release group |
| `QualityParser.ParseQuality(string name)` / `ParseQualityName(string name)` | → `QualityModel` |
| `LanguageParser.ParseLanguages(string title)` | → `List<Language>` |
| `LanguageParser.ParseSubtitleLanguage`, `ParseLanguageTags`, `ParseSubtitleLanguageInformation` | subtitles |
| `SceneChecker.IsSceneTitle` / `GetSceneTitle` | "scene" naming detection |
| `Quality.All`, `Quality.AllLookup`, `Quality.FindById`, `Quality.DefaultQualityDefinitions` | quality reference data |
| `Language.All`, `Language.FindById` | language reference data |
| `QualityFinder.FindBySourceAndResolution` | source + resolution + modifier → `Quality` |

> **`Quality` and `Language` ids are identical to Radarr's** (verified: 30 qualities, ids 0 to 31
> minus 11/13, and 60 languages). Bigarr's `QualityDefinition` table must be seeded from
> `Quality.DefaultQualityDefinitions` without re-indexing.

## Runtime prerequisite — do not forget

`Parser.CleanMovieTitle()` and `Parser.ToUrlSlug()` strip diacritics through
`string.Normalize(NormalizationForm.FormD)`. In **globalization-invariant mode**, `Normalize()` is a
no-op: "Carnivàle" never becomes "carnivale" and accented titles silently stop matching.

The repository therefore does **not** enable `InvariantGlobalization` (removed from
`Directory.Build.props` on 2026-09-08: Bigarr needs ICU for titles, sorting and slugs), and the Docker
image installs ICU (`icu-libs`, `icu-data-full`) with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`,
since the `aspnet:alpine` image enables invariant mode by default. If either is dropped, 12 upstream
test cases become wrong in production without any visible error.

## Vendored files (27 files)

Each file carries a header with the tag, SHA, original path and license.

### Parser — `Bigarr.Parsing` (was `NzbDrone.Core.Parser`)

| Bigarr file | Original path |
|---|---|
| `Parser.cs` | `src/NzbDrone.Core/Parser/Parser.cs` |
| `ParserCommon.cs` | `src/NzbDrone.Core/Parser/ParserCommon.cs` |
| `RegexReplace.cs` | `src/NzbDrone.Core/Parser/RegexReplace.cs` |
| `ReleaseGroupParser.cs` | `src/NzbDrone.Core/Parser/ReleaseGroupParser.cs` |
| `QualityParser.cs` | `src/NzbDrone.Core/Parser/QualityParser.cs` (contains the `Resolution` enum) |
| `LanguageParser.cs` | `src/NzbDrone.Core/Parser/LanguageParser.cs` |
| `IsoLanguage.cs` | `src/NzbDrone.Core/Parser/IsoLanguage.cs` |
| `IsoLanguages.cs` | `src/NzbDrone.Core/Parser/IsoLanguages.cs` |
| `InvalidDateException.cs` | `src/NzbDrone.Core/Parser/InvalidDateException.cs` |
| `SceneChecker.cs` | `src/NzbDrone.Core/Parser/SceneChecker.cs` |

### Models — `Bigarr.Parsing.Model` (was `NzbDrone.Core.Parser.Model`)

| Bigarr file | Original path |
|---|---|
| `Model/ParsedMovieInfo.cs` | `src/NzbDrone.Core/Parser/Model/ParsedMovieInfo.cs` |
| `Model/SubtitleTitleInfo.cs` | `src/NzbDrone.Core/Parser/Model/SubtitleTitleInfo.cs` |

### Qualities — `Bigarr.Parsing.Qualities` (was `NzbDrone.Core.Qualities`)

| Bigarr file | Original path |
|---|---|
| `Qualities/Quality.cs` | `src/NzbDrone.Core/Qualities/Quality.cs` |
| `Qualities/QualityModel.cs` | `src/NzbDrone.Core/Qualities/QualityModel.cs` |
| `Qualities/QualityDefinition.cs` | `src/NzbDrone.Core/Qualities/QualityDefinition.cs` |
| `Qualities/QualityDetectionSource.cs` | `src/NzbDrone.Core/Qualities/QualityDetectionSource.cs` |
| `Qualities/QualityFinder.cs` | `src/NzbDrone.Core/Qualities/QualityFinder.cs` |
| `Qualities/QualitySource.cs` | `src/NzbDrone.Core/Qualities/QualitySource.cs` (`QualitySource` enum) |
| `Qualities/Modifier.cs` | `src/NzbDrone.Core/Qualities/Modifier.cs` |
| `Qualities/Revision.cs` | `src/NzbDrone.Core/Qualities/Revision.cs` |

> `Resolution` has no dedicated file upstream: the enum is declared at the end of
> `Parser/QualityParser.cs` and was kept in the same place.

### Languages — `Bigarr.Parsing.Languages` (was `NzbDrone.Core.Languages`)

| Bigarr file | Original path |
|---|---|
| `Languages/Language.cs` | `src/NzbDrone.Core/Languages/Language.cs` |

### Internal helpers — `Bigarr.Parsing.Internal`

| Bigarr file | Original path | Trimming |
|---|---|---|
| `Internal/StringExtensions.cs` | `src/NzbDrone.Common/Extensions/StringExtensions.cs` | 4 members kept out of 25: `RemoveAccent`, `IsNullOrWhiteSpace`, `IsNotNullOrWhiteSpace`, `ContainsIgnoreCase` |
| `Internal/EnumerableExtensions.cs` | `src/NzbDrone.Common/Extensions/IEnumerableExtensions.cs` | 1 member kept: `Empty<T>` |
| `Internal/PathExtensions.cs` | `src/NzbDrone.Common/Extensions/PathExtensions.cs` | 2 members kept: `GetPathExtension`, `ContainsInvalidPathChars` |
| `Internal/FileExtensions.cs` | `src/NzbDrone.Core/MediaFiles/FileExtensions.cs` | only `RemoveFileExtension` kept (the archive/dangerous/executable sets belong to import) |
| `Internal/MediaFileExtensions.cs` | `src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs` | whole file |
| `Internal/Iso639BTMap.cs` | `src/NzbDrone.Core/Organizer/FileNameBuilder.cs` | **only** the `Iso639BTMap` field |

### File added by Bigarr (not vendored)

| File | Role |
|---|---|
| `Internal/ParserLog.cs` | replaces NLog. See "Logging". |

## Modifications applied

All modifications are mechanical. **No regex was touched.**

### 1. Namespaces

| Upstream | Bigarr |
|---|---|
| `NzbDrone.Core.Parser` | `Bigarr.Parsing` |
| `NzbDrone.Core.Parser.Model` | `Bigarr.Parsing.Model` |
| `NzbDrone.Core.Qualities` | `Bigarr.Parsing.Qualities` |
| `NzbDrone.Core.Languages` | `Bigarr.Parsing.Languages` |
| `NzbDrone.Common.Extensions`, `NzbDrone.Core.MediaFiles`, `NzbDrone.Core.Organizer` | `Bigarr.Parsing.Internal` |

The matching `using` directives were rewritten. Since two upstream namespaces merge into
`Bigarr.Parsing.Internal`, the duplicated `using` was removed in `Parser.cs` and `QualityParser.cs`.

### 2. Persistence base classes and interfaces removed

| Type | Upstream | Bigarr |
|---|---|---|
| `Quality` | `: IEmbeddedDocument, IEquatable<Quality>` | `: IEquatable<Quality>` |
| `QualityModel` | `: IEmbeddedDocument, IEquatable<QualityModel>` | `: IEquatable<QualityModel>` |
| `Language` | `: IEmbeddedDocument, IEquatable<Language>` | `: IEquatable<Language>` |
| `QualityDefinition` | `: ModelBase` | no base; a `public int Id { get; set; }` was added (the only member `ModelBase` provided) |
| `InvalidDateException` | `: NzbDroneException` | `: Exception`, with `base(string.Format(message, args))` to keep the `params object[]` overload |

`using NzbDrone.Core.Datastore;` and `using NzbDrone.Common.Exceptions;` removed.

### 3. Logging: NLog → `ParserLog`

NLog is not a Bigarr dependency. `Microsoft.Extensions.Logging.Abstractions` was **not** chosen: its
message templates interpret braces, and release titles contain them (`Movie Name (2016) {tmdb-43074}`),
and the NLog signatures used upstream (`Trace(object)`, `Debug(Exception, string, params object[])`)
do not map one to one.

`Internal/ParserLog.cs` therefore provides a minimal logger with NLog-identical signatures, which
leaves **every call site unchanged**. Only the declaration line was modified in the 4 files concerned
(`Parser.cs`, `QualityParser.cs`, `LanguageParser.cs`, `QualityFinder.cs`):

```diff
-private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Parser));
+private static readonly ParserLog Logger = ParserLog.For(typeof(Parser));
```

`ParserLog` is **silent by default**. To wire logging, assign once at startup:

```csharp
ParserLog.Sink = (level, name, message, ex) => logger.Log(/* ... */);
```

`src/Bigarr.Parsing` references no NuGet package.

### 4. `Iso639BTMap` extracted from `FileNameBuilder`

`IsoLanguages.Find` needed the ISO 639-2/B → 639-2/T mapping table, declared upstream in
`Organizer/FileNameBuilder`, a class that pulls the whole `Core` dependency graph (naming config,
quality service, translations, custom formats). Only the field was copied, into
`Internal/Iso639BTMap`. Single call-site change:

```diff
-if (FileNameBuilder.Iso639BTMap.TryGetValue(langCode, out var mapped))
+if (Iso639BTMap.Map.TryGetValue(langCode, out var mapped))
```

### 5. Visibility

The `Bigarr.Parsing.Internal` helpers were made `internal` (`StringExtensions`,
`EnumerableExtensions`, `PathExtensions`, `FileExtensions`, `MediaFileExtensions`, `Iso639BTMap`).
`ParserLog` stays public: it is the logging hook. All parser, model, quality and language types keep
their upstream visibility.

### 6. `NoWarn` — `src/Bigarr.Parsing/Bigarr.Parsing.csproj`

The upstream code predates nullable reference types; `Directory.Build.props` enables `Nullable`.
Annotating the vendored files would complicate the quarterly diff without changing behavior, so the
corresponding diagnostics are disabled **at project level** rather than "fixed" in the files:

- `CS8600`–`CS8625`, `CS8629`, `CS8714` — nullability analysis
- `CS8632` — `?` annotation outside a `#nullable` context
- `CS8765`, `CS8767` — nullability of `Equals` / `CompareTo` parameters

With these exclusions, `dotnet build src/Bigarr.Parsing` ends with **0 warnings, 0 errors**. Every
other warning stays active: a new warning after an upstream bump is a signal.

### 7. Globalization

No change in the `.csproj` files: the constraint is carried by the repository
(`Directory.Build.props`) and by the Docker image, see "Runtime prerequisite".

## Exclusions

| Upstream item | Reason |
|---|---|
| `Parser/ParsingService.cs` | depends on `IMovieService`, `IMovieTranslationService`, the cache and the database |
| `Parser/Model/{LocalMovie,RemoteMovie,ReleaseInfo,TorrentInfo,GrabbedReleaseInfo,FindMovieResult,IndexerFlags,SeriesTitleInfo}.cs` | indexer / download / import models, outside the parser |
| `Parser/RomanNumerals/*` | not used by the release parser (serves title search) |
| `Parser/Augmenters/*`, `CustomFormats/*` | decision pipeline, depends on the database |
| `Qualities/{QualityDefinitionService,QualityDefinitionRepository,QualityDefinitionLimits,QualitiesBelowCutoff,QualityModelComparer,ProperDownloadTypes}.cs`, `Qualities/Commands/*` | persistence, configuration and comparison — belong to `Bigarr.Decision` / `Bigarr.Domain` |
| `Languages/{LanguageExtensions,LanguagesComparer,LanguageFieldConverter,OriginalLanguageFieldConverter,RealLanguageFieldConverter}.cs` | profiles, comparison and config field converters |
| `Datastore/IEmbeddedDocument`, `Datastore/ModelBase` | persistence contract (see modification 2) |
| `Common/Instrumentation/*` (NLog) | see modification 3 |
| `Common/EnvironmentInfo/OsInfo` | replaced by `OperatingSystem.IsWindows()` on the test side |

## Ported tests

Source: `src/NzbDrone.Core.Test/ParserTests/`. NUnit → xUnit v2 conversion.

**14 fixtures ported, 1065 test cases, 1065 passing, 0 skipped.**

| Fixture | Cases | Covers |
|---|---|---|
| `ParserFixture` | 111 | titles, alternative titles (AKA), German/anime releases, year, TMDb, IMDb, hardcoded subs |
| `QualityParserFixture` | 287 | every quality, detection sources, repack/proper |
| `LanguageParserFixture` | 195 | ~50 languages, subtitles, German `DL`/`ML` tags |
| `ReleaseGroupParserFixture` | 152 | standard groups, exception groups, anime, stray suffixes |
| `SlugParserFixture` | 79 | `ToUrlSlug` (accents, trimming, de-duplication) |
| `EditionParserFixture` | 48 | editions (Director's Cut, Extended, IMAX…) |
| `IsoLanguagesFixture` | 43 | ISO 639-1/2 code resolution |
| `NormalizeTitleFixture` | 43 | `CleanMovieTitle` (common words, acronyms) |
| `ExtendedQualityParserRegex` | 30 | `REAL`/`PROPER`, version, UltraHD |
| `CrapParserFixture` | 26 | rejection of hashes, MD5, random strings |
| `UrlFixture` | 22 | site prefixes/suffixes in names |
| `SceneCheckerFixture` | 12 | "scene" naming detection |
| `HashedReleaseFixture` | 11 | `ParseMoviePath` on hashed releases |
| `AnimeVersionFixture` | 6 | anime `v2`/`v3` versions |

### Conversions applied to the tests

- `[TestCase(...)]` → `[InlineData(...)]`, plus one `[Theory]` at the top of each data block
- `[Test]` → `[Fact]`
- `[TestCaseSource(nameof(X))]` → `[MemberData(nameof(X))]`, and `public static object[] X` →
  `public static IEnumerable<object[]> X = new[]` (`HashedReleaseFixture`, `QualityParserFixture`)
- `[TestFixture]` and the `CoreTest` base class removed; `[SetUp] Setup()` blocks (empty upstream)
  removed
- `TestCase`'s `Description = "..."`: `InlineData` has no equivalent, the upstream note is kept as an
  end-of-line comment
- `ExceptionVerification.IgnoreWarns()` removed (NUnit/NLog plumbing)
- `Parser.Parser.X` → `Parser.X`, `Parser.ReleaseGroupParser.X` → `ReleaseGroupParser.X`
- `AsOsAgnostic()`: re-implemented in `tests/Bigarr.Parsing.Tests/TestPathExtensions.cs` from
  `src/NzbDrone.Test.Common/StringExtensions.cs`, with `OsInfo.IsNotWindows` →
  `!OperatingSystem.IsWindows()`
- `FluentAssertions 7.2.0` **exactly** (Apache-2.0): 8.x changed license. Upstream assertions
  (`Should().Be()`, `AssertionScope`, `BeEquivalentTo`) are kept unchanged.
- Test project `NoWarn`: `xUnit1012` (upstream passes `null` to `string` parameters), `xUnit1026`
  (`should_parse_ultrahd_from_title` receives a `version` it never asserts), plus the nullability
  diagnostics.

### Removed test case

- `EditionParserFixture.should_parse_edition`: upstream repeats
  `[TestCase("Movie Title 2012 Directors Cut", "Directors Cut")]` verbatim (twice in the same block).
  xUnit rejects duplicated `InlineData` (`xUnit1025`); the duplicate was removed. **No coverage
  lost.**

### Fixtures not ported

| Upstream fixture | Reason |
|---|---|
| `ParsingServiceTests/GetMovieFixture.cs` | tests `ParsingService`, excluded (database + `IMovieService`) |
| `ParsingServiceTests/MapFixture.cs` | same, plus Moq mocks |
| `ParsingServiceTests/ParseQualityDefinitionFixture.cs` | tests `IQualityDefinitionService`, excluded |
| `RomanNumeralTests/RomanNumeralConversionFixture.cs` | tests `Parser/RomanNumerals`, excluded; also needs Newtonsoft.Json and a JSON data file |

## Quarterly diff procedure

1. Find the latest stable tag: `gh release list -R Radarr/Radarr --exclude-pre-releases -L 1`
2. Clone that tag and diff every file of the "Vendored files" table against its original path. The
   only expected differences are those listed under "Modifications applied" (header, namespaces,
   `using`, logger declaration line, base classes, `Iso639BTMap.Map` call).
3. Diff the 14 fixtures as well: a new upstream `TestCase` signals a fixed regex.
4. Update tag / SHA / date at the top of this file.
5. `dotnet build src/Bigarr.Parsing` must stay at 0 warnings; `dotnet test tests/Bigarr.Parsing.Tests`
   must stay 100% green.
6. If a `Quality` or `Language` id changes upstream: **do not follow blindly**, Bigarr's
   `QualityDefinition` table is seeded from these ids and a migration would be needed.

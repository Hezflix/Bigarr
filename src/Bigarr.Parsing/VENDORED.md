# Code vendorisé depuis Radarr — `Bigarr.Parsing`

Ce document est le **contrat de reprise** entre Bigarr et Radarr. Il liste précisément ce qui a été
copié, depuis quelle version, et quelles modifications ont été appliquées. Toute comparaison
trimestrielle avec l'amont doit partir de ce fichier.

## Source

| | |
|---|---|
| Projet amont | [Radarr/Radarr](https://github.com/Radarr/Radarr) |
| Licence | GPL-3.0 (Bigarr est également GPL-3.0 — reprise conforme) |
| Tag | `v6.3.0.10514` (dernière release stable, non-prerelease) |
| Commit SHA | `7827e5368947f158ad06f757334f5cde6c406411` |
| Date du commit | 2026-07-12 |
| Date de la vendorisation | 2026-09-08 |

Commande de récupération pour le prochain diff :

```bash
git clone --depth 1 --branch v6.3.0.10514 https://github.com/Radarr/Radarr.git /tmp/radarr
```

## Périmètre

Seul le **parseur de noms de release** est reprisé : analyse d'un titre de release ou d'un chemin de
fichier vers un `ParsedMovieInfo` (titre, année, édition, groupe, qualité, langues, IMDb/TMDb id,
sous-titres hardcodés). Tout ce qui suppose une base de données, un dépôt de films ou le reste de
`NzbDrone.Core` est exclu (voir « Exclusions »).

## Points d'entrée publics

| API | Rôle |
|---|---|
| `Parser.ParseMovieTitle(string title, bool isDir = false)` | parse un nom de release → `ParsedMovieInfo` (`null` si non parsable) |
| `Parser.ParseMoviePath(string path)` | parse un chemin de fichier (retombe sur le nom du dossier) |
| `Parser.ParseEdition`, `ParseImdbId`, `ParseTmdbId`, `ParseHardcodeSubs`, `HasMultipleLanguages` | extractions unitaires |
| `Parser.NormalizeImdbId`, `ToUrlSlug`, `NormalizeMovieTitle`, `NormalizeTitle`, `ReplaceGermanUmlauts` | normalisations |
| `Parser.CleanMovieTitle()`, `SimplifyReleaseTitle()` | méthodes d'extension sur `string` |
| `ReleaseGroupParser.ParseReleaseGroup(string title)` | groupe de release |
| `QualityParser.ParseQuality(string name)` / `ParseQualityName(string name)` | → `QualityModel` |
| `LanguageParser.ParseLanguages(string title)` | → `List<Language>` |
| `LanguageParser.ParseSubtitleLanguage`, `ParseLanguageTags`, `ParseSubtitleLanguageInformation` | sous-titres |
| `SceneChecker.IsSceneTitle` / `GetSceneTitle` | détection de nommage « scene » |
| `Quality.All`, `Quality.AllLookup`, `Quality.FindById`, `Quality.DefaultQualityDefinitions` | référentiel qualité |
| `Language.All`, `Language.FindById` | référentiel langues |
| `QualityFinder.FindBySourceAndResolution` | résolution source+résolution+modifier → `Quality` |

> **Les ids de `Quality` et de `Language` sont identiques à ceux de Radarr** (vérifié : 30 qualités,
> ids 0 à 31 hors 11/13, et 60 langues). La table `QualityDefinition` de Bigarr doit être semée
> depuis `Quality.DefaultQualityDefinitions` sans réindexation.

## Prérequis d'exécution — à ne pas oublier

`Parser.CleanMovieTitle()` et `Parser.ToUrlSlug()` retirent les diacritiques via
`string.Normalize(NormalizationForm.FormD)`. En **mode globalization-invariant**, `Normalize()` est
un no-op : « Carnivàle » ne devient jamais « carnivale » et les titres accentués cessent
silencieusement de matcher.

Le dépôt n'active donc **pas** `InvariantGlobalization` (retiré de `Directory.Build.props` le
2026-09-08 : Bigarr a besoin d'ICU pour les titres, le tri et les slugs), et l'image Docker installe
ICU (`icu-libs`, `icu-data-full`) avec `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`, l'image
`aspnet:alpine` l'activant par défaut. Si l'un des deux saute, 12 cas de test amont deviennent faux
en production sans aucune erreur visible.

## Fichiers vendorisés (27 fichiers)

Chaque fichier porte un en-tête indiquant tag, SHA, chemin d'origine et licence.

### Parseur — `Bigarr.Parsing` (ex `NzbDrone.Core.Parser`)

| Fichier Bigarr | Chemin d'origine |
|---|---|
| `Parser.cs` | `src/NzbDrone.Core/Parser/Parser.cs` |
| `ParserCommon.cs` | `src/NzbDrone.Core/Parser/ParserCommon.cs` |
| `RegexReplace.cs` | `src/NzbDrone.Core/Parser/RegexReplace.cs` |
| `ReleaseGroupParser.cs` | `src/NzbDrone.Core/Parser/ReleaseGroupParser.cs` |
| `QualityParser.cs` | `src/NzbDrone.Core/Parser/QualityParser.cs` (contient l'enum `Resolution`) |
| `LanguageParser.cs` | `src/NzbDrone.Core/Parser/LanguageParser.cs` |
| `IsoLanguage.cs` | `src/NzbDrone.Core/Parser/IsoLanguage.cs` |
| `IsoLanguages.cs` | `src/NzbDrone.Core/Parser/IsoLanguages.cs` |
| `InvalidDateException.cs` | `src/NzbDrone.Core/Parser/InvalidDateException.cs` |
| `SceneChecker.cs` | `src/NzbDrone.Core/Parser/SceneChecker.cs` |

### Modèles — `Bigarr.Parsing.Model` (ex `NzbDrone.Core.Parser.Model`)

| Fichier Bigarr | Chemin d'origine |
|---|---|
| `Model/ParsedMovieInfo.cs` | `src/NzbDrone.Core/Parser/Model/ParsedMovieInfo.cs` |
| `Model/SubtitleTitleInfo.cs` | `src/NzbDrone.Core/Parser/Model/SubtitleTitleInfo.cs` |

### Qualités — `Bigarr.Parsing.Qualities` (ex `NzbDrone.Core.Qualities`)

| Fichier Bigarr | Chemin d'origine |
|---|---|
| `Qualities/Quality.cs` | `src/NzbDrone.Core/Qualities/Quality.cs` |
| `Qualities/QualityModel.cs` | `src/NzbDrone.Core/Qualities/QualityModel.cs` |
| `Qualities/QualityDefinition.cs` | `src/NzbDrone.Core/Qualities/QualityDefinition.cs` |
| `Qualities/QualityDetectionSource.cs` | `src/NzbDrone.Core/Qualities/QualityDetectionSource.cs` |
| `Qualities/QualityFinder.cs` | `src/NzbDrone.Core/Qualities/QualityFinder.cs` |
| `Qualities/QualitySource.cs` | `src/NzbDrone.Core/Qualities/QualitySource.cs` (enum `QualitySource`) |
| `Qualities/Modifier.cs` | `src/NzbDrone.Core/Qualities/Modifier.cs` |
| `Qualities/Revision.cs` | `src/NzbDrone.Core/Qualities/Revision.cs` |

> `Resolution` n'a pas de fichier dédié en amont : l'enum est déclaré en fin de
> `Parser/QualityParser.cs` et a été conservé au même endroit.

### Langues — `Bigarr.Parsing.Languages` (ex `NzbDrone.Core.Languages`)

| Fichier Bigarr | Chemin d'origine |
|---|---|
| `Languages/Language.cs` | `src/NzbDrone.Core/Languages/Language.cs` |

### Helpers internes — `Bigarr.Parsing.Internal`

| Fichier Bigarr | Chemin d'origine | Réduction |
|---|---|---|
| `Internal/StringExtensions.cs` | `src/NzbDrone.Common/Extensions/StringExtensions.cs` | 4 membres conservés sur 25 : `RemoveAccent`, `IsNullOrWhiteSpace`, `IsNotNullOrWhiteSpace`, `ContainsIgnoreCase` |
| `Internal/EnumerableExtensions.cs` | `src/NzbDrone.Common/Extensions/IEnumerableExtensions.cs` | 1 membre conservé : `Empty<T>` |
| `Internal/PathExtensions.cs` | `src/NzbDrone.Common/Extensions/PathExtensions.cs` | 2 membres conservés : `GetPathExtension`, `ContainsInvalidPathChars` |
| `Internal/FileExtensions.cs` | `src/NzbDrone.Core/MediaFiles/FileExtensions.cs` | seul `RemoveFileExtension` conservé (les sets archive/dangerous/executable relèvent de l'import) |
| `Internal/MediaFileExtensions.cs` | `src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs` | intégral |
| `Internal/Iso639BTMap.cs` | `src/NzbDrone.Core/Organizer/FileNameBuilder.cs` | **uniquement** le champ `Iso639BTMap` |

### Fichier ajouté par Bigarr (non vendorisé)

| Fichier | Rôle |
|---|---|
| `Internal/ParserLog.cs` | remplace NLog. Voir « Journalisation ». |

## Modifications appliquées

Toutes les modifications sont mécaniques. **Aucune regex n'a été touchée.**

### 1. Namespaces

| Amont | Bigarr |
|---|---|
| `NzbDrone.Core.Parser` | `Bigarr.Parsing` |
| `NzbDrone.Core.Parser.Model` | `Bigarr.Parsing.Model` |
| `NzbDrone.Core.Qualities` | `Bigarr.Parsing.Qualities` |
| `NzbDrone.Core.Languages` | `Bigarr.Parsing.Languages` |
| `NzbDrone.Common.Extensions`, `NzbDrone.Core.MediaFiles`, `NzbDrone.Core.Organizer` | `Bigarr.Parsing.Internal` |

Les `using` correspondants ont été réécrits. Comme deux namespaces amont fusionnent vers
`Bigarr.Parsing.Internal`, le doublon de `using` a été supprimé dans `Parser.cs` et
`QualityParser.cs`.

### 2. Classes de base et interfaces de persistance retirées

| Type | Amont | Bigarr |
|---|---|---|
| `Quality` | `: IEmbeddedDocument, IEquatable<Quality>` | `: IEquatable<Quality>` |
| `QualityModel` | `: IEmbeddedDocument, IEquatable<QualityModel>` | `: IEquatable<QualityModel>` |
| `Language` | `: IEmbeddedDocument, IEquatable<Language>` | `: IEquatable<Language>` |
| `QualityDefinition` | `: ModelBase` | plus de base ; ajout d'un `public int Id { get; set; }` (seul membre que `ModelBase` fournissait) |
| `InvalidDateException` | `: NzbDroneException` | `: Exception`, avec `base(string.Format(message, args))` pour préserver la surcharge `params object[]` |

`using NzbDrone.Core.Datastore;` et `using NzbDrone.Common.Exceptions;` supprimés.

### 3. Journalisation : NLog → `ParserLog`

NLog n'est pas une dépendance de Bigarr. `Microsoft.Extensions.Logging.Abstractions` n'a **pas** été
retenu : ses gabarits de message interprètent les accolades, or les titres de release en contiennent
(`Movie Name (2016) {tmdb-43074}`), et les signatures NLog utilisées en amont (`Trace(object)`,
`Debug(Exception, string, params object[])`) ne correspondent pas une pour une.

`Internal/ParserLog.cs` fournit donc un logger minimal aux signatures identiques à NLog, ce qui
laisse **tous les sites d'appel inchangés**. Seule la ligne de déclaration a été modifiée dans les
4 fichiers concernés (`Parser.cs`, `QualityParser.cs`, `LanguageParser.cs`, `QualityFinder.cs`) :

```diff
-private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(Parser));
+private static readonly ParserLog Logger = ParserLog.For(typeof(Parser));
```

`ParserLog` est **silencieux par défaut**. Pour brancher la journalisation, affecter une fois au
démarrage :

```csharp
ParserLog.Sink = (level, name, message, ex) => logger.Log(/* ... */);
```

Aucun paquet NuGet n'est référencé par `src/Bigarr.Parsing`.

### 4. `Iso639BTMap` extrait de `FileNameBuilder`

`IsoLanguages.Find` avait besoin de la table de correspondance ISO 639-2/B → 639-2/T, déclarée en
amont dans `Organizer/FileNameBuilder`, une classe qui tire tout le graphe de dépendances de
`Core` (config de nommage, service de qualité, traductions, custom formats). Seul le champ a été
copié, dans `Internal/Iso639BTMap`. Unique changement d'appel :

```diff
-if (FileNameBuilder.Iso639BTMap.TryGetValue(langCode, out var mapped))
+if (Iso639BTMap.Map.TryGetValue(langCode, out var mapped))
```

### 5. Visibilité

Les helpers de `Bigarr.Parsing.Internal` sont passés `internal` (`StringExtensions`,
`EnumerableExtensions`, `PathExtensions`, `FileExtensions`, `MediaFileExtensions`, `Iso639BTMap`).
`ParserLog` reste public : c'est le point de branchement de la journalisation. Tous les types du
parseur, des modèles, des qualités et des langues gardent la visibilité de l'amont.

### 6. `NoWarn` — `src/Bigarr.Parsing/Bigarr.Parsing.csproj`

Le code amont est antérieur aux types référence nullables ; `Directory.Build.props` active
`Nullable`. Annoter les fichiers vendorisés compliquerait le diff trimestriel sans changer le
comportement, donc les diagnostics correspondants sont désactivés **au niveau du projet** plutôt que
« corrigés » dans les fichiers :

- `CS8600`–`CS8625`, `CS8629`, `CS8714` — analyse de nullabilité
- `CS8632` — annotation `?` hors contexte `#nullable`
- `CS8765`, `CS8767` — nullabilité des paramètres de `Equals` / `CompareTo`

Avec ces exclusions, `dotnet build src/Bigarr.Parsing` sort à **0 warning, 0 erreur**. Tout autre
avertissement reste actif : un nouveau warning après une mise à jour amont est donc un signal.

### 7. Globalisation

Aucune modification dans les `.csproj` : la contrainte est portée par le dépôt
(`Directory.Build.props`) et par l'image Docker, voir « Prérequis d'exécution ».

## Exclusions

| Élément amont | Raison |
|---|---|
| `Parser/ParsingService.cs` | dépend de `IMovieService`, `IMovieTranslationService`, du cache et de la base |
| `Parser/Model/{LocalMovie,RemoteMovie,ReleaseInfo,TorrentInfo,GrabbedReleaseInfo,FindMovieResult,IndexerFlags,SeriesTitleInfo}.cs` | modèles indexer / download / import, hors parseur |
| `Parser/RomanNumerals/*` | non utilisé par le parseur de release (sert à la recherche de titres) |
| `Parser/Augmenters/*`, `CustomFormats/*` | pipeline de décision, dépend de la base |
| `Qualities/{QualityDefinitionService,QualityDefinitionRepository,QualityDefinitionLimits,QualitiesBelowCutoff,QualityModelComparer,ProperDownloadTypes}.cs`, `Qualities/Commands/*` | persistance, configuration et comparaison — relèvent de `Bigarr.Decision` / `Bigarr.Domain` |
| `Languages/{LanguageExtensions,LanguagesComparer,LanguageFieldConverter,OriginalLanguageFieldConverter,RealLanguageFieldConverter}.cs` | profils, comparaison et convertisseurs de champs de config |
| `Datastore/IEmbeddedDocument`, `Datastore/ModelBase` | contrat de persistance (voir modification 2) |
| `Common/Instrumentation/*` (NLog) | voir modification 3 |
| `Common/EnvironmentInfo/OsInfo` | remplacé par `OperatingSystem.IsWindows()` côté tests |

## Tests portés

Source : `src/NzbDrone.Core.Test/ParserTests/`. Conversion NUnit → xUnit v2.

**14 fixtures portées, 1065 cas de test, 1065 passants, 0 ignoré.**

| Fixture | Cas | Couvre |
|---|---|---|
| `ParserFixture` | 111 | titres, titres alternatifs (AKA), releases allemandes/anime, année, TMDb, IMDb, subs hardcodés |
| `QualityParserFixture` | 287 | toutes les qualités, sources de détection, repack/proper |
| `LanguageParserFixture` | 195 | ~50 langues, sous-titres, tags `DL`/`ML` allemands |
| `ReleaseGroupParserFixture` | 152 | groupes standards, groupes d'exception, anime, suffixes parasites |
| `SlugParserFixture` | 79 | `ToUrlSlug` (accents, trim, déduplication) |
| `EditionParserFixture` | 48 | éditions (Director's Cut, Extended, IMAX…) |
| `IsoLanguagesFixture` | 43 | résolution des codes ISO 639-1/2 |
| `NormalizeTitleFixture` | 43 | `CleanMovieTitle` (mots communs, acronymes) |
| `ExtendedQualityParserRegex` | 30 | `REAL`/`PROPER`, version, UltraHD |
| `CrapParserFixture` | 26 | rejet des hashes, MD5, chaînes aléatoires |
| `UrlFixture` | 22 | préfixes/suffixes de sites dans les noms |
| `SceneCheckerFixture` | 12 | détection de nommage « scene » |
| `HashedReleaseFixture` | 11 | `ParseMoviePath` sur des releases hashées |
| `AnimeVersionFixture` | 6 | versions `v2`/`v3` anime |

### Conversions appliquées aux tests

- `[TestCase(...)]` → `[InlineData(...)]`, plus un `[Theory]` en tête de chaque bloc de données
- `[Test]` → `[Fact]`
- `[TestCaseSource(nameof(X))]` → `[MemberData(nameof(X))]`, et `public static object[] X` →
  `public static IEnumerable<object[]> X = new[]` (`HashedReleaseFixture`, `QualityParserFixture`)
- `[TestFixture]` et la classe de base `CoreTest` supprimés ; blocs `[SetUp] Setup()` (vides en
  amont) supprimés
- `Description = "..."` de `TestCase` : `InlineData` n'a pas d'équivalent, la note amont est
  conservée en commentaire de fin de ligne
- `ExceptionVerification.IgnoreWarns()` supprimé (plumbing NUnit/NLog)
- `Parser.Parser.X` → `Parser.X`, `Parser.ReleaseGroupParser.X` → `ReleaseGroupParser.X`
- `AsOsAgnostic()` : réimplémenté dans `tests/Bigarr.Parsing.Tests/TestPathExtensions.cs` depuis
  `src/NzbDrone.Test.Common/StringExtensions.cs`, avec `OsInfo.IsNotWindows` →
  `!OperatingSystem.IsWindows()`
- `FluentAssertions 7.2.0` **exactement** (Apache-2.0) : la 8.x a changé de licence. Les assertions
  amont (`Should().Be()`, `AssertionScope`, `BeEquivalentTo`) sont reprises sans modification.
- `NoWarn` du projet de test : `xUnit1012` (l'amont passe `null` à des paramètres `string`),
  `xUnit1026` (`should_parse_ultrahd_from_title` reçoit un `version` qu'il n'assert pas), plus les
  diagnostics de nullabilité.

### Cas de test retiré

- `EditionParserFixture.should_parse_edition` : l'amont répète à l'identique
  `[TestCase("Movie Title 2012 Directors Cut", "Directors Cut")]` (deux fois dans le même bloc).
  xUnit refuse les `InlineData` dupliqués (`xUnit1025`) ; le doublon a été supprimé. **Aucune perte
  de couverture.**

### Fixtures non portées

| Fixture amont | Raison |
|---|---|
| `ParsingServiceTests/GetMovieFixture.cs` | teste `ParsingService`, exclu (base + `IMovieService`) |
| `ParsingServiceTests/MapFixture.cs` | idem, plus des mocks Moq |
| `ParsingServiceTests/ParseQualityDefinitionFixture.cs` | teste `IQualityDefinitionService`, exclu |
| `RomanNumeralTests/RomanNumeralConversionFixture.cs` | teste `Parser/RomanNumerals`, exclu ; nécessite en outre Newtonsoft.Json et un fichier de données JSON |

## Procédure de diff trimestriel

1. Relever le dernier tag stable : `gh release list -R Radarr/Radarr --exclude-pre-releases -L 1`
2. Cloner ce tag et diffusé chaque fichier du tableau « Fichiers vendorisés » contre son chemin
   d'origine. Les seuls écarts attendus sont ceux listés en « Modifications appliquées » (en-tête,
   namespaces, `using`, ligne de déclaration du logger, classes de base, appel `Iso639BTMap.Map`).
3. Diffuser également les 14 fixtures : un nouveau `TestCase` amont signale une regex corrigée.
4. Mettre à jour tag / SHA / date en tête de ce fichier.
5. `dotnet build src/Bigarr.Parsing` doit rester à 0 warning ; `dotnet test tests/Bigarr.Parsing.Tests`
   doit rester à 100 % vert.
6. Si un id de `Quality` ou de `Language` change en amont : **ne pas suivre aveuglément**, la table
   `QualityDefinition` de Bigarr est semée depuis ces ids et une migration serait nécessaire.

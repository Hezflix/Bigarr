using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Naming;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Import;

/// <summary>The single media management row (blueprint §3.7): created on first read, validated on write.</summary>
public sealed class MediaManagementService(BigarrDbContext db, IMediaInfoReader probe)
{
    public async Task<MediaManagementConfig> GetAsync(CancellationToken ct)
    {
        var config = await db.MediaManagement.FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct);
        if (config is not null)
        {
            return config;
        }

        config = MediaManagementConfig.Default();
        db.MediaManagement.Add(config);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced; the other one won.
            db.Entry(config).State = EntityState.Detached;
            config = await db.MediaManagement.FirstAsync(c => c.Id == MediaManagementConfig.SingletonId, ct);
        }

        return config;
    }

    public async Task<Result<MediaManagementConfig>> UpdateAsync(MediaManagementRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fileErrors = NamingTemplate.Validate(request.FileNameTemplate, forFile: true);
        if (fileErrors.Count > 0)
        {
            return Failure.Invalid("Invalid file name template", string.Join(" ", fileErrors));
        }

        var folderErrors = NamingTemplate.Validate(request.FolderNameTemplate, forFile: false);
        if (folderErrors.Count > 0)
        {
            return Failure.Invalid("Invalid folder name template", string.Join(" ", folderErrors));
        }

        if (request.RecycleBinRetentionDays is < 0 or > 3650)
        {
            return Failure.Invalid("Invalid retention", "The recycle bin retention is a number of days between 0 (keep forever) and 3650.");
        }

        string? bin = null;
        if (!string.IsNullOrWhiteSpace(request.RecycleBinPath))
        {
            var candidate = request.RecycleBinPath.Trim();
            if (!Path.IsPathRooted(candidate))
            {
                return Failure.Invalid("Invalid recycle bin path", "The recycle bin path must be absolute.");
            }

            bin = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            try
            {
                Directory.CreateDirectory(bin);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure.Invalid("Recycle bin not accessible", $"'{bin}' cannot be created or written: {ex.Message}");
            }
        }

        string? extensions = null;
        if (request.ExtraFileExtensions is not null)
        {
            extensions = ExtraFiles.Normalize(request.ExtraFileExtensions, out var extensionError);
            if (extensions is null)
            {
                return Failure.Invalid("Invalid extra file extensions", extensionError);
            }
        }

        if (request.SeasonPackUpgrade is { } mode && !Enum.IsDefined(mode))
        {
            return Failure.Invalid("Invalid season pack upgrade", "All, threshold or any.");
        }

        if (request.SeasonPackUpgradeThreshold is < 0 or > 100)
        {
            return Failure.Invalid("Invalid season pack threshold", "A percentage between 0 and 100.");
        }

        if (request.ColonReplacement is { } colon && (!Enum.IsDefined(colon) || colon == ColonReplacement.Custom))
        {
            return Failure.Invalid("Invalid colon replacement", "Delete, dash, space dash, space dash space or smart (Radarr has no custom one).");
        }

        var config = await GetAsync(ct);
        config.ReplaceIllegalCharacters = request.ReplaceIllegalCharacters ?? config.ReplaceIllegalCharacters;
        config.ColonReplacement = request.ColonReplacement ?? config.ColonReplacement;
        config.SeasonPackUpgrade = request.SeasonPackUpgrade ?? config.SeasonPackUpgrade;
        config.SeasonPackUpgradeThreshold = request.SeasonPackUpgradeThreshold ?? config.SeasonPackUpgradeThreshold;
        config.RenameFiles = request.RenameFiles;
        config.FileNameTemplate = request.FileNameTemplate.Trim();
        config.FolderNameTemplate = request.FolderNameTemplate.Trim();
        config.TransferMode = Enum.IsDefined(request.TransferMode) ? request.TransferMode : TransferMode.HardlinkOrCopy;
        config.RecycleBinPath = bin;
        config.RecycleBinRetentionDays = request.RecycleBinRetentionDays;
        config.ImportExtraFiles = request.ImportExtraFiles ?? config.ImportExtraFiles;
        config.ExtraFileExtensions = extensions ?? config.ExtraFileExtensions;
        await db.SaveChangesAsync(ct);
        return Result<MediaManagementConfig>.Ok(config);
    }

    public MediaManagementResponse ToResponse(MediaManagementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new MediaManagementResponse(
            config.RenameFiles,
            config.FileNameTemplate,
            config.FolderNameTemplate,
            config.TransferMode,
            config.RecycleBinPath,
            config.RecycleBinRetentionDays,
            config.ImportExtraFiles,
            config.ExtraFileExtensions,
            probe.IsAvailable,
            probe is FfprobeMediaInfoReader ffprobe ? ffprobe.ExecutablePath : null,
            SafeRender(config.FileNameTemplate, NamingTemplate.Sample, config.FilmNameCleaning, folder: false) + ".mkv",
            SafeRender(config.FolderNameTemplate, NamingTemplate.Sample, config.FilmNameCleaning, folder: true),
            NamingTemplate.Tokens.Select(t => new NamingTokenResponse(t.Name, t.Description, t.Example, t.Folder)).ToList(),
            config.SeasonPackUpgrade,
            config.SeasonPackUpgradeThreshold,
            config.ReplaceIllegalCharacters,
            config.ColonReplacement);
    }

    // ---- series naming ------------------------------------------------------------------------------------------------

    public SeriesNamingResponse ToSeriesNaming(MediaManagementConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new SeriesNamingResponse(
            config.RenameEpisodes,
            config.SeriesFolderTemplate,
            config.SeasonFolderTemplate,
            config.SpecialsFolderName,
            config.StandardEpisodeTemplate,
            config.DailyEpisodeTemplate,
            config.AnimeEpisodeTemplate,
            config.MultiEpisodeStyle,
            PreviewSeries(new SeriesNamingPreviewRequest(
                config.SeriesFolderTemplate, config.SeasonFolderTemplate, config.SpecialsFolderName,
                config.StandardEpisodeTemplate, config.DailyEpisodeTemplate, config.AnimeEpisodeTemplate, config.MultiEpisodeStyle,
                config.SeriesReplaceIllegalCharacters, config.SeriesColonReplacement, config.SeriesCustomColonReplacement)),
            SeriesNamingTemplate.Tokens.Select(t => new SeriesNamingTokenResponse(t.Name, t.Description, t.Example, t.Scope)).ToList(),
            config.SeriesReplaceIllegalCharacters,
            config.SeriesColonReplacement,
            config.SeriesCustomColonReplacement);
    }

    public async Task<Result<MediaManagementConfig>> UpdateSeriesNamingAsync(SeriesNamingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.MultiEpisodeStyle))
        {
            return Failure.Invalid("Invalid multi-episode style", "Extend, duplicate, repeat, scene, range or prefixed range.");
        }

        if (request.ColonReplacement is { } colon && !Enum.IsDefined(colon))
        {
            return Failure.Invalid("Invalid colon replacement", "Delete, dash, space dash, space dash space, smart or custom.");
        }

        var config = await GetAsync(ct);
        var preview = PreviewSeries(new SeriesNamingPreviewRequest(
            request.SeriesFolderTemplate, request.SeasonFolderTemplate, request.SpecialsFolderName,
            request.StandardEpisodeTemplate, request.DailyEpisodeTemplate, request.AnimeEpisodeTemplate, request.MultiEpisodeStyle,
            request.ReplaceIllegalCharacters ?? config.SeriesReplaceIllegalCharacters,
            request.ColonReplacement ?? config.SeriesColonReplacement,
            request.CustomColonReplacement ?? config.SeriesCustomColonReplacement));
        if (preview.Errors.Count > 0)
        {
            return Failure.Invalid("Invalid series naming", string.Join(" ", preview.Errors.Select(e => $"{Labels[e.Key]}: {string.Join(" ", e.Value)}")));
        }

        Apply(config, request);
        await db.SaveChangesAsync(ct);
        return Result<MediaManagementConfig>.Ok(config);
    }

    /// <summary>Copies a naming config that passed <see cref="PreviewSeries"/> (used by the Sonarr import as well).</summary>
    public static void Apply(MediaManagementConfig config, SeriesNamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(request);
        config.RenameEpisodes = request.RenameEpisodes;
        config.SeriesFolderTemplate = request.SeriesFolderTemplate.Trim();
        config.SeasonFolderTemplate = request.SeasonFolderTemplate.Trim();
        config.SpecialsFolderName = request.SpecialsFolderName.Trim();
        config.StandardEpisodeTemplate = request.StandardEpisodeTemplate.Trim();
        config.DailyEpisodeTemplate = request.DailyEpisodeTemplate.Trim();
        config.AnimeEpisodeTemplate = request.AnimeEpisodeTemplate.Trim();
        config.MultiEpisodeStyle = request.MultiEpisodeStyle;
        config.SeriesReplaceIllegalCharacters = request.ReplaceIllegalCharacters ?? config.SeriesReplaceIllegalCharacters;
        config.SeriesColonReplacement = request.ColonReplacement ?? config.SeriesColonReplacement;
        config.SeriesCustomColonReplacement = request.CustomColonReplacement ?? config.SeriesCustomColonReplacement;
    }

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["seriesFolder"] = "Series folder",
        ["seasonFolder"] = "Season folder",
        ["specialsFolder"] = "Specials folder",
        ["standardEpisode"] = "Standard episode format",
        ["dailyEpisode"] = "Daily episode format",
        ["animeEpisode"] = "Anime episode format",
        ["customColonReplacement"] = "Custom colon replacement",
    };

    /// <summary>Renders draft series templates on Sonarr's sample series, with the errors that would refuse each.</summary>
    public static SeriesNamingPreviewResponse PreviewSeries(SeriesNamingPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, List<string>>();

        var cleaning = new NameCleaning(
            request.ReplaceIllegalCharacters ?? true,
            request.ColonReplacement is { } colon && Enum.IsDefined(colon) ? colon : ColonReplacement.Smart,
            request.CustomColonReplacement ?? string.Empty);
        if (request.ColonReplacement == ColonReplacement.Custom)
        {
            var custom = NameCleaning.ValidateCustomColon(request.CustomColonReplacement).ToList();
            if ((request.CustomColonReplacement?.Length ?? 0) > MaxCustomColonLength)
            {
                custom.Add($"The custom colon replacement is at most {MaxCustomColonLength} characters.");
            }

            if (custom.Count > 0)
            {
                errors["customColonReplacement"] = custom;
                cleaning = cleaning with { CustomColon = string.Empty };
            }
        }

        string? Check(string key, string? template, SeriesTemplateKind kind, Func<string, string> render)
        {
            if (template is null)
            {
                return null;
            }

            var found = SeriesNamingTemplate.Validate(template, kind).ToList();
            if (found.Count > 0)
            {
                errors[key] = found;
            }

            try
            {
                return render(template);
            }
            catch (Exception)
            {
                return null;
            }
        }

        var style = Enum.IsDefined(request.MultiEpisodeStyle) ? request.MultiEpisodeStyle : MultiEpisodeStyle.PrefixedRange;
        var sample = WithTitle(SeriesNamingTemplate.Sample, request.SampleTitle);
        var anime = WithTitle(SeriesNamingTemplate.AnimeSample, request.SampleTitle);
        var multi = WithTitle(SeriesNamingTemplate.MultiSample, request.SampleTitle);
        return new SeriesNamingPreviewResponse(
            Check("seriesFolder", request.SeriesFolderTemplate, SeriesTemplateKind.SeriesFolder, t => SeriesNamingTemplate.RenderSeriesFolder(t, sample, cleaning)),
            Check("seasonFolder", request.SeasonFolderTemplate, SeriesTemplateKind.SeasonFolder, t => SeriesNamingTemplate.RenderSeasonFolder(t, t, 1, sample, cleaning)),
            Check("specialsFolder", request.SpecialsFolderName, SeriesTemplateKind.SpecialsFolder, t => SeriesNamingTemplate.RenderSeasonFolder(t, t, 0, sample, cleaning)),
            Check("standardEpisode", request.StandardEpisodeTemplate, SeriesTemplateKind.Standard, t => SeriesNamingTemplate.RenderFile(t, sample, style, ".mkv", cleaning) + ".mkv"),
            request.StandardEpisodeTemplate is null ? null : SafeSeries(() => SeriesNamingTemplate.RenderFile(request.StandardEpisodeTemplate, multi, style, ".mkv", cleaning) + ".mkv"),
            Check("dailyEpisode", request.DailyEpisodeTemplate, SeriesTemplateKind.Daily, t => SeriesNamingTemplate.RenderFile(t, sample, style, ".mkv", cleaning) + ".mkv"),
            Check("animeEpisode", request.AnimeEpisodeTemplate, SeriesTemplateKind.Anime, t => SeriesNamingTemplate.RenderFile(t, anime, style, ".mkv", cleaning) + ".mkv"),
            errors);
    }

    /// <summary>Longest custom colon replacement (the column's size).</summary>
    public const int MaxCustomColonLength = 255;

    private static SeriesNamingContext WithTitle(SeriesNamingContext sample, string? title) =>
        string.IsNullOrWhiteSpace(title) ? sample : sample with { SeriesTitle = title.Trim() };

    private static string? SafeSeries(Func<string> render)
    {
        try
        {
            return render();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Renders a draft template on the sample film, with the errors that would refuse it.</summary>
    public static NamingPreviewResponse Preview(NamingPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fileErrors = request.FileNameTemplate is null ? [] : NamingTemplate.Validate(request.FileNameTemplate, forFile: true).ToList();
        var folderErrors = request.FolderNameTemplate is null ? [] : NamingTemplate.Validate(request.FolderNameTemplate, forFile: false).ToList();
        var cleaning = new NameCleaning(
            request.ReplaceIllegalCharacters ?? true,
            request.ColonReplacement is { } colon && Enum.IsDefined(colon) && colon != ColonReplacement.Custom ? colon : ColonReplacement.Smart);
        var sample = string.IsNullOrWhiteSpace(request.SampleTitle)
            ? NamingTemplate.Sample
            : NamingTemplate.Sample with { MovieTitle = request.SampleTitle.Trim(), OriginalTitle = request.SampleTitle.Trim() };
        return new NamingPreviewResponse(
            request.FileNameTemplate is null ? null : SafeRender(request.FileNameTemplate, sample, cleaning, folder: false) + ".mkv",
            fileErrors,
            request.FolderNameTemplate is null ? null : SafeRender(request.FolderNameTemplate, sample, cleaning, folder: true),
            folderErrors);
    }

    private static string SafeRender(string template, NamingContext sample, NameCleaning cleaning, bool folder)
    {
        try
        {
            return folder ? NamingTemplate.RenderFolder(template, sample, cleaning) : NamingTemplate.Render(template, sample, cleaning);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

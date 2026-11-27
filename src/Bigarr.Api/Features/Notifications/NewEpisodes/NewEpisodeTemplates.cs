using System.Globalization;
using System.Text.RegularExpressions;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>One text of the new-episode message the admin can reword.</summary>
public enum NewEpisodeText
{
    Content,
    Title,
    BatchContent,
    BatchTitle,
    BatchLine,
    BatchMore,
    LinkName,
    LinkText,
}

/// <summary>A placeholder a text does not know, or a format it cannot take.</summary>
public sealed record TemplateProblem(NewEpisodeText Field, string Placeholder);

/// <summary>
/// The texts of the new-episode direct message: their English defaults, the placeholders each one takes, and how they
/// are filled. A placeholder is <c>{Name}</c>; a number can be padded with zeros, <c>{Season:00}</c> gives <c>01</c>.
/// A placeholder a text does not know is refused when saved, and left as typed if one is ever met when sending.
/// </summary>
public static partial class NewEpisodeTemplates
{
    public const int MaxLength = 512;

    public const int MaxAddressLength = 256;

    /// <summary>Placeholders that are numbers, the only ones a zero format pads.</summary>
    private static readonly HashSet<string> Numbers = ["Season", "Episode", "Count", "More"];

    public static IReadOnlyDictionary<NewEpisodeText, string> Defaults { get; } = new Dictionary<NewEpisodeText, string>
    {
        [NewEpisodeText.Content] = "📺 {Show} - Season {Season} · Episode {Episode} - {EpisodeTitle} 👈 just added to Plex!",
        [NewEpisodeText.Title] = "{Show} - {EpisodeTitle} (S{Season} · E{Episode})",
        [NewEpisodeText.BatchContent] = "📺 {Show} - {Count} new episodes 👈 just added to Plex!",
        [NewEpisodeText.BatchTitle] = "{Show} - {Count} new episodes ({Range})",
        [NewEpisodeText.BatchLine] = "S{Season:00} · E{Episode:00} - {EpisodeTitle}",
        [NewEpisodeText.BatchMore] = "… and {More} more",
        [NewEpisodeText.LinkName] = "View on Plex",
        [NewEpisodeText.LinkText] = "Open the show on Plex",
    };

    /// <summary>What each text can say.</summary>
    public static IReadOnlyDictionary<NewEpisodeText, string[]> Placeholders { get; } = new Dictionary<NewEpisodeText, string[]>
    {
        [NewEpisodeText.Content] = ["Show", "Season", "Episode", "EpisodeTitle"],
        [NewEpisodeText.Title] = ["Show", "Season", "Episode", "EpisodeTitle"],
        [NewEpisodeText.BatchContent] = ["Show", "Count", "Range"],
        [NewEpisodeText.BatchTitle] = ["Show", "Count", "Range"],
        [NewEpisodeText.BatchLine] = ["Show", "Season", "Episode", "EpisodeTitle"],
        [NewEpisodeText.BatchMore] = ["Show", "More"],
        [NewEpisodeText.LinkName] = ["Show"],
        [NewEpisodeText.LinkText] = ["Show"],
    };

    /// <summary>The admin's text, or the default when it is empty.</summary>
    public static string Text(NewEpisodeSettings? settings, NewEpisodeText field)
    {
        var own = settings is null ? null : Get(settings, field);
        return string.IsNullOrWhiteSpace(own) ? Defaults[field] : own;
    }

    public static string? Get(NewEpisodeSettings settings, NewEpisodeText field) => field switch
    {
        NewEpisodeText.Content => settings.Content,
        NewEpisodeText.Title => settings.Title,
        NewEpisodeText.BatchContent => settings.BatchContent,
        NewEpisodeText.BatchTitle => settings.BatchTitle,
        NewEpisodeText.BatchLine => settings.BatchLine,
        NewEpisodeText.BatchMore => settings.BatchMore,
        NewEpisodeText.LinkName => settings.LinkName,
        NewEpisodeText.LinkText => settings.LinkText,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    /// <summary>Fills a text. Values missing from <paramref name="values"/> and unknown placeholders stay as typed.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, object> values) =>
        Placeholder().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out var value))
            {
                return match.Value;
            }

            var format = match.Groups["format"];
            if (!format.Success)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            return value is int number && ZeroFormat(format.Value) ? number.ToString(format.Value, CultureInfo.InvariantCulture) : match.Value;
        });

    /// <summary>Placeholders a text does not take, or that take no format; empty when every text is fine.</summary>
    public static List<TemplateProblem> Validate(NewEpisodeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var problems = new List<TemplateProblem>();
        foreach (var field in Enum.GetValues<NewEpisodeText>())
        {
            if (Get(settings, field) is not { } text)
            {
                continue;
            }

            var known = Placeholders[field];
            foreach (Match match in Placeholder().Matches(text))
            {
                var name = match.Groups["name"].Value;
                var format = match.Groups["format"];
                var fine = known.Contains(name, StringComparer.Ordinal) && (!format.Success || (Numbers.Contains(name) && ZeroFormat(format.Value)));
                if (!fine && !problems.Any(p => p.Field == field && p.Placeholder == match.Value))
                {
                    problems.Add(new TemplateProblem(field, match.Value));
                }
            }
        }

        return problems;
    }

    /// <summary>The address as links use it (no trailing slash), or null when it is not an absolute http(s) URL.</summary>
    public static string? WebAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static bool ZeroFormat(string format) => format.Length is > 0 and <= 4 && format.All(c => c == '0');

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(?::(?<format>[^{}]*))?\}")]
    private static partial Regex Placeholder();
}

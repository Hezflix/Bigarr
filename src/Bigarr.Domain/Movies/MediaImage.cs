namespace Bigarr.Domain.Movies;

public enum ImageType
{
    Poster = 1,
    Fanart = 2,
}

/// <summary>
/// Remote image only. The local cache path is deterministic from (MovieId, type, size) so the compat layer
/// can serve <c>/MediaCover/{id}/poster-500.jpg</c> without storing anything.
/// </summary>
public sealed record MediaImage(ImageType Type, string RemoteUrl);

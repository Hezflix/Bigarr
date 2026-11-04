using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Features.MediaServers;

public sealed record PathMappingDto(string From, string To);

/// <summary>Shared by create and update.</summary>
public sealed record MediaServerRequest(
    string Name,
    MediaServerKind Kind,
    string Url,
    string Token,
    bool Enabled = true,
    List<PathMappingDto>? PathMappings = null);

public sealed record MediaServerResponse(
    int Id,
    string Name,
    MediaServerKind Kind,
    string Url,
    string Token,
    bool Enabled,
    List<PathMappingDto> PathMappings,
    DateTime? LastRefreshAt,
    string? LastError,
    DateTime? LastErrorAt,
    bool IsFailing,
    DateTime CreatedAt);

public sealed record MediaServerTestResponse(string? Name, string? Version, int MovieLibraries);

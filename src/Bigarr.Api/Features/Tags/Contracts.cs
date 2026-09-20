namespace Bigarr.Api.Features.Tags;

public sealed record TagResponse(int Id, string Label);

public sealed record TagRequest(string Label);

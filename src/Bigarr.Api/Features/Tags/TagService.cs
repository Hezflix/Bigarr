using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tags;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Tags;

/// <summary>Tags (blueprint §3.7): a label, an id, and one join table per tagged entity. Seerr creates one per user.</summary>
public sealed class TagService(BigarrDbContext db)
{
    public const int MaxLabelLength = 64;

    public async Task<List<TagResponse>> ListAsync(CancellationToken ct) =>
        await db.Tags.AsNoTracking().OrderBy(t => t.Label).Select(t => new TagResponse(t.Id, t.Label)).ToListAsync(ct);

    public async Task<TagResponse?> GetAsync(int id, CancellationToken ct)
    {
        var tag = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return tag is null ? null : new TagResponse(tag.Id, tag.Label);
    }

    /// <summary>Radarr normalizes labels to lowercase without spaces; Seerr relies on the label it posted coming back as is. We trim and lowercase only.</summary>
    public static string NormalizeLabel(string? label) => (label ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<Result<TagResponse>> AddAsync(TagRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var label = NormalizeLabel(request.Label);
        if (label.Length == 0 || label.Length > MaxLabelLength)
        {
            return Failure.Invalid("Invalid label", $"A tag label is required (max {MaxLabelLength} characters).");
        }

        var existing = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Label == label, ct);
        if (existing is not null)
        {
            // Radarr answers the existing tag rather than failing: clients create tags idempotently.
            return Result<TagResponse>.Ok(new TagResponse(existing.Id, existing.Label));
        }

        var tag = new Tag { Label = label };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return Result<TagResponse>.Ok(new TagResponse(tag.Id, tag.Label));
    }

    public async Task<Result<TagResponse>> UpdateAsync(int id, TagRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
        {
            return Failure.NotFound("Tag");
        }

        var label = NormalizeLabel(request.Label);
        if (label.Length == 0 || label.Length > MaxLabelLength)
        {
            return Failure.Invalid("Invalid label", $"A tag label is required (max {MaxLabelLength} characters).");
        }

        if (await db.Tags.AnyAsync(t => t.Id != id && t.Label == label, ct))
        {
            return Failure.Conflict("Tag label already used", $"A tag labelled '{label}' exists.");
        }

        tag.Label = label;
        await db.SaveChangesAsync(ct);
        return Result<TagResponse>.Ok(new TagResponse(tag.Id, tag.Label));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
        {
            return Failure.NotFound("Tag");
        }

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    /// <summary>The tags behind <paramref name="ids"/>, or a failure naming the unknown ones.</summary>
    public async Task<Result<List<Tag>>> ResolveAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return Result<List<Tag>>.Ok([]);
        }

        var tags = await db.Tags.Where(t => wanted.Contains(t.Id)).ToListAsync(ct);
        var unknown = wanted.Except(tags.Select(t => t.Id)).ToList();
        return unknown.Count == 0 ? Result<List<Tag>>.Ok(tags) : Failure.Invalid("Unknown tag", $"No tag with id {string.Join(", ", unknown)}.");
    }
}

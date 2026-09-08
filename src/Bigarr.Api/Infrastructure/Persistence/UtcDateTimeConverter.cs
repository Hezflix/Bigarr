using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>All timestamps are UTC. SQLite has no timezone, so the kind is restored on read.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

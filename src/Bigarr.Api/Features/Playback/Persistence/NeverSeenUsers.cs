namespace Bigarr.Api.Features.Playback.Persistence;

/// <summary>
/// Repairs the users older Tautulli imports dated "last seen" at the import while they never played. Such a user has
/// no play and no open stream, was last seen exactly when first seen, and shares that instant to the tick with another
/// user of the server: an import stamps all its users at once, a live stream dates one user at a time. A user seen live
/// once with nothing recorded (history off, or a stream too short) and a lone import without history look the same;
/// both keep their date, the safe side.
/// </summary>
public static class NeverSeenUsers
{
    public const string RepairSql = """
        UPDATE "MediaServerUsers" SET "LastSeenAt" = NULL
        WHERE "LastSeenAt" = "FirstSeenAt"
          AND NOT EXISTS (SELECT 1 FROM "Plays" p WHERE p."UserId" = "MediaServerUsers"."Id")
          AND NOT EXISTS (SELECT 1 FROM "PlaybackSessions" s WHERE s."UserId" = "MediaServerUsers"."Id")
          AND (SELECT COUNT(*) FROM "MediaServerUsers" o
               WHERE o."MediaServerId" = "MediaServerUsers"."MediaServerId" AND o."FirstSeenAt" = "MediaServerUsers"."FirstSeenAt") > 1;
        """;
}

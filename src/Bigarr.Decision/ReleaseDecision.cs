namespace Bigarr.Decision;

public sealed record ReleaseDecision(TargetContext Target, IReadOnlyList<Rejection> Rejections, int CustomFormatScore = 0)
{
    public bool Approved => Rejections.Count == 0;

    public bool IsTemporary => Rejections.Count > 0 && Rejections.All(r => r.Type == RejectionType.Temporary);
}

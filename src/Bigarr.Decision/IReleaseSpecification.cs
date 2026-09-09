namespace Bigarr.Decision;

/// <summary>One rule. Returns null when satisfied; every specification runs so the user sees every reason at once.</summary>
public interface IReleaseSpecification
{
    Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target);
}

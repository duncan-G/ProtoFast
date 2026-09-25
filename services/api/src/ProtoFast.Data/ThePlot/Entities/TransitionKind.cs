namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// How a transition cuts to what follows (<c>CUT TO:</c>).
/// Stored by name, so members can be reordered or added without a data migration; renaming one does need one.
/// </summary>
public enum TransitionKind
{
    CutTo,
    DissolveTo,
    SmashCutTo,
    MatchCutTo,
    TimeCut,
    FadeOut,
}

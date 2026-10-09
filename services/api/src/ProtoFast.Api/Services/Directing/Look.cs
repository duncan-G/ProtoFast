using SceneAppearanceRecord = ProtoFast.Data.ThePlot.Entities.SceneAppearance;
using SceneLookChangeRecord = ProtoFast.Data.ThePlot.Entities.SceneLookChange;

namespace ProtoFast.Api.Services.Directing;

/// <summary>What a character looks like for a stretch of the story, on top of their sheet.</summary>
/// <param name="WardrobeId">Null wears the sheet's first wardrobe.</param>
/// <param name="Holding">Prop ids, in the order they were taken.</param>
public sealed record Look(Guid? WardrobeId, string WardrobeChange, string AppearanceChange, IReadOnlyList<Guid> Holding)
{
    public static readonly Look Plain = new(null, "", "", []);

    public static Look Of(SceneAppearanceRecord appearance) =>
        new(appearance.WardrobeId, appearance.WardrobeChange, appearance.AppearanceChange, appearance.Holding);

    public static Look Of(SceneLookChangeRecord change) =>
        new(change.WardrobeId, change.WardrobeChange, change.AppearanceChange, change.Holding);

    /// <summary>Nothing on top of the sheet: the library's picture of the character shows it.</summary>
    public bool IsSheet(Data.ThePlot.Entities.Character character) =>
        (WardrobeId is null || WardrobeId == character.Wardrobes.OrderBy(w => w.Position).FirstOrDefault()?.Id)
        && WardrobeChange.Trim().Length == 0
        && AppearanceChange.Trim().Length == 0
        && Holding.Count == 0;

    /// <summary>Record equality compares the holding list by reference; this compares what it holds.</summary>
    public bool SameAs(Look other) =>
        WardrobeId == other.WardrobeId
        && WardrobeChange == other.WardrobeChange
        && AppearanceChange == other.AppearanceChange
        && Holding.Count == other.Holding.Count
        && Holding.All(other.Holding.Contains);
}

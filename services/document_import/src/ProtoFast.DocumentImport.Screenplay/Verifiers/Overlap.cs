namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <param name="Kept">The share of the story's words that are the manuscript's, 0..1.</param>
/// <param name="Covered">The share of the manuscript's words that the story keeps, 0..1.</param>
/// <param name="Missing">Longest first.</param>
public sealed record Overlap(
    double Kept, double Covered, IReadOnlyList<UnitOverlap> Units, IReadOnlyList<MissingPassage> Missing);

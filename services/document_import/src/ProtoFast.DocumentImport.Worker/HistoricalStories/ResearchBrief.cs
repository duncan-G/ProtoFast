namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>What the research desk hands the documentary writer: one moment, its context, people, events and texture, each cited to passages.</summary>
/// <param name="Stakes">What the people in the moment stand to win or lose.</param>
/// <param name="Background">What the narrator needs to set the moment in its time: the larger situation and how it came to this.</param>
/// <param name="Aftermath">What came of the moment, for its people and beyond.</param>
public sealed record ResearchBrief(
    string Title,
    string Moment,
    string When,
    string Where,
    string Summary,
    string? Stakes,
    IReadOnlyList<BriefFact>? Background,
    IReadOnlyList<BriefFact>? Aftermath,
    IReadOnlyList<BriefPerson>? People,
    IReadOnlyList<BriefEvent>? Timeline,
    IReadOnlyList<BriefPlace>? Places,
    IReadOnlyList<BriefDetail>? Details,
    IReadOnlyList<BriefQuote>? Quotes,
    IReadOnlyList<BriefSource>? Sources)
{
    public IEnumerable<long> CitedPassages() =>
        (People ?? []).SelectMany(p => p.Sources ?? [])
            .Concat((Timeline ?? []).SelectMany(e => e.Sources ?? []))
            .Concat((Background ?? []).SelectMany(f => f.Sources ?? []))
            .Concat((Aftermath ?? []).SelectMany(f => f.Sources ?? []))
            .Concat((Places ?? []).SelectMany(p => p.Sources ?? []))
            .Concat((Details ?? []).SelectMany(d => d.Sources ?? []))
            .Concat((Quotes ?? []).Select(q => q.Source))
            .Concat((Sources ?? []).Select(s => s.PassageId))
            .Distinct();
}

namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>One model turn's tool calls and what they did.</summary>
/// <param name="Sequence">The transcript sequence of the assistant message that made the calls.</param>
public sealed record RunStep(int Sequence, IReadOnlyList<StepCall> Calls)
{
    public string Headline => string.Join("; ", Calls.Select(c => c.Headline));
}

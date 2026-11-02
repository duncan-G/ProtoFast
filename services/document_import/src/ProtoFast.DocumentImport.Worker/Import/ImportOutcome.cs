namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>What one delivery of an upload came to: a saved story, nothing to do, or a hand-off to the conversion worker.</summary>
public sealed record ImportOutcome(Guid? StoryId, bool SentForConversion = false)
{
    public static readonly ImportOutcome Skipped = new(StoryId: null);

    public static readonly ImportOutcome Converting = new(StoryId: null, SentForConversion: true);
}

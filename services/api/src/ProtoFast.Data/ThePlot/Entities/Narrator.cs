namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A voice that reads the lines nobody speaks: action and narration. Owned by its
/// <see cref="Story"/>: it has no <c>UserId</c> and is only read and written through it. The first
/// by position is the story's default; a cue can pick another.
/// </summary>
public sealed class Narrator
{
    public Guid Id { get; set; }

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public int Position { get; set; }

    public required string Name { get; set; }

    public VoiceProfile Voice { get; set; } = new();
}

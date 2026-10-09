namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// One outfit a character wears somewhere in the story. Owned by its <see cref="Character"/>: it
/// has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class CharacterWardrobe
{
    public Guid Id { get; set; }

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    public int Position { get; set; }

    /// <summary>Unique per character; a scene's look names it.</summary>
    public required string Label { get; set; }

    public string Description { get; set; } = "";
}

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// How a dialogue, narration or action line is delivered. Owned by its <see cref="Scene"/>: it has no
/// <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class Cue
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid ElementId { get; set; }

    public SceneElement Element { get; set; } = null!;

    /// <summary>The line's own words with delivery tags, e.g. "[sad] I'm sorry."; the words never change.</summary>
    public string Performance { get; set; } = "";

    /// <summary>Minimum silence before the line; negative overlaps the previous one.</summary>
    public int GapBeforeMs { get; set; }

    /// <summary>Cut off by the next line.</summary>
    public bool Interrupted { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }

    /// <summary>Who reads an action or narration line; null is the story's first narrator.</summary>
    public Guid? NarratorId { get; set; }

    public Narrator? Narrator { get; set; }

    /// <summary>The line spoken, if generated; stale once the performance or the voice changed.</summary>
    public Guid? AudioAssetId { get; set; }

    public MediaAsset? Audio { get; set; }
}

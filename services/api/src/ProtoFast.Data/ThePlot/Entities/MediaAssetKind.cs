namespace ProtoFast.Data.ThePlot.Entities;

public enum MediaAssetKind
{
    /// <summary>One line's performance, spoken.</summary>
    CueAudio,

    /// <summary>One frame of a scene, pictured.</summary>
    FrameImage,

    /// <summary>A character, location or prop as the library pictures it; frames take it as a reference.</summary>
    LibraryImage,

    /// <summary>A character in a look other than the library's: carrying something, hurt, dressed differently.</summary>
    LookImage,
}

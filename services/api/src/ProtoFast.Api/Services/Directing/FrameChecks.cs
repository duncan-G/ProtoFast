namespace ProtoFast.Api.Services.Directing;

/// <summary>What the frame verifiers look at.</summary>
public static class FrameChecks
{
    /// <summary>The frame agrees with the frames around it, the looks and the pictures already made.</summary>
    public const string Consistency = "consistency";

    /// <summary>Everyone and everything in the picture is where the rows, the place and the camera need them.</summary>
    public const string Positioning = "positioning";
}

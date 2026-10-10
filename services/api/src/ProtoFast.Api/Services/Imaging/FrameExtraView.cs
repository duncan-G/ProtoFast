using ExtraRecord = ProtoFast.Data.ThePlot.Entities.Extra;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>Extras one frame shows, with the staging the frame gives them, if any.</summary>
/// <param name="Pose">For a group, which of them the staging is about.</param>
public sealed record FrameExtraView(ExtraRecord Extra, string Screen, string Facing, string Moving, string Looking, string Pose);

using PropRecord = ProtoFast.Data.ThePlot.Entities.Prop;

namespace ProtoFast.Api.Services.Imaging;

public sealed record FramePropView(PropRecord Prop, IReadOnlyList<string> State);

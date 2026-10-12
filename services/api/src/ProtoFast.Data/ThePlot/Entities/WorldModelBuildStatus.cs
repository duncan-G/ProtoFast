namespace ProtoFast.Data.ThePlot.Entities;

public enum WorldModelBuildStatus
{
    Queued,
    Running,
    Retrying,
    Finished,
    Failed,
    Cancelled,
}

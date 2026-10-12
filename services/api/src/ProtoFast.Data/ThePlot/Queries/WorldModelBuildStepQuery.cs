using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class WorldModelBuildStepQuery : Query<WorldModelBuildStep>, IWorldModelBuildStepQuery
{
    public IWorldModelBuildStepQuery WithId(Guid id)
    {
        Where(s => s.Id == id);
        return this;
    }

    public IWorldModelBuildStepQuery InBuild(Guid buildId)
    {
        Where(s => s.BuildId == buildId);
        return this;
    }

    public IWorldModelBuildStepQuery OfKind(WorldModelStepKind kind)
    {
        Where(s => s.Kind == kind);
        return this;
    }

    public IWorldModelBuildStepQuery WithStatus(WorldModelStepStatus status)
    {
        Where(s => s.Status == status);
        return this;
    }

    public IWorldModelBuildStepQuery InOrder()
    {
        Apply(q => q.OrderBy(s => s.Sequence).ThenBy(s => s.Id));
        return this;
    }
}

using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class CastMemberQuery : Query<CastMember>, ICastMemberQuery
{
    public ICastMemberQuery WithId(Guid id)
    {
        Where(c => c.Id == id);
        return this;
    }

    public ICastMemberQuery InStory(Guid storyId)
    {
        Where(c => c.StoryId == storyId);
        return this;
    }

    public ICastMemberQuery WithName(string name)
    {
        string lowered = name.ToLower();
        Where(c => c.Name.ToLower() == lowered);
        return this;
    }

    public ICastMemberQuery NameContains(string text)
    {
        string lowered = text.ToLower();
        Where(c => c.Name.ToLower().Contains(lowered));
        return this;
    }

    public ICastMemberQuery OfKind(CastMemberKind kind)
    {
        Where(c => c.Kind == kind);
        return this;
    }

    public ICastMemberQuery ByName()
    {
        Apply(q => q.OrderBy(c => c.Name).ThenBy(c => c.Id));
        return this;
    }
}

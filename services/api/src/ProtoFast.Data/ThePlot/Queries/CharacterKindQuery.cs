using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class CharacterKindQuery : Query<CharacterKind>, ICharacterKindQuery
{
    public ICharacterKindQuery WithId(Guid id)
    {
        Where(v => v.Id == id);
        return this;
    }

    public ICharacterKindQuery InStory(Guid storyId)
    {
        Where(v => v.StoryId == storyId);
        return this;
    }

    public ICharacterKindQuery WithLabel(string label)
    {
        string lowered = label.ToLower();
        Where(v => v.Label.ToLower() == lowered);
        return this;
    }

    public ICharacterKindQuery InOrder()
    {
        Apply(q => q.OrderBy(v => v.Position).ThenBy(v => v.Id));
        return this;
    }
}

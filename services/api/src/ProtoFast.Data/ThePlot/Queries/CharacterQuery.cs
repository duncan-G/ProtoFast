using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database;

namespace ProtoFast.Data.ThePlot.Queries;

public sealed class CharacterQuery : Query<Character>, ICharacterQuery
{
    public ICharacterQuery WithId(Guid id)
    {
        Where(c => c.Id == id);
        return this;
    }

    public ICharacterQuery InStory(Guid storyId)
    {
        Where(c => c.StoryId == storyId);
        return this;
    }

    public ICharacterQuery WithName(string name)
    {
        string lowered = name.ToLower();
        Where(c => c.Name.ToLower() == lowered);
        return this;
    }

    public ICharacterQuery NameContains(string text)
    {
        string lowered = text.ToLower();
        Where(c => c.Name.ToLower().Contains(lowered));
        return this;
    }

    public ICharacterQuery OfKind(Guid kindId)
    {
        Where(c => c.KindId == kindId);
        return this;
    }

    public ICharacterQuery ByName()
    {
        Apply(q => q.OrderBy(c => c.Name).ThenBy(c => c.Id));
        return this;
    }
}

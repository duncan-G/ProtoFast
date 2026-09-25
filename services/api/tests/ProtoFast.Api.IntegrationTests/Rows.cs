namespace ProtoFast.Api.IntegrationTests;

public static class Rows
{
    public static SceneElement Heading(string? locationId = null, string? timeOfDay = null)
    {
        var row = Row(SceneElementType.Heading);
        if (locationId is not null)
        {
            row.LocationId = locationId;
        }

        if (timeOfDay is not null)
        {
            row.TimeOfDay = timeOfDay;
        }

        return row;
    }

    public static SceneElement Beat(SceneElementType type, string text, params SceneElementMention[] mentions)
    {
        var row = Row(type);
        row.Text = text;
        row.Mentions.AddRange(mentions);
        return row;
    }

    public static SceneElement Action(string text, params SceneElementMention[] mentions) =>
        Beat(SceneElementType.Action, text, mentions);

    public static SceneElement Dialogue(string? speakerId, string text, string? parenthetical = null)
    {
        var row = Beat(SceneElementType.Dialogue, text);
        if (speakerId is not null)
        {
            row.SpeakerId = speakerId;
        }

        if (parenthetical is not null)
        {
            row.Parenthetical = parenthetical;
        }

        return row;
    }

    public static SceneElement Transition(string label)
    {
        var row = Row(SceneElementType.Transition);
        row.Transition = label;
        return row;
    }

    /// <summary>The <paramref name="occurrence"/>th <paramref name="reference"/> in the text.</summary>
    public static SceneElementMention Mention(string text, string reference, Character character, int occurrence = 0) =>
        new() { Id = Guid.NewGuid().ToString(), CharacterId = character.Id, Offset = IndexOf(text, reference, occurrence), Length = reference.Length };

    public static SceneElementMention Mention(string text, string reference, Location location, int occurrence = 0) =>
        new() { Id = Guid.NewGuid().ToString(), LocationId = location.Id, Offset = IndexOf(text, reference, occurrence), Length = reference.Length };

    public static SceneElementMention Mention(string text, string reference, Prop prop, int occurrence = 0) =>
        new() { Id = Guid.NewGuid().ToString(), PropId = prop.Id, Offset = IndexOf(text, reference, occurrence), Length = reference.Length };

    /// <summary>Each mention's slice of the text, in order.</summary>
    public static string[] Slices(SceneElement row) =>
        row.Mentions.Select(m => row.Text.Substring(m.Offset, m.Length)).ToArray();

    private static SceneElement Row(SceneElementType type) => new() { Id = Guid.NewGuid().ToString(), Type = type };

    private static int IndexOf(string text, string reference, int occurrence)
    {
        var at = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            at = text.IndexOf(reference, at + 1, StringComparison.Ordinal);
        }

        return at;
    }
}

using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Builders for the scene layers. They exist because every test below needs the same three
/// awkward things — a paragraph whose text and offsets agree, items that actually partition it,
/// and a registry the ids resolve against — and hand-writing those is how a test ends up asserting
/// something other than what it says.
/// </summary>
internal static class SceneFixtures
{
    public static Paragraph Paragraph(string id, string text) =>
        new(id, "L000001", "L000001", text, text.Split(' ').Length, ParagraphKind.Body, Ids.Sha256Hex(text))
        {
            LineIds = ["L000001"],
        };

    /// <summary>Runs the real materializer, so the items are a partition rather than a hopeful list.</summary>
    public static IReadOnlyList<SceneItem> Items(
        Paragraph paragraph,
        IReadOnlyList<ItemCut> cuts,
        IReadOnlyList<TagProposal>? tags = null,
        int itemCounter = 0,
        int tagCounter = 0)
    {
        var result = ItemMaterializer.Materialize(
            paragraph, cuts, tags ?? [], ref itemCounter, ref tagCounter);

        Assert.True(result.Success, result.Validation.ErrorReport);
        return result.Items;
    }

    public static Persona Persona(string id, string name, PersonaKind kind = PersonaKind.Individual) =>
        new(id, name, kind, PersonaScope.Persistent, null, [name], null, []);

    public static Registries Registry(params Persona[] personas) => new(personas, [], []);

    public static SectionNode Leaf(string sectionId, string title, params string[] paragraphIds) =>
        new(sectionId, title, false, "L000001", 1, [], paragraphIds);
}

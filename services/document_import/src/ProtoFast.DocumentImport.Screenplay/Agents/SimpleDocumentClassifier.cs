using System.Text.RegularExpressions;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>Two families: text that already reads as a screenplay, and prose that has to become one.</summary>
public sealed partial class SimpleDocumentClassifier(IArtifactStore artifacts) : IDocumentClassifier
{
    public const string ScreenplayFamily = "screenplay";
    public const string ProseFamily = "prose";

    private const int HeadingsForScreenplay = 3;

    public async Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct)
    {
        var text = await ArtifactText.ReadAsync(artifacts, input, ct);
        var headings = SceneHeading().Count(text);
        var family = headings >= HeadingsForScreenplay ? ScreenplayFamily : ProseFamily;
        return new DocumentSignature(family, new Dictionary<string, string>
        {
            ["headings"] = headings.ToString(),
            ["chars"] = text.Length.ToString(),
        });
    }

    [GeneratedRegex(@"^\s*(?:INT|EXT|INT\./EXT|I/E)[.\s]", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SceneHeading();
}

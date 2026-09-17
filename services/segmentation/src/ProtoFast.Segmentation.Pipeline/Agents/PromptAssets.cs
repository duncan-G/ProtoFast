using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Loads the embedded rules, skills, schemas and prompt templates, and computes the prompt
/// version each role runs at (plan §15).
///
/// <para>Assets are embedded resources rather than files on disk because <c>PromptVersion</c> has
/// to be a property of the deployed image. That is what makes rollback coherent: rolling the
/// image back rolls the prompts back with it, and the qualification records for that version are
/// still in the database, so routing does not have to re-qualify anything (plan §23.5).</para>
/// </summary>
public sealed class PromptAssets
{
    /// <summary>
    /// Resource names are pinned to their paths by the csproj's <c>LogicalName</c>, so this is a
    /// prefix rather than a transformation. See the comment there for why.
    /// </summary>
    private const string Root = "Assets/";

    private readonly Assembly _assembly = typeof(PromptAssets).Assembly;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<AgentRole, string> _versions = new();

    /// <summary>Reads one asset by its path under <c>Assets/</c>, e.g. <c>rules/common.md</c>.</summary>
    public string Read(string path) => _cache.GetOrAdd(path, key =>
    {
        var resource = Root + key;
        using var stream = _assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException(
                $"Prompt asset '{key}' is not embedded. Expected resource '{resource}'; the "
                + $"assembly has: {string.Join(", ", _assembly.GetManifestResourceNames().Order())}",
                key);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    public string? TryRead(string path)
    {
        try
        {
            return Read(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public string Rules => Read("rules/common.md");

    public string Skill(string name) => Read($"skills/{name}/SKILL.md");

    /// <summary>
    /// The family skill, or empty when the family has none. An unknown family is the common case,
    /// not an error — the generic skills handle it.
    /// </summary>
    public string FamilySkill(string family) =>
        family == Core.Ingest.FamilyDetector.Unknown
            ? string.Empty
            : TryRead($"skills/families/{family}/SKILL.md") ?? string.Empty;

    public string Schema(string name) => Read($"schemas/{name}.schema.json");

    public string Template(string name) => Read($"prompts/{name}.md");

    /// <summary>
    /// The hash of every asset a role uses. Stored with each artifact, each <c>model_calls</c> row
    /// and each qualification record, and part of the qualification lookup key — so changing a
    /// prompt invalidates that model's qualification for the role automatically (plan §15.3).
    /// </summary>
    public string VersionFor(AgentRole role) => _versions.GetOrAdd(role, r =>
    {
        var builder = new StringBuilder();
        foreach (var path in AssetsFor(r))
        {
            builder.Append(path).Append('\n').Append(TryRead(path) ?? string.Empty).Append('\n');
        }

        return Ids.Sha256Hex(builder.ToString())[..16];
    });

    /// <summary>
    /// Which assets a role's prompt is built from. Family skills are deliberately excluded: they
    /// vary per document, and including them would give the same role a different prompt version
    /// per family, fragmenting qualification into a matrix nobody could keep evaluated.
    /// </summary>
    private static IEnumerable<string> AssetsFor(AgentRole role) => role switch
    {
        AgentRole.Labeler =>
            ["rules/common.md", "skills/layout-labeling/SKILL.md", "prompts/labeler.v1.md", "schemas/labels.schema.json"],
        AgentRole.HeadingLeveler =>
            ["rules/common.md", "skills/heading-levels/SKILL.md", "prompts/heading-levels.v1.md", "schemas/heading-levels.schema.json"],
        AgentRole.Structurer =>
            ["rules/common.md", "skills/hierarchy-inference/SKILL.md", "prompts/structurer.v1.md", "schemas/tree.schema.json"],
        AgentRole.StructureReviewer =>
            ["rules/common.md", "skills/structure-review/SKILL.md", "prompts/structure-reviewer.v1.md", "schemas/review.schema.json"],
        AgentRole.TreeRepairer =>
            ["rules/common.md", "skills/tree-repair/SKILL.md", "prompts/tree-repair.v1.md", "schemas/tree.schema.json"],
        AgentRole.Augmenter =>
            ["rules/common.md", "prompts/augmenter.v1.md"],
        AgentRole.AugmentReviewer =>
            ["rules/common.md", "prompts/augment-reviewer.v1.md", "schemas/review.schema.json"],
        _ => ["rules/common.md"],
    };
}

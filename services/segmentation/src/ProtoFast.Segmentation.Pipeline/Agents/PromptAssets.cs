using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    private readonly ConcurrentDictionary<string, JsonElement> _schemaElements = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, Core.Grounding.ClosedClassLexicon> _lexicons =
        new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// The closed-class allowlist for a language, or the English fallback (scene plan §3.7).
    ///
    /// <para>It ships beside the prompts and is versioned with them, because it is part of what
    /// <c>render-text-grounding</c> means — a run whose allowlist changed is a run whose gate
    /// changed. A language with no file still gets a hard gate rather than no gate.</para>
    /// </summary>
    public Core.Grounding.ClosedClassLexicon ClosedClass(string language = "en") =>
        _lexicons.GetOrAdd(language, key =>
            TryRead($"rules/closed-class.{key}.txt") is { } content
                ? Core.Grounding.ClosedClassLexicon.Parse(content)
                : Core.Grounding.ClosedClassLexicon.English);

    /// <summary>
    /// The schema actually sent as the provider's structured-output parameter, from
    /// <c>schemas/wire/</c>.
    ///
    /// <para>A second file rather than a transformation of <see cref="Schema"/>, because the two
    /// have different jobs. The authoring schema is written for people and for the prompt: it
    /// carries the id patterns, the title length, and the children-xor-paragraphs <c>oneOf</c>
    /// that say what a correct artifact looks like. The wire schema is written for a constrained
    /// decoder, which supports none of those — no recursion, no <c>oneOf</c>, no <c>not</c>, no
    /// numeric or length bounds — and rejects the whole request with a 400 if it sees one. Nothing
    /// is lost by the narrowing: these schemas have never been enforced at runtime, and the
    /// constraints the wire form drops are exactly the ones <c>Checks</c> already enforces.</para>
    ///
    /// <para>Parsed once and cloned off its document, since the element is handed to every call
    /// the role makes.</para>
    /// </summary>
    public JsonElement WireSchemaElement(string name) => _schemaElements.GetOrAdd(
        name, key => JsonDocument.Parse(WireSchema(key)).RootElement.Clone());

    public string WireSchema(string name) => Read($"schemas/wire/{name}.schema.json");

    public string Template(string name) => Read($"prompts/{name}.md");

    /// <summary>
    /// The hash of every asset a role uses — prompt text and the wire schema alike, since a model
    /// asked for a different shape is not the same call the qualification measured. Stored with
    /// each artifact, each <c>model_calls</c> row
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
            ["rules/common.md", "skills/layout-labeling/SKILL.md", "prompts/labeler.v1.md", "schemas/labels.schema.json", "schemas/wire/labels.schema.json"],
        AgentRole.HeadingLeveler =>
            ["rules/common.md", "skills/heading-levels/SKILL.md", "prompts/heading-levels.v1.md", "schemas/heading-levels.schema.json", "schemas/wire/heading-levels.schema.json"],
        AgentRole.Structurer =>
            ["rules/common.md", "skills/hierarchy-inference/SKILL.md", "prompts/structurer.v1.md", "schemas/tree.schema.json", "schemas/wire/tree.schema.json"],
        AgentRole.StructureWindower =>
            ["rules/common.md", "skills/hierarchy-inference/SKILL.md", "prompts/structure-window.v1.md", "prompts/structure-followup.v1.md", "schemas/structure-window.schema.json", "schemas/wire/structure-window.schema.json", "schemas/structure-answers.schema.json", "schemas/wire/structure-answers.schema.json"],
        AgentRole.StructureOrchestrator =>
            ["rules/common.md", "skills/structure-orchestration/SKILL.md", "prompts/structure-orchestrator.v1.md", "schemas/assembly-plan.schema.json", "schemas/wire/assembly-plan.schema.json"],
        AgentRole.StructureReviewer =>
            ["rules/common.md", "skills/structure-review/SKILL.md", "prompts/structure-reviewer.v1.md", "schemas/review.schema.json", "schemas/wire/review.schema.json"],
        AgentRole.TreeRepairer =>
            ["rules/common.md", "skills/tree-repair/SKILL.md", "prompts/tree-repair.v1.md", "schemas/tree.schema.json", "schemas/wire/tree.schema.json"],
        // The five scene phases (scene plan §8.1). Each role's version is the hash of exactly the
        // assets its prompt is built from, so changing one phase's skill invalidates that role's
        // qualification and no other's.
        AgentRole.PresentationClassifier =>
            ["rules/common.md", "skills/presentation-classification/SKILL.md", "prompts/presentation.v1.md", "schemas/presentation.schema.json", "schemas/wire/presentation.schema.json"],
        AgentRole.ItemTyper =>
            ["rules/common.md", "skills/item-typing/SKILL.md", "prompts/item-typing.v1.md", "schemas/items.schema.json", "schemas/wire/items.schema.json"],
        AgentRole.PersonaWindower =>
            ["rules/common.md", "skills/referent-resolution/SKILL.md", "prompts/persona-window.v1.md", "schemas/persona-window.schema.json", "schemas/wire/persona-window.schema.json"],
        AgentRole.PersonaOrchestrator =>
            ["rules/common.md", "skills/referent-orchestration/SKILL.md", "prompts/persona-orchestrator.v1.md", "schemas/registry-plan.schema.json", "schemas/wire/registry-plan.schema.json"],
        AgentRole.SceneCutter =>
            ["rules/common.md", "skills/scene-cutting/SKILL.md", "prompts/scene-cut.v1.md", "schemas/scene-cut.schema.json", "schemas/wire/scene-cut.schema.json"],
        AgentRole.SceneLinkWindower =>
            ["rules/common.md", "skills/scene-linking/SKILL.md", "prompts/scene-link-window.v1.md", "schemas/scene-links.schema.json", "schemas/wire/scene-links.schema.json"],
        AgentRole.SceneLinkOrchestrator =>
            ["rules/common.md", "skills/scene-link-orchestration/SKILL.md", "prompts/scene-link-orchestrator.v1.md", "schemas/scene-link-plan.schema.json", "schemas/wire/scene-link-plan.schema.json"],

        // The re-writer runs under Augmenter and is a TYPE rather than a role (scene plan §3.6), so
        // its skill, schema and the closed-class list it is gated against belong to this role's
        // version: changing any of them changes what an Augmenter call means.
        AgentRole.Augmenter =>
            ["rules/common.md", "prompts/augmenter.v1.md", "skills/augment/render-text/SKILL.md", "schemas/augment-render-text.schema.json", "rules/closed-class.en.txt"],
        AgentRole.AugmentReviewer =>
            ["rules/common.md", "prompts/augment-reviewer.v1.md", "schemas/review.schema.json"],
        _ => ["rules/common.md"],
    };
}

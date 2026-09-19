using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// What the five scene phases carry between them (scene plan §8).
///
/// <para>Two families, not one (§6). Scene work reads them separately because a scanned PDF of a
/// novel and a scanned PDF of a textbook share every production instinct and almost no composition
/// instinct — one field cannot carry both. <see cref="CompositionFamily"/> is the run's, which is
/// the <em>fallback</em> of the §6.1 resolution rather than the only answer: a consumer working on
/// a node resolves at that node, through <c>FamilyResolver</c>.</para>
///
/// <para><see cref="Instincts"/> is keyed by <see cref="InstinctScope"/> and not a flat list, which
/// is what keeps the "max 6 per prompt" budget spent on relevant guidance (§7.1): without the
/// scope every consumer would draw from one pool and see instincts meant for another agent.</para>
/// </summary>
public sealed record SceneContext(
    string RunId,
    string DocumentId,
    string DocumentTitle,
    string ProductionFamily,
    string CompositionFamily,
    Sensitivity Sensitivity,
    IReadOnlyDictionary<InstinctScope, IReadOnlyList<string>> Instincts)
{
    public IReadOnlyList<string> For(InstinctScope scope) =>
        Instincts.TryGetValue(scope, out var list) ? list : [];

    /// <summary>Per-role pins, so a resumed run does not switch models mid-document [orchestrator §7].</summary>
    public IReadOnlyDictionary<AgentRole, string> PinnedModels { get; init; } =
        new Dictionary<AgentRole, string>();

    public string? PinnedKey(AgentRole role) => PinnedModels.GetValueOrDefault(role);

    /// <summary>The instinct block a prompt renders, or empty when the family has none.</summary>
    public string RenderInstincts(InstinctScope scope)
    {
        var instincts = For(scope);

        return instincts.Count == 0
            ? string.Empty
            : $"## Learned guidance for this {Axis(scope)} family\n"
              + string.Join('\n', instincts.Select(i => "- " + i));
    }

    /// <summary>
    /// Which axis a scope's guidance comes from (§7.2). Metadata draws from both — a publisher's
    /// template decides where the front matter ends, and the kind of work decides whether an
    /// epigraph is part of it.
    /// </summary>
    private static string Axis(InstinctScope scope) => scope switch
    {
        InstinctScope.Labeling or InstinctScope.Structure => "production",
        InstinctScope.Metadata => "production and composition",
        _ => "composition",
    };
}

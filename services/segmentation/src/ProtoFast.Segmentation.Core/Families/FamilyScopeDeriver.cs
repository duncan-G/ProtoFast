using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Families;

/// <summary>
/// Resolves a composition family for any node: <b>nearest ancestor-or-self carrying a scope, else
/// the run's family</b> (scene plan §6.1).
///
/// <para>A single-family document has no scope roots and resolves to the run family everywhere, so
/// the ordinary case pays one lookup that terminates at the root. That is the whole cost a scope
/// imposes on a consumer — and every consumer (phase 8 for item typing, phase 9 for Persona
/// instincts, phase 10 for cutting and for <c>MinSceneSpan</c>) resolves at the node it is working
/// on, after phase 7, with its windows bounded by section. No window straddles two families and no
/// join reconciles two policies.</para>
/// </summary>
public sealed class FamilyResolver
{
    private readonly Dictionary<string, string> _scopeBySection;
    private readonly Dictionary<string, string?> _parentOf;

    public FamilyResolver(SectionNode root, IReadOnlyList<FamilyScope> scopes, string runFamily)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scopes);

        RunFamily = runFamily;
        _scopeBySection = scopes.ToDictionary(s => s.SectionId, s => s.CompositionFamily, StringComparer.Ordinal);
        _parentOf = new Dictionary<string, string?>(StringComparer.Ordinal) { [root.SectionId] = null };

        foreach (var node in root.Descend())
        {
            foreach (var child in node.Children)
            {
                _parentOf[child.SectionId] = node.SectionId;
            }
        }
    }

    public string RunFamily { get; }

    public bool HasScopes => _scopeBySection.Count > 0;

    public string Resolve(string? sectionId)
    {
        var current = sectionId;

        while (current is not null)
        {
            if (_scopeBySection.TryGetValue(current, out var family))
            {
                return family;
            }

            current = _parentOf.GetValueOrDefault(current);
        }

        return RunFamily;
    }

    /// <summary>
    /// The scope root a node lives under, or null in a single-family document. This is the value
    /// <see cref="Persona.ScopeRootSectionId"/> carries, and the thing
    /// <c>RegistryMaterializer</c> refuses to merge across.
    /// </summary>
    public string? ScopeRootOf(string? sectionId)
    {
        var current = sectionId;

        while (current is not null)
        {
            if (_scopeBySection.ContainsKey(current))
            {
                return current;
            }

            current = _parentOf.GetValueOrDefault(current);
        }

        return null;
    }
}

/// <summary>
/// Derives family scopes from phase 5's evidence, <b>in code</b> (scene plan §6.1).
///
/// <para>No second detector and no extra model call: phase 5 already emitted a feature vector per
/// paragraph, and a section is a scope root when its subtree's aggregated evidence disagrees with
/// its parent's resolved family beyond the family's learned band. Phase 7 is the place for it
/// because it is the phase that already holds both the tree and the validation machinery.</para>
///
/// <para>The four guards of §6.1 are what keep the mechanism from firing on noise, and each rules
/// out a specific false positive rather than being a generic sanity bound.</para>
/// </summary>
public static class FamilyScopeDeriver
{
    public static IReadOnlyList<FamilyScope> Derive(
        SectionNode root,
        IReadOnlyList<FamilyEvidence> evidence,
        string runFamily,
        FamilyScopeOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(options);

        var byParagraph = evidence.ToDictionary(e => e.ParagraphId, StringComparer.Ordinal);
        var documentProfile = FamilyEvidenceExtractor.Aggregate(evidence);

        if (documentProfile is null)
        {
            return [];
        }

        var candidates = new List<FamilyScope>();

        // Depth is 1-based at the root, and the root itself can never be a scope: a scope that
        // resolved to what it would have inherited is redundant, and the root inherits the run
        // family by definition.
        Walk(root, depth: 1, parentProfile: documentProfile, parentFamily: runFamily);

        return
        [
            .. candidates
                .OrderByDescending(s => s.Confidence)
                .Take(Math.Max(0, options.MaxFamilyScopes))
                .OrderBy(s => s.SectionId, StringComparer.Ordinal),
        ];

        void Walk(SectionNode node, int depth, FamilyProfile parentProfile, string parentFamily)
        {
            var subtreeEvidence = node.Descend()
                .SelectMany(n => n.ParagraphIds)
                .Where(byParagraph.ContainsKey)
                .Select(id => byParagraph[id])
                .ToList();

            var profile = FamilyEvidenceExtractor.Aggregate(subtreeEvidence);
            var family = parentFamily;

            if (depth > 1
                && profile is not null
                && depth <= options.MaxFamilyScopeDepth
                && subtreeEvidence.Count >= options.MinFamilyScopeParagraphs)
            {
                var distance = profile.DistanceTo(parentProfile);
                var detected = FamilyEvidenceExtractor.Confirm(subtreeEvidence, parentFamily);

                // Two conditions, not one. The distance says the evidence disagrees; the detected
                // family says what it disagrees *into*. A subtree that is merely noisier than its
                // parent clears the first and fails the second, and it is the second that keeps a
                // scope a claim about the kind of work rather than about the variance.
                if (distance >= options.DisagreementBand
                    && !string.Equals(detected, parentFamily, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new FamilyScope(
                        node.SectionId,
                        detected,
                        [.. subtreeEvidence.Take(MaxEvidenceIds).Select(e => e.ParagraphId)],
                        Math.Min(1.0, distance / Math.Max(options.DisagreementBand, 1e-6) * 0.5)));

                    family = detected;
                }
            }

            foreach (var child in node.Children)
            {
                Walk(child, depth + 1, profile ?? parentProfile, family);
            }
        }
    }

    /// <summary>Enough to audit the claim, few enough that the artifact stays readable.</summary>
    private const int MaxEvidenceIds = 12;
}

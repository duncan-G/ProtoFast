using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Personas;

/// <summary>The bound items and the registries, or the exact reason the plan could not be applied.</summary>
public sealed record RegistryMaterializationResult(
    IReadOnlyList<SceneItem> Items,
    Registries Registries,
    ValidationResult Validation)
{
    public bool Success => Validation.Passed;
}

/// <summary>
/// Applies a <see cref="RegistryPlan"/> to the windowers' candidates (scene plan §8.7).
///
/// <para>Pure code, and the only thing in phase 9 that can produce a registry. Two invariants are
/// <b>asserted</b> here rather than checked downstream, which is why <c>tag-resolution</c>,
/// <c>group-closure</c> and <c>persona-scope</c> are properties of this class rather than checks
/// that can fail: <b>every candidate is assigned exactly once</b>, and <b>no merge crosses a family
/// scope root</b>. A plan that would orphan a candidate or merge across a scope cannot be applied,
/// and the error names the offending row so the repair round has something to act on.</para>
///
/// <para>The scope rule is the one worth stating plainly, because it looks like a restriction and
/// is really a division of labour: the orchestrator judges whether two surface forms co-refer, and
/// code decides whether they were ever eligible to. The asymmetry that settles it is §6.1's — a
/// false merge bleeds one story's voice into another's and is nearly invisible in review, while a
/// missed merge costs a duplicated registry entry that K3 never promised to unify, since K3's
/// promise is consistency <em>within</em> a work.</para>
/// </summary>
public static class RegistryMaterializer
{
    public const string RegistryPlanCheck = "registry-plan";

    /// <summary>
    /// The explicit persona every unattributed utterance binds to (C8). It is a real registry entry
    /// rather than a null speaker, because "no anonymous speakers" is only enforceable if there is
    /// something for an anonymous speaker to be.
    /// </summary>
    public const string UnattributedName = "Unattributed";

    /// <summary>The voice that tells without appearing. A persona like any other [unit §3.3].</summary>
    public const string NarratorName = "Narrator";

    public static RegistryMaterializationResult Apply(
        RegistryPlan plan,
        CandidateDigest digest,
        IReadOnlyList<SceneItem> items,
        FamilyResolver families)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(families);

        var candidates = digest.AllCandidates.ToDictionary(c => c.Ref, StringComparer.Ordinal);
        var errors = new List<string>();
        var assignedBy = new Dictionary<string, int>(StringComparer.Ordinal);

        // Document order, from the tags the candidates carry. Ids are allocated in this order so a
        // registry reads like the document does, and so two runs over the same document produce the
        // same ids (K2).
        var firstTagOf = candidates.Values.ToDictionary(
            c => c.Ref,
            c => c.TagIds.Order(StringComparer.Ordinal).FirstOrDefault() ?? string.Empty,
            StringComparer.Ordinal);

        var rows = new List<(RegistryRow Row, IReadOnlyList<ReferentCandidate> Members, string Order)>();

        for (var i = 0; i < plan.Registry.Count; i++)
        {
            var row = plan.Registry[i];
            var label = $"registry row {i + 1}";
            var members = new List<ReferentCandidate>();

            if (string.IsNullOrWhiteSpace(row.CanonicalName))
            {
                errors.Add($"{label}: a registry entry needs a canonical name");
            }

            foreach (var reference in row.Candidates)
            {
                if (!candidates.TryGetValue(reference, out var candidate))
                {
                    errors.Add(
                        $"{label}: '{reference}' is not a candidate any window returned — references are "
                        + "issued by the pipeline and may not be invented");
                    continue;
                }

                if (assignedBy.TryGetValue(reference, out var firstRow))
                {
                    errors.Add($"{label}: '{reference}' was already assigned by row {firstRow + 1}");
                    continue;
                }

                assignedBy[reference] = i;
                members.Add(candidate);
            }

            if (members.Count == 0)
            {
                errors.Add($"{label}: names no candidate that exists");
                continue;
            }

            // The scope rule. Checked before anything is allocated, so a plan that violates it
            // produces no registry at all rather than a half-built one.
            var scopeRoots = members
                .Select(m => families.ScopeRootOf(m.SectionId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (scopeRoots.Count > 1)
            {
                errors.Add(
                    $"{label}: merges candidates from different family scopes "
                    + $"({string.Join(", ", scopeRoots.Select(s => s ?? "the document"))}) — a persona is "
                    + "persistent within its family scope and never merges out of it. A character "
                    + "genuinely recurring across an anthology is a link between registries, not one entry.");
            }

            // Ordering key: the earliest tag id any of the row's candidates carries. Tag ids are
            // zero-padded and issued in document order, so ordinal comparison on them IS document
            // order — and a row whose candidates carry no tags falls back to its first reference,
            // which is issued in window order and therefore also ascending.
            var earliestTag = members.Select(m => firstTagOf[m.Ref]).Where(t => t.Length > 0).Order(StringComparer.Ordinal).FirstOrDefault();

            rows.Add((row, members, earliestTag ?? members[0].Ref));
        }

        foreach (var orphan in candidates.Values.Where(c => !assignedBy.ContainsKey(c.Ref)))
        {
            errors.Add(
                $"candidate '{orphan.Ref}' ('{orphan.SurfaceForm}') is in no registry row — every "
                + "candidate is assigned exactly once, to a new entry if it merges with nothing");
        }

        if (errors.Count > 0)
        {
            return new RegistryMaterializationResult(
                items, Registries.Empty, ValidationResult.Fail(RegistryPlanCheck, errors));
        }

        return Build(rows, families, items);
    }

    private static RegistryMaterializationResult Build(
        List<(RegistryRow Row, IReadOnlyList<ReferentCandidate> Members, string Order)> rows,
        FamilyResolver families,
        IReadOnlyList<SceneItem> items)
    {
        var personas = new List<Persona>();
        var places = new List<PlaceEntry>();
        var exhibits = new List<ExhibitEntry>();

        // Tag id -> referent id. The whole output of the phase is this map plus the registries; the
        // items themselves are rewritten from it below and in no other way.
        var binding = new Dictionary<string, string>(StringComparer.Ordinal);
        var personaOfCandidate = new Dictionary<string, string>(StringComparer.Ordinal);

        var personaCounter = 0;
        var placeCounter = 0;
        var exhibitCounter = 0;

        foreach (var (row, members, _) in rows.OrderBy(r => r.Order, StringComparer.Ordinal))
        {
            var kind = members[0].Kind;
            var surfaceForms = members
                .SelectMany(m => m.SurfaceForms.Count > 0 ? m.SurfaceForms : [m.SurfaceForm])
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var evidence = members.SelectMany(m => m.TagIds).Distinct(StringComparer.Ordinal).ToList();
            var referentId = kind switch
            {
                TagKind.Place => Ids.Place(placeCounter++),
                TagKind.ExhibitRef => Ids.Exhibit(exhibitCounter++),
                _ => Ids.Persona(personaCounter++),
            };

            switch (kind)
            {
                case TagKind.Place:
                    places.Add(new PlaceEntry(referentId, row.CanonicalName, surfaceForms, evidence));
                    break;

                case TagKind.ExhibitRef:
                    exhibits.Add(new ExhibitEntry(
                        referentId, row.CanonicalName, members[0].SectionId ?? string.Empty, evidence));
                    break;

                default:
                    var personaKind = string.Equals(row.Kind, "group", StringComparison.OrdinalIgnoreCase)
                        || kind == TagKind.Group
                        ? PersonaKind.Group
                        : PersonaKind.Individual;

                    personas.Add(new Persona(
                        referentId,
                        row.CanonicalName,
                        personaKind,
                        string.Equals(row.Scope, "scene-local", StringComparison.OrdinalIgnoreCase)
                            ? PersonaScope.SceneLocal
                            : PersonaScope.Persistent,
                        families.ScopeRootOf(members[0].SectionId),
                        surfaceForms,
                        personaKind == PersonaKind.Group
                            ? Formation(row, members)
                            : null,
                        evidence));
                    break;
            }

            foreach (var member in members)
            {
                personaOfCandidate[member.Ref] = referentId;

                foreach (var tagId in member.TagIds)
                {
                    binding[tagId] = referentId;
                }
            }
        }

        var bound = items
            .Select(item => Bind(item, binding, personaOfCandidate, rows))
            .ToList();

        return new RegistryMaterializationResult(
            bound,
            new Registries(personas, places, exhibits),
            ValidationResult.Pass(RegistryPlanCheck));
    }

    private static GroupFormation Formation(RegistryRow row, IReadOnlyList<ReferentCandidate> members) =>
        string.Equals(row.Formation, "enumerated", StringComparison.OrdinalIgnoreCase)
        || members.Any(m => m.Formation == GroupFormation.Enumerated)
            ? GroupFormation.Enumerated
            : GroupFormation.Named;

    /// <summary>
    /// Binds one item's tags and its speaker. <see cref="GroupMembership"/> is attached <b>to the
    /// tag</b>, resolved from the members that reference named at <em>this point</em> in the
    /// document — which is the whole of §4.3: identity persists, the roster does not.
    ///
    /// <para><paramref name="binding"/> is keyed by the ids a candidate claimed, and those are not
    /// always tag ids. A speaker resolved from an attribution clause or a speaker label — the whole
    /// transcript and screenplay case, and most quoted dialogue in prose — has no
    /// <see cref="TagKind.Persona"/> tag to bind through, so its cluster claims the <em>item</em>
    /// ids instead. Binding only through tags would leave every deterministically-resolved
    /// utterance unattributed, which C8 then reports as a missing speaker rather than as the
    /// binding gap it is.</para>
    /// </summary>
    private static SceneItem Bind(
        SceneItem item,
        IReadOnlyDictionary<string, string> binding,
        IReadOnlyDictionary<string, string> personaOfCandidate,
        List<(RegistryRow Row, IReadOnlyList<ReferentCandidate> Members, string Order)> rows)
    {
        var membershipByTag = rows
            .SelectMany(r => r.Members)
            .Where(m => m.MemberRefs.Count > 0)
            .SelectMany(m => m.TagIds.Select(tagId => (tagId, Member: m)))
            .ToDictionary(x => x.tagId, x => x.Member, StringComparer.Ordinal);

        var tags = item.Tags
            .Select(tag => tag with
            {
                ReferentId = binding.GetValueOrDefault(tag.TagId),
                Membership = tag.Kind == TagKind.Group && membershipByTag.TryGetValue(tag.TagId, out var source)
                    ? new GroupMembership(
                        [
                            .. source.MemberRefs
                                .Select(personaOfCandidate.GetValueOrDefault)
                                .OfType<string>(),
                        ],
                        source.MembershipComplete)
                    : tag.Membership,
            })
            .ToList();

        if (item.Speech is not { } speech)
        {
            return item with { Tags = tags };
        }

        // The speaker binds from the Persona or Group tag inside the item's own span where there is
        // one, and from the item's own id where the cluster was resolved deterministically. Both are
        // lookups rather than a second judgement. An item that matches neither keeps a null speaker
        // here and is bound to Unattributed by CloseCast (C8).
        var speaker = tags
            .FirstOrDefault(t => t.Kind is TagKind.Persona or TagKind.Group && t.ReferentId is not null)
            ?.ReferentId
            ?? binding.GetValueOrDefault(item.ItemId);

        return item with
        {
            Tags = tags,
            Speech = speech with { SpeakerPersonaId = speaker ?? speech.SpeakerPersonaId },
        };
    }

    /// <summary>
    /// Binds every still-unattributed speaker to the <c>Unattributed</c> persona, registering it on
    /// first use. C8 says there are no anonymous speakers; this is what makes that true without
    /// inventing a name for one (C11).
    /// </summary>
    public static (IReadOnlyList<SceneItem> Items, Registries Registries) CloseCast(
        IReadOnlyList<SceneItem> items, Registries registries)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(registries);

        if (!items.Any(i => i.Speech is { SpeakerPersonaId: null }))
        {
            return (items, registries);
        }

        var unattributed = registries.Personas
            .FirstOrDefault(p => p.CanonicalName == UnattributedName);

        if (unattributed is null)
        {
            unattributed = new Persona(
                Ids.Persona(registries.Personas.Count),
                UnattributedName,
                PersonaKind.Individual,
                PersonaScope.Persistent,
                ScopeRootSectionId: null,
                [UnattributedName],
                Formation: null,
                EvidenceIds: []);

            registries = registries with { Personas = [.. registries.Personas, unattributed] };
        }

        var closed = items
            .Select(item => item.Speech is { SpeakerPersonaId: null } speech
                ? item with { Speech = speech with { SpeakerPersonaId = unattributed.PersonaId } }
                : item)
            .ToList();

        return (closed, registries);
    }
}

using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>
/// What the agent supplies for one proposed scene: the two coordinates that genuinely need
/// judgement, plus a generated title (C9).
///
/// <para>Mode and Cast are not here, and that is the point of typing items first: they are derived
/// from the item mix and from resolved Speech speakers, leaving the model only Setting and
/// Subject (§8.3). Setting arrives as a <b>place id</b> the tag layer already resolved, never as a
/// name — a model that could name a place could invent one, and C11 forbids exactly that.</para>
/// </summary>
public sealed record SceneAssignment(
    int Index,
    string? PlaceId,
    string Subject,
    TimeValue Time,
    string? Title,
    IReadOnlyList<string> EvidenceIds);

/// <summary>The scenes of one leaf section, or the exact reason the assignment could not be applied.</summary>
public sealed record SceneMaterializationResult(IReadOnlyList<Scene> Scenes, ValidationResult Validation)
{
    public bool Success => Validation.Passed;
}

/// <summary>
/// Step 3 of scene plan §8.8: turns confirmed boundaries and coordinate assignments into
/// <see cref="Scene"/> records, with C1–C8 and C12 enforced.
///
/// <para>Most of the constraints are <b>structural</b> here rather than checked. Scenes are the
/// runs between consecutive boundaries inside one leaf section, so C1 (partition), C2 (contiguity
/// and order), C4 (leaf) and C12 (no straddling) hold by construction, and C5 holds because a
/// coordinate is one field. What is actually checked is C8 — every cast entry resolves to a
/// persona in the registry — and the four bounds on inheritance, which are the only places a wrong
/// answer could propagate past the scene that produced it.</para>
/// </summary>
public static class SceneMaterializer
{
    public const string SceneAssignmentCheck = "scene-assignment";

    public static SceneMaterializationResult Materialize(
        string sectionId,
        IReadOnlyList<SceneItem> items,
        IReadOnlyList<BoundaryProposal> boundaries,
        IReadOnlyList<SceneMode> modeByItem,
        IReadOnlyDictionary<int, SceneAssignment> assignments,
        Registries registries,
        SceneCutOptions options,
        int ordinalOffset,
        Scene? previousSceneInSection = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(boundaries);
        ArgumentNullException.ThrowIfNull(modeByItem);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(options);

        if (items.Count == 0)
        {
            return new SceneMaterializationResult([], ValidationResult.Pass(SceneAssignmentCheck));
        }

        var errors = new List<string>();
        var personaIds = registries.Personas.Select(p => p.PersonaId).ToHashSet(StringComparer.Ordinal);
        var placeIds = registries.Places.Select(p => p.PlaceId).ToHashSet(StringComparer.Ordinal);

        var starts = new List<int> { 0 };
        starts.AddRange(boundaries.Select(b => b.ItemIndex).Where(i => i > 0 && i < items.Count).Distinct().Order());

        var scenes = new List<Scene>();
        var previous = previousSceneInSection;
        var inheritanceDepth = previousSceneInSection?.InheritanceDepth ?? 0;

        for (var s = 0; s < starts.Count; s++)
        {
            var start = starts[s];
            var end = s + 1 < starts.Count ? starts[s + 1] : items.Count;
            var run = items.Skip(start).Take(end - start).ToList();

            if (run.Count == 0)
            {
                // C3: a scene owns at least one item. Two boundaries at one index would produce an
                // empty run, and an empty scene is not a degraded scene — it is not a scene.
                continue;
            }

            var assignment = assignments.GetValueOrDefault(s)
                ?? new SceneAssignment(s, null, string.Empty, TimeValue.Unanchored, null, []);

            if (assignment.PlaceId is { } place && !placeIds.Contains(place))
            {
                errors.Add(
                    $"scene {s + 1} of {sectionId}: place '{place}' is not in the run's place registry — "
                    + "a Setting is a resolved place or it is Void, never a name");
                continue;
            }

            var mode = DominantMode(modeByItem, start, end);
            var cast = DeriveCast(run, personaIds, errors, sectionId, s);
            var setting = ResolveSetting(run, assignment, mode, previous, options, ref inheritanceDepth);

            var situation = new Situation(
                setting.PlaceId,
                setting.Provenance,
                assignment.Time,
                cast,
                mode,
                string.IsNullOrWhiteSpace(assignment.Subject)
                    ? SubjectValue.None
                    : new SubjectValue(assignment.Subject, Provenance.Stated(assignment.EvidenceIds)));

            var paragraphIds = run.Select(i => i.ParagraphId).Distinct(StringComparer.Ordinal).ToList();
            var sceneId = Ids.Scene(ordinalOffset + scenes.Count, run[0].ParagraphId, run[0].StartOffset);

            scenes.Add(new Scene(
                sceneId,
                sectionId,
                run[0].ItemId,
                run[^1].ItemId,
                [.. run.Select(i => i.ItemId)],
                situation,
                Links: [],
                assignment.Title,
                TitleInferred: true,
                Flags: DeriveFlags(situation, paragraphIds, setting.Depth, options),
                ContentHash: ContentHash(run, situation))
            {
                InheritanceDepth = setting.Depth,
                ParagraphIds = paragraphIds,
            });

            previous = scenes[^1];
        }

        if (errors.Count > 0)
        {
            return new SceneMaterializationResult([], ValidationResult.Fail(SceneAssignmentCheck, errors));
        }

        return new SceneMaterializationResult(scenes, ValidationResult.Pass(SceneAssignmentCheck));
    }

    /// <summary>
    /// The mode of a run. Boundaries were proposed at mode changes, so a run is normally
    /// homogeneous; where the agent rejected a boundary it may not be, and the plurality wins
    /// rather than the first item — a run whose mode was overruled should read as what it mostly is.
    /// </summary>
    private static SceneMode DominantMode(IReadOnlyList<SceneMode> modes, int start, int end) =>
        modes.Skip(start).Take(end - start)
            .GroupBy(m => m)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;

    /// <summary>
    /// Cast from <b>role</b>, never from tag count [unit §3.3]. A persona tagged only in running
    /// text is <em>referenced</em> and stays out of the cast however often it occurs — the single
    /// rule that removes most cast ambiguity in expository text.
    ///
    /// <para>Presence propagates one way: a group in the cast puts the members <em>its tag resolved
    /// at this scene</em> into the scene; members being present does not conjure the group.</para>
    /// </summary>
    private static IReadOnlyList<CastEntry> DeriveCast(
        IReadOnlyList<SceneItem> run,
        IReadOnlySet<string> personaIds,
        List<string> errors,
        string sectionId,
        int sceneIndex)
    {
        var roles = new Dictionary<string, (CastRole Role, List<string> Evidence)>(StringComparer.Ordinal);

        void Add(string personaId, CastRole role, string evidenceId)
        {
            if (!personaIds.Contains(personaId))
            {
                errors.Add(
                    $"scene {sceneIndex + 1} of {sectionId}: cast entry '{personaId}' is not in the run's "
                    + "persona registry (C8)");
                return;
            }

            if (roles.TryGetValue(personaId, out var existing))
            {
                existing.Evidence.Add(evidenceId);

                // Speaking outranks Addressed outranks Present: the strongest role a persona held
                // anywhere in the scene is the role it has in the scene.
                if (role < existing.Role)
                {
                    roles[personaId] = (role, existing.Evidence);
                }

                return;
            }

            roles[personaId] = (role, [evidenceId]);
        }

        foreach (var item in run)
        {
            if (item.Speech is { SpeakerPersonaId: { } speaker })
            {
                Add(speaker, CastRole.Speaking, item.ItemId);
            }

            // An actor is present by acting. This is the one role derivable from a tag, and it is
            // derivable because Action is an event with an agent — not because the persona was
            // mentioned.
            if (item.Kind == ItemKind.Action)
            {
                foreach (var tag in item.Tags.Where(t => t.Kind is TagKind.Persona or TagKind.Group))
                {
                    if (tag.ReferentId is { } referent)
                    {
                        Add(referent, CastRole.Present, item.ItemId);
                    }

                    foreach (var member in tag.Membership?.MemberPersonaIds ?? [])
                    {
                        Add(member, CastRole.Present, item.ItemId);
                    }
                }
            }
        }

        return
        [
            .. roles
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new CastEntry(
                    kv.Key, kv.Value.Role, Provenance.Stated(kv.Value.Evidence))),
        ];
    }

    /// <summary>
    /// Setting, and the four bounds on inheriting it [unit §3.1.1].
    ///
    /// <para>Unbounded inheritance is the real hazard — one wrong place silently poisons every
    /// scene after it — so inheritance is bounded on <b>mode</b> (only where Setting is live),
    /// <b>section</b> (the caller passes only a sibling in the same leaf section), <b>evidence</b>
    /// (any Place tag resolving elsewhere breaks it) and <b>depth</b>. Anything not inherited under
    /// those four is Void, which remains the default: a scene is located only when the text locates
    /// it, and the absence of a place claim is itself the derivation, so Void needs no evidence
    /// under C13.</para>
    /// </summary>
    private static (string? PlaceId, Provenance? Provenance, int Depth) ResolveSetting(
        IReadOnlyList<SceneItem> run,
        SceneAssignment assignment,
        SceneMode mode,
        Scene? previous,
        SceneCutOptions options,
        ref int depth)
    {
        var placeTags = run
            .SelectMany(i => i.Tags.Where(t => t.Kind == TagKind.Place && t.ReferentId is not null))
            .ToList();

        // Stated: the text located this scene, and the Place tags are the citation (§4.4). Cast and
        // Setting are the two coordinates C13 evidences mechanically, and this is why.
        var stated = assignment.PlaceId
            ?? placeTags.Select(t => t.ReferentId!).Distinct(StringComparer.Ordinal).SingleOrDefaultSafe();

        if (stated is not null)
        {
            depth = 0;
            return (stated, Provenance.Stated([.. placeTags.Select(t => t.TagId).DefaultIfEmpty(run[0].ItemId)]), 0);
        }

        // Bound 1 — mode. Elsewhere Setting is Void and inheritance is moot.
        if (mode is not (SceneMode.Enacted or SceneMode.Narrated))
        {
            depth = 0;
            return (null, null, 0);
        }

        // Bound 3 — evidence. A Place tag resolving to a different place breaks the chain, as do
        // several places in one run: C5 says two places means two scenes, so a run with two is one
        // the agent should have cut, and inheriting through it would hide that.
        if (placeTags.Count > 0 || previous?.Situation.PlaceId is not { } inherited)
        {
            depth = 0;
            return (null, null, 0);
        }

        // Bound 4 — depth. Beyond the ceiling the chain stops rather than raising a flag and
        // continuing: a flag catches drift for a reviewer, and stopping is what keeps the drift from
        // reaching the scenes after this one.
        if (depth >= options.MaxInheritanceDepth)
        {
            depth = 0;
            return (null, null, 0);
        }

        depth++;

        // C7: inherited values are copied in with the source scene id. A scene never says "as
        // before"; a reader of one scene record never has to look elsewhere.
        return (inherited, Provenance.Inherited(previous.SceneId, [previous.SceneId]), depth);
    }

    /// <summary>
    /// The derived flags [unit §3.1, C14]. Rules over the tuple, evaluated in code after
    /// assignment: they cost nothing at assignment time, never block, and can be retuned per family
    /// without touching the unit.
    /// </summary>
    private static IReadOnlyList<Flag> DeriveFlags(
        Situation situation, IReadOnlyList<string> paragraphIds, int depth, SceneCutOptions options)
    {
        var flags = new List<Flag>();

        if (situation.Mode == SceneMode.Enacted && situation.IsVoid)
        {
            flags.Add(new Flag(
                Flag.UnlocatedEnactment,
                "people are acting somewhere but the text does not say where"));
        }

        if (paragraphIds.Count > options.OversizedSceneParagraphs)
        {
            flags.Add(new Flag(
                Flag.OversizedScene,
                $"{paragraphIds.Count} paragraphs touched, over the advisory ceiling of "
                + $"{options.OversizedSceneParagraphs} (C14) — advisory, never an automatic cut"));
        }

        if (depth > options.MaxInheritanceDepth)
        {
            flags.Add(new Flag(
                Flag.DeepSettingInheritance,
                $"setting carried through {depth} scenes without being restated"));
        }

        return flags;
    }

    /// <summary>
    /// The scene's own hash, over item ids and the situation — never over text. A scene names text
    /// and does not carry it (C9), so its hash is a hash of references, which is also what makes it
    /// stable across a re-run that left the paragraphs alone (C10, K6).
    /// </summary>
    private static string ContentHash(IReadOnlyList<SceneItem> run, Situation situation) =>
        Ids.Sha256Hex(string.Join(
            '|',
            [
                .. run.Select(i => $"{i.ItemId}:{i.ParagraphId}:{i.StartOffset}:{i.EndOffset}:{i.Kind}"),
                situation.Mode.ToString(),
                situation.PlaceId ?? "void",
                situation.Time.Relation.ToString(),
                situation.Subject.Text,
                .. situation.Cast.Select(c => $"{c.PersonaId}:{c.Role}"),
            ]));

    /// <summary>
    /// <see cref="Enumerable.SingleOrDefault{T}(IEnumerable{T})"/> throws on two; here two is an
    /// ordinary answer meaning "this run is not single-placed", and the caller reads null as Void.
    /// </summary>
    private static string? SingleOrDefaultSafe(this IEnumerable<string> values)
    {
        string? only = null;

        foreach (var value in values)
        {
            if (only is not null)
            {
                return null;
            }

            only = value;
        }

        return only;
    }
}

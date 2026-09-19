using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;

namespace ProtoFast.Segmentation.Core.Validation;

/// <summary>
/// The deterministic gates of scene plan §9. Pure functions, like <see cref="Checks"/>: no model,
/// no I/O, no clock.
///
/// <para>Five of these are <b>not checks at all in the phase that owns them</b>.
/// <see cref="CheckTagResolution"/>, <see cref="CheckGroupClosure"/>,
/// <see cref="CheckPersonaScope"/> and the two link checks are asserted by their materializers
/// (§8.7, §8.9): a plan that would violate one cannot be applied, and the error names the offending
/// op. They exist here because the freeze re-runs them, where an artifact is being <em>checked</em>
/// rather than <em>constructed</em> — and a check that can only fail when storage has been
/// corrupted is exactly what a freeze gate should be.</para>
/// </summary>
public static class SceneChecks
{
    public const string FamilyScope = "family-scope";
    public const string FamilyHomogeneity = "family-homogeneity";
    public const string PresentationIntegrity = "presentation-integrity";
    public const string MetadataRecall = "metadata-recall";
    public const string ItemCoverage = "item-coverage";
    public const string DisplayIntegrity = "display-integrity";
    public const string TagBounds = "tag-bounds";
    public const string TagResolution = "tag-resolution";
    public const string GroupClosure = "group-closure";
    public const string PersonaScope = "persona-scope";
    public const string ScenePartition = "scene-partition";
    public const string SceneLeaf = "scene-leaf";
    public const string TreeLeafScenes = "tree-leaf-scenes";
    public const string SituationSingle = "situation-single";
    public const string SituationTotal = "situation-total";
    public const string CastClosure = "cast-closure";
    public const string NoFabrication = "no-fabrication";
    public const string EvidencePresent = "evidence-present";
    public const string SectionContainment = "section-containment";
    public const string LinkIntegrity = "link-integrity";
    public const string LinkEvidence = "link-evidence";

    // ---- Phase 5 ------------------------------------------------------------------------------

    /// <summary>
    /// Displayable and metadata paragraphs, in order, equal the cleaned source. <b>Hard gate.</b>
    ///
    /// <para>This is the metadata half of integrity, and it is a separate check at a separate phase
    /// from <c>display-integrity</c> on purpose (§3.4): exclusion is never deletion, so the frozen
    /// record still accounts for every character while the scene stream sees only the displayable
    /// ones.</para>
    /// </summary>
    public static ValidationResult CheckPresentationIntegrity(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<ParagraphPresentation> presentations,
        IReadOnlyList<Assembly.HeadingRecord>? headings = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(presentations);

        var errors = new List<string>();
        var classified = presentations.Select(p => p.ParagraphId).ToHashSet(StringComparer.Ordinal);

        foreach (var paragraph in paragraphs.Where(p => !classified.Contains(p.ParagraphId)))
        {
            errors.Add($"{paragraph.ParagraphId} has no presentation verdict — absence is not a class");
        }

        foreach (var presentation in presentations)
        {
            if (presentation.Presentation == Presentation.Metadata && presentation.Class is null)
            {
                errors.Add($"{presentation.ParagraphId} is metadata with no class");
            }

            if (presentation.Presentation == Presentation.Displayable && presentation.Class is not null)
            {
                errors.Add(
                    $"{presentation.ParagraphId} is displayable but carries the metadata class "
                    + $"'{presentation.Class}'");
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Fail(PresentationIntegrity, errors);
        }

        // The reconstruction is over EVERY paragraph, displayable and metadata alike, which is the
        // whole claim: classifying a paragraph withheld it from the scene stream and changed
        // nothing about the record.
        var reconstructed = TextMetrics.NormalizeWhitespace(Checks.Reconstruct(paragraphs, headings));
        var byPresentation = TextMetrics.NormalizeWhitespace(Checks.Reconstruct(
            [.. paragraphs.OrderBy(p => p.FirstLineId, StringComparer.Ordinal)], headings));

        return string.Equals(reconstructed, byPresentation, StringComparison.Ordinal)
            ? ValidationResult.Pass(PresentationIntegrity)
            : ValidationResult.Fail(
                PresentationIntegrity,
                "the paragraphs do not reconstruct to the same text when read in document order");
    }

    /// <summary>The metadata rate falls within the family's learned band. <b>Warn → review.</b></summary>
    public static ValidationResult CheckMetadataRecall(
        IReadOnlyList<ParagraphPresentation> presentations, PresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(presentations);
        ArgumentNullException.ThrowIfNull(options);

        if (presentations.Count == 0)
        {
            return ValidationResult.Pass(MetadataRecall);
        }

        var rate = (double)presentations.Count(p => p.Presentation == Presentation.Metadata) / presentations.Count;

        return rate >= options.MinMetadataRate && rate <= options.MaxMetadataRate
            ? ValidationResult.Pass(MetadataRecall)
            : ValidationResult.Fail(
                MetadataRecall,
                $"{rate:P1} of paragraphs were classified as metadata, outside the expected band "
                + $"{options.MinMetadataRate:P0}..{options.MaxMetadataRate:P0}");
    }

    // ---- Phase 7 ------------------------------------------------------------------------------

    /// <summary>
    /// Every family scope root is an existing section id, differs from its parent's resolved
    /// family, and satisfies the §6.1 guards. <b>Hard gate.</b>
    /// </summary>
    public static ValidationResult CheckFamilyScope(
        SectionNode root,
        IReadOnlyList<FamilyScope> scopes,
        string runFamily,
        FamilyScopeOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        var paragraphCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        Measure(root, 1);

        if (scopes.Count > options.MaxFamilyScopes)
        {
            errors.Add(
                $"{scopes.Count} family scopes, over the ceiling of {options.MaxFamilyScopes} — a volume "
                + "holds tens of works; hundreds means the detector is chasing sections");
        }

        var resolver = new Families.FamilyResolver(root, scopes, runFamily);

        foreach (var scope in scopes)
        {
            if (!depths.TryGetValue(scope.SectionId, out var depth))
            {
                errors.Add($"family scope names '{scope.SectionId}', which is not a section of this tree");
                continue;
            }

            if (depth > options.MaxFamilyScopeDepth)
            {
                errors.Add(
                    $"{scope.SectionId}: a family scope at depth {depth} is a detector error, not a "
                    + $"document — anthology stories and course-pack units sit at depth "
                    + $"{options.MaxFamilyScopeDepth} or above");
            }

            if (paragraphCounts.GetValueOrDefault(scope.SectionId) < options.MinFamilyScopeParagraphs)
            {
                errors.Add(
                    $"{scope.SectionId}: {paragraphCounts.GetValueOrDefault(scope.SectionId)} paragraphs, "
                    + $"under the floor of {options.MinFamilyScopeParagraphs} — a short vignette inside a "
                    + "textbook is an Enacted island, not a second composition family");
            }

            // A scope resolving to what it would have inherited is redundant. Resolving the PARENT
            // rather than the node itself is the point: the node resolves to its own scope by
            // definition, so comparing against that would make the guard vacuous.
            var parentFamily = resolver.Resolve(ParentOf(root, scope.SectionId));
            if (string.Equals(parentFamily, scope.CompositionFamily, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"{scope.SectionId}: resolves to '{scope.CompositionFamily}', which is what it would "
                    + "have inherited — a redundant scope is dropped, not stored");
            }
        }

        return ValidationResult.Fail(FamilyScope, errors);

        void Measure(SectionNode node, int depth)
        {
            depths[node.SectionId] = depth;
            paragraphCounts[node.SectionId] = node.Descend().Sum(n => n.ParagraphIds.Count);

            foreach (var child in node.Children)
            {
                Measure(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// Paragraph-level family evidence disagreeing with its section's resolved family stays inside
    /// the family's learned band. <b>Warn → review.</b>
    /// </summary>
    public static ValidationResult CheckFamilyHomogeneity(
        SectionNode root,
        IReadOnlyList<FamilyEvidence> evidence,
        IReadOnlyList<FamilyScope> scopes,
        string runFamily,
        FamilyScopeOptions options)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(options);

        var byParagraph = evidence.ToDictionary(e => e.ParagraphId, StringComparer.Ordinal);
        var resolver = new Families.FamilyResolver(root, scopes, runFamily);
        var warnings = new List<string>();

        foreach (var node in root.Descend().Where(n => n.ParagraphIds.Count > 0))
        {
            var rows = node.ParagraphIds
                .Where(byParagraph.ContainsKey)
                .Select(id => byParagraph[id])
                .ToList();

            var profile = FamilyEvidenceExtractor.Aggregate(rows);
            if (profile is null || rows.Count < 5)
            {
                continue;
            }

            var resolved = resolver.Resolve(node.SectionId);
            var detected = FamilyEvidenceExtractor.Confirm(rows, resolved);

            if (!string.Equals(detected, resolved, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"{node.SectionId} ('{node.Title}') resolves to '{resolved}' but its {rows.Count} "
                    + $"paragraphs read as '{detected}'");
            }
        }

        return ValidationResult.Fail(FamilyHomogeneity, warnings);
    }

    // ---- Phase 8 ------------------------------------------------------------------------------

    /// <summary>
    /// Every character of every <b>displayable</b> paragraph lies in exactly one item's span.
    /// <b>Hard gate.</b>
    /// </summary>
    public static ValidationResult CheckItemCoverage(
        IReadOnlyList<Paragraph> displayable, IReadOnlyList<SceneItem> items)
    {
        ArgumentNullException.ThrowIfNull(displayable);
        ArgumentNullException.ThrowIfNull(items);

        var errors = new List<string>();
        var byParagraph = items.ToLookup(i => i.ParagraphId, StringComparer.Ordinal);

        foreach (var paragraph in displayable)
        {
            var spans = byParagraph[paragraph.ParagraphId].OrderBy(i => i.StartOffset).ToList();

            if (spans.Count == 0)
            {
                errors.Add($"{paragraph.ParagraphId}: no items — a displayable paragraph is covered or it is metadata");
                continue;
            }

            if (spans[0].StartOffset != 0)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: the first item starts at {spans[0].StartOffset}, leaving "
                    + $"characters 0..{spans[0].StartOffset - 1} in no item");
            }

            for (var i = 1; i < spans.Count; i++)
            {
                if (spans[i].StartOffset > spans[i - 1].EndOffset)
                {
                    errors.Add(
                        $"{paragraph.ParagraphId}: characters {spans[i - 1].EndOffset}..{spans[i].StartOffset - 1} "
                        + $"lie between {spans[i - 1].ItemId} and {spans[i].ItemId} and belong to neither");
                }
                else if (spans[i].StartOffset < spans[i - 1].EndOffset)
                {
                    errors.Add(
                        $"{paragraph.ParagraphId}: {spans[i - 1].ItemId} and {spans[i].ItemId} overlap at "
                        + $"characters {spans[i].StartOffset}..{spans[i - 1].EndOffset - 1}");
                }
            }

            if (spans[^1].EndOffset != paragraph.Text.Length)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: the last item ends at {spans[^1].EndOffset} of "
                    + $"{paragraph.Text.Length} characters");
            }
        }

        var known = displayable.Select(p => p.ParagraphId).ToHashSet(StringComparer.Ordinal);
        foreach (var stray in items.Where(i => !known.Contains(i.ParagraphId)))
        {
            errors.Add(
                $"{stray.ItemId} names paragraph '{stray.ParagraphId}', which is not displayable — "
                + "items exist only over displayable paragraphs, which is what lets C1 partition "
                + "without an escape clause");
        }

        return ValidationResult.Fail(ItemCoverage, errors);
    }

    /// <summary>
    /// A paragraph's item spans, concatenated in order, reproduce that paragraph's frozen text
    /// exactly. <b>Hard gate</b>, and the item-level analogue of <c>text-integrity</c>.
    ///
    /// <para>It runs over <b>spans only</b> and never sees render text (§3.6), which is why adding
    /// a re-writer cannot weaken it.</para>
    /// </summary>
    public static ValidationResult CheckDisplayIntegrity(
        IReadOnlyList<Paragraph> displayable, IReadOnlyList<SceneItem> items)
    {
        ArgumentNullException.ThrowIfNull(displayable);
        ArgumentNullException.ThrowIfNull(items);

        var errors = new List<string>();
        var byParagraph = items.ToLookup(i => i.ParagraphId, StringComparer.Ordinal);

        foreach (var paragraph in displayable)
        {
            var spans = byParagraph[paragraph.ParagraphId].OrderBy(i => i.StartOffset).ToList();
            if (spans.Count == 0)
            {
                continue;
            }

            var rebuilt = string.Concat(spans.Select(s => s.SpanOf(paragraph.Text)));

            if (!string.Equals(rebuilt, paragraph.Text, StringComparison.Ordinal))
            {
                var at = Checks.FirstDifference(paragraph.Text, rebuilt);
                errors.Add(
                    $"{paragraph.ParagraphId}: the item spans do not reproduce the paragraph — first "
                    + $"difference at character {at}: expected '{Checks.Excerpt(paragraph.Text, at, 20)}', "
                    + $"got '{Checks.Excerpt(rebuilt, at, 20)}'");
            }
        }

        return ValidationResult.Fail(DisplayIntegrity, errors);
    }

    /// <summary>
    /// Every tag's offsets lie inside its item's span; tags nest or are disjoint, never partially
    /// overlap. <b>Hard gate.</b>
    ///
    /// <para>A containment test between two ranges in the <b>same</b> coordinate system — items and
    /// tags are both paragraph-relative character offsets (§3.3, §4.1), which is the reason there is
    /// one coordinate system for both layers rather than two.</para>
    /// </summary>
    public static ValidationResult CheckTagBounds(IReadOnlyList<SceneItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var errors = new List<string>();

        foreach (var item in items)
        {
            foreach (var tag in item.Tags)
            {
                if (tag.StartOffset < item.StartOffset || tag.EndOffset > item.EndOffset)
                {
                    errors.Add(
                        $"{tag.TagId} spans {tag.StartOffset}..{tag.EndOffset} but its item {item.ItemId} "
                        + $"spans {item.StartOffset}..{item.EndOffset}");
                }

                if (tag.EndOffset <= tag.StartOffset)
                {
                    errors.Add($"{tag.TagId} is empty ({tag.StartOffset}..{tag.EndOffset})");
                }
            }

            var tags = item.Tags.OrderBy(t => t.StartOffset).ToList();

            for (var i = 0; i < tags.Count; i++)
            {
                for (var j = i + 1; j < tags.Count; j++)
                {
                    if (!tags[j].NestsIn(tags[i]) && !tags[i].NestsIn(tags[j]) && !tags[i].IsDisjointFrom(tags[j]))
                    {
                        errors.Add(
                            $"{tags[i].TagId} and {tags[j].TagId} partially overlap — tags may nest "
                            + "(an Enumerated group over two Persona tags) but never straddle each other");
                    }
                }
            }
        }

        return ValidationResult.Fail(TagBounds, errors);
    }

    // ---- Phase 9 ------------------------------------------------------------------------------

    /// <summary>Every tag binds to an existing registry id; no dangling referents. <b>Hard gate.</b></summary>
    public static ValidationResult CheckTagResolution(IReadOnlyList<SceneItem> items, Registries registries)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(registries);

        var personas = registries.Personas.Select(p => p.PersonaId).ToHashSet(StringComparer.Ordinal);
        var places = registries.Places.Select(p => p.PlaceId).ToHashSet(StringComparer.Ordinal);
        var exhibits = registries.Exhibits.Select(e => e.ExhibitId).ToHashSet(StringComparer.Ordinal);

        var errors = new List<string>();

        foreach (var (item, tag) in items.SelectMany(i => i.Tags.Select(t => (Item: i, Tag: t))))
        {
            if (tag.ReferentId is not { } referent)
            {
                errors.Add($"{tag.TagId} ('{tag.SurfaceForm}') in {item.ItemId} was never bound to a referent");
                continue;
            }

            var known = tag.Kind switch
            {
                TagKind.Persona or TagKind.Group => personas.Contains(referent),
                TagKind.Place => places.Contains(referent),
                TagKind.ExhibitRef => exhibits.Contains(referent),
                _ => false,
            };

            if (!known)
            {
                errors.Add($"{tag.TagId} binds to '{referent}', which is not in the {tag.Kind} registry");
            }
        }

        foreach (var speech in items.Where(i => i.Speech is not null))
        {
            if (speech.Speech!.SpeakerPersonaId is not { } speaker)
            {
                errors.Add(
                    $"{speech.ItemId} is Speech with no speaker — C8 has no anonymous speakers, and "
                    + "'Unattributed' is a real persona rather than an empty field");
            }
            else if (!personas.Contains(speaker))
            {
                errors.Add($"{speech.ItemId}: speaker '{speaker}' is not in the persona registry");
            }
        }

        return ValidationResult.Fail(TagResolution, errors);
    }

    /// <summary>
    /// Every member id on a <c>Group</c> tag exists, and a group's presence puts that tag's resolved
    /// members in the scene. <b>Hard gate.</b>
    /// </summary>
    public static ValidationResult CheckGroupClosure(
        IReadOnlyList<SceneItem> items, Registries registries, IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(scenes);

        var personas = registries.Personas.Select(p => p.PersonaId).ToHashSet(StringComparer.Ordinal);
        var byItem = items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var tag in items.SelectMany(i => i.Tags).Where(t => t.Kind == TagKind.Group))
        {
            foreach (var member in tag.Membership?.MemberPersonaIds ?? [])
            {
                if (!personas.Contains(member))
                {
                    errors.Add($"{tag.TagId}: member '{member}' is not in the persona registry");
                }
            }
        }

        foreach (var scene in scenes)
        {
            var cast = scene.Situation.Cast.Select(c => c.PersonaId).ToHashSet(StringComparer.Ordinal);

            var members = scene.ItemIds
                .Select(byItem.GetValueOrDefault)
                .OfType<SceneItem>()
                .SelectMany(i => i.Tags)
                .Where(t => t.Kind == TagKind.Group && t.ReferentId is not null && cast.Contains(t.ReferentId))
                .SelectMany(t => t.Membership?.MemberPersonaIds ?? []);

            foreach (var missing in members.Where(m => !cast.Contains(m)).Distinct(StringComparer.Ordinal))
            {
                errors.Add(
                    $"{scene.SceneId}: a group in the cast resolved member '{missing}' at this scene, "
                    + "but the member is not in the cast — presence propagates from the group to its members");
            }
        }

        return ValidationResult.Fail(GroupClosure, errors);
    }

    /// <summary>No persona merges candidates with different family scope roots (§6.1). <b>Hard gate.</b></summary>
    public static ValidationResult CheckPersonaScope(
        Registries registries, IReadOnlyList<FamilyScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(scopes);

        var roots = scopes.Select(s => s.SectionId).ToHashSet(StringComparer.Ordinal);

        var errors = registries.Personas
            .Where(p => p.ScopeRootSectionId is { } root && !roots.Contains(root))
            .Select(p =>
                $"{p.PersonaId} ('{p.CanonicalName}') claims scope root '{p.ScopeRootSectionId}', which is "
                + "not a family scope of this run")
            .ToList();

        return ValidationResult.Fail(PersonaScope, errors);
    }

    // ---- Phase 10 -----------------------------------------------------------------------------

    /// <summary>
    /// Every displayable item lies in exactly one scene, contiguous and ordered (C1–C3).
    /// <b>Hard gate.</b>
    /// </summary>
    public static ValidationResult CheckScenePartition(
        IReadOnlyList<SceneItem> items, IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(scenes);

        var order = items
            .Select((i, index) => (i.ItemId, index))
            .ToDictionary(x => x.ItemId, x => x.index, StringComparer.Ordinal);

        var errors = new List<string>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var scene in scenes)
        {
            if (scene.ItemIds.Count == 0)
            {
                errors.Add($"{scene.SceneId} owns no items — a scene owns at least one (C3)");
                continue;
            }

            var indices = new List<int>(scene.ItemIds.Count);

            foreach (var itemId in scene.ItemIds)
            {
                if (!order.TryGetValue(itemId, out var index))
                {
                    errors.Add($"{scene.SceneId} names item '{itemId}', which this run did not produce");
                    continue;
                }

                if (!owner.TryAdd(itemId, scene.SceneId))
                {
                    errors.Add($"{itemId} belongs to both {owner[itemId]} and {scene.SceneId}");
                }

                indices.Add(index);
            }

            if (indices.Count > 1 && indices.Max() - indices.Min() != indices.Count - 1)
            {
                errors.Add(
                    $"{scene.SceneId}: items are not contiguous — {indices.Count} items spanning positions "
                    + $"{indices.Min()}..{indices.Max()} (C2)");
            }

            if (indices.Count > 1 && !indices.SequenceEqual(indices.Order()))
            {
                errors.Add($"{scene.SceneId}: items are not in document order (C2)");
            }
        }

        foreach (var orphan in items.Where(i => !owner.ContainsKey(i.ItemId)))
        {
            errors.Add($"{orphan.ItemId} is in no scene — every displayable item belongs to exactly one (C1)");
        }

        return ValidationResult.Fail(ScenePartition, errors);
    }

    /// <summary>
    /// Every section holds sections or scenes, never both, and no scene contains a scene (C4, §2).
    /// <b>Hard gate.</b>
    /// </summary>
    public static ValidationResult CheckTreeLeafScenes(
        SectionNode root, IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scenes);

        var errors = new List<string>();
        var bySection = scenes.ToLookup(s => s.SectionId, StringComparer.Ordinal);
        var known = root.Descend().Select(n => n.SectionId).ToHashSet(StringComparer.Ordinal);

        foreach (var node in root.Descend())
        {
            var owned = bySection[node.SectionId].Count();

            if (node.Children.Count > 0 && owned > 0)
            {
                errors.Add(
                    $"{node.SectionId} ('{node.Title}') has {node.Children.Count} child sections and "
                    + $"{owned} scenes — a section contains one or the other, never both (§2)");
            }
        }

        foreach (var orphan in scenes.Where(s => !known.Contains(s.SectionId)))
        {
            errors.Add($"{orphan.SceneId} names section '{orphan.SectionId}', which is not in the tree");
        }

        return ValidationResult.Fail(TreeLeafScenes, errors);
    }

    /// <summary>
    /// Every coordinate is single-valued (C5) and has a value or its nothing-value (C6). Two hard
    /// gates over one walk, because the two questions are asked of the same five fields.
    /// </summary>
    public static IReadOnlyList<ValidationResult> CheckSituations(
        IReadOnlyList<Scene> scenes, Registries registries)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(registries);

        var single = new List<string>();
        var total = new List<string>();
        var cast = new List<string>();
        var personas = registries.Personas.Select(p => p.PersonaId).ToHashSet(StringComparer.Ordinal);
        var places = registries.Places.Select(p => p.PlaceId).ToHashSet(StringComparer.Ordinal);

        foreach (var scene in scenes)
        {
            var situation = scene.Situation;

            if (!Enum.IsDefined(situation.Mode))
            {
                total.Add($"{scene.SceneId}: '{situation.Mode}' is not a mode");
            }

            if (!Enum.IsDefined(situation.Time.Relation))
            {
                total.Add($"{scene.SceneId}: '{situation.Time.Relation}' is not a time relation");
            }

            if (situation.PlaceId is { } place && !places.Contains(place))
            {
                single.Add($"{scene.SceneId}: setting '{place}' is not in the place registry");
            }

            // C5 over cast: a persona holds one role in a scene. Two entries for one persona is two
            // values of one coordinate, which is the thing C5 rules out.
            foreach (var duplicate in situation.Cast.GroupBy(c => c.PersonaId, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                single.Add(
                    $"{scene.SceneId}: '{duplicate.Key}' appears {duplicate.Count()} times in the cast with "
                    + $"roles {string.Join(", ", duplicate.Select(c => c.Role))}");
            }

            foreach (var entry in situation.Cast.Where(c => !personas.Contains(c.PersonaId)))
            {
                cast.Add($"{scene.SceneId}: cast entry '{entry.PersonaId}' is not in the run's registry (C8)");
            }

            // C7: an inherited value is copied in WITH the source scene id. A scene never says "as
            // before", so an inherited coordinate that cannot name its source is not resolved.
            if (situation.SettingProvenance is { Source: CoordinateSource.Inherited, InheritedFromSceneId: null })
            {
                total.Add($"{scene.SceneId}: setting is inherited but names no source scene (C7)");
            }
        }

        return
        [
            ValidationResult.Fail(SituationSingle, single),
            ValidationResult.Fail(SituationTotal, total),
            ValidationResult.Fail(CastClosure, cast),
        ];
    }

    /// <summary>
    /// Every cited evidence id exists and was issued by code. <b>Hard gate</b>, and the scene-level
    /// analogue of the rule that any unissued id in model output is a validation error — what makes
    /// C11's "no guessing" enforceable rather than aspirational.
    /// </summary>
    public static ValidationResult CheckNoFabrication(
        IReadOnlyList<Scene> scenes,
        IReadOnlyList<SceneItem> items,
        IReadOnlyList<Paragraph> paragraphs,
        Registries registries)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(registries);

        var issued = new HashSet<string>(StringComparer.Ordinal);
        issued.UnionWith(items.Select(i => i.ItemId));
        issued.UnionWith(items.SelectMany(i => i.Tags).Select(t => t.TagId));
        issued.UnionWith(paragraphs.Select(p => p.ParagraphId));
        issued.UnionWith(paragraphs.SelectMany(p => p.LineIds));
        issued.UnionWith(scenes.Select(s => s.SceneId));
        issued.UnionWith(scenes.Select(s => s.SectionId));
        issued.UnionWith(registries.Personas.Select(p => p.PersonaId));
        issued.UnionWith(registries.Places.Select(p => p.PlaceId));
        issued.UnionWith(registries.Exhibits.Select(e => e.ExhibitId));

        var errors = new List<string>();

        foreach (var scene in scenes)
        {
            foreach (var id in Evidence(scene).Where(id => !issued.Contains(id)))
            {
                errors.Add(
                    $"{scene.SceneId} cites '{id}', which the pipeline never issued — evidence ids are "
                    + "issued by code and may not be invented (C11)");
            }
        }

        return ValidationResult.Fail(NoFabrication, errors);

        static IEnumerable<string> Evidence(Scene scene) =>
        [
            .. scene.Situation.SettingProvenance?.EvidenceIds ?? [],
            .. scene.Situation.Time.Provenance?.EvidenceIds ?? [],
            .. scene.Situation.Subject.Provenance?.EvidenceIds ?? [],
            .. scene.Situation.Cast.SelectMany(c => c.Provenance.EvidenceIds),
            .. scene.Links.SelectMany(l => l.EvidenceIds),
        ];
    }

    /// <summary>
    /// Every non-inherited, non-nothing coordinate cites ids (C13). <b>Warn.</b> Uncitable equals
    /// unknown, and a warning rather than a gate because the remedy is a reviewer's attention.
    /// </summary>
    public static ValidationResult CheckEvidencePresent(IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var warnings = new List<string>();

        foreach (var scene in scenes)
        {
            var situation = scene.Situation;

            // Void needs no evidence under C13: the absence of a place claim IS the derivation.
            if (situation.PlaceId is not null
                && situation.SettingProvenance is not { Source: CoordinateSource.Inherited }
                && (situation.SettingProvenance?.EvidenceIds.Count ?? 0) == 0)
            {
                warnings.Add($"{scene.SceneId}: setting '{situation.PlaceId}' cites nothing");
            }

            if (situation.Subject.Text.Length > 0 && (situation.Subject.Provenance?.EvidenceIds.Count ?? 0) == 0)
            {
                warnings.Add($"{scene.SceneId}: subject cites nothing");
            }

            if (situation.Time.Relation != TimeRelation.Unanchored
                && (situation.Time.Provenance?.EvidenceIds.Count ?? 0) == 0)
            {
                warnings.Add($"{scene.SceneId}: time relation '{situation.Time.Relation}' cites nothing");
            }
        }

        return ValidationResult.Fail(EvidencePresent, warnings);
    }

    /// <summary>
    /// No scene straddles a section boundary (C12). <b>Warn + <c>Continues</c> link.</b>
    ///
    /// <para>It is structurally impossible for a scene to straddle when scenes are section
    /// <em>children</em> (§2), so this check exists for the artifact rather than for the cutter: a
    /// scene whose items sit in two sections' paragraphs means the tree and the items disagree
    /// about which paragraphs belong where.</para>
    /// </summary>
    public static ValidationResult CheckSectionContainment(
        SectionNode root, IReadOnlyList<Scene> scenes, IReadOnlyList<SceneItem> items)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(items);

        var sectionOfParagraph = root.Descend()
            .SelectMany(n => n.ParagraphIds.Select(p => (Paragraph: p, n.SectionId)))
            .ToDictionary(x => x.Paragraph, x => x.SectionId, StringComparer.Ordinal);

        var byItem = items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var scene in scenes)
        {
            var sections = scene.ItemIds
                .Select(byItem.GetValueOrDefault)
                .OfType<SceneItem>()
                .Select(i => sectionOfParagraph.GetValueOrDefault(i.ParagraphId))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (sections.Count > 1)
            {
                warnings.Add(
                    $"{scene.SceneId} touches sections {string.Join(", ", sections)} — a scene is a leaf of "
                    + "one section (C4, C12); the staging that continues across the break carries a "
                    + "Continues link instead");
            }
        }

        return ValidationResult.Fail(SectionContainment, warnings);
    }

    // ---- Phase 11 -----------------------------------------------------------------------------

    /// <summary>
    /// Every link's endpoints exist and differ; inverses stored in pairs; orientation follows
    /// document order. <b>Hard gate</b> — asserted by <c>SceneLinkMaterializer</c> and re-run here
    /// at the freeze.
    /// </summary>
    public static ValidationResult CheckLinkIntegrity(IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var order = scenes
            .Select((s, index) => (s.SceneId, index))
            .ToDictionary(x => x.SceneId, x => x.index, StringComparer.Ordinal);

        var all = scenes.SelectMany(s => s.Links).Distinct().ToList();
        var present = all.Select(l => (l.FromSceneId, l.ToSceneId, l.Kind)).ToHashSet();
        var errors = new List<string>();

        foreach (var link in all)
        {
            if (!order.TryGetValue(link.FromSceneId, out var from))
            {
                errors.Add($"link names '{link.FromSceneId}', which is not a scene of this run");
                continue;
            }

            if (!order.TryGetValue(link.ToSceneId, out var to))
            {
                errors.Add($"link names '{link.ToSceneId}', which is not a scene of this run");
                continue;
            }

            if (from == to)
            {
                errors.Add($"{link.FromSceneId}: a scene cannot link to itself");
                continue;
            }

            var orientationOk = link.Kind switch
            {
                SceneLinkKind.ConcurrentWith => true,
                SceneLinkKind.Frames => to > from,
                _ => to < from,
            };

            if (!orientationOk)
            {
                errors.Add(
                    $"{link.Kind} from {link.FromSceneId} to {link.ToSceneId} runs against document order");
            }

            var inverse = link.Kind switch
            {
                SceneLinkKind.Frames => SceneLinkKind.FramedBy,
                SceneLinkKind.FramedBy => SceneLinkKind.Frames,
                SceneLinkKind.ConcurrentWith => SceneLinkKind.ConcurrentWith,
                _ => (SceneLinkKind?)null,
            };

            if (inverse is { } kind && !present.Contains((link.ToSceneId, link.FromSceneId, kind)))
            {
                errors.Add(
                    $"{link.Kind} from {link.FromSceneId} to {link.ToSceneId} has no {kind} back — inverses "
                    + "are stored on both scenes or on neither, so K5 is an index lookup from either end");
            }
        }

        return ValidationResult.Fail(LinkIntegrity, errors);
    }

    /// <summary>Every link cites the ids justifying it (C13). <b>Hard gate.</b></summary>
    public static ValidationResult CheckLinkEvidence(IReadOnlyList<Scene> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        var errors = scenes
            .SelectMany(s => s.Links)
            .Where(l => l.EvidenceIds.Count == 0)
            .Select(l => $"{l.Kind} from {l.FromSceneId} to {l.ToSceneId} cites nothing — an uncited link "
                + "is not stored, because K4 is worth less than C11")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return ValidationResult.Fail(LinkEvidence, errors);
    }

    /// <summary>
    /// The scene half of the freeze gate. Every hard check of §9 in one call, so the freeze and the
    /// phase gates cannot drift apart about what "validated" means — the same reason
    /// <see cref="Checks.CheckAll"/> exists.
    /// </summary>
    public static ValidationReport CheckAll(
        SectionNode root,
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<ParagraphPresentation> presentations,
        IReadOnlyList<FamilyScope> scopes,
        IReadOnlyList<SceneItem> items,
        Registries registries,
        IReadOnlyList<Scene> scenes,
        string runFamily,
        PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(presentations);
        ArgumentNullException.ThrowIfNull(options);

        var displayableIds = presentations
            .Where(p => p.IsDisplayable)
            .Select(p => p.ParagraphId)
            .ToHashSet(StringComparer.Ordinal);

        var displayable = paragraphs.Where(p => displayableIds.Contains(p.ParagraphId)).ToList();

        return new ValidationReport(
        [
            CheckPresentationIntegrity(paragraphs, presentations),
            CheckFamilyScope(root, scopes, runFamily, options.FamilyScopes),
            CheckItemCoverage(displayable, items),
            CheckDisplayIntegrity(displayable, items),
            CheckTagBounds(items),
            CheckTagResolution(items, registries),
            CheckGroupClosure(items, registries, scenes),
            CheckPersonaScope(registries, scopes),
            CheckScenePartition(items, scenes),
            CheckTreeLeafScenes(root, scenes),
            .. CheckSituations(scenes, registries),
            CheckNoFabrication(scenes, items, paragraphs, registries),
            CheckLinkIntegrity(scenes),
            CheckLinkEvidence(scenes),
        ]);
    }

    private static string? ParentOf(SectionNode root, string sectionId) =>
        root.Descend()
            .FirstOrDefault(n => n.Children.Any(c => c.SectionId == sectionId))
            ?.SectionId;
}

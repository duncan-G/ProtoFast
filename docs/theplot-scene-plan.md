# ThePlot — Scene segmentation plan

Status: **plan / complete**. Every question this plan raises is decided; §13 states the
measurement that would reopen one and the action chosen for that branch.

This document specifies how the segmentation pipeline produces **scenes**. It sits between two
others and says nothing they already say:

| Document | Role here |
|---|---|
| `theplot-scene-unit.md` | Defines the scene: five coordinates, constraints C1–C14, capacities K1–K9. This plan implements it. |
| `theplot-segmentation-plan.md` | Defines the pipeline: phases, agents, validation, storage, infra. This plan specifies the phases that produce scenes. |

**Reference convention.** `[unit §3.3]` cites the scene-unit document. `[pipeline §9.7]` cites
the segmentation plan. A bare `§4.2` is a section of *this* document. `C1`–`C14` are unit
constraints, `K1`–`K9` unit capacities, `F2`/`F8` pipeline features.

**Principles held in common with the pipeline** [pipeline §3]: code runs the pipeline and models
make only judgment calls; models label and never rewrite text; every phase leaves an inspectable
artifact.

---

## 1. Definitions

Every term this document uses in a specific sense, in one place. Terms the pipeline also defines
[pipeline §4] are repeated because the rest are defined against them.

| Term | Definition |
|---|---|
| **Line** | Smallest unit from conversion: one visual line or layout block, with a stable `LineId`. |
| **Paragraph** | A contiguous run of lines judged to form one paragraph. The **text-integrity unit**: frozen, content-hashed, never written by a model. |
| **Sentence index** | The 0-based position of a sentence within its paragraph, as `paragraphEdits.beforeSentence` in `tree.schema.json` uses it. |
| **Item** | A typed, contiguous **character span** inside exactly one paragraph, owned by exactly one scene. The **composition unit** and the renderer's input (§3). Finer than a sentence: one sentence can hold a `Speech` item and an `Action` item. |
| **Render text** | Generated prose that makes a non-standalone item stageable — "as he ran for the door" → "James ran for the door." Marked as generated, never content, never hashed (§3.6). |
| **Displayable text** | The concatenation, in document order, of every paragraph whose `Presentation` is `Displayable`. This is the work; everything else is metadata. |
| **Metadata** | Content present in the source but not part of the displayable work — page furniture, front and back matter, navigational labels, production noise (§5). Metadata is **excluded from the scene stream, never deleted**. |
| **Presentation** | The per-paragraph classification that decides the above: `Displayable` or `Metadata(class)`. |
| **Tag** | A typed reference over a character range inside an item, resolved to a registry id — the mechanism that makes "who and where" enumerable rather than a judgment call (§4). |
| **Registry** | The per-run id table for a referent type. Three exist: personas, places, exhibits. |
| **Persona** | A registry entry for anyone who speaks, is addressed, or is present. Individuals and groups alike; the narrator and `Unattributed` are personas [unit §3.3]. |
| **Group** | A persona whose referent is several people ("the brothers", "the crowd"). A `PersonaKind`, not a separate referent type (§4.3). |
| **Scene** | The maximal contiguous run of **items** over which one **situation** — `(Setting, Time, Cast, Mode, Subject)` — holds [unit §1]. |
| **Section** | A tree node with a title. Contains sections **or scenes**, never both and never paragraphs (§2). |
| **Production family** | What made the file: converter, publisher, scanner, template. Governs cleaning and page furniture (§6). |
| **Composition family** | What kind of work it is: novel, textbook, transcript, screenplay, paper, manual. Governs metadata policy, item typing, and scene cutting (§6). |
| **Instinct** | Learned, advisory, per-family prompt guidance, carrying an axis and a scope [pipeline §15.4, §7]. |

## 2. Three layers

The pipeline's product is a **sequence of scenes over items**, held in a tree of sections:

| Layer | Contains | Invariant |
|---|---|---|
| **Section** | sections **or** scenes, never both | children-xor, over scenes |
| **Scene** | items | C1–C5 |
| **Item** | a character span + tags | `item-coverage` (§3.4) |

The two lower layers divide one job in two. **The paragraph holds text integrity** — frozen,
content-hashed, never written by a model — and **the item holds composition**: a span plus typed
tags over sub-ranges, resolved to registry ids (§4). Both persist, and neither substitutes for
the other. Content that is not part of the displayable work is classified and withheld from the
scene stream while staying in the frozen record (§5), so exclusion is never deletion.

Four things follow from scenes being the section layer's children:

- **`tree.schema.json` carries a `scenes` branch** in its `oneOf`, not a `paragraphs` branch, and
  `TreeProposal` / `TreeProposalNode` and the 6-level flattened wire schema match it.
- **C12 is a structural fact, not a check.** A scene cannot straddle a section boundary when it
  is a *child* of one. The real disagreement between structural and staging boundaries
  [unit §7 case 6] surfaces as a `Continues` link between sibling scenes rather than as a
  validation warning.
- **Montage and problem sets get their grouping without a fourth layer.** An **inferred** section
  (F8) holds a run of short scenes, so a consumer wanting the beat takes the section and one
  wanting the cuts takes the scenes. C5 stays intact [unit §4.4, §7 case 5].
- **Narrative groupings the headings never marked** — acts, sequences, montages — are inferred
  sections, the same capacity the tree uses to infer structure the source did not state.

The section tree is therefore not the deliverable. It is a constraint on where scenes may be cut
(C12) and a navigation aid over them.

## 3. Items

> An **item** is a typed, contiguous character span inside one paragraph, owned by exactly one
> scene.

### 3.1 The five kinds

A kind exists **only if a renderer does something different with it**. That test is what keeps
the list at five.

| Kind | Content | What makes it distinct to a renderer |
|---|---|---|
| **`Speech`** | An utterance attributed to a persona. | The only kind that produces audio from a voice. |
| **`Action`** | Something that happens or is done; an event in time. | Animates the frame. |
| **`Description`** | The state of a place, person, or thing; not an event. | Sets the frame — the opposite of `Action`. |
| **`Exhibit`** | A non-prose artifact shown: figure, table, equation, code, media. Its caption attaches to it. | Inserted as a visual, not depicted. |
| **`Transition`** | The explicit marker of the shift *into* this scene ("Three years later"). | The only kind that describes the boundary rather than the contents. It is the title-card text. |

Non-speech sound is an attribute of `Action`, not a sixth kind.

**`Action` and `Description` will be confused sometimes, and that is tolerable.** The same span
is often defensibly either — "the door stood open" against "the door swung open". They stay
separate kinds because a renderer does different things with them (animate the frame vs. set
it), but nothing load-bearing depends on telling them apart: no hard gate reads the distinction,
and mode derivation is deliberately built not to be sensitive to it (§8.3). A wrong call
degrades one beat; it never mis-cuts a scene.

### 3.2 Speech attributes

The three cases in the brief — a cast member speaking, a narrator speaking, a presenter
speaking — are **one kind with two attributes**, not three kinds.

- **`Embodied`** answers *does a mouth appear on screen*.
- **`Addressee`** answers *does anyone in the world react*.

Those are the only two questions a renderer asks, so they are the only two attributes. `Voiced`
is a third field but not a question — it is set false only for interiority.

| Preset | `Embodied` | `Addressee` | `Voiced` |
|---|---|---|---|
| Dialogue (cast member) | yes | in-scene persona | yes |
| Lecture / presentation | yes | `Audience` | yes |
| Narration | no | `Audience` | yes |
| Interiority (thought) | yes | `Self` | no |

### 3.3 Spans

An item names a span of its paragraph's frozen text and **never carries source text** (C9):

```
Item span = (ParagraphId, StartOffset, EndOffset)   // half-open character offsets
```

Character offsets, not sentence indices, because **a single sentence routinely mixes kinds**:

> James shouted, "Bye" as he ran for the door.

That is one `Speech` item and one `Action` item, not one item of either. Items therefore
partition the paragraph at character granularity, in the same coordinate system tags use (§4.1)
— one coordinate system for both layers rather than two.

An attribution clause belongs to the `Speech` item it attributes, so the partition above is
`James shouted, "Bye"` and ` as he ran for the door.` — adjacent, no gap, no overlap. That is
also where `SpeechAttributes.SurfaceForm` is read from.

Sentence indices keep one job: **scene** boundaries, which never fall mid-sentence (§3.5).

### 3.4 Coverage

| Check | Rule |
|---|---|
| `item-coverage` | Every character of every **displayable** paragraph lies in exactly one item's span. No gaps, no overlaps. |
| `display-integrity` | A paragraph's item **spans**, concatenated in order, reproduce that paragraph's frozen text exactly. |

`display-integrity` is the item-level analogue of the `text-integrity` gate [pipeline §11], and
a hard gate on the same terms. The *metadata* half of integrity — that
displayable and metadata paragraphs together still reproduce the cleaned source — is a separate
check at a separate phase, `presentation-integrity` (§9).

Both run over **spans only**. Neither sees render text (§3.6), which is why adding a re-writer
cannot weaken either one.

### 3.5 Scenes become item-granular

A scene is a contiguous run of **items** [unit §1, C1–C3]. One dense prose
paragraph can hold a `Description`, two `Speech` items, an `Action` — and, where the text
genuinely shifts mid-paragraph, a scene boundary. A paragraph is a unit of text integrity, and
nothing about staging obliges a situation to change only at its edges.

Sub-paragraph cutting is **wanted**, not merely permitted: a paragraph mixing dialogue, action
and description should be cut into those items, and the scene boundary follows wherever the
staging genuinely shifts.

Two granularities, deliberately different:

| Boundary | Granularity | Why |
|---|---|---|
| **Item** | sub-sentence, character offsets (§3.3) | Kinds mix inside one sentence; a renderer needs them apart. |
| **Scene** | sentence, never finer | A situation does not change halfway through a sentence, and a cut there would strand a clause. |

Cutting this finely needs an explicit floor against over-cutting, since paragraph edges are no
longer supplying one:

> An **unmarked** mode change spanning fewer than **`MinSceneSpan`** sentences is recorded as an
> item attribute, not a cut.

`MinSceneSpan` is per composition family, and the floor never overrules an explicit marker —
§8.8 gives the values and the reasoning. It is what keeps the passing aside in a keynote ("when I
was in Berlin, anyway —") from becoming a scene [unit §7 case 2].

### 3.6 Render text and the re-writer

Cutting at character granularity produces fragments that are correctly typed but not
independently stageable:

| Item | Span text | Stageable alone? |
|---|---|---|
| `Speech` | `James shouted, "Bye"` | Yes — the utterance is `Bye`, the speaker resolves from the attribution |
| `Action` | ` as he ran for the door.` | **No** — a subordinate clause with an unresolved pronoun |

A renderer cannot stage "as he ran for the door". It needs **"James ran for the door."** So an
item may carry **render text**: a generated, independently stageable rendition of its own span.

> **Render text is the one place in the pipeline a model writes prose.** It is fenced on four
> sides, and the fence is what keeps "models label, never rewrite" true.

1. **It is generated metadata, never content.** C9 already carves out exactly this for titles,
   summaries and place names — "clearly marked as generated, and never re-enter the content
   stream". Render text joins that list: stored on the item, flagged as generated, never
   concatenated back into the document. C9 names it among the generated metadata it permits [unit C9].
2. **The frozen record is untouched.** `text-integrity` still hashes paragraphs and
   `item-coverage` still partitions spans, both over span text alone (§3.4). A re-writer that
   produced nothing at all would leave every gate passing identically.
3. **It is grounded, and checkably so.** `render-text-grounding` restricts the output to the
   lemmas of its own span, the canonical names of the personas its own tags resolve, and a
   closed-class allowlist — §3.7 states the rule exactly. Rewriting "he" to "James" is legal
   because a `Persona` tag inside the span resolves to James. Introducing a door the span never
   mentions is not.
4. **It is the exception, not the rule.** An item whose span already stands alone — the large
   majority — carries no render text and costs nothing. The re-writer runs only over fragments
   flagged `IsStandalone = false` at typing time.

**It runs after the freeze, as an item-scoped augmentation.** Two facts settle the placement.
The first is a dependency: expanding "he" into "James" *is* referent resolution, so it cannot
run before phase 9 binds the tags (§8.4). The second is that **nothing in the pipeline consumes
it** — no coordinate, no gate and no freeze invariant reads `RenderText`; only a renderer does.
A step nothing downstream depends on does not belong on the critical path.

So phase 8 types the fragment and flags `IsStandalone = false`, phase 9 binds the tags, and the
re-writer is **`render-text`, an `IAugmentationType` at `AugmentationScope.Item`** (§10), run in
phase 15 over frozen items. Four properties follow from that placement, and together they are
why the re-writer's cost needs no measurement to price:

| Property | Why it follows from being an augmentation |
|---|---|
| **Cost scales with what is rendered, not with the corpus** | Augmentation fans out per item over the frozen record, on demand. A document nobody stages costs nothing, so whether the non-standalone rate is 5% or 60% it moves the bill of a render job, not the shape of the pipeline. |
| **Re-running it cannot disturb the freeze** | The freeze (phase 14) covers items and spans. Render text is produced *against* a frozen item hash and keyed by it [pipeline §12.2], so regenerating it re-derives an idempotency key rather than invalidating an artifact. |
| **A failure degrades one item** | An output failing §3.7 is rejected and the item keeps `RenderText = null`; the renderer falls back to the span. That is K7, not a blocked run. |
| **It needs no role of its own** | It runs under `AgentRole.Augmenter` and is reviewed by `AugmentReviewer`, so the re-writer is a *type* — not a tier, a qualification key or a registry seed. |

The one thing given up is sharing phase 9's window context. It is worth little: a frozen item
carries its span, its bound tags and its personas' canonical names, which is the re-writer's
entire input (§3.7), so reading it later is a read rather than a re-derivation.

### 3.7 The grounding rule, precisely

`render-text-grounding` is a **hard gate on the output** — an output that fails it is never
stored — and never a gate on the run. It is the `Validate` of the `render-text` augmentation
type, so it sits exactly where `aug-grounding` sits [pipeline §11], with the
regenerate-once-then-flag path behind it [pipeline §12.3].

The rule admits three sources and nothing else:

| Admitted | Example |
|---|---|
| Lemmas of the item's **own** span | `ran`, `door` from " as he ran for the door." |
| Canonical names of personas the span's **own** tags resolve | `James`, because a `Persona` tag inside the span binds to him |
| A **closed-class allowlist** — determiners, copulas, auxiliaries, prepositions, conjunctions, pronouns, possessive markers, sentence punctuation | `the`, `was`, `'s`, `.` |

The third row is what the naive rule was missing, and it is what lets the gate be hard. Every
false failure the rule was feared to produce — a rewrite needing a copula the fragment lacks, or
a possessive to attach a name — is **closed-class**, and closed classes are finite, enumerable
per language, and carry no referents. Admitting them wholesale removes the false failures without
admitting one content word: invention requires an open-class item — a noun, a verb, an adjective,
a name — and those stay restricted to rows one and two. The fence keeps all four of its sides.

Two consequences worth stating:

- **The list is an asset, not code.** It ships beside the prompts as
  `rules/closed-class.{lang}.txt` with the lemmatiser for that language, and is versioned with
  them [pipeline §15.3]. A new language is a file.
- **The gate stays checkable without a model.** Lemmatise, then two set lookups. No judgement and
  no I/O, so it runs in the same suite as every other deterministic check [pipeline §11].

## 4. Tags

> A **tag** is a typed reference over a character range inside an item, resolved to a registry
> id.

```
"The professor turned. She had been waiting."
 ^^^^^^^^^^^^^          ^^^
 Persona:PR003          Persona:PR003
```

Tags name offsets into frozen paragraph text and never carry text of their own, so "models
label, never rewrite" holds.

### 4.1 Offsets

```
Tag span = (StartOffset, EndOffset)   // half-open character offsets into the PARAGRAPH's frozen text
```

Offsets are relative to the **paragraph**, not to the item. The paragraph is the hashed,
immutable artifact, so paragraph-relative offsets stay verifiable and survive items being
re-cut; item-relative offsets would shift under a re-run that changed nothing a reader could
see. `tag-bounds` checks that a tag's range falls inside its own item's span — a containment
test between two ranges in the **same** coordinate system, since items are paragraph-relative
character offsets too (§3.3).

### 4.2 The four kinds

| Tag kind | Resolves to | Feeds |
|---|---|---|
| `Persona` | persona id | Cast — "the professor", "she", "Dr. Vance" all resolve to one id |
| `Group` | persona id with `Kind = Group` | Cast — "Mr and Mrs Smith", "the gang", "the crowd" |
| `Place` | place id | Setting |
| `ExhibitRef` | exhibit id | Links, K4 — "see Figure 3", "the table above" |

`ExhibitRef` is named apart from the `Exhibit` **item** kind on purpose: the item is the artifact
itself, the tag is a pointer to one.

`Time` and `Topic` were candidates and are deliberately **not** tag kinds. Their coordinates are
cheap to evidence with paragraph ids the ordinary way (C13), and they are the two kinds that
would fire on nearly every sentence — which is where the cost would have gone superlinear
(§4.5).

### 4.3 Groups

A group is a **persona**, so cast handling, speech attribution, and C8 closure need no special
case for "the crowd". Two fields carry the whole difference:

| Field | Values | Why it exists |
|---|---|---|
| `Formation` | `Enumerated` ("Mr and Mrs Smith") / `Named` ("the gang") | An enumerated group resolves **deterministically** from a conjunction of already-tagged individuals; a named group needs the registry and an agent. This moves the cheap half out of the model. |
| `IsComplete` | bool | A rendering switch: a complete group renders as its members, an opaque one as a crowd. Partial membership is a fact about the group, not a defect. |

**Membership belongs to the reference, not to the registry entry.** "The brothers" may resolve
to two people in chapter 1 and three in chapter 20. The group's *identity* persists across the
document; its *membership* does not, and a registry field cannot express that. So
`GroupMembership` hangs off the **`Group` tag**: the registry holds the group's id, canonical
name and surface forms, and each tag carries the members resolved **at that point in the
document**, with its own `IsComplete` [unit §9].

Presence propagates one way: a group in the cast puts the members *its tag resolved at that
scene* into the scene; members being present does not conjure the group [unit §3.3].

**Tags may nest but may never partially overlap.** An `Enumerated` group tag spans "Mr and Mrs
Smith" with two `Persona` tags inside it. `tag-bounds` enforces that containment lattice.

### 4.4 What tags buy

Two things, both of which convert a judgment call into a query.

**C13 evidence is mechanical.** C13 requires every coordinate to cite what justifies it. For the
two coordinates that are expensive to evidence by hand, the citation is a by-product: Cast cites its `Persona` and `Group` tags, Setting cites its `Place` tags. An
unevidenced Cast or Setting is detectable by construction — it is one with no tags under it.
Time and Subject cite paragraph ids the ordinary way, which is exactly why they need no tag
kinds.

**"Mention is not presence" is computable** [unit §3.3]. Tags enumerate every reference;
cast membership is derived from **role**, not from tag presence. A persona tagged only in
running text is *referenced* and stays out of the cast however often it occurs.

### 4.5 Cost

Tagging is the most token-expensive thing in this plan — it fires per referring expression, not
per paragraph. Three things hold it down, in order of effect:

1. **The tag set itself.** Dropping `Time` and `Topic` removes the two kinds that fire on almost
   every sentence, leaving four that fire on references to *entities*, which are far sparser.
2. **Deterministic pre-tagging.** Speaker labels, registry proper nouns, `Enumerated` group
   conjunctions, and repeat mentions of an already-tagged surface form are resolved in code, so
   the model sees only novel or ambiguous references.
3. **No extra read.** Tagging runs inside the phase-8 pass that already reads every displayable
   sentence, so it costs output tokens rather than a second traversal of the document.

## 5. Metadata

> **Metadata is content that is not part of the displayable work.** It is classified and
> withheld from the scene stream. It is never deleted.

### 5.1 Classes

| Class | Examples | Where handled |
|---|---|---|
| `RunningApparatus` | Running headers and footers, page numbers, running titles, "continued on p. 4" | Phase 1 (F2) |
| `ProductionNoise` | Watermarks, scanner artifacts, OCR junk, crop marks | Phase 1 |
| `FrontMatter` | Title page, copyright, ISBN block, dedication, epigraph, table of contents, list of figures | Phase 5 |
| `BackMatter` | Index, colophon, about-the-author, ad pages | Phase 5 |
| `NavigationalLabel` | Figure numbers, "Chapter 4" as a bare label, section numbering separated from its title | Phase 5 |

### 5.2 The boundary is family-dependent

Whether a class is metadata is **not universal**, which is precisely why instincts earn their
place (§7):

- A dedication is apparatus in a textbook and arguably the work in a poetry collection.
- An epigraph opens a novel's chapter as content and heads a manual's section as apparatus.
- A bibliography is back matter in a novel and the point of a survey paper.

So the pipeline does not hardcode the boundary. It classifies against the **composition
family's** learned policy, defaults to `Displayable` when uncertain, and flags the uncertainty.
The asymmetry drives the default: **under-removal is recoverable in review; over-removal
silently loses the work.**

### 5.3 Why exclusion does not break text integrity

The frozen record keeps every paragraph with its `Presentation` value. Scenes reference
displayable spans only, while `text-integrity` continues to concatenate *everything* in order
and match the source hash. "Removed" means **absent from the scene stream** — which is what the
product needs — and nothing weaker than the full record is true of the archive.

## 6. Two families, not one

Family is two independent facts about a document [pipeline §9.2], and scene work reads them
separately:

| Axis | Detects | Governs | Signals |
|---|---|---|---|
| **Production family** | How the file was made | Cleaning, `RunningApparatus`, OCR repair, layout trust | Producer string, page size, font set, margin geometry |
| **Composition family** | What kind of work it is | Metadata policy, scene cutting, item typing, persona scope | Speaker-label density, dialogue ratio, heading regularity, tense, person, imperative density |

A scanned PDF of a novel and a scanned PDF of a textbook share every production instinct and
almost no composition instinct. One field cannot carry both.

`RunManifest` carries both. Both are detected in phase 0, and the composition family is
re-confirmed in phase 5, once paragraph-level evidence exists.

**Composition family is the single strongest predictor of everything in this plan**, which is
why it is worth a field, a detector, and its own instinct scope rather than a prompt hint.

### 6.1 Mixed documents — family is a scope, not a global

An anthology, a course pack and a collected-works volume are several composition families in one
file, so one composition family per run cannot be the whole answer. It does not need a second
detector or a per-document special case, because the document already carries the boundaries:
they are **sections**.

> A **family scope** is a section whose subtree carries its own `CompositionFamily`. Resolution
> for any node is *nearest ancestor-or-self with a scope, else the run's family.*

A single-family document has no scope roots and resolves to the run family everywhere, so the
ordinary case pays one lookup that terminates at the root.

**Where scopes come from, and why no model is involved.** A scope root is a claim about a
*section*, so it cannot be made before the tree exists at phase 6 — but the evidence for it can
be, and is. Phase 5 re-confirms the run family against paragraph-level features (§6): dialogue
ratio, speaker-label density, heading regularity, tense, person, imperative density. It emits
that feature vector per paragraph as a by-product. **Phase 7 attributes it to the tree in
code** — a section is a scope root when its subtree's aggregated evidence disagrees with its
parent's resolved family beyond the family's learned band. No second detector, no extra model
call, and a deterministic step in the phase that already holds both the tree and the validation
machinery.

Four guards keep the mechanism from firing on noise:

| Guard | Default | Why |
|---|---|---|
| `MaxFamilyScopeDepth` | 2 | Anthology stories and course-pack units sit at the top of the tree. A family that changes at depth 6 is a detector error, not a document. |
| `MinFamilyScopeParagraphs` | 60 | A three-paragraph vignette inside a textbook is an `Enacted` island (§8.3), not a second composition family. |
| Must differ from the parent scope | — | A scope resolving to what it would have inherited is redundant and is dropped. |
| `MaxFamilyScopes` | 32 | A volume holds tens of works. Hundreds means the detector is chasing sections. |

**What a scope costs a consumer: one lookup.** Every consumer resolves a family before doing
anything — phase 8 for item typing, phase 9 for `Persona` instincts, phase 10 for cutting and
for `MinSceneSpan` (§8.8) — and resolves it *at the node it is working on*. All of them run
after phase 7, and phases 8 and 10 are windowed by section, so no window straddles two families
and no join reconciles two policies.

Phase 5 is the exception: it runs before the tree exists and classifies against the **run**
family. That is the right answer rather than a concession — front matter, a copyright page and a
table of contents belong to the volume, not to the works inside it, so an anthology's metadata
policy is the anthology's.

**Personas do not cross a scope.** `Persona` gains `ScopeRootSectionId` — null in a single-family
document, the enclosing scope root otherwise. `RegistryMaterializer` **rejects a plan that merges
two candidates with different scope roots** (§8.7), which keeps the division of labour intact:
the orchestrator judges whether two surface forms co-refer, and code decides whether they were
ever eligible to. The asymmetry that settles it is the one that already governs metadata (§5.2) —
a false merge bleeds one story's voice and appearance into another's and is nearly invisible in
review, while a missed merge costs a duplicated registry entry that K3 never promised to unify,
since K3's promise is consistency *within* a work. A character genuinely recurring across an
anthology is a link between registries rather than one entry, and that link is out of scope for
v1 (§13).

## 7. Family instincts

Learned, advisory, per-family prompt guidance [pipeline §15.4], with six consumers across the
scene phases.

### 7.1 Axis and scope

```csharp
public sealed record FamilyInstinct(
    string Family,
    FamilyAxis Axis,             // Production | Composition
    InstinctScope Scope,         // which agent may see it
    string Pattern, string Guidance,
    double Confidence, int Confirmations, DateTimeOffset LastSeen);

public enum FamilyAxis { Production, Composition }
public enum InstinctScope { Labeling, Structure, Metadata, ItemType, SceneCut, Persona }
```

`Scope` is what keeps the "max 6 per prompt" budget spent on relevant guidance: without it every
consumer would draw from one pool and see instincts meant for another agent. With it, each agent
draws from its own. The rest of the mechanism is as [pipeline §15.4] defines it — inject at
confidence ≥ 0.7, max 6 per prompt, decay −0.05 per 30 days, advisory only, never overriding
trusted boundaries or validation, clusters promoted into a family skill by a person.

### 7.2 What each scope learns

| Scope | Axis | Example instinct |
|---|---|---|
| `Metadata` | Production | "Pages 1–4 are front matter in this publisher's template." |
| `Metadata` | Composition | "In this collection, epigraphs are displayable." |
| `ItemType` | Composition | "An ALL-CAPS centred line is a speaker cue, not a heading." |
| `ItemType` | Composition | "Em-dash-opened lines are dialogue in this translation convention." |
| `SceneCut` | Composition | "Chapter openings restate place; do not inherit setting across them." |
| `SceneCut` | Composition | "Scene breaks are marked by a centred asterisk trio." |
| `Persona` | Composition | "Speaker labels are two-letter initials." |
| `Persona` | Composition | "Worked-example names are disposable." |

That last one is how persona scope gets handled in practice [unit §7 case 7]: a textbook's two
hundred throwaway Alices are a **learned family fact**, not a universal rule and not a code
branch.

### 7.3 Instincts are the mechanism for content-type breadth

There is no per-content-type code path in this plan, and there should not be one. A new content
type arrives as three things: composition-family detection (a classifier, not a branch), a
handful of instincts learned from the first reviews, and — once a cluster of them is stable —
promotion into a family skill. The pipeline stays one pipeline.

## 8. Pipeline

### 8.1 Phase map

The bolded phases are the ones this document specifies; the rest are [pipeline §9].

| # | Phase | Kind | What it does | Output |
|---|---|---|---|---|
| 0–4 | Ingest → Assemble paragraphs | — | Convert, clean, triage, label, assemble; detect both families (§6) | `04_paragraphs.jsonl` |
| **5** | **Classify presentation** | Code + agent (fan-out) | Separate displayable text from metadata; emit per-paragraph composition-family evidence (§6.1, §8.5) | `05_presentation.json` |
| 6 | Infer structure | Agent | Build the section tree over displayable paragraphs | `06_tree.json` |
| 7 | Validate and repair | Code + agents | Gate and repair the tree; derive family scopes from phase 5's evidence, in code (§6.1) | `07_validation.json`, `07_family_scopes.json` |
| **8** | **Type items** | Agent (fan-out) | Partition displayable paragraphs into typed items with candidate tags (§8.6) | `08_items.jsonl` |
| **9** | **Resolve referents** | **Orchestrated** | Cluster surface forms into personas; bind every tag to a registry id (§8.7) | `09_registries.json`, `09_personas/window_{n}.json` |
| **10** | **Cut scenes** | Code + agent (fan-out per leaf section) | Assign situations and cut scene boundaries (§8.8) | `10_scenes.json` |
| **11** | **Link scenes** | **Orchestrated**, skippable | Infer the scene links a coordinate cannot derive (§8.9) | `11_links.json`, `11_links/window_{n}.json` |
| 12 | Review | Agent (fresh context) | Review scenes, links and items against the source | `12_review.json` |
| 13 | Human gate | Request port | A person accepts, rejects or annotates the scenes | `13_decision.json` |
| 14 | Freeze | Code | Freeze paragraphs, presentation, tree, items, registries, scenes and links under one hash | `14_frozen.json` |
| 15–17 | Augment, review, publish | — | Augment **per item**, including the re-writer (§3.6); review; publish | — |

### 8.2 Executor shape: when an executor is an orchestrator

**Default to an orchestrator agent driving a bench of sub-agents.** Phase 6 is the reference
shape — `StructureWindowerAgent` (Mid) fanned out by `WindowBench`, assembled by
`StructureOrchestratorAgent` (Large), materialized by pure code — and the scene phases reuse it
rather than reinventing a fan-out each time.

But "when possible" is a real qualifier, and the orchestrator plan already states the test in
its §13, where it argues labelling must *not* become an orchestration:

> The join rests on an invariant the planner guarantees — every line in a suspect region is
> committed by exactly one window — so the merge needs no judgement, because there is never a
> tie. An orchestrator there would **replace a guarantee with an opinion**, spend a large-model
> call per document to do it, and make the result non-reproducible.

Generalised:

> **An executor becomes an orchestrator exactly where the cross-window join needs judgement.
> Where the window planner can guarantee the join, code performs it.**

Two corollaries, both load-bearing:

- **Downgrading a guarantee is a regression, not an upgrade.** If a deterministic merge is
  already correct, an agent can only make it slower, costlier and non-reproducible.
- **Even where an orchestrator is used, its output is materialized deterministically.** The
  orchestrator composes *references*, never content, and a pure materializer applies the plan
  and asserts the phase's invariant — the discipline of `OrchestratedTreeMaterializer`, which
  turns coverage from a check that can fail into a property that cannot [orchestrator §4.3].

Applying the test across the pipeline:

| Phase | Window | What the join must do | Shape |
|---|---|---|---|
| 3 Label | suspect region | Each line committed by exactly one window — planner-guaranteed | Fan-out + code |
| **5** Classify presentation | document edges | Front matter is a prefix, back matter a suffix; other classes are local to a paragraph | **Fan-out + code** |
| **6** Infer structure | skeleton window | Reparent and merge sections across boundaries — a splice cannot | **Orchestrated** |
| **8** Type items | paragraph run | Items never cross a paragraph, and each paragraph is committed by exactly one window | **Fan-out + code** |
| **9** Resolve referents | paragraph run | Decide whether window 3's "the professor" is window 1's "Dr. Vance" — **irreducibly global** | **Orchestrated** (§8.7) |
| **10** Cut scenes | leaf section | Setting inheritance is bounded to within a leaf section [unit §3.1.1] — so it never crosses a window | **Fan-out + code** |
| **11** Link scenes | scene run | `FlashbackOf`, `ConcurrentWith`, `Frames` relate scenes that may be a hundred apart — **no planner can put both endpoints in one window** | **Orchestrated** (§8.9) |

Phase 10 is the pleasing case: the unit definition's bound on setting inheritance — *never across
a section boundary* — is exactly what makes the phase windowable with a guaranteed join. A
constraint written to cap error propagation turns out to buy reproducibility too.

Phases 9 and 11 are the opposite, and the two strongest fits in this plan — for the same reason
stated over different objects. Coreference has no invariant a planner can impose: no windowing
makes "is this the same person" decidable locally. Neither does a frame: a scene and the scene it
frames can sit a hundred apart, and no window contains both. Where phase 6 would merely be
*better* with judgement, 9 and 11 are **impossible without it**.

They differ in one respect that matters for cost. Phase 9 runs on every document, because every
document has referents. Phase 11 runs only where the deterministic pass found a candidate, which
on a textbook or a transcript is usually nowhere at all (§8.9).

### 8.3 Why items are typed before scenes are cut

The naive order is scenes-then-items, and it is wrong. **Mode is largely a function of the item
mix**, and mode selects which coordinates are cut-bearing at all [unit §4.2]:

| Item mix over a span | Implied mode |
|---|---|
| `Speech(embodied, in-scene)` + `Action` | `Enacted` |
| `Speech(embodied, audience)` dominant | `Expounded` / `Addressed` |
| `Speech(disembodied, audience)` + `Description` | `Narrated` |
| `Exhibit` only | `Exhibited` |

The table's discriminating power sits in the **Speech attributes**, not in the
`Action`/`Description` split: `Enacted` and `Narrated` are separated by `Embodied` and
`Addressee`, which are assigned far more reliably. That is deliberate — mode is always a cut
[unit §4.2], so it must not hang on a distinction the model gets wrong sometimes (§3.1).
`Action` and `Description` break ties; neither ever carries a mode decision alone.

Typing items first makes scene cutting **mostly deterministic over item features**, leaving the
model only Setting and Subject — the two coordinates that genuinely need judgment. Cutting first
would force the model to guess mode from raw prose and then be stuck with that guess.

### 8.4 Why referents resolve between the two

Cast is a scene coordinate, needed at phase 10, but personas are discovered from speech
attribution, available only at phase 8. The circularity is broken by splitting the work rather
than by ordering it:

1. **Phase 8** records each Speech item's speaker **surface form** ("she", "Dr. Vance", "Q:")
   and emits **candidate tags** with surface forms only. No resolution is attempted.
2. **Phase 9** clusters surface forms into personas and **binds every tag to an id** —
   deterministic for speaker-labelled transcripts, screenplays and `Enumerated` groups;
   agent-assisted for prose coreference and `Named` groups. It assigns `PersonaKind`, scope and
   group membership using `Persona` instincts, and resolves `Place` and `ExhibitRef` tags
   against their registries in the same pass. It writes no prose: render text needs the ids this
   phase binds, but nothing in the pipeline needs render text, so it runs later as an
   augmentation (§3.6).
3. **Phase 10** cuts scenes against resolved ids, so C8 holds at assignment time rather than
   being patched afterwards.

Step 2 is an orchestration, not a fan-out, and §8.7 specifies it.

### 8.5 Phase 5 — Classify presentation

Placed **before** structure inference deliberately: front matter and a table of contents
inferred *as sections* is noise in the tree, and withholding them first makes the tree both
smaller and better.

1. Deterministic candidates: position (leading and trailing blocks), phase-1 artifact labels,
   ToC signatures (dot leaders, trailing page numbers, heading-like lines in a dense run).
2. `Metadata`-scope instincts for both the production and the composition family.
3. Agent pass **over candidates only**, never the whole document, returning a class per
   paragraph with evidence ids.
4. Default to `Displayable` on uncertainty and flag it (§5.2).
5. Deterministic: emit the per-paragraph composition-family feature vector and re-confirm the
   run family from it (§6). Phase 7 aggregates that vector onto the tree to derive family scopes
   (§6.1); this phase only produces the evidence, because the tree it would be attributed to does
   not exist yet.

### 8.6 Phase 8 — Type items

Fan-out over displayable paragraphs, windowed like phase 3 with overlap context. Returns, per
span: character range, kind, Speech attributes, speaker surface form, `IsStandalone`, and
candidate tags with surface forms but unresolved referents. It splits mixed-kind sentences
(§3.3) but never writes text — render text belongs to the `render-text` augmentation, after the
freeze (§3.6).

Deterministic pre-segmentation handles quoted dialogue, speaker labels, fenced blocks and
tables, so the model sees only genuinely ambiguous prose. `ItemType` instincts are injected.
Gated by `item-coverage`, `display-integrity` and `tag-bounds`.

### 8.7 Phase 9 — Resolve referents (orchestrated)

The one scene phase that is an orchestration, for the reason in §8.2: coreference cannot be made
local. It reuses phase 6's machinery part for part.

| Part | Phase 6 | Phase 9 |
|---|---|---|
| Sub-agent | `StructureWindowerAgent` (Mid) | `PersonaWindowerAgent` (Mid) |
| Bench | `WindowBench` | same class, its own directive type |
| Orchestrator | `StructureOrchestratorAgent` (Large) | `PersonaOrchestratorAgent` (Large) |
| Plan | assembly plan over subtree nodes | **registry plan** over candidate referents |
| Materializer | `OrchestratedTreeMaterializer` | `RegistryMaterializer` |
| Invariant asserted | every paragraph id placed exactly once | **every candidate assigned exactly once** |

**Round 0 — the bench.** Each windower sees a run of displayable paragraphs with their typed
items and candidate tags, and returns *local* referents: clusters of surface forms it is
confident co-refer inside its own window ("she" = "the professor" = `C7`), each with its tag
ids, plus `openQuestions` for what it cannot settle alone — "`C7` is introduced by a pronoun
before my first paragraph."

**Rounds 1..n — the orchestrator.** It sees candidate digests, never tag text or paragraph text,
and asks follow-ups from a closed vocabulary. It emits a **registry plan**: which candidates
merge into one persona, which register as new, which are `SceneLocal` rather than `Persistent`,
and each group's per-reference membership (§4.3).

**The load-bearing constraint is phase 6's** [orchestrator §4.3]: *the orchestrator composes
references, not content.* It never emits a surface form, a paragraph id, or a tag id it was not
given — it names candidates the windowers produced. So the same argument holds here as there:
adding an agent strengthens `text-integrity`, because the only ids in play are ones code issued.

**Materialize.** `RegistryMaterializer` (pure, `Core/Personas/`) applies the plan, allocates
persona ids in document order, binds every tag's `ReferentId`, and asserts two things: every
candidate is assigned exactly once, and no merge crosses a family scope root (§6.1). A plan that
would orphan a candidate or merge across a scope cannot be materialized; the error is exact and
goes back through the repair path. `tag-resolution`, `group-closure` and `persona-scope` are
therefore properties of the materializer rather than checks that can fail.

**The phase ends there.** It does exactly one job — turning surface forms into ids — which is
the shape §8.2's test asks for: an orchestration because its join needs judgement, carrying
nothing that does not.

**Roles, routing and spend.** Two `AgentRole` members — `PersonaWindower` (Mid),
`PersonaOrchestrator` (Large) — which means two qualification keys, two model-registry seed
entries and two `PresumedQualifiedRoles` bootstrap entries before anything routes
[orchestrator §7]. No third role: the re-writer runs as an augmentation under `Augmenter`
(§3.6). Both pin per run so a resumed run does not switch models mid-document. Spend is bounded
by the budget ledger, `FanOutBatchSize` and `MaxOrchestratorRounds`; the orchestrator's input is
candidate digests, not document text.

### 8.8 Phase 10 — Cut scenes

1. **Deterministic.** Derive candidate mode per span from item mix (§8.3); derive cast from
   resolved Speech speakers; propose boundaries at mode changes, cast-set changes, and explicit
   `Transition` items.
2. **Agent.** Assign Setting and Subject, confirm or reject candidate boundaries, apply the
   treated-vs-listed granularity rule [unit §4.4] and boundary precedence [unit §4.3].
3. **Deterministic.** Enforce C1–C5 and C12; resolve inheritance per C7 within its four bounds
   [unit §3.1.1]; apply the `MinSceneSpan` floor; evaluate the derived flag rules —
   `unlocated-enactment` [unit §3.1], `oversized-scene` (C14), `deep-setting-inheritance`.

**The `MinSceneSpan` floor** (§3.5, C3) is the only thing standing between sub-paragraph cutting
and one scene per clause. Three properties define it:

1. **It is per composition family, resolved at the leaf section** (§6.1) — not one global number.
   The v1 values, priors for the S4 sweep rather than constants: **novel 2, textbook 2,
   transcript 3, screenplay 1.** The transcript is highest because the passing aside is its
   characteristic failure [unit §7 case 2]. The textbook is low because its short `Enacted`
   islands — worked examples, historical vignettes — are precisely the spans a renderer wants,
   and a high floor would swallow them [unit §7 case 7]. The screenplay is 1 because its mode
   changes are marked, which by rule 2 means the floor never runs there anyway.
2. **It applies to unmarked mode changes only.** An explicit `Transition` item or a trusted
   structural boundary *is* evidence, and the floor never overrules evidence: a marked
   one-sentence cut stands. The floor exists for prose that shifts without saying so, which is
   the only place a length heuristic is doing any judging.
3. **Its effect is measured, not assumed.** Step 3 emits `scene-cuts-suppressed-by-floor` per run
   and per family. That counter is the entire tuning signal — near zero and the floor is inert,
   large and it is setting the document's scene count by itself. S4 sweeps 1–4 per family against
   boundary F1 and writes the winner to `SceneCutOptions.MinSceneSpanByFamily`: config, not code,
   and deliberately not an instinct, because it is a number code reads rather than guidance a
   model reads (§7.1).

### 8.9 Phase 11 — Link scenes (orchestrated)

K4 requires scenes to carry typed links. §8.2's test decides where each one is produced, and it
splits the set cleanly in two:

| Link | Derivable? | Where |
|---|---|---|
| `Continues` | **Yes** — it is the C12 split: a cut forced at a section boundary across which the situation is unchanged | Phase 10, in code |
| `ReturnsTo`, `FlashbackOf`, `ConcurrentWith`, `Frames`/`FramedBy` | **No** — they relate scenes that may be a hundred apart, and no window planner can guarantee both endpoints land in one window | **Phase 11, orchestrated** |

That second row is the definition of orchestrator shape, and it is the second and last place in
this plan that meets it. The cheaper alternative — a step inside review — does not work: review
runs in fresh context by design [pipeline §9.9], and a reviewer that must also hold the whole
scene sequence in order to find a frame has stopped being a reviewer.

**The machinery is phase 9's, part for part**, which is why a separate phase is affordable: it
costs a directive type and a materializer, not an architecture.

| Part | Phase 11 |
|---|---|
| Sub-agent | `SceneLinkWindowerAgent` (Mid) — proposes links inside a window of scene digests |
| Bench | `WindowBench`, with its own directive type |
| Orchestrator | `SceneLinkOrchestratorAgent` (Large) |
| Plan | **link plan** over scene ids and link kinds |
| Materializer | `SceneLinkMaterializer` (pure, `Core/Scenes/`) |
| Invariant asserted | every link names two scenes that exist, and every link is stored with its inverse |

**The input is digests, never text.** A scene digest is its id, title, mode, `Time`, cast ids,
place id, subject, and the span of its `Transition` item — which is what a link decision actually
reads, and is orders of magnitude smaller than the scenes themselves. A thousand-scene novel
presents as a digest list, not as a document.

**Windows are runs of consecutive scenes** with overlap, and the windowers' `openQuestions` carry
exactly the candidate whose other endpoint is out of window — "SC0412 resumes something; its
frame is before my first scene." That is the case the orchestrator exists to settle, and there is
no windowing that removes it.

**The orchestrator composes references, not content** [orchestrator §4.3]: it names scene ids
code issued and link kinds from a closed enum, and emits no title, no span and no text. The same
guarantee therefore holds as in phases 6 and 9 — adding an agent cannot touch `text-integrity`.

**Four rules the materializer enforces**, so they cannot be violated rather than being caught
afterwards:

1. **Endpoints exist and differ.** No dangling id, no self-link.
2. **Inverses are materialized in pairs.** `Frames`/`FramedBy`, and `ReturnsTo` with its origin,
   are stored on both scenes or on neither, so K5's "every scene that frames another" is an index
   lookup from either end.
3. **Orientation follows document order.** `FlashbackOf`, `FramedBy` and `ReturnsTo` point
   backwards; `ConcurrentWith` is symmetric. A plan proposing a forward `FlashbackOf` is rejected
   with an exact error rather than silently flipped — the orchestrator disagreeing with document
   order is a signal, not a typo to repair.
4. **Evidence or nothing.** Every link cites the ids justifying it (C13): a `Transition` item, a
   `Time` relation of `Earlier` or `Simultaneous`, or a place or cast identity with the earlier
   scene. An uncited link is not stored. K4 is worth less than C11.

**Bounds.** `MaxLinksPerScene` (default 4) caps the fan one scene can accumulate; the existing
ledger, `FanOutBatchSize` and `MaxOrchestratorRounds` bound the spend. Two roles —
`SceneLinkWindower` (Mid), `SceneLinkOrchestrator` (Large) — with their qualification keys,
registry seeds and `PresumedQualifiedRoles` entries, pinned per run [orchestrator §7].

**It is skippable, and usually skipped.** The deterministic pass runs first; a run whose scenes
yield no candidate — no backward `Time` relation, no frame signal, no `Transition` pointing at an
earlier situation — makes **zero model calls**, the same way a clean heading hierarchy skips the
structure agents [orchestrator §8]. Most textbooks and most transcripts have no frames and no
flashbacks and pay nothing for the phase.

## 9. Validation

Every new phase gets a deterministic gate, per the pipeline's existing posture.

| Check | Phase | Rule | Severity |
|---|---|---|---|
| `family-scope` | 7, 14 | Every family scope root is an existing section id, differs from its parent's resolved family, and satisfies the §6.1 guards | **Hard gate** |
| `family-homogeneity` | 7 | Paragraph-level family evidence disagreeing with its section's resolved family stays inside that family's learned band | Warn → review |
| `presentation-integrity` | 5, 14 | Displayable + metadata paragraphs, in order, equal the cleaned source | **Hard gate** |
| `metadata-recall` | 5 | Metadata rate falls within the family's learned band | Warn → review |
| `item-coverage` | 8, 14 | Every displayable character lies in exactly one item's span | **Hard gate** |
| `display-integrity` | 8, 14 | A paragraph's item spans, concatenated, equal its frozen text | **Hard gate** |
| `tag-bounds` | 8, 14 | Every tag's offsets lie inside its item's span; tags nest or are disjoint, never partially overlap | **Hard gate** |
| `tag-resolution` | 9, 14 | Every tag binds to an existing registry id; no dangling referents | **Hard gate** |
| `group-closure` | 9, 14 | Every member id on a `Group` tag exists; a group's presence puts that tag's resolved members in the scene | **Hard gate** |
| `persona-scope` | 9, 14 | No persona merges candidates with different family scope roots (§6.1) | **Hard gate** |
| `scene-partition` | 10, 14 | Every displayable item lies in exactly one scene, contiguous and ordered (C1–C3) | **Hard gate** |
| `scene-leaf` | 10 | No scene contains a scene (C4) | **Hard gate** |
| `tree-leaf-scenes` | 10, 14 | Every section holds sections or scenes, never both (§2.2) | **Hard gate** |
| `situation-single` | 10 | Every coordinate is single-valued (C5) | **Hard gate** |
| `situation-total` | 10 | Every coordinate has a value or its nothing-value (C6) | **Hard gate** |
| `cast-closure` | 10 | Every cast entry resolves to a persona id (C8) | **Hard gate** |
| `no-fabrication` | 10, 11 | Every cited evidence id exists and was issued by code | **Hard gate** |
| `evidence-present` | 10 | Every non-inherited, non-nothing coordinate cites ids (C13) | Warn |
| `section-containment` | 10 | No scene straddles a section boundary (C12) | Warn + `Continues` link |
| `link-integrity` | 11, 14 | Every link's endpoints exist and differ; inverses stored in pairs; orientation follows document order (§8.9) | **Hard gate** |
| `link-evidence` | 11 | Every link cites the ids justifying it; an uncited link is not materialized (C13) | **Hard gate** |
| `render-text-grounding` | 15 | Generated render text uses only its span's lemmas, the canonical names of the personas its own tags resolve, and the closed-class allowlist (§3.7) | **Hard gate on the output** — rejected, regenerated once, then flagged [pipeline §12.3]; never blocks a run |

`no-fabrication` is the scene-level analogue of the rule that any unissued id in model output is
a validation error [pipeline §8.5]. It is what makes C11 — *no guessing* — enforceable rather
than aspirational.

**Five of these are not checks at all in the phase that owns them.** `tag-resolution`,
`group-closure`, `persona-scope`, `link-integrity` and `link-evidence` are asserted by their
materializers (§8.7, §8.9): a plan that would violate one cannot be applied, and the error names
the offending op. They appear in the table because they are re-run at the freeze, where an
artifact is being *checked* rather than *constructed* — and a check that can only fail when
storage has been corrupted is exactly what a freeze gate should be.

`render-text-grounding` is the one row with a different severity shape, and §3.7 explains why:
it guards an output produced after the freeze, so rejecting the output is the complete remedy.
There is nothing to block.

## 10. Data model

Records live in `ProtoFast.Segmentation.Core/`. Id conventions follow [pipeline §8.5].

```csharp
// ---- Presentation (phase 5) -------------------------------------------------
public enum Presentation { Displayable, Metadata }
public enum MetadataClass
{
    RunningApparatus, FrontMatter, BackMatter, NavigationalLabel, ProductionNoise
}

public sealed record ParagraphPresentation(
    string ParagraphId,
    Presentation Presentation,
    MetadataClass? Class,                // non-null iff Presentation == Metadata
    IReadOnlyList<string> EvidenceIds,
    double Confidence);

// ---- Items (phase 8) --------------------------------------------------------
public enum ItemKind { Speech, Action, Description, Exhibit, Transition }
public enum Embodiment { Embodied, Disembodied }
public enum Addressee { InScene, Audience, Self }

public sealed record SceneItem(
    string ItemId,                       // "I000431"
    string ParagraphId,                  // an item never spans two paragraphs
    int StartOffset,                     // half-open char offsets into the paragraph (§3.3)
    int EndOffset,
    ItemKind Kind,
    SpeechAttributes? Speech,            // non-null iff Kind == Speech
    string? ExhibitId,                   // non-null iff Kind == Exhibit
    IReadOnlyList<Tag> Tags,             // §4 — the item is JSON, not text
    bool IsStandalone,                   // false => the span cannot be staged alone (phase 8)
    string? RenderText,                  // generated, never content (§3.6); null when standalone
    IReadOnlyList<string> EvidenceIds);

public sealed record SpeechAttributes(
    string SurfaceForm,                  // as written: "she", "Dr. Vance", "Q:" — set at phase 8
    string? SpeakerPersonaId,            // null until phase 9 binds it (§8.4)
    Embodiment Embodiment,
    Addressee Addressee,
    bool Voiced);

// ---- Tags (phase 8 emits, phase 9 resolves) ---------------------------------
public enum TagKind { Persona, Group, Place, ExhibitRef }

public sealed record Tag(
    string TagId,
    TagKind Kind,
    int StartOffset,                     // half-open char offsets into the PARAGRAPH text (§4.1)
    int EndOffset,
    string SurfaceForm,                  // as written: "the professor", "she"
    string? ReferentId,                  // null until phase 9; then a persona, place or exhibit id
    GroupMembership? Membership,         // non-null iff Kind == Group — per reference, not per
                                         // registry entry (§4.3): "the brothers" may be 2 here
                                         // and 3 in chapter 20
    double Confidence);

// ---- Personas (phase 9) -----------------------------------------------------
public enum PersonaKind { Individual, Group }
public enum PersonaScope { Persistent, SceneLocal }
public enum GroupFormation { Enumerated, Named }

public sealed record Persona(
    string PersonaId,
    string CanonicalName,
    PersonaKind Kind,
    PersonaScope Scope,
    string? ScopeRootSectionId,          // null == document-wide; otherwise the family scope
                                         // this persona lives in and never merges out of (§6.1)
    IReadOnlyList<string> SurfaceForms,
    GroupFormation? Formation,           // non-null iff Kind == Group; identity, not membership
    IReadOnlyList<string> EvidenceIds);

// Hangs off the Group TAG, not the Persona: membership is contextual (§4.3).
public sealed record GroupMembership(
    IReadOnlyList<string> MemberPersonaIds,
    bool IsComplete);                    // rendering switch: members vs. crowd

// ---- Family scopes (evidence at phase 5, derived in code at phase 7) --------
// Resolution for any node: nearest ancestor-or-self carrying a scope, else the run's family
// (§6.1). A single-family document produces none of these.
public sealed record FamilyScope(
    string SectionId,
    string CompositionFamily,
    IReadOnlyList<string> EvidenceIds,
    double Confidence);

// ---- Scene links (phase 11) -------------------------------------------------
public enum SceneLinkKind { Continues, ReturnsTo, FlashbackOf, ConcurrentWith, Frames, FramedBy }

public sealed record SceneLink(
    string FromSceneId,
    string ToSceneId,
    SceneLinkKind Kind,
    IReadOnlyList<string> EvidenceIds,   // never empty: an uncited link is not materialized (§8.9)
    double Confidence);

// ---- Augmentation targets (phase 15) ----------------------------------------
// Augmentation is pluggable [pipeline §12.1]; its granularity is a property of the type,
// not of the pipeline. Each type declares what one call covers.
public enum AugmentationScope { Item, Scene, Section, Persona }

// Which means the paragraph-shaped contract generalises: IAugmentationType gains
// `AugmentationScope Scope { get; }`, BuildContext is typed by that scope, and `aug-grounding`
// [pipeline §11] reads "its own target id" where it read "its own paragraph id".
public sealed record ItemAugmentationContext(
    SceneItem Item,
    string ParagraphText,                     // the frozen paragraph, so offsets resolve
    IReadOnlyList<Persona> ResolvedPersonas,  // exactly those the item's own tags bind (§3.7)
    string SceneId);

// The re-writer is one such type rather than a phase (§3.6):
//   Name = "render-text", Scope = Item, Tier = Mid, Role = Augmenter,
//   selector = items with IsStandalone == false,
//   Validate  = render-text-grounding (§3.7).

// ---- Options (bound from Seg_Pipeline__*, [pipeline §20.3]) -----------------
public sealed class FamilyScopeOptions                                    // §6.1
{
    public int MaxFamilyScopeDepth { get; set; } = 2;
    public int MinFamilyScopeParagraphs { get; set; } = 60;
    public int MaxFamilyScopes { get; set; } = 32;
}

public sealed class SceneCutOptions                                       // §8.8
{
    public int DefaultMinSceneSpan { get; set; } = 2;                     // sentences (§3.5)
    public IDictionary<string, int> MinSceneSpanByFamily { get; set; }
        = new Dictionary<string, int>
          { ["novel"] = 2, ["textbook"] = 2, ["transcript"] = 3, ["screenplay"] = 1 };
    public int OversizedSceneParagraphs { get; set; } = 40;               // C14, advisory
    public int MaxInheritanceDepth { get; set; } = 3;                     // [unit §3.1.1]
    public int FanOutBatchSize { get; set; } = 8;
}

public sealed class SceneLinkOptions                                      // §8.9
{
    public int MaxLinksPerScene { get; set; } = 4;
    public int FanOutBatchSize { get; set; } = 8;
    public int MaxOrchestratorRounds { get; set; } = 3;
}
```

Four records defined elsewhere carry this plan's fields:

- **`Scene`** [unit §8] — holds `ItemIds`, `FirstItemId` and `LastItemId`; `SceneId` derives from
  `(FirstParagraphId, StartOffset)`, since two scenes may start in one paragraph (C10); `Links`
  is populated by phase 11 (§8.9).
- **`SectionNode`** — holds `SceneIds` under children-xor-scenes (§2), and carries an optional
  `FamilyScope` (§6.1).
- **`RunManifest`** — carries `CompositionFamily` beside the production family (§6), which is the
  fallback of the §6.1 resolution rather than the only answer.
- **`FamilyInstinct`** — carries `Axis` and `Scope` (§7.1).

## 11. Milestones

| # | Milestone | Ships | Done when |
|---|---|---|---|
| **S1** | Presentation classification | Phase 5, `Presentation` model, the per-paragraph family evidence vector, `presentation-integrity`, `Metadata` instinct scope; family scopes derived at phase 7 with `family-scope` (§6.1) | Front matter and ToC excluded on the gold set at ≥ 0.9 recall with **0 over-removals**; `text-integrity` still green on every document; a single-family document produces zero scope roots |
| **S2** | Item typing | Phase 8, the five kinds, Speech attributes, sub-sentence spans, `item-coverage`, `display-integrity` | Item-kind agreement with gold ≥ 0.85 **with `Action` and `Description` collapsed** (the split figure is reported, not gated — §12); coverage 100% on every document; sub-sentence splits agree with gold on the mixed-kind sentences in the set |
| **S3** | Referents and tags | Phase 9 as an orchestration (§8.7), the tag layer, three registries, scope assignment, per-reference group membership | Speaker-labelled sources resolve deterministically; prose coreference ≥ 0.8 on gold; `tag-resolution`, `tag-bounds`, `group-closure`, `persona-scope` green; tagging cost measured against the §4.5 estimate; **`IsStandalone` rate instrumented per family** — it prices S7 and gates nothing (§13) |
| **S4** | Scene cutting and grouping | Phase 10, all C-checks, derived flags, children-xor-scenes tree, `SceneCutOptions` | Boundary F1 ≥ 0.8 on gold; every hard gate green; `Continues`-link rate measured [unit §9.1]; `MinSceneSpan` swept 1–4 per family and the winner written back to config (§8.8) |
| **S5** | Scene links | Phase 11 (§8.9), `SceneLink`, `SceneLinkMaterializer`, the two roles, `link-integrity`, `link-evidence` | Frame and flashback links agree with gold at ≥ 0.7 **precision** — precision rather than recall, because a wrong link misreads the plot for every consumer while a missing one costs a link; and zero model calls on documents the deterministic pass finds no candidates in |
| **S6** | Family instincts end to end | `Axis` / `Scope`, composition-family detector, family scopes in anger, instinct capture from review | A new composition type reaches S4 thresholds **with instincts only — no code change**; a two-family anthology resolves per section and produces no cross-scope persona merge |
| **S7** | Renderer-facing output | Per-item augmentation, the `render-text` type and its grounding gate (§3.7), scene records over gRPC, the ThePlot scene view | K1 demonstrated: a scene renders from its own record with no document access; every non-standalone item in a rendered scene carries grounded render text or falls back cleanly to its span |

**S6 is the milestone that proves the architecture.** If a new content type needs a code branch,
§7.3 is wrong and the design should be revisited before more types are added.

## 12. Decisions

| Question | Decision |
|---|---|
| **Gold-set scope** | Four composition families for v1: **novel, textbook, interview transcript, screenplay**. Chosen to span the two axes that actually vary — dialogue density and setting explicitness — rather than to sample genres: the screenplay is explicit on both, the textbook near-null on both, the novel dialogue-heavy with implicit settings, the transcript dialogue-heavy with absent settings. A design that scores well on all four is unlikely to be surprised by a fifth. **Sizing: 20 documents per family, scene-cut by hand and annotated with their frame and flashback links.** This is the plan's largest unbudgeted cost and it gates S4, S5 and S6. Its adequacy is itself tripwired on within-family variance (§13). |
| **Tagging cost** | Held down by the tag set rather than by a fallback (§4.5): dropping `Time` and `Topic` removes the kinds that fire per sentence. No v1 reduction to `Persona`-only is planned. If S3 measurement still shows cost dominating, `ExhibitRef` is the next to go, since its coordinate is a link rather than a cast or setting fact. |
| **Tag offsets** | Paragraph-relative, not item-relative (§4.1), so they stay verifiable against the hashed artifact. |
| **Where personas resolve** | Phase 9, between item typing and scene cutting (§8.4) — not inside either. |
| **Executor shape** | **Orchestrator by default, fan-out where the join is guaranteed** (§8.2). An executor becomes an orchestrator exactly where the cross-window join needs judgement; where the window planner guarantees the join, code performs it, because replacing a guarantee with an opinion is a regression. Of the five scene phases, two qualify: 9, where coreference cannot be made local (§8.7), and 11, where a link's two endpoints cannot be forced into one window (§8.9). |
| **`Action` vs. `Description`** | **Keep both; tolerate confusion.** They are not merged into a `Depiction` kind with an `IsEvent` flag. The pair is genuinely ambiguous on many spans, so instead of fighting it, nothing load-bearing is allowed to depend on it: no hard gate reads the distinction and mode derivation keys on Speech attributes (§3.1, §8.3). S2's threshold is measured with the pair collapsed as well as split, and only the collapsed figure gates. |
| **Item granularity** | **Sub-sentence, character offsets** (§3.3). A sentence that mixes dialogue and action is two items. Scene boundaries stay at sentence granularity (§3.5). |
| **Non-stageable fragments** | **A re-writer**, and it is an **item-scoped augmentation, not a phase** (§3.6). Generated `RenderText` is fenced by C9's carve-out and gated per output by `render-text-grounding`. Placing it after the freeze is what makes its cost scale with what is rendered rather than with the corpus — which is why it needs no measurement to price. |
| **Render-text grounding** | **A hard gate on the output** (§3.7): the span's own lemmas, plus the canonical names of the personas the span's own tags resolve, plus a **closed-class allowlist**. The allowlist is what makes hardness affordable — every feared false failure was a function word, and function words carry no referents. It never drops to a warning; an ungrounded rewrite is worse than a missing one. |
| **Mixed composition families** | **Family is a scope on a section, not a field on a run** (§6.1). Resolution is nearest-ancestor-or-self; four deterministic guards keep scopes at the top of the tree; personas never merge across a scope, enforced by the materializer rather than by a check. A single-family document is unaffected and produces no scopes. |
| **Scene links (K4)** | **Phase 11, orchestrated** (§8.9). `Continues` stays deterministic at phase 10; the five links that relate distant scenes are the only other place in this plan meeting §8.2's test, because no window planner can put both endpoints in one window. Skippable at zero cost when the deterministic pass finds no candidates. |
| **`MinSceneSpan`** | **Per composition family, and applied to unmarked mode changes only** (§8.8): novel 2, textbook 2, transcript 3, screenplay 1. A marked boundary is evidence and bypasses the floor entirely, which confines the heuristic to prose that shifts without saying so. `scene-cuts-suppressed-by-floor` is the tuning signal S4 sweeps against. |
| **Group membership** | **Per reference, on the `Group` tag** (§4.3), not on the registry entry. Membership is contextual; identity is not. |
| **Augmentation granularity** | **A property of the augmentation type, not of the pipeline.** The contract is already pluggable [pipeline §12.1], so `AugmentationScope` admits `Item`, `Scene`, `Section` and `Persona` and each type declares its own. No single granularity is imposed. |

## 13. What would reopen a decision

No question in this plan is left open. What remains is the honest residue of deciding under
uncertainty: each row below names a measurement, the threshold at which it fires, and **the
action already chosen for that branch.** A tripwire that never fires costs nothing; one that
fires does not need a meeting.

| Decision | Tripwire | Threshold | Pre-decided action |
|---|---|---|---|
| `Action` and `Description` kept as separate kinds (§3.1) | Gap between collapsed and split S2 agreement | Split < 0.6 while collapsed ≥ 0.85, on two consecutive gold runs | Keep both kinds; stop reporting the split figure. At that gap it is measuring annotator disagreement, not model error, and no gate reads the distinction (§8.3), so nothing downstream moves. |
| Render text as an augmentation rather than a phase (§3.6) | `IsStandalone == false` rate, per family (S3) | > 0.5 of items | **The pipeline does not change** — by construction the cost already lands on render jobs rather than on runs. Batch `render-text` by scene (several items per call), and re-examine phase 8's span boundaries, since over-splitting is the likelier cause of a fragment that cannot stand alone. |
| `render-text-grounding` as a hard gate (§3.7) | Rejection rate on outputs a reviewer judged good | > 0.05 after the closed-class allowlist | Extend the allowlist for that language and re-measure: a rejected good rewrite is, by the argument in §3.7, always a missing function word. The gate does **not** become a warning. |
| Four tag kinds, no `Prop` (§4.2) | A renderer needing object continuity | A shipped renderer feature that cannot be built from `Persona`, `Place` and `ExhibitRef` | Add `Prop` as a fifth kind behind a per-family switch, off by default, and price it against §4.5 before enabling it anywhere. It does not become a coordinate, and [unit §6.3] still stands: tagging a prop is not maintaining world state. |
| `ExhibitRef` retained despite tagging cost (§12) | Tagging's share of run spend (S3) | > 0.4 of total model spend | Drop `ExhibitRef` first, exactly as §12 states — its coordinate is a link, not a cast or setting fact. `Persona`, `Group` and `Place` are not negotiable: they are C13's evidence for two coordinates (§4.4). |
| `MinSceneSpan` per-family priors (§8.8) | `scene-cuts-suppressed-by-floor` | > 0.25 of candidate cuts in a family | The floor is setting that family's scene count; take the S4 sweep's value over the prior. If the sweep prefers 1, that family's mode changes are marked and the floor was inert there by design — record it and move on. |
| Phase 11 as a phase of its own (§8.9) | Model calls made by phase 11 | Zero on > 0.9 of a family's runs | Keep the phase. A phase that costs nothing on documents without frames is not overhead, and the deterministic skip is doing precisely its job. Fold it into review only if that skip stops being free. |
| One persona registry per family scope (§6.1) | Cross-scope duplicates a reviewer merges by hand | > 2 per document on the anthology gold set | Add a `SameAs` link **between** scope registries rather than merging entries, so each work's casting stays independent and K3's promise — consistency within a work — is unchanged. Merging registries is not the fallback; it is the thing the scope exists to prevent. |
| Gold set at 20 documents × 4 families (§12) | Variance of S4 boundary F1 across the family's documents | Standard deviation > 0.1 within a family | Add documents to **that family only**, in blocks of 10, until it is under 0.1. The thresholds do not move to accommodate a noisy family — a metric that will not stabilise is measuring the family's diversity, and that is the thing the gold set is for. |

**One thing is deliberately not a tripwire.** If a new composition family needs a code branch to
reach S4, that is not a threshold to tune — it falsifies §7.3, and the instinct mechanism should
be reconsidered before any further families are added. S6 exists to find that out early, while
the pipeline is still small enough to change.

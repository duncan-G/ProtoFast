# ThePlot — Scene segmentation plan

Status: **plan / for review**.

This document specifies how the segmentation pipeline produces **scenes**. It sits between two
others and adds nothing they already say:

| Document | Role | This plan's relationship |
|---|---|---|
| `theplot-scene-unit.md` | Defines the scene: five coordinates, constraints C1–C14, capacities K1–K9 | **Consumed.** This plan implements it and amends five points (§12). |
| `theplot-segmentation-plan.md` | Defines the pipeline: phases, agents, validation, storage, infra | **Extended.** This plan adds four phases and changes one schema. |

**Reference convention.** `[unit §3.3]` cites the scene-unit document. `[pipeline §9.7]` cites
the segmentation plan. A bare `§4.2` is a section of *this* document. `C1`–`C14` are unit
constraints, `K1`–`K9` unit capacities, `F2`/`F8` pipeline features.

**Principles inherited unchanged from the pipeline** [pipeline §3]: code runs the pipeline and
models make only judgment calls; models label and never rewrite text; every phase leaves an
inspectable artifact.

---

## 1. Definitions

Every term this plan introduces or redefines, in one place. Terms marked *existing* are
unchanged from [pipeline §4] and are repeated only because the new terms are defined against
them.

| Term | Definition |
|---|---|
| **Line** *(existing)* | Smallest unit from conversion: one visual line or layout block, with a stable `LineId`. |
| **Paragraph** *(existing)* | A contiguous run of lines judged to form one paragraph. The **text-integrity unit**: frozen, content-hashed, never written by a model. |
| **Sentence index** | The 0-based position of a sentence within its paragraph. Already in the pipeline — `paragraphEdits.beforeSentence` in `tree.schema.json` uses it. |
| **Item** | A typed, contiguous **character span** inside exactly one paragraph, owned by exactly one scene. The **composition unit** and the renderer's input (§3). Finer than a sentence: one sentence can hold a `Speech` item and an `Action` item. |
| **Render text** | Generated prose that makes a non-standalone item stageable — "as he ran for the door" → "James ran for the door." Marked as generated, never content, never hashed (§3.6). |
| **Displayable text** | The concatenation, in document order, of every paragraph whose `Presentation` is `Displayable`. This is the work; everything else is metadata. |
| **Metadata** | Content present in the source but not part of the displayable work — page furniture, front and back matter, navigational labels, production noise (§5). Metadata is **excluded from the scene stream, never deleted**. |
| **Presentation** | The per-paragraph classification that decides the above: `Displayable` or `Metadata(class)`. |
| **Tag** | A typed reference over a character range inside an item, resolved to a registry id — the mechanism that makes "who and where" enumerable rather than a judgment call (§4). |
| **Registry** | The per-run id table for a referent type. Three exist: personas, places, exhibits. |
| **Persona** *(unit)* | A registry entry for anyone who speaks, is addressed, or is present. Individuals and groups alike; the narrator and `Unattributed` are personas [unit §3.3]. |
| **Group** | A persona whose referent is several people ("the brothers", "the crowd"). A `PersonaKind`, not a separate referent type (§4.3). |
| **Scene** *(unit)* | The maximal contiguous run over which one **situation** — `(Setting, Time, Cast, Mode, Subject)` — holds [unit §1]. In this plan it runs over **items**, not paragraphs (§12.1). |
| **Section** *(existing, retargeted)* | A tree node with a title. Contains sections **or scenes**, never both and never paragraphs (§2.2). |
| **Production family** | What made the file: converter, publisher, scanner, template. Governs cleaning and page furniture (§6). |
| **Composition family** | What kind of work it is: novel, textbook, transcript, screenplay, paper, manual. Governs metadata policy, item typing, and scene cutting (§6). |
| **Instinct** *(existing, scoped)* | Learned, advisory, per-family prompt guidance [pipeline §15.4]. Now carries an axis and a scope (§7). |

## 2. What changes

### 2.1 The shift

Today the pipeline's product is a **tree of sections over paragraphs**. It becomes a **sequence
of scenes over items**.

| Change | Consequence |
|---|---|
| **Scenes are the output unit** | The section tree stops being the deliverable. It becomes a *constraint* on where scenes may be cut (C12) and a navigation aid. |
| **Items become the leaf** | The paragraph keeps its job — text integrity — and the item takes the new one, composition. Both persist; neither replaces the other. |
| **Items are structured, not text** | An item is a span plus typed tags over sub-ranges, resolved to registry ids (§4). |
| **Metadata is excluded, not deleted** | Non-displayable content is classified and withheld from the scene stream while staying in the frozen record (§5). |
| **Scenes become the tree's leaves** | Sections stop containing paragraphs and contain scenes (§2.2). |

### 2.2 Three layers

| Layer | Contains | Invariant |
|---|---|---|
| **Section** | sections **or** scenes, never both | the existing children-xor rule, retargeted from paragraphs to scenes |
| **Scene** | items | C1–C5 |
| **Item** | a character span + tags | `item-coverage` (§3.4) |

Four consequences follow, and each retires something:

- **`tree.schema.json` changes shape.** The `paragraphs` branch of the `oneOf` becomes a
  `scenes` branch. `TreeProposal` / `TreeProposalNode` and the 6-level flattened wire schema
  change with it.
- **C12 stops being a check and becomes a structural fact.** A scene cannot straddle a section
  boundary when it is a *child* of one. The real disagreement between structural and staging
  boundaries [unit §7 case 6] does not disappear — it surfaces as a `Continues` link between
  sibling scenes instead of as a validation warning.
- **Montage and problem sets get their grouping without a fourth layer.** An **inferred** section
  (F8) holds a run of short scenes, so a consumer wanting the beat takes the section and one
  wanting the cuts takes the scenes. C5 is not weakened [unit §4.4, §7 case 5].
- **Narrative groupings the headings never marked** — acts, sequences, montages — are inferred
  sections. The tree already infers structure the source did not state; that capacity now does
  double duty.

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

`display-integrity` is the item-level analogue of the existing `text-integrity` gate
[pipeline §11], and a hard gate on the same terms. The *metadata* half of integrity — that
displayable and metadata paragraphs together still reproduce the cleaned source — is a separate
check at a separate phase, `presentation-integrity` (§9).

Both run over **spans only**. Neither sees render text (§3.6), which is why adding a re-writer
cannot weaken either one.

### 3.5 Scenes become item-granular

A scene is now a contiguous run of **items** (C2 and C3 amended, §12.1). One dense prose
paragraph can hold a `Description`, two `Speech` items, an `Action` — and, where the text
genuinely shifts mid-paragraph, a scene boundary. Paragraph granularity was never a principle;
it was an accident of the tree being the only structure available.

Sub-paragraph cutting is **wanted**, not merely permitted: a paragraph mixing dialogue, action
and description should be cut into those items, and the scene boundary follows wherever the
staging genuinely shifts.

Two granularities, deliberately different:

| Boundary | Granularity | Why |
|---|---|---|
| **Item** | sub-sentence, character offsets (§3.3) | Kinds mix inside one sentence; a renderer needs them apart. |
| **Scene** | sentence, never finer | A situation does not change halfway through a sentence, and a cut there would strand a clause. |

The paragraph-granularity accident was also the only thing preventing over-cutting, so it is
replaced by an explicit floor:

> A mode change spanning fewer than **`MinSceneSpan`** sentences (default 2) is recorded as an
> item attribute, not a cut.

The passing aside in a keynote ("when I was in Berlin, anyway —") still does not become a scene
[unit §7 case 2] — now for a stated reason rather than a structural side effect.

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
   concatenated back into the document. C9 is amended to name it (§12.6).
2. **The frozen record is untouched.** `text-integrity` still hashes paragraphs and
   `item-coverage` still partitions spans, both over span text alone (§3.4). A re-writer that
   produced nothing at all would leave every existing gate passing identically.
3. **It is grounded, and checkably so.** `render-text-grounding` restricts the output to the
   lemmas of its own span plus the canonical names of personas its own tags resolve. Rewriting
   "he" to "James" is legal because a `Persona` tag inside the span resolves to James.
   Introducing a door the span never mentions is not.
4. **It is the exception, not the rule.** An item whose span already stands alone — the large
   majority — carries no render text and costs nothing. The re-writer runs only over fragments
   flagged `IsStandalone = false` at typing time.

**It runs in phase 9, not phase 8.** Expanding "he" into "James" *is* referent resolution, and
the persona ids it needs do not exist until tags are bound (§8.4). Phase 8 types the fragment
and flags it as non-standalone; phase 9 resolves the tags and then writes the text.

## 4. Tags

> A **tag** is a typed reference over a character range inside an item, resolved to a registry
> id.

```
"The professor turned. She had been waiting."
 ^^^^^^^^^^^^^          ^^^
 Persona:PR003          Persona:PR003
```

Tags name offsets into frozen paragraph text and never carry text of their own, so "models
label, never rewrite" holds unchanged.

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
document**, with its own `IsComplete`. This settles unit §9.2's open question (§12.7).

Presence propagates one way: a group in the cast puts the members *its tag resolved at that
scene* into the scene; members being present does not conjure the group [unit §3.3].

**Tags may nest but may never partially overlap.** An `Enumerated` group tag spans "Mr and Mrs
Smith" with two `Persona` tags inside it. `tag-bounds` enforces that containment lattice.

### 4.4 What tags buy

Two things, both of which convert a judgment call into a query.

**C13 evidence becomes mechanical.** C13 requires every coordinate to cite what justifies it.
For the two coordinates that are expensive to evidence by hand, the citation is now a
by-product: Cast cites its `Persona` and `Group` tags, Setting cites its `Place` tags. An
unevidenced Cast or Setting is detectable by construction — it is one with no tags under it.
Time and Subject cite paragraph ids the ordinary way, which is exactly why they need no tag
kinds.

**"Mention is not presence" becomes computable** [unit §3.3]. Tags enumerate every reference;
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
| `RunningApparatus` | Running headers and footers, page numbers, running titles, "continued on p. 4" | Phase 1 today (F2) — extend, don't rebuild |
| `ProductionNoise` | Watermarks, scanner artifacts, OCR junk, crop marks | Phase 1, extended |
| `FrontMatter` | Title page, copyright, ISBN block, dedication, epigraph, table of contents, list of figures | **New** — phase 5 |
| `BackMatter` | Index, colophon, about-the-author, ad pages | **New** — phase 5 |
| `NavigationalLabel` | Figure numbers, "Chapter 4" as a bare label, section numbering separated from its title | **New** — phase 5 |

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

The current `DocumentFamily` [pipeline §9.2] conflates two independent things, and scene work
needs them split:

| Axis | Detects | Governs | Signals |
|---|---|---|---|
| **Production family** | How the file was made | Cleaning, `RunningApparatus`, OCR repair, layout trust | Producer string, page size, font set, margin geometry |
| **Composition family** | What kind of work it is | Metadata policy, scene cutting, item typing, persona scope | Speaker-label density, dialogue ratio, heading regularity, tense, person, imperative density |

A scanned PDF of a novel and a scanned PDF of a textbook share every production instinct and
almost no composition instinct. One field cannot carry both.

`RunManifest` gains `CompositionFamily` beside the existing family field. Both are detected in
phase 0; the composition family is re-confirmed in phase 5, once paragraph-level evidence
exists.

**Composition family is the single strongest predictor of everything in this plan**, which is
why it is worth a field, a detector, and its own instinct scope rather than a prompt hint.

## 7. Family instincts

The existing mechanism [pipeline §15.4] is right. It needs one structural change and four new
consumers.

### 7.1 The change: instincts get an axis and a scope

```csharp
public sealed record FamilyInstinct(
    string Family,
    FamilyAxis Axis,             // NEW: Production | Composition
    InstinctScope Scope,         // NEW: which agent may see it
    string Pattern, string Guidance,
    double Confidence, int Confirmations, DateTimeOffset LastSeen);

public enum FamilyAxis { Production, Composition }
public enum InstinctScope { Labeling, Structure, Metadata, ItemType, SceneCut, Persona }
```

Without `Scope`, every new consumer would see every instinct and the "max 6 per prompt" budget
would be spent on irrelevant guidance. With it, each agent draws from its own pool. Everything
else is unchanged: inject at confidence ≥ 0.7, max 6 per prompt, decay −0.05 per 30 days,
advisory only, never overriding trusted boundaries or validation, clusters promoted into a
family skill by a person.

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

Phases 0–4 are unchanged apart from composition-family detection in phase 0. Phases 5, 8, 9 and
10 are new; everything after shifts by four and its artifact keys renumber with it.

| # | Phase | Kind | Change | Output |
|---|---|---|---|---|
| 0–4 | Ingest → Assemble paragraphs | — | unchanged + `CompositionFamily` detection in 0 | `04_paragraphs.jsonl` |
| **5** | **Classify presentation** | Code + agent (fan-out) | **new** | `05_presentation.json` |
| 6 | Infer structure | Agent | was 5; now over displayable paragraphs only | `06_tree.json` |
| 7 | Validate and repair | Code + agents | was 6 | `07_validation.json` |
| **8** | **Type items** | Agent (fan-out) | **new** | `08_items.jsonl` |
| **9** | **Resolve referents** | **Orchestrated** + fan-out | **new** | `09_registries.json`, `09_personas/window_{n}.json` |
| **10** | **Cut scenes** | Code + agent (fan-out per leaf section) | **new** | `10_scenes.json` |
| 11 | Review | Agent (fresh context) | was 7; reviews scenes and items, not just the tree | `11_review.json` |
| 12 | Human gate | Request port | was 8; reviews scenes | `12_decision.json` |
| 13 | Freeze | Code | was 9; freezes paragraphs, presentation, tree, items, registries, scenes | `13_frozen.json` |
| 14–16 | Augment, review, publish | — | was 10–12; augmentation is **per item** | — |

### 8.2 Executor shape: when an executor is an orchestrator

**Default to an orchestrator agent driving a bench of sub-agents.** The pattern is built and
shipped for phase 6 — `StructureWindowerAgent` (Mid) fanned out by `WindowBench`, assembled by
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
| 3 Label | suspect region | Each line committed by exactly one window — planner-guaranteed | Fan-out + code *(unchanged)* |
| **5** Classify presentation | document edges | Front matter is a prefix, back matter a suffix; other classes are local to a paragraph | **Fan-out + code** |
| **6** Infer structure | skeleton window | Reparent and merge sections across boundaries — a splice cannot | **Orchestrated** *(built)* |
| **8** Type items | paragraph run | Items never cross a paragraph, and each paragraph is committed by exactly one window | **Fan-out + code** |
| **9** Resolve referents | paragraph run | Decide whether window 3's "the professor" is window 1's "Dr. Vance" — **irreducibly global** | **Orchestrated** *(new, §8.7)* |
| **10** Cut scenes | leaf section | Setting inheritance is bounded to within a leaf section [unit §3.1.1] — so it never crosses a window | **Fan-out + code** |
| — Scene links (K4) | whole document | `FlashbackOf`, `ConcurrentWith`, `Frames` relate distant scenes — a global judgement | **Orchestrator candidate** (§14) |

Phase 10 is the pleasing case: the unit definition's bound on setting inheritance — *never across
a section boundary* — is exactly what makes the phase windowable with a guaranteed join. A
constraint written to cap error propagation turns out to buy reproducibility too.

Phase 9 is the opposite, and the strongest fit in this plan. Coreference has no invariant a
planner can impose: no windowing makes "is this the same person" decidable locally. Where phase 6
would merely be *better* with judgement, phase 9 is **impossible without it**.

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
   against their registries in the same pass. It then writes **render text** for every item
   flagged non-standalone (§3.6) — the only model-authored prose in the pipeline, and possible
   only here because it needs the ids this phase just bound.
3. **Phase 10** cuts scenes against resolved ids, so C8 holds at assignment time rather than
   being patched afterwards.

Step 2 is an orchestration, not a fan-out, and §8.7 specifies it.

### 8.5 Phase 5 — Classify presentation

Placed **before** structure inference deliberately: front matter and a table of contents
inferred *as sections* is exactly the noise the tree suffers today, and removing it first makes
the tree both smaller and better.

1. Deterministic candidates: position (leading and trailing blocks), phase-1 artifact labels,
   ToC signatures (dot leaders, trailing page numbers, heading-like lines in a dense run).
2. `Metadata`-scope instincts for both the production and the composition family.
3. Agent pass **over candidates only**, never the whole document, returning a class per
   paragraph with evidence ids.
4. Default to `Displayable` on uncertainty and flag it (§5.2).

### 8.6 Phase 8 — Type items

Fan-out over displayable paragraphs, windowed like phase 3 with overlap context. Returns, per
span: character range, kind, Speech attributes, speaker surface form, `IsStandalone`, and
candidate tags with surface forms but unresolved referents. It splits mixed-kind sentences
(§3.3) but never writes text — render text is phase 9's job (§3.6).

Deterministic pre-segmentation handles quoted dialogue, speaker labels, fenced blocks and
tables, so the model sees only genuinely ambiguous prose. `ItemType` instincts are injected.
Gated by `item-coverage`, `display-integrity` and `tag-bounds`.

### 8.7 Phase 9 — Resolve referents (orchestrated)

The one scene phase that is an orchestration, for the reason in §8.2: coreference cannot be made
local. It reuses phase 6's machinery part for part.

| Part | Phase 6 (built) | Phase 9 (new) |
|---|---|---|
| Sub-agent | `StructureWindowerAgent` (Mid) | `PersonaWindowerAgent` (Mid) |
| Bench | `WindowBench` | same class, new directive type |
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

**The load-bearing constraint carries over unchanged** [orchestrator §4.3]: *the orchestrator
composes references, not content.* It never emits a surface form, a paragraph id, or a tag id it
was not given — it names candidates the windowers produced. So the same argument holds here as
there: adding an agent strengthens `text-integrity`, because the only ids in play are ones code
issued.

**Materialize.** `RegistryMaterializer` (pure, `Core/Personas/`) applies the plan, allocates
persona ids in document order, binds every tag's `ReferentId`, and asserts every candidate is
assigned exactly once. A plan that would orphan a candidate cannot be materialized; the error is
exact and goes back through the existing repair path. `tag-resolution` and `group-closure` become
properties of the materializer rather than checks that can fail — the same upgrade phase 6 made
to `CheckIdCoverage`.

**Then a plain fan-out.** Render text (§3.6) is generated per item, with no cross-window
judgement at all, so it is a bounded `Task.WhenAll` over the items flagged non-standalone — a
fan-out inside the executor, not a second orchestration. Applying §8.2's test to it honestly
gives fan-out, and it should stay one.

**Roles, routing and spend.** Two new `AgentRole` members — `PersonaWindower` (Mid),
`PersonaOrchestrator` (Large) — which means two qualification keys, two model-registry seed
entries and two `PresumedQualifiedRoles` bootstrap entries before anything routes
[orchestrator §7]. A third, `RenderTextWriter` (Mid), for the fan-out. All pin per run so a
resumed run does not switch models mid-document. Spend stays bounded by the existing ledger,
`FanOutBatchSize` and `MaxOrchestratorRounds`; the orchestrator's input is candidate digests,
not document text.

### 8.8 Phase 10 — Cut scenes

1. **Deterministic.** Derive candidate mode per span from item mix (§8.3); derive cast from
   resolved Speech speakers; propose boundaries at mode changes, cast-set changes, and explicit
   `Transition` items.
2. **Agent.** Assign Setting and Subject, confirm or reject candidate boundaries, apply the
   treated-vs-listed granularity rule [unit §4.4] and boundary precedence [unit §4.3].
3. **Deterministic.** Enforce C1–C5 and C12; resolve inheritance per C7 within its four bounds
   [unit §3.1.1]; evaluate the derived flag rules — `unlocated-enactment` [unit §3.1],
   `oversized-scene` (C14), `deep-setting-inheritance`.

## 9. Validation

Every new phase gets a deterministic gate, per the pipeline's existing posture.

| Check | Phase | Rule | Severity |
|---|---|---|---|
| `presentation-integrity` | 5, 13 | Displayable + metadata paragraphs, in order, equal the cleaned source | **Hard gate** |
| `metadata-recall` | 5 | Metadata rate falls within the family's learned band | Warn → review |
| `item-coverage` | 8, 13 | Every displayable character lies in exactly one item's span | **Hard gate** |
| `display-integrity` | 8, 13 | A paragraph's item spans, concatenated, equal its frozen text | **Hard gate** |
| `tag-bounds` | 8, 13 | Every tag's offsets lie inside its item's span; tags nest or are disjoint, never partially overlap | **Hard gate** |
| `tag-resolution` | 9, 13 | Every tag binds to an existing registry id; no dangling referents | **Hard gate** |
| `group-closure` | 9, 13 | Every member id on a `Group` tag exists; a group's presence puts that tag's resolved members in the scene | **Hard gate** |
| `render-text-grounding` | 9, 13 | Generated render text uses only its span's lemmas plus canonical names of personas its own tags resolve (§3.6) | **Hard gate** |
| `scene-partition` | 10, 13 | Every displayable item lies in exactly one scene, contiguous and ordered (C1–C3) | **Hard gate** |
| `scene-leaf` | 10 | No scene contains a scene (C4) | **Hard gate** |
| `tree-leaf-scenes` | 10, 13 | Every section holds sections or scenes, never both (§2.2) | **Hard gate** |
| `situation-single` | 10 | Every coordinate is single-valued (C5) | **Hard gate** |
| `situation-total` | 10 | Every coordinate has a value or its nothing-value (C6) | **Hard gate** |
| `cast-closure` | 10 | Every cast entry resolves to a persona id (C8) | **Hard gate** |
| `no-fabrication` | 10 | Every cited evidence id exists and was issued by code | **Hard gate** |
| `evidence-present` | 10 | Every non-inherited, non-nothing coordinate cites ids (C13) | Warn |
| `section-containment` | 10 | No scene straddles a section boundary (C12) | Warn + `Continues` link |

`no-fabrication` is the scene-level analogue of the rule that any unissued id in model output is
a validation error [pipeline §8.5]. It is what makes C11 — *no guessing* — enforceable rather
than aspirational.

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
    IReadOnlyList<string> SurfaceForms,
    GroupFormation? Formation,           // non-null iff Kind == Group; identity, not membership
    IReadOnlyList<string> EvidenceIds);

// Hangs off the Group TAG, not the Persona: membership is contextual (§4.3).
public sealed record GroupMembership(
    IReadOnlyList<string> MemberPersonaIds,
    bool IsComplete);                    // rendering switch: members vs. crowd

// ---- Augmentation targets (phase 14) ----------------------------------------
// Augmentation is pluggable [pipeline §12.1]; its granularity is a property of the type,
// not of the pipeline. Each type declares what one call covers.
public enum AugmentationScope { Item, Scene, Section, Persona }
```

Two records defined elsewhere change with this plan:

- **`Scene`** [unit §8] — `ParagraphIds` becomes `ItemIds` (§12.1).
- **`SectionNode`** — `ParagraphIds` becomes `SceneIds`, and children-xor-paragraphs becomes
  children-xor-scenes (§2.2).

## 11. Milestones

| # | Milestone | Ships | Done when |
|---|---|---|---|
| **S1** | Presentation classification | Phase 5, `Presentation` model, `presentation-integrity`, `Metadata` instinct scope | Front matter and ToC excluded on the gold set at ≥ 0.9 recall with **0 over-removals**; `text-integrity` still green on every document |
| **S2** | Item typing | Phase 8, the five kinds, Speech attributes, sub-sentence spans, `item-coverage`, `display-integrity` | Item-kind agreement with gold ≥ 0.85 **with `Action` and `Description` collapsed** (the split figure is reported, not gated — §13); coverage 100% on every document; sub-sentence splits agree with gold on the mixed-kind sentences in the set |
| **S3** | Referents, tags and the re-writer | Phase 9 as an orchestration (§8.7), the tag layer, three registries, scope assignment, per-reference group membership, render text | Speaker-labelled sources resolve deterministically; prose coreference ≥ 0.8 on gold; `tag-resolution`, `tag-bounds`, `group-closure`, `render-text-grounding` green; **`IsStandalone` rate measured** (open question 2); tagging cost measured against the §4.5 estimate |
| **S4** | Scene cutting and grouping | Phase 10, all C-checks, derived flags, children-xor-scenes tree | Boundary F1 ≥ 0.8 on gold; every hard gate green; `Continues`-link rate measured [unit §9.1] |
| **S5** | Family instincts end to end | `Axis` / `Scope`, composition-family detector, instinct capture from review | A new composition type reaches S4 thresholds **with instincts only — no code change** |
| **S6** | Renderer-facing output | Per-item augmentation, scene records over gRPC, the ThePlot scene view | K1 demonstrated: a scene renders from its own record with no document access |

**S5 is the milestone that proves the architecture.** If a new content type needs a code branch,
§7.3 is wrong and the design should be revisited before more types are added.

## 12. Amendments to the unit definition

Everything this plan changes in `theplot-scene-unit.md`, collected so the two documents can be
reconciled in one pass.

### 12.1 C1, C2, C3 — the scene's leaf becomes the item

| Was | Now |
|---|---|
| C1 partitions **paragraphs** | C1 partitions **displayable items**; metadata is covered by an explicit exclusion set so nothing is dropped silently (§5) |
| C2 contiguity over paragraphs | C2 contiguity over items |
| C3 non-empty, paragraph-granular | C3 non-empty over items, with the `MinSceneSpan` floor as the explicit guard against over-cutting (§3.5) |

### 12.2 Unit §7 case 8 — front matter is not an `Exhibited` scene

It is **metadata**. `Exhibited` remains the mode for apparatus that *is* part of the work —
tables, figures, theorem boxes, problem sets.

### 12.3 Unit §3.5 — Subject resolution does not come from a `Topic` tag

The unit doc points `Subject` at a `Topic` tag layer. There is no `Topic` tag kind (§4.2).
Finer-grained subject resolution comes from paragraph-id evidence under C13, the ordinary way.

### 12.4 Unit §9.1 — tag kinds

The decided list becomes `Persona`, `Group`, `Place`, `ExhibitRef` — the last renamed to keep it
distinct from the `Exhibit` item kind.

### 12.5 C12 — enforcement changes character

C12 is no longer a rule to police but a structural consequence of scenes being tree leaves
(§2.2). `section-containment` is retained as a warning that emits the `Continues` link, not as a
constraint that can be violated.

### 12.6 C9 — "no new source text" gains a named exception

C9 forbids new source text and already exempts generated metadata: "Title, summary, and place
names are metadata, clearly marked as generated, and never re-enter the content stream."
**Render text** (§3.6) is added to that list. The prohibition itself is unchanged — render text
is never source text, never hashed, never concatenated, and never shown as the document. C9's
teeth are in `text-integrity`, which does not read it.

### 12.7 Unit §9.2 — group membership drift is resolved

The unit doc leaves membership drift open between "scene-local (costly, precise)" and
"re-register the group when it changes (cheap, lossy)". **Per-reference membership** (§4.3)
takes the precise option and goes one step finer: membership hangs off each `Group` tag rather
than off the scene, which costs nothing extra because tags are already per reference. The
registry keeps identity; the tag keeps the roster.

## 13. Decisions

| Question | Decision |
|---|---|
| **Gold-set scope** | Four composition families for v1: **novel, textbook, interview transcript, screenplay**. Chosen to span the two axes that actually vary — dialogue density and setting explicitness — rather than to sample genres: the screenplay is explicit on both, the textbook near-null on both, the novel dialogue-heavy with implicit settings, the transcript dialogue-heavy with absent settings. A design that scores well on all four is unlikely to be surprised by a fifth. **Sizing: 20 documents per family, scene-cut by hand.** This is the plan's largest unbudgeted cost and it gates S4 and S5. |
| **Tagging cost** | Held down by the tag set rather than by a fallback (§4.5): dropping `Time` and `Topic` removes the kinds that fire per sentence. No v1 reduction to `Persona`-only is planned. If S3 measurement still shows cost dominating, `ExhibitRef` is the next to go, since its coordinate is a link rather than a cast or setting fact. |
| **Tag offsets** | Paragraph-relative, not item-relative (§4.1), so they stay verifiable against the hashed artifact. |
| **Where personas resolve** | Phase 9, between item typing and scene cutting (§8.4) — not inside either. |
| **Executor shape** | **Orchestrator by default, fan-out where the join is guaranteed** (§8.2). An executor becomes an orchestrator exactly where the cross-window join needs judgement; where the window planner guarantees the join, code performs it, because replacing a guarantee with an opinion is a regression. Of the new phases only 9 qualifies. |
| **`Action` vs. `Description`** | **Keep both; tolerate confusion.** They are not merged into a `Depiction` kind with an `IsEvent` flag. The pair is genuinely ambiguous on many spans, so instead of fighting it, nothing load-bearing is allowed to depend on it: no hard gate reads the distinction and mode derivation keys on Speech attributes (§3.1, §8.3). S2's threshold is measured with the pair collapsed as well as split, and only the collapsed figure gates. |
| **Item granularity** | **Sub-sentence, character offsets** (§3.3). A sentence that mixes dialogue and action is two items. Scene boundaries stay at sentence granularity (§3.5). |
| **Non-stageable fragments** | **A re-writer** produces generated `RenderText` for them (§3.6), fenced by C9's generated-metadata carve-out and gated by `render-text-grounding`. Runs in phase 9, where persona ids exist. |
| **Group membership** | **Per reference, on the `Group` tag** (§4.3), not on the registry entry. Membership is contextual; identity is not. |
| **Augmentation granularity** | **A property of the augmentation type, not of the pipeline.** The contract is already pluggable [pipeline §12.1], so `AugmentationScope` admits `Item`, `Scene`, `Section` and `Persona` and each type declares its own. No single granularity is imposed. |

## 14. Open questions

1. **Composition family for mixed documents.** An anthology or a course pack is several
   composition families in one file. Deferred: v1 assigns one per run and flags the mismatch
   rate.
2. **What fraction of items need render text?** §3.6 assumes a small minority and prices the
   re-writer accordingly. If most `Action` fragments turn out non-standalone, phase 9 becomes
   the most expensive phase in the pipeline rather than a cheap resolution pass. S3 measures it,
   and `IsStandalone` is the field to instrument.
3. **Can `render-text-grounding` be checked strictly enough to be a hard gate?** Lemma
   containment plus resolved persona names is the proposed rule (§3.6). It is tight against
   invention but may be *too* tight for legitimate rewrites that need a copula or a possessive
   the span lacks. If it produces false failures, it drops to a warning with review sampling —
   and the fence loses one of its four sides.
4. **Should scene-link inference (K4) be its own orchestrated phase?** `Continues` falls out of
   C12 deterministically, but `FlashbackOf`, `ConcurrentWith` and `Frames`/`FramedBy` relate
   scenes that may be far apart — a global judgement over scene digests, which is orchestrator
   shape (§8.2). It is currently unspecified in any phase. The cheap version is a step in
   phase 11 over the scene list; the honest version is a phase of its own after 10.
5. **What is `MinSceneSpan` actually worth?** Default 2 sentences is a guess (§3.5). Now that
   sub-paragraph cutting is wanted rather than merely possible, the floor is the only thing
   standing between the pipeline and one scene per clause. S4 should tune it per composition
   family rather than keep one global default.

# ThePlot — The Scene: definition, constraints, capacities

Status: **definition / for review**. Prerequisite to any scene-building work in
`theplot-segmentation-plan.md` (§9 phase 5) and `theplot-segmentation-groupchat-plan.md`.
This document defines the unit only. It does not specify the segmenter, the prompts, or the
renderer — see `theplot-scene-plan.md`, which introduces **scene items** as the scene's leaf and
**metadata exclusion**, and amends C1, C2 and C3 accordingly (marked below).

---

## 1. The definition

> A **scene** is the maximal contiguous run of paragraphs over which one **situation** holds.

A situation is a five-coordinate tuple:

```
Situation = (Setting, Time, Cast, Mode, Subject)
```

Everything else follows from two words in that sentence:

- **maximal** — a scene is extended until a coordinate changes. You never cut a scene for
  length, tidiness, or topic drift that the coordinates don't register.
- **contiguous** — a scene is a span of the frozen paragraph sequence, not a set gathered
  from across the document.

The tuple is what makes a boundary *decidable* rather than a matter of taste. Two adjacent
paragraphs are in the same scene iff their situations are equal; a boundary exists exactly
where one coordinate changes discontinuously (§4).

A scene is the unit that can be **staged**. The test for whether the definition is doing its
job: hand one scene record to something that has never seen the document, and it should be
able to put the right people in the right place doing the right thing. If it needs to read
backwards, the unit is broken.

## 2. What a scene is not

| Not | Why |
|---|---|
| A **paragraph** | A paragraph is a text-integrity unit. A scene owns paragraphs; it is not one. |
| A **section** | A section is a *structural* claim about the document (headings, hierarchy). A scene is a *staging* claim about the world the document depicts. They coincide often and disagree meaningfully (§7.6). |
| A **topic** | Topic is one coordinate (`Subject`) and only cut-bearing in some modes (§4.2). |
| A **shot / panel / frame** | Those are rendering decisions *below* the scene. One scene may render as many shots. The document does not determine them. |
| A **chapter / beat / act** | Those are groupings *above or within* the scene and are out of scope here. |
| An **event in a world model** | A scene records what the text stages, not a reconciled timeline. No world-state arithmetic (§6.3). |

## 3. Anatomy — the five coordinates

Each coordinate is single-valued within a scene (C5) and always present (C6). "Absent" is a
value, never a null.

### 3.1 Setting — *where*

Setting is binary. A scene is either located or it is not.

| Value | Meaning |
|---|---|
| `Place(ref)` | A resolvable place in the run's place registry, stated by the text or inherited resolved from the previous scene (C7). |
| `Void` | Unlocated. Covers both "deliberately nowhere" — narration, exposition, argument, direct address — and "a place exists and the text withholds it". |

**There is no `Unknown` setting.** Whether an unstated place is *meant* to be nowhere or is
merely unsaid is not decidable from the text, and a coordinate the segmenter cannot assign
without guessing violates C11. Void is therefore the **default**: a scene is located only when
the text locates it, and the absence of a place claim is itself the derivation — Void needs no
evidence under C13.

The distinction is recovered where it belongs, as a *derived* signal rather than a stored
value, because Mode already encodes whether a place is expected:

| Mode + Setting | Reading |
|---|---|
| `Enacted` + `Void` | Suspicious — people are acting somewhere. Raises `unlocated-enactment` for review; does not block. |
| `Narrated` + `Void` | Mildly suspicious; common in summary narration. |
| `Expounded` / `Addressed` / `Exhibited` + `Void` | Normal. No flag. |

This is a rule over the tuple, evaluated in code after assignment. It costs nothing at
assignment time and can be retuned per document family without touching the unit.

#### 3.1.1 When Setting inherits rather than falling to Void

**Decided.** Unbounded inheritance is the real hazard: one wrong place silently poisons every
scene after it. So inheritance is bounded on four sides, all checkable in code:

1. **Mode.** Only where Setting is live — `Enacted` and `Narrated`. Elsewhere Setting is `Void`
   and inheritance is moot.
2. **Section.** A scene inherits only from its immediately preceding **sibling scene in the same
   leaf section**, never across a section boundary. This matches how documents behave — authors
   restate location at a chapter or scene break because they know the reader lost it — and it
   caps error propagation at one section.
3. **Evidence.** Inheritance breaks on any `Place` tag resolving to a different place, and on a
   departure or arrival signal (a `SceneCut` family instinct, not a hardcoded verb list).
4. **Depth.** `InheritanceDepth` is recorded on the scene. Beyond 3 it raises
   `deep-setting-inheritance` for review — the cheap way to catch drift before a reader does.

Anything not inherited under those four rules is `Void`. Inheriting is the exception; Void
remains the default (§3.1).

### 3.2 Time — *when*

An anchor (absolute, relative, or none) plus a continuity relation to the preceding scene:
`Continuous`, `Gap`, `Earlier`, `Later`, `Simultaneous`, `Unanchored`. Expository material is
normally `Unanchored`; that is not a defect.

### 3.3 Cast — *who*

A set of persona references, each with a role **in this scene**:

| Role | Meaning |
|---|---|
| `Speaking` | Produces utterances in the scene. |
| `Addressed` | Spoken to (including the audience/reader as an explicit persona). |
| `Present` | In the scene, silent. |

**Mention is not presence.** A persona discussed but not present is not in the cast; it is a
`Subject` reference. This single rule removes most cast ambiguity in expository text.

The **tag layer** (scene-plan §2.5) makes it mechanical rather than a judgment: every reference
in an item's text — "the professor", "she", "Dr. Vance" — is tagged to a persona id, so mentions
are enumerable. Cast membership is then derived from *role*, not from tag count: a persona enters
the cast by speaking, being addressed, or being tagged `Present`. A persona tagged only inside
running text is referenced and stays out of the cast, however often it appears.

**Cast is the set of people present, not the current speaker.** Alternation between speakers
does not change the set and therefore does not cut a scene (§4.2). A scene ends when someone
joins or leaves. Without this rule, dialogue shreds into one scene per turn.

**A group is a persona.** "Mr and Mrs Smith", "the brothers", "the gang", "the class", "the
crowd" are cast members in their own right — they act, speak, and are addressed — so they are
registry entries with `Kind = Group`, not a separate referent type. Nothing in cast handling,
speech attribution, or C8 needs a special case for them.

Presence propagates **one way**: if a group is in the cast, its known members are present. The
reverse does not hold — four brothers in a room does not make "the brothers" the acting unit.
Only the text promoting them to a collective does that.

Group membership may be partial or unknown, which is a fact about the group, not a defect: "the
gang" is often never enumerated. Whether membership is complete is the switch that decides
whether the group renders as its members or as a crowd (scene-plan §2.5).

The narrator is a persona. The lecturer is a persona. An unattributed voice is the explicit
`Unattributed` persona, not an empty cast.

### 3.4 Mode — *how it is delivered*

| Mode | Shape |
|---|---|
| `Enacted` | Characters act and speak inside a world. |
| `Narrated` | A voice tells what happened. |
| `Expounded` | A voice teaches, argues, or explains. |
| `Addressed` | Direct address to the audience — instruction, appeal, framing. |
| `Exhibited` | Apparatus shown with no agent: table, figure, code block, list, front matter. |

Mode is the coordinate that selects which of the others are live (§4.2), and the one that is
always determinable: when nothing else about a span can be established, it is `Exhibited`
(C11, K7).

### 3.5 Subject — *what is happening or being treated*

One per scene — **one coordinate, not two**. In `Enacted`/`Narrated` mode it is the event. In
`Expounded`/`Addressed` mode it is the topic under treatment. In `Exhibited` it is what the
apparatus shows. Expository material that seems to want "topic" and "claim" separately gets that
resolution from the `Topic` tag layer inside its items (scene-plan §2.5), not from a second
coordinate: tags are many per scene, coordinates are one.

## 4. The cut rule

### 4.1 The test

A boundary falls between paragraphs *p* and *p+1* iff at least one **live** coordinate
changes **discontinuously** there.

Operationally: *could a stage crew keep the same set, the same people on it, and the same
thing going on, and just continue?* If yes, no cut — regardless of length or topic drift. If
no, cut.

"Discontinuous" means the text marks the change, or a reader must re-stage to follow. A place
gradually revealed over three paragraphs is one scene; a place *replaced* is two.

### 4.2 Live coordinates

Only coordinates that can vary within a mode are cut-bearing. This is what stops a 400-
paragraph lecture from being one scene, and stops a tense dialogue from being shredded into
one scene per topic.

| Mode | Cut-bearing | Not cut-bearing |
|---|---|---|
| `Enacted` | Setting, Time, Cast | Subject |
| `Narrated` | Setting, Time, Subject | Cast (normally constant) |
| `Expounded` / `Addressed` | Subject, Cast | Setting (`Void` throughout) |
| `Exhibited` | Subject (one artifact = one scene) | Setting, Time, Cast |

A **mode change is always a cut.** Mode is live in every mode.

### 4.3 Precedence

When coordinates disagree about where the boundary sits (a speaker leaves one paragraph
before the location changes), the boundary goes at the **earliest** paragraph at which the new
situation is fully in effect, and the paragraphs in between belong to the *outgoing* scene.
Scenes are maximal backwards, not forwards.

### 4.4 Granularity — treated vs. listed

C5 alone does not fix how *fine* the cutting goes: thirty end-of-chapter exercises each contain
a micro-world with its own place and cast, and a strict reading makes thirty scenes of noise.
The fix is a granularity rule, not a weaker C5:

> **If the text treats an item, the item is a scene. If the text lists items, the list is a
> scene.**

A worked example discussed one at a time in the flow of exposition is a scene — the subject *is*
that example. A problem set is one `Exhibited` scene, because the subject is "the exercises", a
collection; the depictable worlds inside individual problems are a rendering decision below the
scene, like shots. The same rule governs glossaries, bibliographies, data tables, and catalogues,
and it takes most of the pressure off the montage case (§7.5).

## 5. Constraints

Hard invariants. Each is deterministically checkable in C# against the frozen paragraph list;
none requires a model.

| ID | Constraint |
|---|---|
| **C1** | **Partition.** Every **displayable** item belongs to exactly one scene. No gaps, no overlaps. Metadata — page numbers, running heads, front and back matter — is not a scene; it is classified and excluded, and covered by an explicit exclusion set so nothing is dropped silently (scene-plan §3). Apparatus that *is* part of the work — tables, figures, theorem boxes, problem sets — does not float; it becomes an `Exhibited` scene. |
| **C2** | **Contiguity and order.** A scene is a contiguous run of items; scene order is item order. |
| **C3** | **Non-empty.** A scene owns at least one item. A mode change spanning fewer than `MinSceneSpan` sentences (default 2) is an item attribute, not a cut — the floor that stops sub-sentence flicker from becoming scenes. |
| **C4** | **Leaf.** Scenes never nest — scenes are the **leaves of the section tree**, which is where all nesting lives. A leaf section contains scenes; a non-leaf section contains sections. Story-within-story is a *link*, not containment (§7.4). |
| **C5** | **Single situation.** Every coordinate is single-valued. Two places means two scenes — always, including a conversation across a doorway. |
| **C6** | **Total situation.** No coordinate may be omitted. Each carries a value, and every coordinate has a legal "nothing here" value — Setting `Void`, Time `Unanchored`, Cast empty, Mode `Exhibited` — so absence is always expressible without a null. |
| **C7** | **Resolved inheritance.** Inherited values are copied in with the source scene id. A scene never says "as before"; a reader of one scene record never has to look elsewhere. Setting inheritance is **bounded** — §3.1.1. |
| **C8** | **Cast closure.** Every cast entry resolves to a persona id in the run's registry, individual or group alike. No anonymous speakers; `Unattributed` is a real persona. |
| **C9** | **No new source text.** A scene references paragraph ids. Title, summary, and place names are metadata, clearly marked as generated, and never re-enter the content stream. Inherits "models label, never rewrite" from §3 of the segmentation plan. |
| **C10** | **Downstream of freeze.** Scene assignment consumes frozen paragraphs and the frozen tree, and freezes itself with its own content hash. Scene ids derive from the first paragraph id, so they are stable across re-runs that do not change that paragraph. |
| **C11** | **No guessing.** Where the text does not supply a coordinate, the scene takes that coordinate's nothing-value (C6) and any suspicion is raised by a derived flag (§3.1). Fabricating a coordinate — inventing a place, a time, or a persona the text does not support — is a validation failure, not a fallback. |
| **C12** | **Sections are respected.** A scene may not straddle a section boundary — structurally impossible now that scenes are tree leaves (C4), rather than a rule to police. Where staging continues across a section break, the scene splits and the halves carry a `Continues` link (§7.6). |
| **C13** | **Evidence.** Every non-inherited coordinate cites the paragraph or line ids that justify it. Uncitable equals unknown. |
| **C14** | **Bounds are advisory.** Minimum one paragraph, no hard maximum. Exceeding the configured ceiling (default 40 paragraphs) raises `oversized-scene` for review; it is never an automatic cut. |

## 6. Capacities

What the unit must *afford* — the reasons it exists. A change to the model that costs one of
these is a regression.

| ID | Capacity |
|---|---|
| **K1** | **Stageable in isolation.** Given one scene record and its paragraphs, a renderer can stage it without reading the document. This is the point of C6 and C7, and the single capacity the whole design is for. |
| **K2** | **Addressable.** A stable id that anchors comments, review decisions, edits, renders, and versions across re-runs. |
| **K3** | **Castable.** Personas persist across scenes, so voice, appearance, and manner can be assigned once and held consistent document-wide. |
| **K4** | **Sequenceable.** Scenes carry typed links — `Continues`, `ReturnsTo`, `FlashbackOf`, `ConcurrentWith`, `Frames`/`FramedBy` — so a plot is a graph over scenes without violating C4. |
| **K5** | **Queryable.** "Every scene in the lab", "every scene where the professor speaks", "every unlocated scene" are index lookups, not document scans. |
| **K6** | **Independently regenerable.** One scene can be re-segmented, re-reviewed, or re-rendered without invalidating its neighbours — the unit of retry and of cost. |
| **K7** | **Degradable.** There is always a legal answer: `Exhibited`, `Void`, `Unanchored`, empty cast. The pipeline can be uncertain about a scene without failing the run (C1 always holds), and the uncertainty surfaces as flags rather than as missing data. |
| **K8** | **Auditable.** C13's evidence makes every coordinate checkable against the source by a human or by code. |
| **K9** | **Budgetable.** One scene is one render job — the natural unit for cost estimation, quotas, and progress reporting in ThePlot. |

### 6.3 Deliberate non-capacities

A scene does **not** carry camera, shot list, visual style, or pacing — those are rendering
decisions made below it. It does **not** maintain a reconciled world timeline or world state;
`Time` is a local relation to the previous scene, not an arithmetic. It does **not** judge
narrative quality or importance. It does **not** model simultaneity by itself — that is a
`ConcurrentWith` link between two scenes.

## 7. Hard cases

The definition earns its keep here.

1. **The 400-paragraph lecture.** One lecturer, `Void`, no time. Setting and Cast are dead
   coordinates; `Subject` is live (§4.2), so the lecture cuts at topic changes and each scene
   is "this person, in the void, treating this". Correct and renderable.
2. **A transcript.** A transcript is a source format, not a mode — like a PDF. Mode is assigned
   per scene and one transcript normally carries several. The discriminator is **who is
   addressed**:

   | The speech is… | Mode |
   |---|---|
   | Addressed to others present, who respond — interview, argument, deposition, panel exchange | `Enacted` |
   | Addressed past the room to an audience, the content being the thing — lecture, keynote, solo podcast | `Expounded` |
   | Addressed to the audience about their own situation — welcomes, appeals, instructions, framing | `Addressed` |
   | A speaker recounting events at length inside any of the above | `Narrated` |

   **Mode count is orthogonal to speaker count.** Speakers are a `Cast` fact; mode is about what
   the speech is doing. All four combinations are ordinary:

   | | One mode | Several modes |
   |---|---|---|
   | **One speaker** | Dictated memo, statement read into the record — `Expounded` throughout, cut on subject | Keynote: welcome (`Addressed`) → teach (`Expounded`) → recount what happened in the lab (`Narrated`) |
   | **Several speakers** | Panel of pure exchange — `Enacted` throughout, cut when someone joins or leaves | Interview with intro and sign-off |

   A one-speaker transcript is therefore a single mode, and a single scene if the subject never
   shifts. An interview with framing at both ends is three or more, because a mode change is
   always a cut (§4.2). Neither is a rule about transcripts.

   The keynote cell is where the cut rule earns its keep: the anecdote has a place and a time,
   the lecture around it has neither, and a renderer wants to cut away to it and back. The guard
   against over-cutting is the C3 floor: a mode change shorter than `MinSceneSpan` is an item
   attribute, so a passing aside ("when I was in Berlin, anyway —") does not become a scene.

   Getting this wrong is expensive, because mode selects the live coordinates: an `Enacted`
   transcript cuts when the speaker *set* changes, an `Expounded` one cuts on subject. Misread
   it and the output is either one 900-turn scene or one scene per turn. The two rules that
   prevent the latter are §3.3 (cast is the set present, not the current speaker) and §1
   (scenes are maximal).

   Setting is `Void` throughout unless the talk locates itself. Under `Enacted` that trips
   `unlocated-enactment` (§3.1) and a reviewer or later pass can supply the room; under
   `Expounded` it is simply a talk. The segmenter never has to adjudicate which, and the record
   is usable either way.
3. **A table or figure between two paragraphs of argument.** `Exhibited` scene of its own
   under C1. It interrupts the surrounding scenes, which do not merge across it; the second
   half carries `Continues` back to the first.
4. **Story within a story.** Two scenes, not one nested scene (C4). The inner carries
   `FramedBy` the outer; the outer resumes as a third scene with `ReturnsTo`.
5. **Montage / rapid succession of places.** Each place is a scene (C5), however short. C14
   permits very short scenes precisely for this. The noise is absorbed by grouping, not by
   weakening C5: the montage becomes one **inferred section** (F8) holding its scenes, so a
   consumer that wants the beat takes the section and one that wants the cuts takes the scenes.
6. **Section break in the middle of continuous staging.** C12 splits the scene. The document's
   structural fact and the staging fact are both preserved, and `Continues` records the
   disagreement rather than hiding it — which also makes the disagreement *measurable* across
   a corpus.
7. **A textbook.** Predominantly `Expounded` prose over `Exhibited` apparatus (definitions,
   theorems, tables, figures), punctuated by `Addressed` turns to the reader — "in this chapter
   you will…". The load-bearing observation is what else is present: a worked example ("a train
   leaves Chicago at 3pm") builds a miniature world and is `Enacted`; a historical vignette is
   `Narrated`. **In expository material, mode identifies exactly which spans are depictable** —
   the `Expounded` majority stages as a voice in the void, while the `Enacted`/`Narrated` islands
   are the only spans with a world to build. Granularity is settled by §4.4. Textbooks are also
   the worst case for C12 (dense, deeply nested sections) and for persona scope (§9.2), and are
   the right corpus to instrument both against.
8. **Front matter, running headers, page numbers.** Not scenes at all — **metadata** (C1,
   scene-plan §3). Classified and excluded from the scene stream, retained in the frozen record
   so `text-integrity` still accounts for every character. What counts as metadata is
   family-dependent: a dedication is apparatus in a textbook and may be the work in a poetry
   collection, so the boundary is learned rather than hardcoded. Footnote *text* is usually
   displayable and `Exhibited`; the footnote *marker* is a `NavigationalLabel`.

## 8. Shape

Sketch only; the record lives in `ProtoFast.Segmentation.Core/Scenes/` and follows §8 of the
segmentation plan for id conventions.

```csharp
public sealed record Scene(
    string SceneId,                      // "SC0012"; derived from FirstParagraphId
    string SectionId,                    // owning section (C12)
    string FirstParagraphId,
    string LastParagraphId,
    IReadOnlyList<string> ParagraphIds,  // contiguous, ordered (C2)
    Situation Situation,
    IReadOnlyList<SceneLink> Links,      // K4
    string? Title,                       // generated metadata, never content (C9)
    bool TitleInferred,
    IReadOnlyList<Flag> Flags,           // oversized-scene, unlocated-enactment, ... (derived)
    string ContentHash);

public sealed record Situation(
    string? PlaceId,                     // null == Void (§3.1); no third state
    TimeValue Time,
    IReadOnlyList<CastEntry> Cast,
    SceneMode Mode,
    SubjectValue Subject);

// Coordinates that were determined carry provenance and evidence (C7, C13).
// A nothing-value (Void, Unanchored, empty cast) carries no provenance: nothing was claimed.
public sealed record Provenance(
    CoordinateSource Source,             // Stated, Inferred, Inherited
    string? InheritedFromSceneId,
    IReadOnlyList<string> EvidenceIds,   // paragraph or line ids
    string? Reason);
```

## 9. Decisions and what is still open

### 9.1 Decided

| Question | Decision |
|---|---|
| Is `Subject` one coordinate or two? | **One** (§3.5). Finer resolution comes from `Topic` evidence, not a second coordinate. |
| Persona identity | The **tag layer** resolves every referring expression to a persona id; scope (persistent vs. scene-local) is a registry field (scene-plan §2.5). |
| A unit between scene and document | **The section.** Scenes are the tree's leaves (C4); groupings the headings do not mark are inferred sections. No fourth layer. |
| Tag kinds | `Persona`, `Group`, `Place`, `Exhibit` (scene-plan §2.5.1). |
| **C12's residual cost** | **Split the metric by boundary provenance.** A `Continues` link across a *trusted* heading is expected and needs no action — chapters interrupt scenes constantly. A `Continues` link across an *inferred* boundary is evidence the inference was wrong, and above a per-family threshold becomes a review finding routed back to structure repair. The section tree yields to staging only where the tree was itself inferred; trusted boundaries are never overridden, per the platform's existing posture. |
| **Setting inheritance vs. Void** | **Bounded on four sides** — mode, section, evidence, depth (§3.1.1). Unbounded inheritance was the hazard, not the choice between inheriting and not. |

### 9.2 Still open

1. **Group membership drift.** "The brothers" may mean two people in chapter 1 and three in
   chapter 20. Membership is currently a registry-level fact, which cannot express that. Either
   membership becomes scene-local (costly, precise) or a group is re-registered when it changes
   (cheap, lossy). Undecided until a corpus shows how often it matters.
2. **Tagging *things*.** Props and objects a character handles are the obvious next tag kind and
   the first step toward world state, which §6.3 rules out. Deferred until a renderer asks.

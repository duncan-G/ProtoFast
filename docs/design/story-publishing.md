# Story publishing

Publishing turns a ThePlot story into an image and audio experience: a sequence of frames per scene,
played over the scene's performed dialogue. It has two stages.

- **Director.** Reads the story and writes everything a renderer needs that a screenplay leaves out:
  what every character, location and prop looks like and sounds like, what each character looks like
  in each scene, how each line is delivered (`JOHN: I'm sorry` becomes `JOHN: [sad] I'm sorry`),
  how many frames each scene gets, and for each frame who is where, doing what.
- **Generation.** Renders the director's output. A frame image per frame (today a blank black image)
  and one audio track per scene, then a cut that times each frame against the audio.

Everything either stage produces is an **item**: one character's description, one character's look
in one scene, one scene's frames and cues, one scene's audio, one frame's image. An item can be
published on its own, and it is added to the story when it is. Publishing a whole story is
publishing every item. Items record what they were built from, so the system always knows what a
change makes out of date, and publishing one item never quietly contradicts another (section 2).

The director is agent-driven and promotable, the same as the discovery agent. Generation runs
host-written executors. Both run on the agent workflow engine
(`docs/design/agent-workflow-engine.md`), so both produce the same ledger, verdicts and cost
records as an import.

The hardest problem is **consistency**: the same voice sounding like the same person across a
conversation and across scenes, and the same character wearing the same clothes from one frame to
the next. Section 5 covers it. The item graph is what makes it enforceable when things are
published one at a time.

```
 source/…            (the story text; the author edits it)
   │
   ▼
 character/{c}/sheet ──▶ character/{c}/voice        narrator/voice
 location/{l}/sheet      prop/{p}/sheet      story-days      pronunciations
   │                       │                     │
   ▼                       ▼                     │
 scene/{s}/appearance/{c}  scene/{s}/prop/{p} ◀──┘    (each chained to its previous scene)
   │                       │
   ▼                       ▼
 scene/{s}/direction  (frames + cues)
   │                         │
   ▼                         ▼
 scene/{s}/frame/{n}/image   scene/{s}/audio      ◀── voices, narrator, pronunciations
   ▲   (references)          │
   └────────────┬────────────┘
                ▼
          scene/{s}/cut ──▶ player
```

## 1. What exists and what's missing

**Story model** (`services/api/src/ProtoFast.Data/ThePlot/Entities`). `Character` has name, kind and
hue. `Location` has name, setting (Interior/Exterior) and hue. `Prop` has a name. None has a
description. `SceneElement` has a type (Heading, Action, Description, Narration, Dialogue,
Transition), and dialogue has a speaker, an extension (V.O./O.S./O.C.) and a parenthetical.
Mentions tie element text to characters, props and locations by offset.

The import agent already writes a one-sentence `description` for every character, location and
prop in `story-draft@1`, but `StoryWriter` drops it. That is the first fix (section 10, phase 0).

**Engine.** These parts are generic over contracts and can be reused as they are: `RunDispatcher`,
`AgentTools`, `VerifierRunner`, `Scheduler`, the tier ladder, the shadow sampler, `WorkflowMiner`,
the run ledger and run progress.

These parts are tied to import and need generalising:

- One `DiscoveryGoal` and one `DiscoveryAgent` are registered as singletons per process.
- `DiscoveryGoal` names a single deliverable stage.
- The system prompt says "document import engine".
- The classifier assumes manuscripts.
- `DocumentImportRunner` hard-codes the `story` stage and `StoryWriter`.
- `DocumentFamilyPolicy` is keyed by family alone, not by family and workflow.
- The only `IExecutorFactory` builds language-model executors. A `Codified` spec cannot be resolved.
- `WorkflowPromotion.PromoteAsync` exists, but nothing calls it, and there is no promote RPC.

**Media.** Nothing exists: no TTS, no image generation and no media bucket. `IObjectStore` already
has `WriteFrozenBytesAsync`, and `IPresignedUrlFactory` already has `PresignGet`. Together these are
enough to store and serve generated media.

## 2. Items

### 2.1 Kinds

An item is addressed by a key. Its value lives in ThePlot rows (section 8), where the editor and
the player read it. The item row records provenance and freshness, never the value itself.

| Kind | Key | Value lives in | Produced by | Inputs |
|---|---|---|---|---|
| source | `source/scene/{s}`, `source/character/{c}`, `source/location/{l}`, `source/prop/{p}` | `scenes`, `scene_elements` | Author, in the story editor | — |
| style | `style` | `publications.style` | Author, from presets | — |
| story-days | `story-days` | `scenes.story_day` | Director | every `source/scene` (headings and transitions only) |
| pronunciations | `pronunciations` | `stories.vocabulary` | Director | every `source/character` and `source/location` |
| character-sheet | `character/{c}/sheet` | `characters.description`, `appearance`, `voice` (description and baseline), `character_wardrobes` | Director or Author | `source/character/{c}`, `style` |
| voice | `character/{c}/voice` | `characters.voice` (voice id and settings) | Director (casting) or Author | `character/{c}/sheet` |
| narrator-voice | `narrator/voice` | `publications.narrator_voice` | Director (casting) or Author | `style` |
| location-sheet | `location/{l}/sheet` | `locations.description`, `appearance`, `acoustics` | Director or Author | `source/location/{l}`, `style` |
| prop-sheet | `prop/{p}/sheet` | `props.description` | Director or Author | `source/prop/{p}`, `style` |
| appearance | `scene/{s}/appearance/{c}` | `scene_appearances` | Director or Author | `source/scene/{s}`, `character/{c}/sheet`, `story-days`, c's previous appearance |
| prop-state | `scene/{s}/prop/{p}` | `scene_prop_states` | Director or Author | `source/scene/{s}`, `prop/{p}/sheet`, p's previous state |
| direction | `scene/{s}/direction` | `frames`, `frame_placements`, `frame_props`, `cues` | Director or Author | `source/scene/{s}`, every appearance and prop state in s, the location sheet, sheets of props in s, `style` |
| reference | `reference/character/{c}/{w}`, `reference/location/{l}/{tod}`, `reference/prop/{p}` | `media_assets` | Generator | the subject's sheet, `style` |
| ambience | `ambience/{l}/{tod}` | `media_assets` | Generator | `location/{l}/sheet` |
| audio | `scene/{s}/audio` | `published_scenes.audio_asset_id`, cue timings | Generator | `scene/{s}/direction`, the speakers' voices, `narrator/voice` when s has narration, `pronunciations`, the previous scene's audio when s is `CONTINUOUS` |
| frame-image | `scene/{s}/frame/{n}/image` | `frames.image_asset_id` | Generator | `scene/{s}/direction`, references for the entities in frame n, `style` |
| cut | `scene/{s}/cut` | frame timings, `published_scenes.duration_ms` | Generator (code) | audio, every frame image in s, the location's ambience |

Notes on the table:

- **"The character's appearance in this scene"** is `scene/{s}/appearance/{c}`. It holds the
  wardrobe, the state (bandaged hand, wet hair) and, when it differs from c's previous appearance on
  the same story day, the reason and the element that causes the change. It depends on c's previous
  appearance, so looks form a chain through the story. Changing a look in scene 3 makes scene 5's
  look stale. Re-publishing scene 5 then either carries the change forward or records why it ends.
- **Props chain the same way.** `scene/{s}/prop/{p}` exists for each prop mentioned in s. It holds
  the prop's state (broken, lit, half-empty) and the change that caused it. Unlike a look, a prop's
  state does not reset with the story day: a vase broken on day 2 is still broken on day 5.
- **Narration has its own voice.** Narration elements have no speaker, so the publication has one
  narrator voice, `narrator/voice`, cast like a character's voice.
- **Source items are per subject as well as per scene.** `source/character/{c}` hashes the text of
  every element that mentions c or that c speaks. A character sheet becomes stale when anything
  said about the character changes, including a new mention in a scene the sheet never read.
  `StoryService` maintains source hashes on every save, because it already knows each element's
  mentions.
- **Frame images are keyed by position.** Re-directing a scene keeps the keys. If a frame's compiled
  request is unchanged, the media cache returns the same image.

### 2.2 Inputs and freshness

Publishing works like a build system:

- **Inputs are declared, not discovered.** Each kind has one function that resolves an item's
  inputs from the current rows (the table above). The planner resolves the inputs, the run receives
  them, and the writer records them. What the agent chooses to read along the way is never an input.
  That rule is what makes freshness trustworthy, and it is why sibling reads (section 4.3) cannot
  create cycles.
- **Every item has a value hash.** It is a canonical hash of the item's current value. The writer
  computes it when a run produces the item, and the API computes it when the author edits the item.
  The same `ItemHashing` code in `ProtoFast.Data` does both, with one serializer, so the two sides
  agree.
- **An item records the value hash of every input it was built from.** It is **stale** when any
  input's current hash differs, or when an input no longer exists. Staleness is computed when it is
  read and never stored, so nothing has to propagate it and there is no race.
- **Changes stop early when nothing changed.** If re-publishing an item produces the same value
  hash, items downstream of it stay fresh. A re-directed scene that comes out the same does not
  regenerate its audio.
- **"May change" is transitive and shown, not acted on.** A recursive query from the stale items
  gives the downstream set, for example "3 stale, 12 more may change".

### 2.3 Publishing an item

The planner and the scheduler handle publishing one item, many items, and the whole story the same
way.

1. **Plan.** `PlanPublish(keys, scope)` takes the requested keys and a scope:
   - **`Item`.** The requested items, plus any missing or stale inputs they need. Downstream
     items are left stale for the author to decide.
   - **`ItemAndDownstream`.** The above, plus everything downstream that becomes stale.
   - **`Story`.** Every item in the publication.

   The client's default scope depends on where the action is:
   - **Library actions default to `Item`.** "Describe" on a character is an example. A sheet's
     downstream spans the whole story, so regenerating it is the author's explicit choice.
   - **Scene actions default to `ItemAndDownstream`.** "Direct" or "Look in this scene" are
     examples. A scene action means "make this scene right", including its audio and frames.

   Missing inputs are always planned. **Stale inputs are refreshed first**, unless they are pinned.
   An item built on a stale input would only be valid until that input catches up, so the money is
   better spent once.

   Pinned items (2.4) are never planned, and neither is anything already queued in another plan.
   The plan comes back as ordered waves, each item with a reason (requested, missing, stale) and an
   estimated cost. The estimate comes from the ledger's average cost per (family, kind), and from
   character counts for audio.
2. **Confirm.** The author sees, for example, "Publishing John's look in scene 12 also needs John's
   sheet (missing). About $0.04." Then they start the plan.
3. **Run.** An `ItemScheduler` in the worker works through the waves.
   - **A plan is an upper bound.** Before each wave, the scheduler re-checks freshness. It drops
     items that early cutoff has made fresh, and items that the author has pinned since. For
     example, if a scene-3 change carried forward unchanged, the later looks it listed never run and
     cost nothing.
   - **Items run in batches by kind.** Within a wave the scheduler groups items by kind. Each batch
     is one engine run with an `item-batch@1` input. Batch size is set per kind:

     | Kinds | Batch |
     |---|---|
     | Sheets, voices | up to 8 |
     | `story-days`, `pronunciations`, `narrator-voice` | 1 (story-wide) |
     | Appearances and prop states | one scene's worth |
     | `direction` | 1 scene |
     | Generator kinds | one scene's worth |

     A single-item publish is a batch of one.
4. **Write.** Each item is verified and written on its own (section 4.2). The item row records its
   value hash, inputs, run, artifact and cost. Downstream items become stale as a consequence.

Queueing marks each planned item `Queued` with the plan's id, which acts as a lease: a second plan
skips the item instead of producing it twice. Progress and cost are reported in `run_progress`
under the plan id.

### 2.4 Who produces an item

Every item has an origin: Director, Generator or Author.

- **The author can write any director item directly.** Examples are editing a character's
  description in the library panel, changing a look in a scene, or editing a cue's tags. The Stories
  or Publications RPC writes the rows and gives the item a new value hash. Downstream items become
  stale as they would after any other change. "Add John's description to the story" works the same
  whether the author or the director wrote it.
  - A sheet, look, prop state or voice is one row per item, so editing it makes the item `Author`
    and pins it.
  - A direction is large, so an edit marks only the touched cue or frame as `edited` (9.1).
- **A pinned item is never overwritten by a run.** It can still go stale, and the author sees that.
  Unpinning hands the item back to the director. This replaces the earlier "only fill null fields"
  rule.
- **Review by kind.** A publication lists the kinds that need review in `review_kinds`. The
  default is `{voice, narrator-voice}`, and the author can add any director kind.
  - Voices are reviewed by default because they are the one choice a machine cannot verify, and
    the most expensive to reverse. A voice locks on first use (4.4), and recasting regenerates
    every line it speaks.
  - Everything else is cheap to edit afterwards, and an edit is still a correction signal (4.5).
    Reviewing 100 scene directions up front would be a chore with little gain.

  A director item of a reviewed kind that passes its verifiers lands as `Proposed`. The rows are
  not changed, and the item keeps the artifact. The author then does one of three things:
  - **Accept.** The writer applies the artifact.
  - **Edit, then accept.** This is an `ExternalCorrection` outcome.
  - **Reject.** The item returns to its previous value, also with an `ExternalCorrection`.

  Generator items are never proposed. They are cheap to regenerate and are judged by playing them.

## 3. Director: item values

The director's output is structured data that references the story by id. Prose goes only in the
fields that a renderer turns into a prompt. Every free-text claim about an entity carries
**evidence**: the element ids whose text supports it. This makes grounding deterministically
checkable (section 6).

### 3.1 `item-batch@1`

A run's input is a batch of one kind. It holds each item's key and its resolved inputs: the
source text as numbered elements with ids, plus the current value of each upstream item.

```csharp
public sealed record ItemBatch(string Kind, IReadOnlyList<ItemRequest> Items);
public sealed record ItemRequest(string Key, IReadOnlyDictionary<string, JsonElement> Inputs);
```

### 3.2 Values

Each kind's value is its own contract (`character-sheet@1`, `scene-appearance@1` and so on), and
the agent writes one artifact per item.

```csharp
public sealed record StyleGuide(string Look, string Palette, string AspectRatio, string Lens);

public sealed record CharacterSheet(
    Guid CharacterId,
    string Description,
    Identity Identity,                    // never changes within the story
    IReadOnlyList<Wardrobe> Wardrobes,
    VoiceProfile Voice);

public sealed record Identity(
    string Age, string Build, string Face, string Hair, string Skin,
    IReadOnlyList<string> Marks, IReadOnlyList<Evidence> Evidence);

public sealed record Wardrobe(string Label, string Description, IReadOnlyList<Evidence> Evidence);

public sealed record VoiceProfile(
    string Description,                   // "late 50s, gravelly, slow, Glaswegian"
    string Baseline);                     // default delivery, e.g. "dry, understated"

public sealed record VoiceCasting(Guid CharacterId, string Provider, string VoiceId, VoiceSettings Settings);

public sealed record LocationSheet(
    Guid LocationId, string Description, string Layout, string KeyFeatures,
    IReadOnlyDictionary<string, string> LightingByTimeOfDay,
    Acoustics Acoustics, IReadOnlyList<Evidence> Evidence);

public sealed record Acoustics(string Space, string Ambience);   // "small tiled kitchen", "fridge hum, distant traffic"

public sealed record PropSheet(Guid PropId, string Description, IReadOnlyList<Evidence> Evidence);

public sealed record StoryDays(IReadOnlyList<StoryDay> Days);
public sealed record StoryDay(int Day, IReadOnlyList<Guid> SceneIds);

public sealed record SceneAppearance(
    Guid SceneId, Guid CharacterId,
    string Wardrobe,                      // a label from the character's sheet
    IReadOnlyList<string> State,          // "left hand bandaged", "coffee stain on shirt"
    Change? Change);                      // required when this differs from the previous look on the same story day

public sealed record PropState(
    Guid SceneId, Guid PropId,
    IReadOnlyList<string> State,          // "broken", "lit"; empty = as described on the sheet
    Change? Change);                      // required when this differs from the previous state, on any day

public sealed record Change(string Reason, Guid? ElementId);

public sealed record SceneDirection(
    Guid SceneId,
    IReadOnlyList<Frame> Frames,
    IReadOnlyList<Cue> Cues);

public sealed record Frame(
    Guid FirstElementId, Guid LastElementId,   // contiguous, in order, covering the scene exactly once
    Guid LocationId,
    string TimeOfDay,
    IReadOnlyList<Placement> Characters,
    IReadOnlyList<Guid> Props,
    string Beat,                               // what is happening: an action, or "MARA presses, TOM deflects"
    string Shot,                               // wide | medium | close | over-the-shoulder | insert
    int? HoldMs);                              // only for frames with no cue to time them

public sealed record Placement(
    Guid CharacterId, string Screen,           // left | centre | right | off
    string Pose, string Expression,
    IReadOnlyList<string> State);              // changes within the scene only; the look comes from the appearance

public sealed record Cue(
    Guid ElementId,                            // Dialogue or Narration element
    Guid? SpeakerId,                           // null for Narration, which uses the narrator voice
    string Performance,                        // "[sad] I'm sorry." — the original words plus tags and pause punctuation
    int GapBeforeMs,                           // minimum silence before this cue; < 0 overlaps the previous cue
    bool Interrupted);                         // cut off by the next cue

public sealed record Evidence(Guid ElementId, string Quote);
public sealed record Pronunciation(string Word, string Alias);    // "Siobhan" -> "shi-VAWN"
```

Most screenplays describe a character once, at their introduction, and never again. A sheet
separates what the text states (with evidence) from what the director invents. An invented trait
is still canonical once it is written: the point is that it is decided once, not that it is sourced.

A frame's `Beat` is usually an action line, but not always. A run of six dialogue lines with no
action between them is still one or more frames, and its beat describes the exchange. A frame
boundary falls where the picture would change, not where the text changes.

## 4. Director: the agent workflow

### 4.1 Engine changes

- **One workflow per director kind.** Replace the singleton `DiscoveryGoal` with `IDiscoveryGoals`,
  keyed by workflow: `story-import`, `character-sheet`, `location-sheet`, `prop-sheet`, `story-days`,
  `pronunciations`, `voice`, `narrator-voice`, `appearance`, `prop-state`, `direction`.
  - `RunDispatcher` takes the workflow key with the input.
  - A goal supplies the brief, the item contract, the verifiers and the system-prompt framing,
    which replaces "document import engine".
- **Batch goals.** A goal's deliverable is "one passing artifact per item in the batch".
  - `write-artifact` takes the item key, and refuses a key that is not in the batch.
  - The run ends when every item has passed. When the agent stops short, the items that passed
    are still written and the rest are marked `Failed`.
- **Policy per family and workflow.** `DocumentFamilyPolicy` and the run modes are keyed by
  (family, workflow). Each kind therefore moves from Discovery to Scheduled on its own.
- **Families per publication.** A publication is classified once, when it is created. The
  classifier is the same synopsis-then-match classifier as import, but uses a `publish` taxonomy:
  genre and visual register matter here, and manuscript form does not. Every run in the
  publication inherits the family. A publish family is something like
  `grounded-contemporary-drama` or `animated-comedy`, so a family's skills and playbooks encode a
  directing style.
- **Worker.** Add a `story-publish` queue, a `ProtoFast.Publishing.Worker` that references Engine
  and Screenplay, and the `ItemScheduler`. Generation calls slow external APIs and should scale
  separately from import.

### 4.2 How the agent works

Each workflow is exactly the discovery harness: `execute_skill` and `execute_code`, built-in
primitives, family skills written in C#, script safety review, and skill verifiers. The briefs
carry the same lesson the import brief learned: **code cuts, the model labels**. For `direction`:

1. Code numbers the elements, groups them into candidate beats (an action line, or a run of
   dialogue between actions), and collects the characters speaking or mentioned in each beat.
2. The model, or a delegate, labels beats with frame boundaries, shot, placement, pose and
   expression. It also writes delivery tags as `(elementId, tags, pauseHints)`, never as rewritten
   text.
3. Code assembles each `Cue.Performance` from the original dialogue text plus the tags, and calls
   `write-artifact`.

The model never emits a line's words, and it never decides wardrobe: that comes from the scene's
appearance items, which are inputs. It is the same "slice, don't retype" rule that `StoryGoal`
enforces for import.

Family skills that the agent writes here generalise across stories in a family. Examples are
`screenplay-beat-splitter` and `parenthetical-to-tag`. The existing `skill-names` verifier applies
unchanged: a skill that hard-codes one story's cast is refused.

### 4.3 Reading siblings

Consistency across items of the same kind needs context that is not an input. For example, two
characters should not end up with the same face, and a voice must differ from the voices of
characters who share scenes with it. A `read-item` built-in skill returns any item's current value
and freshness in the publication, and `list-items` lists them by kind.

These reads are not recorded as inputs, so siblings never depend on each other. Rules that compare
siblings are verifiers instead (`voice-distinct`, `look-distinct`), and they run on every write.

### 4.4 Casting

The `voice` and `narrator-voice` items are produced by a `cast-voice` built-in skill. It either
searches the ElevenLabs voice library or designs a voice from `VoiceProfile.Description`. It
proposes the best candidate, and the item keeps up to two runners-up with a sample line each. Voices
are reviewed by default (2.4), so the author listens to the candidates and accepts one, or picks a
runner-up.

**A voice pins itself the first time audio is generated with it.** From then on, a changed sheet
shows the voice as stale, but nothing recasts it. Recasting means unpinning the voice, which makes
every audio item that uses it stale, and the plan shows that cost before anything runs. This is
consistency rule A1.

### 4.5 Learning from items

Every item is its own `StageRecord` with its own verdicts, and so its own outcome. That has three
effects:

- **Observations arrive fast.** One story yields about 100 `direction` outcomes and several hundred
  `appearance` outcomes, so `MinObservations = 20` is reached within one story.
  - Those observations are correlated, because they share one author's style, one cast and one set
    of sheets.
  - Add `Thresholds.MinSources` (for example 3 distinct stories) to the promotion check, so that one
    story cannot promote a kind on its own.
- **Small kinds promote first.** `prop-sheet` and `appearance` are small, well-verified decisions.
  They are the natural first candidates to move to a small model.
  - Most appearances simply carry the previous look forward, and the deterministic `continuity`
    verifier covers them.
  - That makes `appearance` the first kind that could reach `Codified`.
- **Author decisions are outcomes.**
  - An edit to, or rejection of, a director item is an `ExternalCorrection` against the executor that
    produced it: a fail at triple weight.
  - Accepting a proposal unchanged adds nothing, because the verifier pass already counted.

## 5. Consistency

Each consistency rule is enforced in one or more of three places:

- **By an input edge.** A change propagates as staleness: appearances chain through the story, and
  a direction depends on the scene's appearances.
- **By compilation.** Code assembles a value from upstream values, so a model cannot paraphrase it.
  Prompts and cue text are built this way.
- **By a verifier.** A check across items: voice distinctness, the 180-degree rule, continuity.

### 5.1 Audio

**A0. What is voiced.** Dialogue elements use the speaker's voice, and Narration elements use the
narrator voice.

Action and Description lines are not narrated. They become frames and timed room tone (5.3).
Reading stage directions aloud turns the experience into an audiobook of a screenplay. That would
also double the audio cost, and it would compete with the pictures, which already carry the action.

Delivery tags stay out of the story text. They live in `cues.performance`, and the review view shows
them over the line. Writing `[sad]` into `SceneElement.Text` would break mention offsets and
`story-fidelity`, and the editor would render the tags as part of the screenplay.

**A1. One voice per character for the whole story, fixed before any line is generated.** The voice
id, the model (`eleven_v4`) and the voice settings live on the character (the narrator's on the
publication) and are passed verbatim on every request. No scene-level item may choose or change a voice. A per-scene `seed` is derived from
`hash(sceneId)`, so a regeneration of unchanged input stays as close to the previous one as the
model allows. The seed improves stability but does not guarantee identical output.

**A2. Generate conversations, not lines.** Concatenating per-line TTS gives every line its own
prosody, with no response to the line before it. The result is the flat, "two people reading" sound,
and the gaps between lines are arbitrary. The ElevenLabs **Text to Dialogue** API takes a list of
`{text, voice_id}` turns and performs them as one exchange, with turn-taking, reactions and pacing
that respond across speakers. Every scene's cues go through it, in order:

- A request holds at most 2,000 characters across its turns. Chunk at beat boundaries (section 4.2,
  step 1), never mid-turn, and prefer to break where an action beat already implies a pause.
- Each chunk after the first passes `previous_request_ids` (the last ≤3 chunks of this scene) and
  `previous_text`/`future_text`. The model then continues the same performance instead of starting
  over.
- The first chunk of a `CONTINUOUS` scene passes the previous scene's last request id. A
  conversation that crosses a scene break therefore does not reset. This is why that scene's audio
  item has the previous scene's audio as an input.
- Use the `with-timestamps` variant. Its `voice_segments` and character alignment give each cue's
  start and end. That is what times the frames (section 5.3) and what the audio verifiers check.

**A3. Pauses come from two places.**

- *Within a line*, the text carries them. Eleven v4 does not support SSML `<break>`, so pauses are
  ellipses (`I... I can't`), dashes (`I can't—`), and tags (`[hesitates]`, `[long pause]`). The
  director emits pause hints and code renders them as punctuation and tags.
- *Between lines*, the timeline carries them.
  - A model-produced gap is kept by default, because it is usually the most natural.
  - `Cue.GapBeforeMs` sets a **minimum**. When an action sits between two lines ("She crosses to
    the window."), the director gives it a duration, and the cut stretches the gap at that turn
    boundary with room tone.
  - A negative gap overlaps the cue with the previous one. `Interrupted` cues end in a dash so the
    voice cuts off, and the next cue is laid over the tail.
  - Lengthening and overlapping are done in the mix using segment boundaries. They are never asked
    of the model.

**A4. Emotion has continuity too.** Tags describe transitions in a character's state through the
scene, not a fresh emotion per line. The director records an arc per speaker per scene: starting
state, turning points (element ids) and end state. Tags are derived from that arc.

- Parentheticals are honoured: `(sadly)` becomes `[sad]`.
- A character with no tag reverts to `VoiceProfile.Baseline`, not to neutral.
- Tag density is capped at two tags per line plus one non-verbal. Over-tagging produces the
  overacted sound that v4 is prone to.
- Tags come from a story vocabulary, seeded with the documented emotion, delivery and non-verbal
  tags and extendable per story (`StoryVocabulary.DeliveryTags`). v4 accepts free-form tags, but a
  closed vocabulary keeps a family's direction comparable across runs and lets a verifier check it.

**A5. Acoustic consistency.**

- Every location has one room profile (`Acoustics`) and one ambience bed per time of day (an
  `ambience` item), reused by every scene set there.
- Extensions map to processing:
  - `V.O.` is dry and close.
  - `O.S.` and `O.C.` are filtered, quieter, and get the room's reverb.
  - A phone or intercom gets a band-pass filter.
- Every scene is loudness-normalised to one target (for example −16 LUFS integrated), so cutting
  between scenes does not jump in level.

**A6. Names are pronounced the same way every time.** The `pronunciations` item becomes an
ElevenLabs pronunciation dictionary attached to every request in the story. A name the model gets
wrong is fixed once, for every scene. Every audio item depends on it.

### 5.2 Images

No real images are generated yet, but every rule below still applies. The placeholder generator
consumes the same request a real model would, and the verifiers run on the request, not on the
pixels.

**I1. Identity is separate from state.** A character's `Identity` (face, age, build, hair, skin,
marks) lives on the sheet and never changes within a story. What does change lives in the scene's
appearance (wardrobe, injuries, dirt) and, within a scene, in the frame's `Placement.State`. Mixing
identity and state is the main source of drift: if a frame prompt says "John, tired, in a grey
coat", the next frame's model re-guesses John's face.

**I2. A look changes only at a recorded change.** This is the script supervisor's continuity
breakdown, expressed as the appearance chain:

- `story-days` assigns every scene a story day.
- A character's look on the same story day equals their previous look, unless the appearance
  carries a `Change` with a reason and, where the text has one, the element that causes it.
- A change persists along the chain until the next change. For example, a coffee spill in scene 14
  stays on the shirt until a scene-22 change.

Prop states follow the same rule, but across story days: a prop stays as it was last left until a
recorded change.

Directions never decide wardrobe or prop state: the scene's appearances and prop states are their
inputs. When the author edits
a look, the next look in the chain and the scene's direction go stale, so the edit cannot be
silently contradicted.

**I3. Prompts are compiled, never written.** A frame's image prompt is built by code from its
inputs. Fragments are emitted verbatim, always in the same order and the same words:

1. the style guide;
2. the location sheet, with the lighting for the time of day;
3. each character's identity, the scene's look, and the frame state;
4. the props;
5. the beat, shot and placement.

The same character is described in identical tokens in every frame of the story. The model writing
the direction never writes prompt prose about an entity.

**I4. References before frames.** `reference` items are inputs to every frame image that shows
their subject:

- a reference sheet per (character, wardrobe), as a turnaround;
- an establishing plate per (location, time of day);
- a reference per prop.

When real generation lands, every frame is conditioned on its references through a multi-reference
or image-to-image model. A reference regenerates only when its sheet changes, and then every frame
showing it goes stale. That is the right consequence of changing what a character looks like. The
placeholder generator already records the reference set in each frame request, so the graph exists
before the pixels do.

**I5. Staging continuity.** `Placement.Screen` records who is on the left and who is on the right.
Within a scene, a character keeps their side across consecutive frames: the 180-degree rule. The
exception is a frame the director marks as crossing the line, with a reason.

**I6. Style is an item every image depends on.** The style guide, aspect ratio and image model are
the `style` item. Changing it makes every reference and frame image stale. The author sees that
cost in the plan before anything regenerates.

### 5.3 Picture and sound together

The `cut` item is assembled by code:

- Each frame's span runs from its first cue's start to the start of the next frame's first cue.
- The times come from the dialogue alignment and include the `GapBeforeMs` adjustments.
- A frame with no cue, such as a pure action beat, uses `HoldMs`, and its duration is filled with the
  location's ambience.

Transitions map to the cut:

- `CUT TO` is a hard cut.
- `DISSOLVE TO` is a crossfade.
- `FADE OUT` is a fade to black and silence.

## 6. Verifiers

Deterministic verifiers run first and stop at the first fail (the existing `VerifierRunner`).
Findings go back to the agent in discovery mode, exactly as `story-draft-json` findings do today.
Verifiers run per item. Some read the item's siblings through the same store that `read-item` uses.

**Library kinds** (`character-sheet`, `location-sheet`, `prop-sheet`, `voice`, `narrator-voice`, `story-days`, `pronunciations`)

| Id | Kind | Checks |
|---|---|---|
| `sheet-json` | det | Schema; the subject id matches the item key; wardrobe labels are unique; every speaking character has a `VoiceProfile` |
| `sheet-evidence` | det | Every `Evidence` element id is among the item's source elements, and its text contains the quote and mentions the subject. A stated trait without evidence fails |
| `look-distinct` | det | No two character sheets in the story share face, hair and build verbatim |
| `voice-distinct` | det | No two characters who share a scene share a voice id, and no character shares the narrator's |
| `story-days-json` | det | Days partition the scenes, in story order |
| `sheet-judge` | model | Rubric: consistent with the text and with sibling sheets; invented traits plausible for the story's world; the voice description fits the character |

**`appearance`, `prop-state`**

| Id | Kind | Checks |
|---|---|---|
| `appearance-json` | det | The wardrobe label exists on the character's sheet; the prop is mentioned in the scene |
| `continuity` | det | A look that differs from the previous look on the same story day has a `Change`. A prop state that differs from the previous state on any day has a `Change`. A `Change.ElementId` must be in this scene |
| `appearance-judge` | model | Rubric: a change is warranted by the text; nothing in the scene contradicts the look or state |

**`direction`**

| Id | Kind | Checks |
|---|---|---|
| `direction-json` | det | Schema; frames cover every element exactly once, contiguous and in order; every Dialogue and Narration element has exactly one cue |
| `performance-fidelity` | det | Each cue's `Performance`, with tags removed and pause punctuation normalised, equals the element's text word for word. The director adds delivery and never edits words; this is the publishing twin of `story-fidelity` |
| `tag-vocabulary` | det | Tags are well formed, come from the story vocabulary, and stay within the per-line cap |
| `frame-grounding` | det | The frame location matches the scene heading. Frame characters are speaking, mentioned, or carried over from an earlier frame of the scene, and each has an appearance in the scene. Props exist |
| `staging` | det | Screen side stays stable across consecutive frames unless a crossing is declared |
| `direction-judge` | model | Rubric: frame count fits the beats; tags fit the context and the speaker's arc; beats describe what is visible |

**Generator kinds**

| Id | Kind | Checks |
|---|---|---|
| `frame-image` | det | Right dimensions; the request contains every reference for its entities and the compiled prompt fragments |
| `audio-coverage` | det | Every cue has a voice segment; per-cue duration is within bounds for its character count; no unplanned silence over a threshold; loudness on target |
| `audio-transcript` | model | Speech-to-text round trip (ElevenLabs Scribe): word error rate per cue against the tag-stripped text stays under a threshold. This catches skipped, repeated or hallucinated words, the usual TTS failure |
| `cut-timing` | det | Frame spans are contiguous, non-overlapping and cover the audio; transitions are applied |
| `frame-consistency` | model | Later, with real images: a vision model compares each frame to its references for identity, wardrobe and location |

Every director kind therefore has deterministic verifiers and can climb the ladder. The generator
kinds also have deterministic verifiers, which the `Codified` tier requires.

## 7. Generation

Generator kinds run as seed workflows, `Scheduled` from the start, with host-written C# executors:
`reference`, `ambience`, `audio`, `frame-image` and `cut`. They are planned, batched, verified and
written like director items. The difference is that no agent is involved unless a stage escalates.

Engine changes:

- **Seed codified executors.** Add an `IExecutorFactory` for `Codified` specs whose `CodeAssembly`
  names a host-registered type (`ExecutorOrigin.Seed`, promoted on publish). This fills the gap
  where nothing can resolve a codified spec today. It is a separate thing from agent-written code.
- **Media outside the artifact store.** A generator item's artifact is a JSON manifest (keys,
  hashes, durations, cue timings). The bytes go to the `media` bucket (section 8.2). Verifiers read
  the manifest, never the bytes.
- **Media clients.**
  - `ISpeechSynthesizer`, implemented by `ElevenLabsDialogueSynthesizer` (Text to Dialogue
    with timestamps, request stitching, pronunciation dictionaries).
  - `IFrameRenderer`, implemented by `PlaceholderFrameRenderer` (writes a black PNG at the style
    guide's aspect ratio and stores the compiled request beside it).
  - The key lives in Secrets Manager as `Api_Providers__elevenlabs__ApiKey`, like the language
    model keys.
- **Cost.** ElevenLabs bills per character. The synthesizer reports `Cost` on the stage result, so
  it reaches the item row, the plan and the publication. The stage `Budget` caps an item.

Two caches stack:

- **Freshness.** A fresh item is never planned.
- **Media.** A stale item whose request hash already exists in `media_assets` reuses the asset
  without calling a provider. Audio is keyed by `hash(cues, voice ids, settings, model, seed,
  pronunciations)` and frames by `hash(compiled request)`. A frame that went stale because its
  scene was re-directed, but whose request came out identical, costs nothing.
- **Audio chunks.** A scene's audio is a list of dialogue chunks (A2) mixed into one file. Each chunk
  is its own `SceneAudioChunk` asset, keyed by its turns, voices, settings and the ids of the
  chunks before it. When one line changes, only its chunk is re-requested, and the scene is
  remixed. The new chunk gets `previous_request_ids` from the unchanged chunks before it and
  `next_request_ids` from the chunk after it, so it joins both sides.

## 8. Storage and schema

### 8.1 Where each kind of data lives

| Data | Store | Why there |
|---|---|---|
| Run artifacts: one per item, plus generator manifests | Engine artifacts in the `documents` bucket (`runs/{runId}/{stageId}/{hash}`), object-locked | This is what the verifiers judged and what the ledger points at. It is the audit trail, the replay source and the holding place for proposals, and import already works this way. |
| Item values: descriptions, voices, wardrobes, looks, frames, cues, timings | ThePlot Postgres (`protofast` database), written by item writers | This is the product's source of truth. The author edits it and the player reads it. The api never reads engine artifacts, except to show a proposal. |
| Item provenance and freshness | ThePlot Postgres, `publication_items` and `publication_item_inputs` | Freshness is a query over the same database the values live in. |
| Media bytes: scene audio, alignment, frame images, references, ambience | A new `media` bucket with content-addressed keys | These are large binaries streamed to the browser, and they must be deletable with the story. |

### 8.2 Media bucket

The `documents` bucket is the wrong home for media:

- Frozen objects there carry GOVERNANCE retention, so a story's media could not be deleted until its
  locks lapse.
- Uploads and run artifacts have nothing in common with media that is streamed for playback.

The new bucket is defined in `infra/media.tf`:

- It is private, with SSE-S3, a TLS-only bucket policy, and the same public-access block as `documents`.
- It has no versioning and no object lock.
- CORS allows `GET` and `HEAD` from the theplot origin, so the player can fetch alignment JSON and
  decode audio with Web Audio.
- The lifecycle rule aborts incomplete multipart uploads.
- The instance role gets put, get and delete on it.
- For dev, add `protofast-media` to the apphost's `WithBuckets` and to the CORS loop in
  `scripts/localstack-init.sh`.

`S3StorageOptions` holds one bucket, so register a second, keyed `IObjectStore` for `media`. That
is the same pattern as the keyed `IMessageQueue`.

```
stories/{storyId}/audio/{sha256}.mp3
stories/{storyId}/audio/{sha256}.alignment.json      character timings and voice_segments from ElevenLabs
stories/{storyId}/frames/{sha256}.png
stories/{storyId}/references/{sha256}.png
stories/{storyId}/ambience/{sha256}.mp3
```

- **Keys are the SHA-256 of the bytes.** A key never changes meaning, so objects are served with
  `Cache-Control: immutable`. Regeneration writes new keys and never overwrites, so a scene being
  played while it regenerates keeps working.
- **Keys are prefixed by story.** Deleting a story deletes one prefix. A later CloudFront signed
  cookie scoped to `stories/{storyId}/*` can then cover a whole story, instead of one presigned URL
  per frame. For now the API returns presigned GET URLs from the existing `IPresignedUrlFactory`.
  Range requests work against them, so audio seeks.
- **Garbage collection.** A sweeper deletes objects that no `media_assets` row references, after a
  7-day grace period so that playback already in flight doesn't break. Deleting a story enqueues a
  delete of its prefix.
- **Dev gotcha.** LocalStack S3 is in-memory, so a restart leaves `media_assets` rows that point at
  nothing. The media cache therefore treats a row whose object is missing (`FindExisting`) as a miss.

### 8.3 Story library columns

What a character looks and sounds like is true of the story, not of one render. These fields live on
the library entities, so the author edits them in the scene editor's library panel and they survive
republishing. Each column group is the value of one item kind.

```
characters        + description       text  null     character-sheet
                  + appearance        jsonb null     character-sheet: Identity
                  + voice             jsonb null     character-sheet (description, baseline), voice (provider, voice_id, settings)

character_wardrobes   (new; owned by Character, no user_id)          character-sheet
                  id                  uuid  pk
                  character_id        uuid  fk characters  on delete cascade
                  position            int   >= 0
                  label               text  unique per character   "work suit"
                  description         text

locations         + description       text  null     location-sheet
                  + appearance        jsonb null     layout, key features, lighting by time of day
                  + acoustics         jsonb null     space, ambience

props             + description       text  null     prop-sheet

scenes            + story_day         int   null     story-days
                  + content_hash      text  null     source/scene, set by StoryService on save

scene_appearances   (new; owned by Scene)                             appearance
                  id                  uuid  pk
                  scene_id            uuid  fk scenes               on delete cascade
                  character_id        uuid  fk characters           on delete cascade
                  unique (scene_id, character_id)
                  wardrobe_id         uuid  fk character_wardrobes  null, on delete set null
                  state               jsonb string[]
                  change_reason       text  null
                  change_element_id   uuid  fk scene_elements       null, on delete set null

scene_prop_states   (new; owned by Scene)                             prop-state
                  id                  uuid  pk
                  scene_id            uuid  fk scenes               on delete cascade
                  prop_id             uuid  fk props                on delete cascade
                  unique (scene_id, prop_id)
                  state               jsonb string[]
                  change_reason       text  null
                  change_element_id   uuid  fk scene_elements       null, on delete set null

stories.vocabulary (json)   + deliveryTags    string[]                        author
                            + pronunciations  [{ word, alias }]               pronunciations
```

### 8.4 Publication tables

There is one publication per story, regenerated in place. Stories have no drafts or versions, and
publications follow the same rule. Ownership follows the owned-entity rule:

- `publications`, `published_scenes`, `publication_items`, `publish_plans` and `media_assets` are
  fetched on their own, so they carry `user_id`.
- Frames, placements, cues and item inputs are only reached through a parent, so they don't.

```
publications
  id                uuid  pk
  user_id           text
  story_id          uuid  fk stories  unique, on delete cascade
  family            text  null   classified once, inherited by every run
  review_kinds      text[] default {voice, narrator-voice}
  style             jsonb look, palette, aspect_ratio, lens, image_model, audio_model, seed    (the style item)
  narrator_voice    jsonb null   description, baseline, provider, voice_id, settings          (the narrator-voice item)
  reconcile_policy  text  Manual | Propose | Auto, default Auto
  reconcile_threshold numeric default 0.8
  cost_usd_micros   bigint
  date_created, date_last_modified

publication_items
  id                uuid  pk
  user_id           text
  publication_id    uuid  fk publications    on delete cascade
  key               text  unique per publication
  kind              text
  scene_id          uuid  fk scenes      null, on delete cascade
  character_id      uuid  fk characters  null, on delete cascade
  location_id       uuid  fk locations   null, on delete cascade
  prop_id           uuid  fk props       null, on delete cascade
  status            text  Queued | Running | Proposed | Fresh | Failed       Missing = no row; Stale is derived
  origin            text  Director | Generator | Author
  pinned            bool
  value_hash        text  null
  plan_id           uuid  fk publish_plans  null, on delete set null
  run_id            text  null
  artifact_hash     text  null   the artifact the current value came from
  proposal_hash     text  null   a verified artifact awaiting review
  proposal_kind     text  null   Publish | Reconcile
  failure           text  null
  cost_usd_micros   bigint
  date_produced     timestamptz null

publication_item_inputs   (owned)            pk (item_id, input_key)
  item_id           uuid  fk publication_items  on delete cascade
  input_key         text
  input_value_hash  text

item_values   (owned by the publication)       pk (publication_id, value_hash)
  publication_id    uuid  fk publications  on delete cascade
  value_hash        text
  value             jsonb  canonical value; source items hold their element snapshot
  kept while an item's current value, an input row or a history row names it

item_history   (owned)
  id                uuid  pk
  item_id           uuid  fk publication_items  on delete cascade
  value_hash        text
  origin            text  Director | Generator | Author
  action            text  Publish | Reconcile | Keep | Edit | Accept | Reject | Undo
  run_id            text  null
  date_created      timestamptz
  the last 20 per item are kept

publish_plans
  id                uuid  pk
  user_id           text
  publication_id    uuid  fk publications  on delete cascade
  requested         jsonb  keys
  scope             text   Item | ItemAndDownstream | Story | Reconcile
  status            text   Planned | Running | Done | Failed | Cancelled
  estimate_usd_micros, cost_usd_micros  bigint
  date_created, date_last_modified

published_scenes
  id                uuid  pk
  user_id           text
  publication_id    uuid  fk publications  on delete cascade
  scene_id          uuid  fk scenes        on delete cascade
  unique (publication_id, scene_id)
  audio_asset_id    uuid  fk media_assets null, on delete set null      audio
  duration_ms       int   null                                          cut

frames   (owned)                                                         direction
  id                uuid  pk
  published_scene_id uuid fk published_scenes on delete cascade
  position          int   >= 0
  first_element_id  uuid  fk scene_elements  on delete cascade
  last_element_id   uuid  fk scene_elements  on delete cascade
  location_id       uuid  fk locations       on delete cascade
  time_of_day       text
  beat              text
  shot              text
  transition_in     text  null
  hold_ms           int   null
  start_ms          int   null                                           cut
  end_ms            int   null   check end_ms > start_ms                 cut
  image_asset_id    uuid  fk media_assets null, on delete set null       frame-image
  edited            bool                                                 kept as fixed input by later runs

frame_placements   (owned)        pk (frame_id, character_id)            direction
  frame_id          uuid  fk frames      on delete cascade
  character_id      uuid  fk characters  on delete cascade
  screen            text  left | centre | right | off
  pose              text
  expression        text
  state             jsonb string[]   within-scene changes only

frame_props   (owned)             pk (frame_id, prop_id), both on delete cascade

cues   (owned)                                                           direction
  id                uuid  pk
  published_scene_id uuid fk published_scenes on delete cascade
  element_id        uuid  fk scene_elements  unique, on delete cascade
  speaker_id        uuid  fk characters      null, on delete cascade   null = narrator
  performance       text  "[sad] I'm sorry."
  gap_before_ms     int
  interrupted       bool
  start_ms          int   null                                           audio
  end_ms            int   null                                           audio
  edited            bool                                                 kept as fixed input by later runs

media_assets
  id                uuid  pk
  user_id           text
  story_id          uuid  fk stories  on delete cascade
  kind              text  SceneAudio | SceneAudioChunk | FrameImage | Reference | Ambience
  storage_key       text
  content_type      text
  content_hash      text
  byte_size         bigint
  request_hash      text
  unique (story_id, kind, request_hash)
  provider, model   text
  duration_ms       int   null
  width, height     int   null
  alignment_key     text  null
  provider_request_id text null   ElevenLabs request id, for request stitching
  cost_usd_micros   bigint
  date_created
```

The freshness query, which also derives the stale flag the API returns:

```sql
select i.key
from publication_items i
join publication_item_inputs d on d.item_id = i.id
left join publication_items x on x.publication_id = i.publication_id and x.key = d.input_key
where i.publication_id = @publication
  and (x.id is null or x.value_hash is distinct from d.input_value_hash);
```

`source/*` items are rows like any other. `StoryService` updates their `value_hash` in the same
unit of work as the scene save, so editing a line makes everything built from it stale immediately.

Cascades follow what makes a value meaningless:

- Deleting a character, location, prop or scene deletes its items and the rows built on it.
- Items that used one of those as an input see the input go missing, and so go stale.

### 8.5 Item writers

Each kind has an `IItemWriter`. Writers live in `ProtoFast.Data`, run in the worker as the user
(`UserContext.SetCurrentUser`), and write in one read-write unit of work:

1. Write the kind's rows (8.3, 8.4).
2. Compute the value hash with `ItemHashing`.
3. Store the value in `item_values`, append an `item_history` row, and upsert the item row with its
   status, origin, run, artifact and cost.
4. Replace its `publication_item_inputs` with the inputs the batch carried. A reconcile or a Keep
   records the moved-forward baseline instead (9.3).

Each kind replaces only the rows it owns:

- `direction` replaces the scene's frames, placements, frame props and cues. It never replaces a
  row marked `edited` whose source elements are unchanged.
- `appearance` replaces one `scene_appearances` row.
- `character-sheet` replaces the character's three columns and its wardrobes.
  - A wardrobe whose label survives keeps its id, so looks that use it stay valid.
  - A wardrobe that disappears nulls the looks that pointed at it. They then fail `appearance-json`
    when they are next published.

`Proposed` items skip step 1 and keep the artifact in `proposal_hash`. Accepting runs the writer
then.

The API side mirrors this for author edits. `UpdateCharacter`, `UpdateAppearance` and the other
single-row edit RPCs write the rows, then upsert the item as `Author` and pinned, with a fresh value
hash and the inputs resolved at that moment. `UpdateCue` and `UpdateFrame` mark the row `edited`
and re-hash the direction item. `SaveDirectedScene` runs the `SaveScene` code and the cue updates
in one unit of work, and moves the edited cues' baselines forward.

### 8.6 Migrations and protos

These are ThePlot migrations in `services/api/src/ProtoFast.Data/Migrations`. The worker already
references `ProtoFast.Data` for `StoryWriter`.

| Migration | Phase | Contents |
|---|---|---|
| `LibraryDescriptions` | 0 | Character, location and prop `description`; `StoryWriter` saves the importer's descriptions |
| `PublicationItems` | 0 | `publications`, `publication_items`, `publication_item_inputs`, `item_values`, `item_history`, `publish_plans`, `scenes.content_hash` |
| `StoryContinuity` | 1 | `appearance`, `voice`, `acoustics`, `character_wardrobes`, `scene_appearances`, `scene_prop_states`, `scenes.story_day` |
| `SceneDirection` | 2 | `published_scenes`, `frames`, `frame_placements`, `frame_props`, `cues` |
| `MediaAssets` | 3 | `media_assets`, plus the asset foreign keys on `frames` and `published_scenes` |

Proto changes:

- **`story.proto`.**
  - `Character` gains `description`, `appearance` and `voice`.
  - `Location` gains `description`, `appearance` and `acoustics`.
  - `Prop` gains `description`.
  - `Scene` gains `story_day`.
  - Wardrobes get create, update and delete RPCs.
  - Each of these edits upserts its item.
- **`publication.proto`, new.** `service Publications`, with these RPCs:
  - `CreatePublication`
  - `ListItems` (kind, status, stale and "may change" filters)
  - `GetItem` (value, origin, pinned, run, cost, and each input with its freshness)
  - `PlanPublish(keys, scope)`
  - `StartPlan`
  - `CancelPlan`
  - `AcceptItem`
  - `RejectItem`
  - `PinItem`
  - `UnpinItem`
  - `UpdateAppearance`
  - `UpdatePropState`
  - `UpdateCue`
  - `UpdateFrame`
  - `SaveDirectedScene` (the scene and its cues, in one save)
  - `GetSceneChanges` (hunks, each with its reconcile proposal if one exists)
  - `ResolveHunk(hunk, action, edit?)`
  - `ResolveAll(scene, action)`
  - `UndoChange(item, history id)`
  - `GetPublishedScene` (frames, placements, cues and presigned media URLs)

**Clients.**

- `theplot`:
  - Director mode in the scene editor (9.1): the Story | Director toggle, inline tags, frame
    brackets, the Direction tab, the change bar and the hunk navigator. Story mode gets the
    "Directed · n changes" badge.
  - Publish actions where the item lives: "Describe" on a character in the library panel, "Look
    in this scene" on a character chip in a scene, and "Direct" or "Generate" on a scene.
  - A status chip on each item (missing, fresh, stale, proposed).
  - A plan confirmation that shows the waves and the cost.
  - A review view for proposals.
  - A player (frames over audio).
- `admin-theplot`: lists the publish runs beside the import runs. It shows the same ledger, and runs
  are filterable by kind and item key.

## 9. Director mode

### 9.1 The toggle

The scene editor gets a **Story | Director** toggle. The mode is in the URL (`?mode=director`), so it
can be linked. Both modes show the same screenplay column, and both can edit it. Director mode
adds the director layer on top:

- **Dialogue rows** show the cue's performance: tags as chips at their positions in the line, plus
  gap and interruption markers between lines.
- **The left gutter** shows numbered frame brackets spanning the elements each frame covers.
- **A Direction tab** in the side frame, beside the library and overview tabs, shows the selected
  frame: shot, beat, placements with screen side, and props. It also shows the scene's looks
  (wardrobe, state, change), prop states and story day.
- **The library panel** in director mode shows sheets and voices, with a sample to play.
- **Every item** shows its status chip and the publish actions from 2.3.

Edits in director mode:

- **Changing a line's words** edits the story element and the cue together, in one save
  (`SaveDirectedScene`). Whoever edits the words is looking at the tags at the same moment, so the
  cue's baseline moves forward with the text and no discrepancy appears. This is the only story edit
  that resolves itself. Every other story edit made in director mode still shows as a change (9.2),
  but it appears inline straight away, where it can be resolved.
- **Tags, gaps, frames, looks and prop states** are edited in place. A direction is large, so
  edits are tracked per row: an edited cue or frame is marked `edited`. Later runs, whether a
  regeneration or a reconcile, keep edited rows as fixed input unless the story text underneath
  them changes. Looks, prop states and sheets are one row per item, so editing one pins its item
  (2.4).

Story mode is unchanged, except that a scene with a director layer shows a badge in its header,
"Directed · 3 changes". The badge opens director mode at the first change. Story mode never warns
about or blocks an edit.

### 9.2 Changes are diffs of inputs

A discrepancy is a stale item seen up close. To show what changed, the system needs the old input,
not just its hash:

- **Item values are kept, addressed by hash** (`item_values`, 8.4). This includes the
  source items: `source/scene/{s}` is the list of the scene's elements (id, type, speaker,
  parenthetical, text).
- **A stale item's recorded input hash names the old value.** The diff is old value against
  current value.
- **Element ids are stable across saves.** `SaveScene` keeps the ids the editor sends, so scene
  diffs are keyed by element id, with a word diff inside each changed element. Splitting or merging
  lines shows up as a changed element plus an added or removed one, and reconcile aligns text across
  neighbours.

Each change becomes one or more **hunks**, grouped by the director part they affect:

| Story change | Hunk | Affects |
|---|---|---|
| A dialogue line's words changed | `line-changed` | Its cue |
| A dialogue line's speaker changed | `speaker-changed` | Its cue (tags may not fit the new speaker's arc), placements |
| A dialogue line added | `cue-missing` | The direction |
| An element added inside a frame's range | `frame-grew` | That frame |
| An element removed | `cue-orphaned`, `frame-broken` | Its cue; the frame, when it was the first or last element |
| An action line's text changed | `beat-changed` | The frames covering it; the looks and prop states of subjects it mentions |
| The heading's location or time changed | `setting-changed` | Every frame, look and prop state in the scene |
| A character speaks or is mentioned for the first time in the scene | `look-missing`, `placement-missing` | A new look; the frames where the character appears |
| An upstream item changed (a wardrobe renamed, the previous look changed) | `upstream-changed` | Every item that used it, with a field diff |

Hunks are derived when they are read, never stored. A hunk's id is
`hash(item key, element ids, baseline hash)`, so it stays stable across reads.

### 9.3 Resolving a hunk

Director mode lists a scene's hunks in a change bar: "7 changes since directed · Review · Reconcile
all". The author steps through them with next and previous (`j` and `k`). Each changed element is
marked in the gutter, with an inline word diff and the old performance ghosted beside the new line.
Each hunk offers four actions:

- **Reconcile.** Carry the director's choices onto the new text with the smallest change (9.4). It
  is shown as a before and after, and it is the default action.
- **Edit.** Fix the hunk by hand, in place.
- **Keep.** The change doesn't affect direction, for example a typo fix. The director layer stays as
  it is.
- **Regenerate.** Re-direct the whole item from scratch, as a normal publish.

Resolving a hunk works by **moving the baseline forward per element**:

1. The writer builds a new source baseline: the old snapshot, with the hunk's elements replaced by
   their current versions.
2. It stores the baseline in `item_values` and records its hash as the item's input.
3. The remaining hunks are the diff between that baseline and the current text. When the two are
   equal, the item is fresh.

Partial progress survives leaving the page, and nothing beyond item values is stored.

When resolving a hunk changes a director value, the items downstream go stale as usual. Audio is
cached per chunk (section 7), so a one-line change regenerates one chunk, not the whole scene.

### 9.4 Reconcile

Reconcile is a workflow per director kind: `reconcile-direction`, `reconcile-appearance`,
`reconcile-prop-state` and `reconcile-sheet`. Its input is the old source, the new source, the
current director value and the hunks. Its output is a new director value that changes only the
hunks. It runs on the same tier ladder as everything else, cheapest first:

1. **Anchor transfer (code, `Codified` seed).**
   - A tag is anchored to the line start, to the word that follows it, or to the line end.
   - The old and new lines are aligned word by word.
   - When every anchor survives, each tag is placed at its anchor in the new line. A line-start tag
     stays at the start, because it sets the delivery for the whole line. So `[sad] I'm sorry` edited to
     `Max I'm really sorry` becomes `[sad] Max I'm really sorry` with no model call.
   - Frame ranges whose end elements survive keep their range. An inserted element joins the frame
     it was inserted into.
   - A new character's look carries forward their previous look.
2. **Small model (`DelegateSmall`).** It runs when an anchor is lost (the tagged words were deleted),
   when a structural hunk needs a judgement (a new line needs tags, or a frame grew past a beat), or
   when the anchor tier fails its verifiers.
   - The model sees the old line and performance, the new line, its neighbours and the speaker's
     arc.
   - It returns tag placements only, and code assembles the performance, so "slice, don't retype"
     still holds.
3. **Regenerate.** When more than half of a scene's elements changed, reconcile is skipped and the
   item is re-directed.

The engine's escalation handles the step from tier 1 to tier 2. Lost anchors fail the
`anchors-intact` verifier, and a failure moves the attempt one tier left on the ladder.

Verifiers:

| Id | Kind | Checks |
|---|---|---|
| `performance-fidelity` | det | Cue words equal the new text |
| `anchors-intact` | det | Every tag of the old performance is placed, or its removal is explained by a deleted anchor |
| `reconcile-minimal` | det | Cues, frames, looks and states outside the hunks are identical to before |
| `reconcile-judge` | model | Does the carried-forward delivery still fit the new meaning? Returns a confidence score. "I'm sorry" edited to "I'm not sorry at all" keeps every anchor, but `[sad]` probably no longer fits, so the score is low |

### 9.5 Auto-approve

`publications.reconcile_policy` sets what happens after a story edit:

- **`Manual`.** Hunks wait for the author.
- **`Propose`.** Reconciles run by themselves and wait as proposals in the change bar.
- **`Auto`, the default.** A reconcile is applied when its verifiers pass and `reconcile-judge`
  scores at least `reconcile_threshold` (default 0.8). Anything else waits as a proposal.

The policy works like this:

- **Trigger.** A story save queues one reconcile plan for the scene's stale director items, after a
  30-second debounce. A later save replaces the pending plan.
- **Scope.** Auto-reconcile stops at director items. Regenerating audio or images is always an
  explicit publish, because it costs money and changes what a reader hears.
- **Undo.** Every auto-applied reconcile appears in the scene's change history (`item_history`)
  with an undo. Undo writes the previous value back from `item_values` and restores the baseline.
- **Learning.** An author's undo or edit of an auto-applied reconcile is an `ExternalCorrection`
  against the tier that produced it. A tier that keeps getting undone demotes, and a family's
  threshold could later be tuned from the same outcomes.

## 10. Phases

0. **Groundwork.**
   - Run the `LibraryDescriptions` and `PublicationItems` migrations.
   - Make `StoryWriter` save descriptions.
   - Maintain the source hashes in `StoryService`.
   - Add `ItemHashing`, the planner and the freshness query.
   - Add keyed goals per kind, batch goals, and policy keyed by (family, workflow).
   - Add the seed codified executor factory.
1. **Library items.**
   - Build `character-sheet`, `location-sheet`, `prop-sheet`, `story-days`, `pronunciations`,
     `voice` and `narrator-voice`: briefs, verifiers, writers, `read-item`/`list-items`, and
     `cast-voice` with candidate samples.
   - Add voice review (accept or pick a runner-up).
   - Add the `StoryContinuity` migration.
   - Add library-panel publish actions and author edits as items.
   - Run them on the existing imported screenplays in the EngineHarness.
2. **Scene items.**
   - Build `appearance`, `prop-state` and `direction`.
   - Add the `SceneDirection` migration.
   - Add `review_kinds` and accept/reject for any director kind.
   - Add scene publish actions.
   - Have the scheduler re-check freshness before each wave.
   - Build director mode: the toggle, the inline layer, the Direction tab and `SaveDirectedScene`.
   - Add `item_values`/`item_history`, hunks, the change bar, Keep/Edit/Regenerate, and anchor
     transfer as the first reconcile tier.
3. **Generator items.**
   - Add the `media` bucket (terraform and LocalStack) and the `MediaAssets` migration.
   - Build placeholder references and frames, ambience, the ElevenLabs dialogue synthesizer, the mix,
     and the cut.
   - Add the audio verifiers.
   - Build the player.
4. **Reconcile and auto-approve.**
   - Build the small-model reconcile tier, `reconcile-judge` and `reconcile_policy`.
   - Add the debounced auto-reconcile plan after a story save.
   - Add the change history with undo.
5. **Promotion.**
   - Add the promote RPC and console button. This is missing for import too.
   - Add `MinSources`.
   - Watch `prop-sheet`, `appearance` and `prop-state` move to small models, and the two chained
     kinds move toward `Codified`.
6. **Real images.**
   - Add a real `IFrameRenderer` and reference rendering, and `frame-consistency`.

## 11. Decisions

These were open questions in earlier drafts. Each is now settled, and the sections cited apply it.

| Question | Decision | Why | Where |
|---|---|---|---|
| Where do delivery tags live? | In `cues.performance`, never in `SceneElement.Text` | Tags in the text would break mention offsets and `story-fidelity`, and would show in the screenplay. `performance-fidelity` keeps the cue's words identical to the line | 5.1 A0, 8.4 |
| Refresh stale inputs, or build on what exists? | Refresh them first, unless they are pinned. Missing inputs are always built | An item built on a stale input is only valid until that input catches up, so it would be paid for twice. Early cutoff and the per-wave re-check make the refresh cheap when nothing really changed | 2.3 |
| Default scope | Library actions use `Item`. Scene actions use `ItemAndDownstream` | A sheet's downstream spans the story, so regenerating it should be deliberate. A scene action means "make this scene right", audio and frames included | 2.3 |
| Narrate action lines? | No. Dialogue uses the speaker's voice and Narration uses a narrator voice (`narrator/voice`). Action and Description lines become frames and room tone | Pictures carry the action. Narrating stage directions makes an audiobook of a screenplay and doubles the audio cost | 2.1, 5.1 A0 |
| Voice approval and pinning | Voices are reviewed by default: the author picks from the proposed voice and two runners-up, with samples. A voice pins itself on first audio use. Recasting means unpinning, and the plan shows the cost | A voice is the one choice a machine cannot verify, and the most expensive to reverse | 2.4, 4.4 |
| Review mode default | Review is chosen per kind with `review_kinds`, defaulting to `{voice, narrator-voice}`. Everything else is `Auto` | Other items are cheap to edit afterwards, and an edit is still a correction signal. Reviewing every scene up front is a chore with little gain | 2.4, 8.4 |
| Per-scene prop state | Yes: a `prop-state` item (`scene/{s}/prop/{p}`) shaped like an appearance, chained per prop, and not reset by the story day | A broken vase or a lit candle is exactly the continuity a reader notices. Most states carry forward, so the chain is cheap and an early `Codified` candidate | 2.1, 5.2 I2, 6, 8.3 |
| Batch size | Set per kind: sheets and voices up to 8; story-wide kinds 1; appearances, prop states and generator kinds one scene's worth; direction 1 scene | Small sheets share context cheaply. A scene direction needs the whole context for one scene | 2.3 |
| Director edits to a line's words | Change the story and the cue together, and resolve themselves | The author sees the tags while typing, so the edit already contains their decision | 9.1 |
| Tracking author edits inside a direction | Per row (`edited` on cues and frames). Items with one row per item pin instead | Pinning a whole scene's direction over one tag would block every later fix to that scene | 2.4, 9.1 |
| How discrepancies are stored | Not at all. Hunks are derived from kept item values, and resolving one moves the baseline forward per element | Nothing can drift out of sync, and partial review survives leaving the page | 9.2, 9.3 |
| Reconcile default | `Auto`: apply when verifiers pass and `reconcile-judge` scores at least 0.8; otherwise propose. Every apply can be undone | Most edits are small and anchor transfer handles them without a model. The judge catches edits whose meaning flipped | 9.5 |
| Does auto-reconcile regenerate media? | No. It stops at director items; audio and images wait for an explicit publish | Regeneration costs money and changes what a reader hears | 9.5 |

## References

- [ElevenLabs Text to Dialogue](https://elevenlabs.io/docs/overview/capabilities/text-to-dialogue): inputs, 2,000-character limit, seed
- [Create dialogue with timestamps](https://elevenlabs.io/docs/api-reference/text-to-dialogue/convert-with-timestamps): `voice_segments`, alignment, `previous_request_ids`
- [Eleven v4 audio tags](https://elevenlabs.io/blog/elevenlabs-audio-tags-list) and [best practices](https://elevenlabs.io/docs/overview/capabilities/text-to-speech/best-practices): free-form tags, no SSML breaks on v4/v3

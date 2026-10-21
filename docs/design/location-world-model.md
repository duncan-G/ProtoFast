# Location world model

*How to model what a location contains, how it changes, how it relates to other
locations, and which part of it each beat happens in, learned from the beats of
the story.*

> **Status (2026-10-11).** Built on the document-import pattern: a queue, a consumer and a
> ledger.
>
> - tables of §9 in the `plot` schema (migration `LocationWorldModel`), plus the ledger:
>   `world_model_builds` (one per request: status, scenes done, cost, counts) and
>   `world_model_build_steps` (seed, one row per scene read with the prompt sent, the reply,
>   tokens and cost, finish);
> - an operator starts or stops a build from the story's world page in theplot's console
>   (`TheplotWorldModels` StartBuild/CancelBuild, run as the story's writer), which queues it on
>   the SQS queue `protofast-world-model-build`; writers have no trigger;
> - the document-import worker drains it (`WorldModelBuildConsumer` → `WorldModelBuildRunner`):
>   scenes in one location in story order, locations side by side; a redelivery resumes at the
>   first scene not read. The fold of §5 and the rules of §4 and §7 are code in
>   `ProtoFast.Data/ThePlot/WorldModel`, shared by the worker and the api;
> - theplot's console lists every build and its steps at `/world-models` (`TheplotWorldModels`),
>   and shows a story's world at `/stories/:id/world` (`GetWorld`): a timeline of scenes and
>   beats, the place at the playhead with what each beat changed, a map of every place, the
>   cast, the conflicts, and each node's history.
>
> - each scene's views (§6) are pictured on demand from that page (`PictureView`, Muse) and kept
>   in `view_pictures`, the bytes in the object store;
> - the build ends by describing every location the story goes to (§7), one step each.
>
> Author corrections (§7) have rows reserved (`Author` origins) but no UI yet; the push-back of
> fixtures (§4) always applies, since nothing records that a beat put a fixture in place.

## 1. What we know and what we don't

- **Known:** every location's name, the location of each scene, and the order of
  scenes and beats.
- **Unknown:**
  - what's in each location;
  - how it changes over time;
  - how it relates to other locations;
  - which part of it a beat happens in.

The model is a **temporal knowledge graph**:

- Places and objects are **nodes**.
- Relationships are **edges**.
- Each edge holds over a stretch of story time and is backed by the beats that
  say so.

## 2. Nodes

| Node         | Comes from      | Examples                                   |
| ------------ | --------------- | ------------------------------------------ |
| **Location** | The heading (known) | TRAUMA WING, PARKING LOT               |
| **Part**     | Beats           | start of the hallway, trauma bay           |
| **Object**   | Beats           | double doors, gurney, blood on the floor   |
| **Character** | The story (known) | BRYAN                                  |

A **place** is a Location or a Part of one.

An Object's kind decides whether it lasts beyond its scene:

| Kind       | Examples                      | Lasts until               |
| ---------- | ----------------------------- | ------------------------- |
| Fixture    | doors, windows, a bar counter | a beat changes it         |
| Prop       | gurney, cooler                | a beat moves or removes it |
| Dressing   | blood, smoke, broken glass    | a beat cleans or ends it  |
| Occupant   | nurses, a crowd               | the end of the scene      |

## 3. Edges

| Edge           | From → To            | Changes over time? |
| -------------- | -------------------- | ------------------ |
| `part_of`      | Part → place         | No                 |
| `next`         | Part → Part          | No. Orders the parts of a linear space: hallway start → middle → end |
| `inside`       | Location → Location  | No                 |
| `connects`     | place ↔ place, optionally through an Object (a door) | Yes |
| `at`           | Object → place, held by a character or not | Yes |
| `state`        | Object → value (`lit = flickering`) | Yes  |
| `character_at` | Character → place    | Yes                |
| `beat_at`      | Beat → place         | Yes                |
| `camera`       | Beat → place, looking toward a place | Yes, within its scene |

```mermaid
graph LR
    subgraph Wing["TRAUMA WING"]
        S["hallway: start"] -->|next| M["hallway: middle"] -->|next| E["hallway: end"]
        Bay["trauma bay"]
    end
    Lot["PARKING LOT"]

    Doors(["double doors"]) -.->|at| S
    Gurney(["gurney"]) -.->|at| M
    Light(["light: flickering"]) -.->|at| E
    E -->|connects| Bay
    Wing -->|connects| Lot
```

### What changes a place

A place changes physically when one of its Fixtures or Dressing appears, moves,
goes or changes state, or when a way in or out opens or closes. Characters,
Props and Occupants are the action in a place, not a change to it: the place's
history is only its physical changes.

## 4. Time

Story time is a position in the story: scene order, then beat order. Each fact
starting at a beat holds until a later fact on the same subject and edge
replaces it. If nothing replaces it, it carries forward, across scenes too.
Occupants are the exception, because their facts end with their scene.

A held object moves with its holder through the scene and is put down where they
are when it ends. The next scene picks it up again only by saying so.

Fixtures and Parts reach back as well: one first mentioned late was there from
the first scene set in its place, unless a beat says it was put there.

Unknown is not false. An Object is gone only when a beat says it left or was
removed.

## 5. Evidence and facts

Each beat gives **observations**, small claims taken from its text:

> *She shoves through the double doors. A gurney blocks half the hallway; the
> light at the far end flickers.*

| Observation                                   | Stated or implied |
| --------------------------------------------- | ----------------- |
| double doors `at` hallway start               | stated            |
| gurney `at` hallway middle                    | stated            |
| light `at` hallway end, `lit = flickering`    | stated            |
| start `next` middle `next` end                | implied           |
| this beat `at` hallway start                  | implied           |

Building facts from observations takes three steps:

1. **Match names.** "the doors", "the double doors" and "the swing doors" are
   the same Object. "the far end" is the hallway's end Part.
2. **Order** each subject's observations for an edge by story time.
3. **Walk** them. Each observation does one of three things:
   - It **confirms** the current fact.
   - It **changes** the fact. The old fact ends at this beat and a new one starts.
   - It **conflicts** with the fact: it disagrees, and no beat in between explains
     the change. An author's statement wins. Otherwise the stated observation
     beats the implied one.

   A later beat explains a change: characters walk, props are carried, the action
   moves on and states change. So a later beat always changes the fact, and only
   two kinds of disagreement conflict: a Fixture's place, which no beat moves
   without saying so, and two observations of the same beat.

Facts are always rebuilt from observations, so editing a beat takes back what it
caused.

## 6. Where a beat happens

- The heading gives the Location.
- The Part is a `beat_at` fact. It carries forward like any other fact.
- When two characters are in different parts, `character_at` records each one.

A scene that walks down the hallway reads as beats 1–3 at the start, 4–7 in the
middle and 8 at the end, without tagging every beat.

### Where the camera stands

Every beat gets a `camera`: the place the camera stands, and the place it looks
toward. It is inferred from where the beat happens and who moves:

- The camera stands in a Part from which the action is seen. A Part is added when
  none fits.
- It moves along a linear space as the characters do: "hallway: start" while
  Bryan comes through the doors, "hallway: middle" as he passes the gurney.
- It looks toward where the action is or is heading. A character walking toward
  the camera is filmed from where they are going, looking back where they came
  from.
- It holds while the action stays put, and on dialogue it stays where it sees
  the speaker.

The camera is a choice per beat, not a claim, so each beat's camera replaces the
last one without conflict. It does not carry across scenes: a scene's first
camera covers its opening, and its last one ends with the scene.

### Views

A **view** is one place a scene's camera stands, with the place it looks
toward. Every beat filmed from there shares it, so a scene that cuts between a
shot and its reverse has two views however often it cuts.

The world pictures a view as the place itself, not as what happens in it: the
set before anyone steps in. A view's picture shows the location's parts, its
Fixtures and Dressing with their states and its ways out, as they stand at the
view's first beat, in the light of the scene's time of day. Characters, Props,
Occupants and the beats' action are left out; they are placed in the set later.

A physical change to the place between two beats filmed from the same camera
starts a new view of it: the counter seen before the window breaks and after are
two pictures, the second drawn from the first with the change made. A change
before the camera's first beat, or one that is only action, starts none.

Every view of a place is drawn from the pictures already made of it, so the
counter seen from the door and the door seen from the counter are the same
diner. A view pictures the place as its location is described (§7), then as it
stands at the view's first beat. A picture is kept with its view, and stays with it through a rebuild as
long as the view's places keep their names.

## 7. Building the graph

The story is read once, in order, one scene at a time.

- **A language model reads the text.** It finds entities, links them to nodes
  or adds new ones, and returns the edges each beat gives.
- **Code applies the rules:** time, carrying forward, ending occupants,
  connections between scenes and pushing back.
- **The author corrects anything.** A correction wins over everything else.

### Seed

- Add one Location per heading name and one node per character.
- Clear what the last build read: its observations, its `inside` edges, and the
  Parts and Objects nothing else refers to. Every scene is read again, and a scene
  must open on what this build has read, not on an older reading of scenes it has
  not reached.
- When a heading nests names (HOSPITAL – TRAUMA WING), add `inside`.

### Read each scene (model)

Each scene gets its own context window, containing:

- every Location name and every character. That is how the model tells a Part
  ("the lobby") from a Location, and how it can link to places no scene has
  reached yet;
- the scene's beats, each labelled with its id;
- the graph of the scene's location as the scene opens: the Locations it is
  inside or that are inside it, Parts, Objects with aliases and states, and
  connections;
- the Props last seen elsewhere, so a carried prop links to its node;
- where each character was last seen, and what they were carrying, which they
  still have only when the scene picks up straight after theirs. A scene read as
  a flashback (set earlier than the scene before it) is not where anyone was
  last seen;
- how the scenes before and after it end and begin, and the fixtures of the
  locations around it;
- each dialogue's extension: a V.O. line says nothing about where its speaker
  is, and code places no one by their own V.O. line;
- the cuts inside the scene. An insert (a close view of a page or a screen) is
  not a place.

The model answers beat by beat, and a worked example shows it a whole scene, so
that no beat goes without its camera or a moving character.

For every beat, the model does three things.

1. **Finds entities:** places and positions, objects and characters.
2. **Links each entity to an existing node, or adds one:**
   - An unknown position becomes a new Part under the current place, named or
     unnamed ("a room off the hallway"). The positions along a linear space are
     named "hallway: start", "hallway: middle"; only they are ordered by `next`.
   - An unknown object becomes a new Object, with a kind chosen from how the text
     uses it.
   - A new name for a known Object becomes one of its aliases. It links only to
     the same physical thing: Fixtures, Dressing and Occupants only to ones
     already in the place, a Prop anywhere it could have been carried from. Doors
     in different places, or a name tag that belongs to someone else, are
     different Objects. No two Objects share an alias. Code links a new name only
     to Objects already in the place or carried in by the scene's characters.
   - A part of a vehicle (a car's hood) is the vehicle's state, not an Object.
   - A place name never creates a Location.
   - A place name that is a Location links to it, even in a scene set elsewhere:
     "the lobby" of a HOSPITAL scene is LOBBY, `inside` HOSPITAL. Code holds to
     this: a Part named like a Location is that Location, and one named after it
     ("lobby: counter") is a Part of it.
   - A name like the heading's ("the mountain road" under SNOWY MOUNTAIN ROAD)
     is the heading's Location.
   - When unsure, it links rather than adds.
3. **Returns edges.** Each edge carries its beat id and whether it is stated or
   implied:

| The text…                                              | Edge           |
| ------------------------------------------------------ | -------------- |
| puts an object somewhere ("a gurney blocks the hallway") | `at`         |
| describes an object ("the light flickers")             | `state`        |
| shows a way to get directly from one place to another ("the doors to the trauma bay") | `connects` |
| orders positions ("halfway down", "the far end")       | `next`         |
| puts one place within another for good (a trauma wing reached from a hospital lobby, a ship's cockpit); never a vehicle inside a place | `inside` |
| moves a character ("she walks to the end"), or has them act at the scene's first beat | `character_at` |
| takes, carries, hands over or puts down an object      | `at`, with its holder |
| happens at a position                                  | `beat_at`      |
| every beat: where the camera stands and looks (§6)    | `camera`       |

Seeing a place is not a way to it, so "through the window, the lot" gives no
`connects`.

### Apply (code)

Each edge goes through the walk in section 5: it confirms, changes or conflicts
with the current fact.

### Close the scene (code)

- Move each held Object with its holder, beat by beat.
- End every Occupant's edges.
- If the next scene follows straight on (its heading says CONTINUOUS or MOMENTS
  LATER) and picks a character up somewhere else, add `connects` between the two
  places. A cut in time or a drive is no way through, and the door used is not
  known, so none is given.

### Order

- A scene runs after every earlier scene in its location or with one of its
  characters, because it starts from the graph those left: what is in the place,
  where its characters were last seen and what they carry.
- Scenes with nothing in common run in parallel.

### Finish the story (code)

Push Fixtures and Parts back to the first scene set in their place. Props,
Dressing, Occupants and characters are not pushed back.

An interior a beat walks into straight from a nested interior is nested in the
same place: the landing a flat opens onto is in the flat's building. One entered
straight from an interior that already holds rooms is one of its rooms.

### Describe each location (model)

Once the graph is finished, the model describes each location the story goes
to, in its own context window: what the place is and what is in it, as the
story first finds it. A script says little about its places, so the
description is inferred from the whole story (its period and region, who uses
the place and how, what happens there) and agrees with everything the beats and
the graph say.

- **description**: the kind of place, era and region, size and layout with its
  parts, architecture and materials, condition, light and atmosphere.
- **contents**: what is built into it and dressed on it, part by part: the
  known Fixtures and Dressing, then what such a place in this story holds.

Characters, Props, Occupants and events are left out, and so are the physical
changes the story makes later: those stay facts in the graph. The context holds
the story in outline, the location with its parts, the Fixtures and Dressing as
first found and its ways, its later changes (to leave out), the beats set
there and the lines elsewhere that name it. An outer place is described first,
and its description is given to the places inside it.

### After an edit

Read only the edited scene again, replace its observations, and rebuild the
facts.

## 8. Example

| Beat       | Text                                           | Facts after it                                       |
| ---------- | ---------------------------------------------- | ---------------------------------------------------- |
| S2 heading | INT. TRAUMA WING – NIGHT                       | scene at TRAUMA WING                                 |
| S2 b1      | Bryan shoves through the double doors.         | doors `at` start; Bryan `at` start; beat `at` start  |
| S2 b3      | A gurney blocks half the hallway.              | gurney `at` middle; beat `at` middle                 |
| S2 b6      | At the far end, a light flickers over the trauma bay doors. | light `at` end, flickering; end `connects` trauma bay; beat `at` end |
| S5 heading | INT. TRAUMA WING – DAY                         | doors, gurney and flickering light carry forward     |
| S5 b2      | An orderly wheels the gurney away.             | gurney no longer `at` middle                         |
| S5 b4      | The light is steady now.                       | light `lit = steady`                                 |

At S5 b1 the hallway's end has the light, still flickering, and the doors to the
trauma bay. At S5 b3 nothing is known in the middle of the hallway.

## 9. Data model (Postgres)

The graph lives in ordinary tables next to the existing ones (`locations`,
`characters`, `props`, `scenes`, `scene_elements`).

- **Nodes are rows.**
- **Timeless edges are columns.**
- **Edges that change over time are rows in one observations table, each tied to
  its beat.**

Facts are not stored. Code folds them from the observations (section 5).

| Graph                          | Stored as                                         |
| ------------------------------ | ------------------------------------------------- |
| Location, Character            | existing `locations`, `characters`                |
| Part                           | `location_parts`                                  |
| Object                         | `location_objects`                                |
| `part_of`                      | `location_parts.location_id` + `parent_part_id`   |
| `next`                         | `location_parts.position`                         |
| `inside`                       | `locations.inside_location_id`                    |
| `at`, `state`, `connects`, `character_at`, `beat_at`, `camera` | `location_observations` rows (a held `at` sets `character_id`) |

A place is written as two columns: `…_location_id` (required) and `…_part_id`
(null for the whole location).

### `locations` (new columns)

| Column             | Type  | Notes                               |
| ------------------ | ----- | ----------------------------------- |
| inside_location_id | uuid? | `inside`. Set null when that location is deleted |
| description        | text? | What the place is, as the build describes it |
| contents           | text? | What is built into it and dressed on it      |

### `location_parts`

Owned by its Location, so it has no `user_id`. Deleting the location deletes its
parts.

| Column         | Type   | Notes                                                   |
| -------------- | ------ | ------------------------------------------------------- |
| id             | uuid   |                                                         |
| location_id    | uuid   |                                                         |
| parent_part_id | uuid?  | `part_of` another part. Null when directly under the location |
| name           | text?  | Null for an unnamed part ("a room off the hallway")     |
| description    | text?  |                                                         |
| position       | int?   | `next`: order among sibling parts of a linear space     |
| origin         | enum   | `Extracted`, `Author`                                   |

### `location_objects`

Top level, because it is listed on its own (a location's contents, an object's
history), so it carries `user_id`.

| Column      | Type   | Notes                                             |
| ----------- | ------ | ------------------------------------------------- |
| id          | uuid   |                                                   |
| user_id     | text   |                                                   |
| story_id    | uuid   |                                                   |
| name        | text   | Canonical name                                    |
| aliases     | text[] | Other names the beats use ("the doors")           |
| kind        | enum   | `Fixture`, `Prop`, `Dressing`, `Occupant`         |
| prop_id     | uuid?  | Set when it is a story Prop. Set null on delete   |
| description | text?  |                                                   |

### `location_observations`

Owned by its beat (`scene_elements`), so it has no `user_id`. Deleting or
re-reading a beat replaces its rows.

| Column             | Type   | Used by                                      |
| ------------------ | ------ | -------------------------------------------- |
| id                 | uuid   |                                              |
| scene_element_id   | uuid   | all: the beat it came from                   |
| edge               | enum   | `At`, `State`, `Connects`, `CharacterAt`, `BeatAt`, `Camera` |
| object_id          | uuid?  | `At`, `State`                                |
| character_id       | uuid?  | `CharacterAt`; the holder of an `At`         |
| place_location_id  | uuid?  | `At`, `CharacterAt`, `BeatAt`, `Camera`, one end of `Connects` |
| place_part_id      | uuid?  | as above                                     |
| other_location_id  | uuid?  | the other end of `Connects`; where a `Camera` looks |
| other_part_id      | uuid?  | as above                                     |
| via_object_id      | uuid?  | `Connects`: the way through                  |
| property           | text?  | `State` ("lit")                              |
| value              | text?  | `State` ("flickering"); `Connects`: `closed` ends the connection |
| basis              | enum   | `Stated`, `Implied`                          |
| origin             | enum   | `Extracted`, `Rule` (cross-scene `connects`), `Author` |

An `At` row with no place means the object left or was removed.

Check constraints, one per edge, set exactly the columns that edge uses:

| Edge          | Required                                             | Null                       |
| ------------- | ---------------------------------------------------- | -------------------------- |
| `At`          | object_id (place optional; required with a holder)   | other place, via, property, value |
| `State`       | object_id, property, value                           | places, character, via     |
| `Connects`    | place_location_id, other_location_id                 | object, character, property |
| `CharacterAt` | character_id, place_location_id                      | object, other place, via, property, value |
| `BeatAt`      | place_location_id                                    | object, character, other place, via, property, value |
| `Camera`      | place_location_id (other place optional)             | object, character, via, property, value |

Indexes: `scene_element_id`, `object_id`, `character_id`, `place_location_id`.

### `view_pictures`

Fetched on its own, so it carries `user_id`. Parts are kept by name, because a
build makes its parts anew.

| Column             | Type   | Notes                                            |
| ------------------ | ------ | ------------------------------------------------ |
| id                 | uuid   |                                                  |
| user_id, story_id  |        |                                                  |
| scene_id           | uuid   | Deleting the scene deletes its pictures          |
| camera_location_id | uuid   |                                                  |
| camera_part        | text?  | The part's name after its parents'; null for the whole location |
| toward_location_id | uuid?  | Null when the camera looks toward nothing in particular |
| toward_part        | text?  | as `camera_part`                                 |
| state              | int    | How many physical changes came between this camera's earlier beats and these |
| storage_key        | text   | `stories/{storyId}/views/{sha256}.{ext}`         |
| prompt, notes      | text   | What the image model was asked, and its notes    |

One row per view: unique on scene, both ends and state, nulls included.

### Reading the graph

One query loads a story's observations joined to their beats' positions,
ordered by container, scene and beat position. The fold runs over that list.
Reordering scenes or beats changes only the positions, never the observations.

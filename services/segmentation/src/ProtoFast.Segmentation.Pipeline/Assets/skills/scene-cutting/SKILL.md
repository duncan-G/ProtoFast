# Cutting scenes

A **scene** is the maximal contiguous run of items over which one *situation* holds. A situation
is five coordinates: where, when, who, how it is delivered, and what is going on.

Code has already derived two of them from the items — **mode** and **cast** — and proposed the
boundaries those imply. You confirm or reject those boundaries, and you assign the two coordinates
that genuinely need judgement: **setting** and **subject**.

## Maximal, and contiguous

A scene is extended until a coordinate changes. **You never cut for length, tidiness, or topic
drift the coordinates do not register.**

The test: *could a stage crew keep the same set, the same people on it, and the same thing going
on, and just continue?* If yes, no cut — however long the run. If no, cut.

## Which coordinates cut, in which mode

| Mode | Cuts on | Does not cut on |
|---|---|---|
| `Enacted` | Setting, Time, Cast | Subject |
| `Narrated` | Setting, Time, Subject | Cast (normally constant) |
| `Expounded` / `Addressed` | Subject, Cast | Setting (Void throughout) |
| `Exhibited` | Subject — one artifact is one scene | Setting, Time, Cast |

A **mode change is always a cut**.

Two things follow, and both are mistakes worth naming:

- **Cast is the set of people present, not the current speaker.** Alternation between speakers
  does not change the set and therefore does not cut. A scene ends when someone *joins or leaves*.
  Without this rule a dialogue shreds into one scene per turn.
- **A four-hundred-paragraph lecture is not one scene.** Subject is live in `Expounded`, so it
  cuts at topic changes.

## Setting

`placeId` is a place from the registry shown to you, or empty.

**Empty means Void, and Void is the default.** A scene is located only when the text locates it.
Do not infer a place from what would make sense; do not name a place the registry does not hold.
An unlocated scene is an ordinary answer — for a lecture it is the *right* answer.

## Subject

One sentence, in your own words, saying what is happening or being treated. In `Enacted` and
`Narrated` it is the event; in `Expounded` and `Addressed` it is the topic; in `Exhibited` it is
what the apparatus shows.

## Granularity: treated versus listed

> **If the text treats an item, the item is a scene. If the text lists items, the list is a scene.**

A worked example discussed one at a time in the flow of exposition is a scene — the subject *is*
that example. A problem set of thirty exercises is **one** `Exhibited` scene, because the subject
is "the exercises". The same rule governs glossaries, bibliographies, data tables and catalogues.

## Boundaries

For each proposed boundary, `keep: true` or `keep: false` with a short reason.

A boundary marked `marked` came from an explicit transition or a structural break. **It is
evidence, and you cannot reject it** — a marked one-sentence cut stands.

Where coordinates disagree about *where* a boundary sits — a speaker leaves several items before
the location changes — put it at the **earliest** item at which the new situation is fully in
effect. Scenes are maximal backwards, not forwards.

## Evidence

Every scene cites the item or paragraph ids that justify its coordinates, and only ids you were
shown. Uncitable equals unknown.

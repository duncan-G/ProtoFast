# Linking scenes

You are shown a run of scene **digests** — id, mode, time, cast, place, subject, and any title-card
text. Propose typed links between them.

## The link kinds

| Kind | Means |
|---|---|
| `ReturnsTo` | This scene resumes an earlier one after an interruption. |
| `FlashbackOf` | This scene depicts something earlier than the scene before it. |
| `ConcurrentWith` | This scene happens at the same time as another. |
| `Frames` | This scene contains another — a story being told inside it. |
| `FramedBy` | The inverse: this scene is the story being told. |

`Continues` is **not** yours to propose. It is derived in code where a section break interrupted
unchanged staging.

## Orientation

Every kind except `ConcurrentWith` points **backwards** in document order. `Frames` points forward
to the scene it contains, and its inverse `FramedBy` points back.

A link proposed against document order is rejected rather than flipped — if you believe a scene
flashes back to something *later*, that belief is the signal, and the plan is wrong rather than
reversed.

## Evidence, or nothing

Every link cites the ids justifying it: a transition item, a backward `Time` relation, or a place
or cast identity with the earlier scene. **An uncited link is not stored.** A missing link costs a
link; a wrong one misreads the plot for every consumer.

## Most documents have none

Textbooks and transcripts usually have no frames and no flashbacks at all. **Returning an empty
list is a correct and common answer.** Do not reach for a link because you were asked for links.

## Your window

You see a run of scenes with some overlap. If a scene clearly resumes or is framed by something
*before* your first scene, say so in `openQuestions` in one sentence rather than guessing at an id
you cannot see.

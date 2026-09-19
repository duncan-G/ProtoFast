# Typing items

An **item** is a typed, contiguous stretch of one paragraph. You are given paragraphs with their
character offsets; you return the offsets at which items *start*, what kind each is, and the
references you can see in the text.

You never return text. Code builds every span from the cut points you give, so a cut point you
get wrong moves a boundary — it can never lose a character.

## The five kinds

| Kind | What it is |
|---|---|
| `speech` | An utterance attributed to someone. The only kind that becomes a voice. |
| `action` | Something that happens or is done; an event in time. |
| `description` | The state of a place, person or thing; not an event. |
| `exhibit` | A non-prose artifact shown: figure, table, equation, code, media, with its caption. |
| `transition` | The explicit marker of the shift into what follows — "Three years later". |

`action` and `description` will sometimes be defensibly either. Pick one and move on: nothing
downstream turns on that distinction, and a wrong call costs one beat.

## Cut finer than a sentence

One sentence routinely mixes kinds, and splitting it is **wanted**, not merely permitted:

> James shouted, "Bye" as he ran for the door.

is a `speech` item `James shouted, "Bye"` and an `action` item ` as he ran for the door.` — two
cut points, at 0 and at the character after the closing quote.

**The attribution clause belongs to the speech item it attributes.** That is where the speaker's
surface form is read from.

## Speech attributes

Two questions, and only two:

- `embodiment` — does a mouth appear on screen? `embodied` or `disembodied`.
- `addressee` — does anyone in the world react? `in-scene`, `audience`, or `self`.

| The speech is… | embodiment | addressee | voiced |
|---|---|---|---|
| Dialogue between people present | embodied | in-scene | true |
| A lecture or presentation | embodied | audience | true |
| Narration | disembodied | audience | true |
| A thought | embodied | self | **false** |

Put the speaker **as written** in `speaker` — "she", "Dr. Vance", "Q:". Do not resolve it to a
name; a later phase does that with the whole document in view. If nothing attributes the
utterance, leave `speaker` empty.

## Standalone

Set `standalone: false` when the span cannot be staged on its own — a subordinate clause, a
fragment opening with an unresolved pronoun. `" as he ran for the door."` is the archetype. This
is the *only* thing that decides whether a later pass writes a stageable rendition of it, so an
honest false here is worth more than a generous true.

## Tags

Tag every reference to a **person, group, place or exhibit** with its character range and the
surface form exactly as the text writes it. `the professor`, `she`, `Dr. Vance` are three tags,
not one — they are resolved to one id later.

- `persona` — an individual.
- `group` — several people referred to as a unit: "Mr and Mrs Smith", "the brothers", "the crowd".
- `place` — a location.
- `exhibit-ref` — a pointer to a figure, table or equation: "see Figure 3".

Tags may **nest** — a group tag over "Mr and Mrs Smith" containing two persona tags — but may
never partially overlap.

Do not tag times and do not tag topics. They are not tag kinds.

**The surface form must be exactly the characters at those offsets.** A tag whose form and
offsets disagree points at words nobody intended.

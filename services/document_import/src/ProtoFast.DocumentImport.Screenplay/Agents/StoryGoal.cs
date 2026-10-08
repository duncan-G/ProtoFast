using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Screenplay.Verifiers;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

public static class StoryGoal
{
    public static readonly DiscoveryGoal Goal = new(
        StoryStages.StoryStage,
        StoryStages.StoryContract,
        [
            new VerifierSpec(
                StoryDraftVerifier.VerifierId, StoryStages.StoryStage,
                "The story has a title, characters and scenes; every scene opens with a heading; speakers and locations are library names."),
            new VerifierSpec(
                StoryFidelityVerifier.VerifierId, StoryStages.StoryStage,
                "The elements' text is the manuscript's own wording, split into elements, and the whole manuscript is covered; nothing is paraphrased, summarised or added."),
        ],
        """
        Import the input manuscript - a screenplay, novel, short story or any prose - as it is written
        into a screenplay story document: stage `story`, contract `{ "schemaId": "story-draft", "version": 1 }`,
        one JSON object of this shape:

        ```json
        {
          "title": "the work's title, or a fitting one if none is given",
          "characters": [{ "name": "...", "kind": "a character kind", "description": "one sentence" }],
          "locations": [{ "name": "...", "setting": "Interior|Exterior", "description": "one sentence" }],
          "props": [{ "name": "...", "description": "one sentence" }],
          "containers": [{
            "label": "Act I",
            "scenes": [{
              "title": "short scene title",
              "elements": [
                { "type": "Heading", "location": "a location name", "timeOfDay": "a time of day" },
                { "type": "Action", "text": "the manuscript's words for what happens" },
                { "type": "Description", "text": "the manuscript's words for what something looks, sounds or feels like" },
                { "type": "Narration", "text": "the manuscript's words in the narrator's voice" },
                { "type": "Dialogue", "speaker": "a character name", "extension": "optional, e.g. V.O.", "parenthetical": "optional", "text": "the spoken words" },
                { "type": "Transition", "transition": "a transition" }
              ]
            }]
          }],
          "vocabulary": {
            "timesOfDay": ["times of day the manuscript uses beyond the defaults"],
            "transitions": ["transitions the manuscript uses beyond the defaults"],
            "extensions": ["cue extensions the manuscript uses beyond the defaults"],
            "characterKinds": [{ "label": "...", "avatarShape": "Circle|Square|Squircle|Teardrop|Pill|Diamond|Triangle|Pentagon|Hexagon|Octagon|Star|Shield" }]
          }
        }
        ```

        Times of day, transitions, cue extensions and character kinds are an open vocabulary, not a
        fixed list. Every story starts with these defaults:

        - times of day: DAY, NIGHT, DAWN, DUSK, CONTINUOUS, LATER
        - transitions: CUT TO, DISSOLVE TO, SMASH CUT TO, MATCH CUT TO, TIME CUT, FADE OUT
        - cue extensions: V.O., O.S., O.C.
        - character kinds: Human, Robot, Animal, Creature, Voice

        Use a default when it says what the manuscript says. When the manuscript uses one that is not
        on the list (MORNING, WIPE TO, FADE IN, IRIS OUT, FILTERED, Ghost, Alien), keep it as written
        rather than forcing it onto the nearest default, and add it to `vocabulary` so the story offers
        it from then on. Times of day and transitions are written in capitals without the trailing
        colon, and cue extensions in capitals without the parentheses; a new character kind is a short
        noun with an avatar shape that sets it apart from the defaults (Human and Voice are circles,
        Robot a square, Animal a teardrop, Creature a squircle). Leave
        `vocabulary` lists empty when the defaults cover the manuscript.

        This is an import, not an adaptation. The story's text is the manuscript's text, in the
        manuscript's order and tense, cut into elements:

        - Leave out only what is not story: title page, author and contact details, copyright, table
          of contents, dedications, notes to the reader, page numbers, running headers and footers,
          scene numbers, CONTINUED, CONT'D and MORE markers, and formatting markup.
        - A prose paragraph often mixes action, description, narration and dialogue. Split it into
          one element per kind, each carrying the manuscript's own words for that part. Quoted speech
          becomes a Dialogue element: the words inside the quotes are its text, the speaker named by the
          attribution (or, when unnamed, the character the manuscript implies) is its `speaker`, and
          the attribution tag itself ("she said", "he whispered") is absorbed by `speaker` or
          `parenthetical` rather than kept as text.
        - A split can leave a fragment ("slamming the door" once "Get out," Mara said has become
          dialogue). Repair such a fragment with the fewest words that make it a sentence, keeping the
          manuscript's wording. That is the only rewriting allowed: no paraphrase, no summary, no
          condensing, no added detail, no corrected or modernised prose, no change of tense or person.
        - Screenplay sources map directly: sluglines become Headings, character cues become
          `speaker`, a cue's extension (the V.O. in `MARA (V.O.)`) becomes `extension` and never
          stays in the speaker's name, parentheticals become `parenthetical`, action lines become
          Action, transitions become Transition. A screenplay uses only those four types:
          Description and Narration are for prose, so a screenplay's descriptive lines and asides to
          the reader stay Action.

        Build the story by slicing the manuscript, not by writing it out. Element text is never typed
        by a model, yours or a delegate's: a model retyping a passage drifts from it and is slow and
        costly on a long manuscript. Instead:

        - Code first cuts the manuscript into numbered units (lines, paragraphs, and within a
          paragraph the quoted speech and the narrative between quotes), dropping the non-story matter
          above wherever a pattern finds it. The manuscript arrives normalised: every apostrophe and
          single quote is a straight `'` and line breaks are `\n`, so code needs no handling of its own.
        - A model - you, or a delegate on a page at a time - only labels the units: which scene each
          starts, each scene's heading, each unit's element type, speaker, extension and
          parenthetical, and where a unit splits further. Its reply holds unit numbers and short
          anchors (the first few words of a split), never the passage.
        - Code assembles the story from those labels, copying each element's text from the manuscript
          character for character, and records it with `write-artifact` from inside the script so the
          text never passes through you.

        The only words a model writes are the title when the manuscript has none, the library
        descriptions, scene titles, new vocabulary entries, and the few words that repair a fragment a
        split leaves; keep those repairs as labels (unit, replacement) that the assembling code applies.

        Rules: names are unique within their list and spelled as the manuscript spells them, without
        cue extensions; every character who speaks is listed; props are the objects that matter to the
        plot; library descriptions are yours to write in one sentence each. Every scene opens with a
        Heading; element types are Heading, Action, Description, Narration, Dialogue and Transition;
        Heading and Transition carry no text, every other element does; speakers and locations are
        names from the lists above; times of day, transitions, cue extensions and character kinds are
        defaults or entries in `vocabulary`; scenes are grouped into acts or parts.

        Cover the whole manuscript, not its opening: an import that drops the second half is wrong.
        The `story-fidelity` verifier measures both - the share of the story's words that are the
        manuscript's and the share of the manuscript that reaches the story - and fails the stage when
        either is low, naming the rewritten elements and the missing passages.
        """);
}

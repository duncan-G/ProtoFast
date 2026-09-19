import { create } from '@bufbuild/protobuf';
import {
  SceneSchema,
  SceneItemSchema,
  SceneLinkSchema,
  SectionNodeSchema,
  SituationSchema,
} from '../../lib/gen/segmentation_pb';
import {
  itemLine,
  linkLine,
  sceneCards,
  sceneLabel,
  sectionPaths,
  situationOf,
  timeOf,
} from './scenes';

function scene(fields: Parameters<typeof create<typeof SceneSchema>>[1]) {
  return create(SceneSchema, fields);
}

describe('scene view model', () => {
  it('numbers an untitled scene by its document-wide ordinal', () => {
    // The ordinal is assigned before any section filter, so it is the label a reviewer can cite
    // whether the reader asked for one chapter or the whole document.
    expect(sceneLabel(scene({ ordinal: 0 }))).toBe('Scene 1');
    expect(sceneLabel(scene({ ordinal: 411 }))).toBe('Scene 412');
    expect(sceneLabel(scene({ ordinal: 3, title: 'The kitchen, after' }))).toBe(
      'The kitchen, after',
    );
  });

  it('reads Void as a claim rather than as a missing value', () => {
    // C6: every coordinate is always present. An empty place_id is "nowhere", not "unknown".
    const line = situationOf(create(SituationSchema, { mode: 'expounded' }));

    expect(line.place).toBe('no place');
    expect(line.placeInherited).toBe(false);
    expect(line.mode).toBe('expounded');
  });

  it('marks a carried setting and keeps an unresolvable one visible', () => {
    const carried = situationOf(
      create(SituationSchema, {
        placeId: 'pl_1',
        placeName: 'The kitchen',
        settingSource: 'inherited',
      }),
    );

    expect(carried.place).toBe('The kitchen');
    expect(carried.placeInherited).toBe(true);

    // A place the registry could not name still claimed a setting; showing the id beats hiding it.
    const unresolved = situationOf(
      create(SituationSchema, { placeId: 'pl_9', settingSource: 'stated' }),
    );

    expect(unresolved.place).toBe('pl_9');
    expect(unresolved.placeInherited).toBe(false);
  });

  it('phrases time from the anchor and the relation together', () => {
    expect(
      timeOf(create(SituationSchema, { timeAnchor: 'the next morning', timeRelation: 'gap' })),
    ).toBe('the next morning · after a gap');

    // Unanchored is the normal answer for expository material and is not a defect, so it says
    // nothing rather than saying "unanchored".
    expect(timeOf(create(SituationSchema, { timeRelation: 'unanchored' }))).toBe('');
    expect(timeOf(create(SituationSchema, { timeRelation: 'continuous' }))).toBe('continuous');
    expect(timeOf(create(SituationSchema, { timeAnchor: '1943' }))).toBe('1943');
  });

  it('prefers render text and says that it is staged', () => {
    // §3.6: a span that cannot be shown alone gets generated staging text. Which one the reader is
    // looking at has to be visible — one is the document, the other is ThePlot's.
    const staged = itemLine(
      create(SceneItemSchema, {
        itemId: 'it_1',
        kind: 'action',
        text: '— he said',
        renderText: 'Tom spoke.',
      }),
    );

    expect(staged.text).toBe('Tom spoke.');
    expect(staged.generated).toBe(true);

    const own = itemLine(
      create(SceneItemSchema, { itemId: 'it_2', kind: 'action', text: 'Tom crossed the room.' }),
    );

    expect(own.text).toBe('Tom crossed the room.');
    expect(own.generated).toBe(false);
  });

  it('carries the speaker and whether the line is aimed at the reader', () => {
    const narration = itemLine(
      create(SceneItemSchema, {
        itemId: 'it_3',
        kind: 'speech',
        text: 'Reader, she was gone.',
        speech: { speakerName: 'the narrator', addressee: 'audience', voiced: true },
      }),
    );

    expect(narration.speaker).toBe('the narrator');
    expect(narration.toAudience).toBe(true);
  });

  it('names a link by its target ordinal and shows confidence only for an inferred one', () => {
    const at = new Map([
      ['sc_a', 0],
      ['sc_b', 11],
    ]);

    const flashback = linkLine(
      create(SceneLinkSchema, {
        fromSceneId: 'sc_a',
        toSceneId: 'sc_b',
        kind: 'flashback_of',
        confidence: 0.82,
      }),
      at,
    );

    expect(flashback.label).toBe('a flashback of scene 12');
    expect(flashback.showsConfidence).toBe(true);
    expect(flashback.targetSceneId).toBe('sc_b');

    // Continues is derived in code at phase 10 (C12), so a figure would be noise.
    const continues = linkLine(
      create(SceneLinkSchema, {
        fromSceneId: 'sc_a',
        toSceneId: 'sc_b',
        kind: 'continues',
        confidence: 1,
      }),
      at,
    );

    expect(continues.label).toBe('continues into scene 12');
    expect(continues.showsConfidence).toBe(false);
  });

  it('disables a jump whose target is not on the page', () => {
    // A section-filtered read can hold the near end of a frame link and not the far one.
    const link = linkLine(
      create(SceneLinkSchema, {
        fromSceneId: 'sc_a',
        toSceneId: 'sc_z',
        kind: 'frames',
        confidence: 0.7,
      }),
      new Map([['sc_a', 0]]),
    );

    expect(link.label).toBe('frames a scene outside this view');
    expect(link.targetSceneId).toBe('');
  });

  it('paths a section by its whole trail', () => {
    const root = create(SectionNodeSchema, {
      sectionId: 'sec_root',
      title: 'The Novel',
      children: [
        {
          sectionId: 'sec_1',
          title: 'Part One',
          children: [{ sectionId: 'sec_1_1', title: 'Chapter 3' }],
        },
      ],
    });

    const paths = sectionPaths(root);

    expect(paths.get('sec_1_1')).toBe('The Novel › Part One › Chapter 3');
    expect(paths.get('sec_root')).toBe('The Novel');
    expect(sectionPaths(undefined).size).toBe(0);
  });

  it('groups scenes in the order their sections first appear, not in tree order', () => {
    // A scene names its section rather than the section naming its scenes, so reading order comes
    // from the stream. A section whose scenes are split by another section's keeps both runs.
    const root = create(SectionNodeSchema, {
      sectionId: 'sec_root',
      title: 'Doc',
      children: [
        { sectionId: 'sec_b', title: 'Second' },
        { sectionId: 'sec_a', title: 'First' },
      ],
    });

    const groups = sceneCards(
      [
        scene({ sceneId: 'sc_1', sectionId: 'sec_a', ordinal: 0 }),
        scene({ sceneId: 'sc_2', sectionId: 'sec_a', ordinal: 1 }),
        scene({ sceneId: 'sc_3', sectionId: 'sec_b', ordinal: 2 }),
      ],
      root,
    );

    expect(groups.map((g) => g.sectionId)).toEqual(['sec_a', 'sec_b']);
    expect(groups[0].path).toBe('Doc › First');
    expect(groups[0].scenes.map((s) => s.label)).toEqual(['Scene 1', 'Scene 2']);
    expect(groups[1].scenes).toHaveLength(1);
  });

  it('does not call a numbered scene inferred', () => {
    // title_inferred is a claim about a title the pipeline invented. A scene with no title at all
    // is numbered by this page, which is not the same claim.
    const [group] = sceneCards(
      [scene({ sceneId: 'sc_1', ordinal: 0, titleInferred: true })],
      undefined,
    );

    expect(group.scenes[0].label).toBe('Scene 1');
    expect(group.scenes[0].titleInferred).toBe(false);
  });
});

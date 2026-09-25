import { InMemoryStoryApi } from './in-memory-story-api';
import { segmentText } from './mentions';
import { SAMPLE_STORY_ID } from './sample-story';
import { Scene } from './model/scene';

describe('InMemoryStoryApi', () => {
  let api: InMemoryStoryApi;

  beforeEach(() => {
    api = new InMemoryStoryApi();
  });

  async function sceneTitled(title: string): Promise<Scene> {
    const story = await api.getStory(SAMPLE_STORY_ID);
    const summary = story.containers.flatMap((c) => c.scenes).find((s) => s.title === title)!;
    return api.getScene(summary.id);
  }

  it('seeds the sample story with its containers, library and linked mentions', async () => {
    const story = await api.getStory(SAMPLE_STORY_ID);
    expect(story.title).toBe('The Signal in the Scrap');
    expect(story.containers.map((c) => [c.label, c.scenes.length])).toEqual([
      ['Act I', 5],
      ['Act II', 1],
    ]);
    const scene = await sceneTitled('Bolt Wakes');
    const beat = scene.elements.find((e) => e.text?.startsWith('@Old Fen turns'))!;
    expect(beat.mentions.map((m) => beat.text!.slice(m.offset, m.offset + m.length))).toEqual([
      '@Old Fen',
      '@Brass Key',
    ]);
  });

  it('refuses a duplicate name in any case, but not across kinds', async () => {
    await expect(
      api.createCharacter(SAMPLE_STORY_ID, {
        name: 'mara',
        kind: 'Human',
        hue: 10,
      }),
    ).rejects.toThrow('There’s already a character called “Mara”.');
    const location = await api.createLocation(SAMPLE_STORY_ID, {
      name: 'Mara',
      setting: 'Interior',
      hue: 10,
    });
    expect(location.name).toBe('Mara');
  });

  it('rewrites mentions across scenes when an entry is renamed', async () => {
    const story = await api.getStory(SAMPLE_STORY_ID);
    const mara = story.characters.find((c) => c.name === 'Mara')!;
    await api.updateCharacter({ ...mara, name: 'Mara Voss' });
    const scene = await sceneTitled('Three Winters');
    expect(scene.elements[1].text).toBe(
      'Every night for three winters, @Mara Voss listened to static.',
    );
    expect(scene.elements[1].mentions[0].length).toBe('@Mara Voss'.length);
  });

  it('clears a deleted character from dialogue and unlinks its mentions, keeping the text', async () => {
    const story = await api.getStory(SAMPLE_STORY_ID);
    const mara = story.characters.find((c) => c.name === 'Mara')!;
    await api.deleteCharacter(mara.id);
    const scene = await sceneTitled('Bolt Wakes');
    expect(scene.elements.filter((e) => e.type === 'Dialogue' && e.speakerId === null).length).toBe(
      3,
    );
    const beat = scene.elements[2];
    expect(beat.text).toContain('@Mara climbs');
    expect(segmentText({ text: beat.text!, mentions: beat.mentions })[0]).toEqual({
      text: '@Mara climbs a heap of washing machines, the ',
      mention: null,
    });
  });

  it('clears a deleted location from headings', async () => {
    const story = await api.getStory(SAMPLE_STORY_ID);
    const docks = story.locations.find((l) => l.name === 'Harbor Docks')!;
    await api.deleteLocation(docks.id);
    const crossing = await sceneTitled('The Crossing');
    expect(crossing.elements[0].locationId).toBeNull();
    const farShore = await sceneTitled('The Far Shore');
    const beat = farShore.elements[1];
    expect(beat.mentions.map((m) => beat.text!.slice(m.offset, m.offset + m.length))).toEqual([
      '@Mara',
    ]);
    expect(beat.text).toContain('@Harbor Docks');
  });

  it('creates a story opening on one untitled scene with an empty heading', async () => {
    const story = await api.createStory('  The Far Shore ');
    expect(story.title).toBe('The Far Shore');
    expect(story.containers.map((c) => [c.label, c.scenes.map((s) => s.title)])).toEqual([
      ['Act I', ['Untitled scene']],
    ]);
    const scene = await api.getScene(story.containers[0].scenes[0].id);
    expect(scene.elements.map((e) => [e.type, e.locationId, e.timeOfDay])).toEqual([
      ['Heading', null, null],
    ]);
    await expect(api.createStory('  ')).rejects.toThrow('A story needs a title.');
  });

  it('lists stories most recently modified first, and renames and deletes them', async () => {
    const story = await api.createStory('The Far Shore');
    await new Promise((resolve) => setTimeout(resolve, 5));
    await api.updateStory(SAMPLE_STORY_ID, 'The Signal');
    expect((await api.listStories()).map((s) => [s.id, s.title])).toEqual([
      [SAMPLE_STORY_ID, 'The Signal'],
      [story.id, 'The Far Shore'],
    ]);
    const sceneId = story.containers[0].scenes[0].id;
    await api.deleteStory(story.id);
    expect((await api.listStories()).map((s) => s.id)).toEqual([SAMPLE_STORY_ID]);
    await expect(api.getScene(sceneId)).rejects.toThrow('That scene could not be found.');
  });
});

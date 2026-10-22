import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { InMemoryStoryApi } from '../../stories/in-memory-story-api';
import { StorySpot } from '../../stories/model/story-spot';
import { SAMPLE_STORY_ID } from '../../stories/sample-story';
import { StoryApi } from '../../stories/story-api';
import { SceneEditorStore } from './scene-editor-store';

describe('SceneEditorStore', () => {
  let api: InMemoryStoryApi;
  let navigations: unknown[][];
  let store: SceneEditorStore;

  beforeEach(() => {
    api = new InMemoryStoryApi();
    navigations = [];
    TestBed.configureTestingModule({
      providers: [
        SceneEditorStore,
        { provide: StoryApi, useValue: api },
        {
          provide: Router,
          useValue: {
            navigate: (commands: unknown[]) => {
              navigations.push(commands);
              return Promise.resolve(true);
            },
          },
        },
      ],
    });
    store = TestBed.inject(SceneEditorStore);
  });

  async function sceneIds(): Promise<string[]> {
    const story = await api.getStory(SAMPLE_STORY_ID);
    return story.containers.flatMap((c) => c.scenes.map((s) => s.id));
  }

  it('opens a story at the scene it was left in, and scrolls to the block once', async () => {
    const [, , third] = await sceneIds();
    await api.saveSpot(SAMPLE_STORY_ID, { sceneId: third, elementId: 'row' });

    await store.open(SAMPLE_STORY_ID, null);
    expect(navigations).toEqual([['/app/stories', SAMPLE_STORY_ID, 'scenes', third]]);

    await store.open(SAMPLE_STORY_ID, third);
    expect(store.resumeAt(third)).toBe('row');
    expect(store.resumeAt(third)).toBeNull();
  });

  it('opens a story never left anywhere at its first scene', async () => {
    const [first, second] = await sceneIds();

    await store.open(SAMPLE_STORY_ID, null);
    expect(navigations).toEqual([['/app/stories', SAMPLE_STORY_ID, 'scenes', first]]);

    await store.open(SAMPLE_STORY_ID, second);
    expect(store.resumeAt(second)).toBeNull();
  });

  it('saves the spot only when it moves', async () => {
    const [first] = await sceneIds();
    const saved: StorySpot[] = [];
    const saveSpot = api.saveSpot.bind(api);
    api.saveSpot = (storyId, spot) => {
      saved.push(spot);
      return saveSpot(storyId, spot);
    };

    await store.open(SAMPLE_STORY_ID, first);
    store.keepSpot(null);
    store.keepSpot(null);
    store.keepSpot('row');
    await new Promise((resolve) => setTimeout(resolve));

    expect(saved).toEqual([
      { sceneId: first, elementId: null },
      { sceneId: first, elementId: 'row' },
    ]);
    expect((await api.getStory(SAMPLE_STORY_ID)).spot).toEqual({
      sceneId: first,
      elementId: 'row',
    });
  });
});

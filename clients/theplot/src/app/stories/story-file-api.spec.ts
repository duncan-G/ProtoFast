import { TestBed } from '@angular/core/testing';
import { createRouterTransport, type ServiceImpl } from '@connectrpc/connect';
import { StoryFiles } from '../../lib/gen/story_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { isStoryFile, StoryFileApi } from './story-file-api';

function apiWith(impl: Partial<ServiceImpl<typeof StoryFiles>>): StoryFileApi {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: GRPC_TRANSPORT,
        useValue: createRouterTransport(({ service }) => service(StoryFiles, impl)),
      },
    ],
  });
  return TestBed.inject(StoryFileApi);
}

describe('StoryFileApi', () => {
  it('hands back the export as a file under the API’s name', async () => {
    const json = '{"format":"theplot"}';
    const api = apiWith({
      exportStory: ({ storyId }) => ({
        fileName: `${storyId}.theplot`,
        content: new TextEncoder().encode(json),
      }),
    });

    const file = await api.exportStory('night-shift');

    expect(file.name).toBe('night-shift.theplot');
    expect(await file.text()).toBe(json);
  });

  it('sends the file’s bytes as they are and returns the new story', async () => {
    let sent = '';
    const api = apiWith({
      importStory: ({ content }) => {
        sent = new TextDecoder().decode(content);
        return {
          story: {
            id: 'copy',
            title: 'Night Shift',
            createdUnixSeconds: 1n,
            lastModifiedUnixSeconds: 2n,
          },
        };
      },
    });

    const story = await api.importStory(new File(['{"title":"“Night”"}'], 'night.theplot'));

    expect(sent).toBe('{"title":"“Night”"}');
    expect(story).toEqual({
      id: 'copy',
      title: 'Night Shift',
      createdAt: new Date(1000),
      lastModifiedAt: new Date(2000),
    });
  });

  it('knows a story file by its extension, in any case', () => {
    expect(isStoryFile('Night Shift.THEPLOT')).toBe(true);
    expect(isStoryFile('night-shift.theplot.json')).toBe(false);
    expect(isStoryFile('theplot')).toBe(false);
  });
});

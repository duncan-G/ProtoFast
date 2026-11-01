import { TestBed } from '@angular/core/testing';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import {
  ImportOpenStoryReplySchema,
  ListOpenStoriesReplySchema,
  OpenStories,
} from '../../lib/gen/open_story_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { OpenStoryApi } from './open-story-api';

describe('OpenStoryApi', () => {
  let imported: string[];

  beforeEach(() => {
    imported = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) => {
            service(OpenStories, {
              listOpenStories: () =>
                create(ListOpenStoriesReplySchema, {
                  stories: [
                    {
                      id: 'o1',
                      title: 'The Crossing',
                      opening: 'Boston. April 1775.',
                      sceneCount: 4,
                      characterCount: 5,
                      publishedUnixSeconds: 1_700_000_000n,
                    },
                    { id: 'o2', title: 'Taken', importedStoryId: 's9' },
                  ],
                }),
              importOpenStory: (request) => {
                imported.push(request.openStoryId);
                return create(ImportOpenStoryReplySchema, {
                  story: {
                    id: 's1',
                    title: 'The Crossing',
                    createdUnixSeconds: 1_700_000_100n,
                    lastModifiedUnixSeconds: 1_700_000_100n,
                  },
                  created: true,
                });
              },
            });
          }),
        },
      ],
    });
  });

  it('maps open stories, with no copy as null', async () => {
    const stories = await TestBed.inject(OpenStoryApi).listOpenStories();

    expect(stories[0]).toEqual({
      id: 'o1',
      title: 'The Crossing',
      opening: 'Boston. April 1775.',
      sceneCount: 4,
      characterCount: 5,
      publishedAt: new Date(1_700_000_000_000),
      importedStoryId: null,
    });
    expect(stories[1].importedStoryId).toBe('s9');
  });

  it('imports by id and returns the copy as a story summary', async () => {
    const result = await TestBed.inject(OpenStoryApi).importOpenStory('o1');

    expect(imported).toEqual(['o1']);
    expect(result).toEqual({
      story: {
        id: 's1',
        title: 'The Crossing',
        createdAt: new Date(1_700_000_100_000),
        lastModifiedAt: new Date(1_700_000_100_000),
      },
      created: true,
    });
  });
});

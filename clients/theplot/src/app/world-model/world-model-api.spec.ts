import { TestBed } from '@angular/core/testing';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../grpc-transport';
import {
  GetLatestBuildReplySchema,
  StartBuildReplySchema,
  WorldModelBuildState,
  WorldModels,
} from '../../lib/gen/world_model_pb';
import { WorldModelApi } from './world-model-api';

describe('WorldModelApi', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) => {
            service(WorldModels, {
              startBuild: () =>
                create(StartBuildReplySchema, {
                  build: {
                    id: 'b1',
                    storyId: 's1',
                    state: WorldModelBuildState.RUNNING,
                    scenesTotal: 12,
                    scenesDone: 3,
                    message: 'Reading Scene 4',
                    costUsdMicros: 412_500n,
                    requestedUnixMs: 1_700_000_000_000n,
                    startedUnixMs: 1_700_000_005_000n,
                  },
                }),
              getLatestBuild: () => create(GetLatestBuildReplySchema, {}),
            });
          }),
        },
      ],
    });
  });

  it('maps a build to the model, with its cost in dollars and unset times as null', async () => {
    const build = await TestBed.inject(WorldModelApi).startBuild('s1');

    expect(build).toEqual({
      id: 'b1',
      storyId: 's1',
      state: 'running',
      scenesTotal: 12,
      scenesDone: 3,
      message: 'Reading Scene 4',
      costUsd: 0.4125,
      requestedAt: new Date(1_700_000_000_000),
      startedAt: new Date(1_700_000_005_000),
      finishedAt: null,
      partCount: 0,
      objectCount: 0,
      observationCount: 0,
    });
  });

  it('answers null for a story that has never been built', async () => {
    expect(await TestBed.inject(WorldModelApi).getLatestBuild('s1')).toBeNull();
  });
});

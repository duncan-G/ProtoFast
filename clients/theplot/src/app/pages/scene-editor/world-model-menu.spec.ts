import { ComponentFixture, TestBed } from '@angular/core/testing';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../../grpc-transport';
import {
  CancelBuildReplySchema,
  GetLatestBuildReplySchema,
  StartBuildReplySchema,
  WorldModelBuildState,
  WorldModels,
  type GetLatestBuildReply,
} from '../../../lib/gen/world_model_pb';
import { WorldModelMenu } from './world-model-menu';

describe('WorldModelMenu', () => {
  let latest: GetLatestBuildReply;
  const started: string[] = [];
  const cancelled: string[] = [];

  beforeEach(async () => {
    started.length = 0;
    cancelled.length = 0;
    latest = create(GetLatestBuildReplySchema, {});
    await TestBed.configureTestingModule({
      imports: [WorldModelMenu],
      providers: [
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) => {
            service(WorldModels, {
              getLatestBuild: () => latest,
              startBuild: (request) => {
                started.push(request.storyId);
                return create(StartBuildReplySchema, {
                  build: {
                    id: 'b1',
                    storyId: request.storyId,
                    state: WorldModelBuildState.QUEUED,
                    scenesTotal: 4,
                    message: 'Waiting for a worker',
                  },
                });
              },
              cancelBuild: (request) => {
                cancelled.push(request.buildId);
                return create(CancelBuildReplySchema, {
                  build: {
                    id: request.buildId,
                    storyId: 's1',
                    state: WorldModelBuildState.CANCELLED,
                    message: 'Stopped',
                  },
                });
              },
            });
          }),
        },
      ],
    }).compileComponents();
  });

  async function settle(fixture: ComponentFixture<WorldModelMenu>): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
  }

  function open(fixture: ComponentFixture<WorldModelMenu>): HTMLElement {
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('.se-world-pop') as HTMLElement;
  }

  it('offers to build a story that has never been read, and starts it', async () => {
    const fixture = TestBed.createComponent(WorldModelMenu);
    fixture.componentRef.setInput('storyId', 's1');
    await settle(fixture);

    const pop = open(fixture);
    expect(pop.textContent).toContain('not been read yet');
    (pop.querySelector('.se-btn-primary') as HTMLButtonElement).click();
    await settle(fixture);

    expect(started).toEqual(['s1']);
    expect(fixture.nativeElement.textContent).toContain('Waiting to start');
    expect(fixture.nativeElement.textContent).toContain('Waiting for a worker');
    expect(fixture.nativeElement.querySelector('.se-btn-danger')).not.toBeNull();
  });

  it('shows a running build with its progress and stops it on request', async () => {
    latest = create(GetLatestBuildReplySchema, {
      build: {
        id: 'b7',
        storyId: 's1',
        state: WorldModelBuildState.RUNNING,
        scenesTotal: 10,
        scenesDone: 4,
        message: 'Reading The wing',
        costUsdMicros: 125_000n,
      },
    });
    const fixture = TestBed.createComponent(WorldModelMenu);
    fixture.componentRef.setInput('storyId', 's1');
    await settle(fixture);

    const pop = open(fixture);
    expect(pop.textContent).toContain('4 of 10 scenes');
    expect(pop.textContent).toContain('Spent so far $0.13');
    expect((pop.querySelector('.se-world-bar-fill') as HTMLElement).style.width).toBe('40%');
    (pop.querySelector('.se-btn-danger') as HTMLButtonElement).click();
    await settle(fixture);

    expect(cancelled).toEqual(['b7']);
    expect(fixture.nativeElement.textContent).toContain('Stopped');
    fixture.destroy();
  });

  it('sums up a finished build', async () => {
    latest = create(GetLatestBuildReplySchema, {
      build: {
        id: 'b2',
        storyId: 's1',
        state: WorldModelBuildState.FINISHED,
        scenesTotal: 3,
        scenesDone: 3,
        partCount: 5,
        objectCount: 12,
        observationCount: 40,
        costUsdMicros: 2_000_000n,
      },
    });
    const fixture = TestBed.createComponent(WorldModelMenu);
    fixture.componentRef.setInput('storyId', 's1');
    await settle(fixture);

    const pop = open(fixture);
    expect(pop.textContent).toContain('Built');
    expect(pop.textContent).toContain('5 parts · 12 objects · 40 observations');
    expect(pop.textContent).toContain('Cost $2.00');
    expect(pop.querySelector('.se-btn-primary')?.textContent).toContain('Build again');
  });
});

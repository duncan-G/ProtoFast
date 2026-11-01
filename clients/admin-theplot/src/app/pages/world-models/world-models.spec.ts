import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../../../admin-kit';
import {
  ListWorldModelBuildsReplySchema,
  TheplotWorldModels,
  WorldModelBuildStatus,
  type ListWorldModelBuildsRequest,
} from '../../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { WorldModels } from './world-models';

describe('WorldModels', () => {
  const requests: ListWorldModelBuildsRequest[] = [];

  beforeEach(async () => {
    requests.length = 0;
    await TestBed.configureTestingModule({
      imports: [WorldModels],
      providers: [
        provideRouter([]),
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) => {
            service(TheplotWorldModels, {
              listBuilds: (request) => {
                requests.push(request);
                return create(ListWorldModelBuildsReplySchema, {
                  total: 2,
                  builds: [
                    {
                      id: '0f8a2c6e-1111-4bbb-9ccc-123456789abc',
                      storyId: 's1',
                      storyTitle: 'The Quiet Year',
                      ownerId: 'writer-1',
                      status: WorldModelBuildStatus.FINISHED,
                      requestedUnixMs: 1_700_000_000_000n,
                      startedUnixMs: 1_700_000_001_000n,
                      finishedUnixMs: 1_700_000_066_000n,
                      scenesTotal: 12,
                      scenesDone: 12,
                      steps: 14,
                      costUsdMicros: 1_250_000n,
                    },
                    {
                      id: '0f8a2c6e-2222-4bbb-9ccc-123456789abc',
                      storyId: 's2',
                      storyTitle: 'Trauma',
                      ownerId: 'writer-2',
                      status: WorldModelBuildStatus.RUNNING,
                      requestedUnixMs: 1_700_000_000_000n,
                      scenesTotal: 8,
                      scenesDone: 3,
                      steps: 5,
                    },
                  ],
                });
              },
            });
          }),
        },
      ],
    }).compileComponents();
  });

  async function settle(fixture: ComponentFixture<WorldModels>): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
  }

  it('lists builds with their story, progress, status, duration and cost', async () => {
    const fixture = TestBed.createComponent(WorldModels);
    await settle(fixture);

    const rows = fixture.nativeElement.querySelectorAll('tbody tr') as NodeListOf<HTMLElement>;
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('The Quiet Year');
    expect(rows[0].textContent).toContain('12 / 12');
    expect(rows[0].textContent).toContain('Finished');
    expect(rows[0].textContent).toContain('1m 5s');
    expect(rows[0].textContent).toContain('$1.25');
    expect(rows[1].textContent).toContain('3 / 8');
    expect(rows[1].textContent).toContain('Running');
    expect(fixture.nativeElement.textContent).toContain('2 builds');
  });

  it('sends the chosen status to the api', async () => {
    const fixture = TestBed.createComponent(WorldModels);
    await settle(fixture);

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = String(WorldModelBuildStatus.FAILED);
    select.dispatchEvent(new Event('change'));
    await settle(fixture);

    expect(requests.at(-1)?.status).toBe(WorldModelBuildStatus.FAILED);
  });
});

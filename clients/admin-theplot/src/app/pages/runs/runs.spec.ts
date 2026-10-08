import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../../../admin-kit';
import {
  ListFamiliesReplySchema,
  ListRunsReplySchema,
  RunMode,
  RunStatus,
  TheplotFamilies,
  TheplotRuns,
  type ListRunsRequest,
} from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { Runs } from './runs';

describe('Runs', () => {
  const requests: ListRunsRequest[] = [];

  beforeEach(async () => {
    requests.length = 0;
    await TestBed.configureTestingModule({
      imports: [Runs],
      providers: [
        provideRouter([]),
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) => {
            service(TheplotRuns, {
              listRuns: (request) => {
                requests.push(request);
                return create(ListRunsReplySchema, {
                  total: 2,
                  runs: [
                    {
                      runId: '01jabcdefghjkmnpqrstvwxyz0',
                      family: 'screenplay',
                      generation: 2,
                      mode: RunMode.DISCOVERY,
                      status: RunStatus.CLOSED,
                      passed: true,
                      openedUnixMs: 1_700_000_000_000n,
                      closedUnixMs: 1_700_000_065_000n,
                      stageAttempts: 3,
                      costUsdMicros: 1_250_000n,
                      sourceId: 'upload-1',
                      name: 'the-quiet-year.fdx',
                    },
                    {
                      runId: '01jabcdefghjkmnpqrstvwxyz1',
                      family: 'prose',
                      mode: RunMode.DISCOVERY,
                      status: RunStatus.ABANDONED,
                      openedUnixMs: 1_700_000_000_000n,
                      abandonedUnixMs: 1_700_000_005_000n,
                      failure: 'turns spent',
                    },
                  ],
                });
              },
            });
            service(TheplotFamilies, {
              listFamilies: () =>
                create(ListFamiliesReplySchema, {
                  families: [{ family: 'screenplay', info: { displayName: 'Screenplays' } }],
                }),
            });
          }),
        },
      ],
    }).compileComponents();
  });

  /** Renders, lets the loads started by afterNextRender resolve, then renders again. */
  async function settle(fixture: ComponentFixture<Runs>): Promise<void> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
  }

  it('lists runs with their name, outcome, generation, cost and duration', async () => {
    const fixture = TestBed.createComponent(Runs);
    fixture.componentRef.setInput('family', '');
    await settle(fixture);

    const rows = fixture.nativeElement.querySelectorAll('tbody tr') as NodeListOf<HTMLElement>;
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('the-quiet-year.fdx');
    expect(rows[0].textContent).toContain('screenplay');
    expect(rows[0].textContent).toContain('gen 2');
    expect(rows[0].textContent).toContain('Passed');
    expect(rows[0].textContent).toContain('$1.25');
    expect(rows[0].textContent).toContain('1m 5s');
    expect(rows[1].textContent).toContain('Abandoned');
    expect(fixture.nativeElement.textContent).toContain('2 runs');
  });

  it('offers the known families as a filter and sends the chosen one to the api', async () => {
    const fixture = TestBed.createComponent(Runs);
    fixture.componentRef.setInput('family', 'screenplay');
    await settle(fixture);

    const options = Array.from(
      fixture.nativeElement.querySelectorAll('select option') as NodeListOf<HTMLOptionElement>,
    ).map((o) => o.textContent?.trim());
    expect(options).toContain('Screenplays');
    expect(requests.at(-1)?.family).toBe('screenplay');
  });
});

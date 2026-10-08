import { ComponentFixture, TestBed } from '@angular/core/testing';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../../admin-kit';
import {
  BriefStatus,
  GetRunReviewReplySchema,
  RebriefRunReplySchema,
  TheplotRuns,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { RunSummary } from './run-summary';

type Review = Parameters<typeof create<typeof GetRunReviewReplySchema>>[1];

describe('RunSummary', () => {
  let rebriefed: string[];

  function render(...replies: Review[]) {
    rebriefed = [];
    TestBed.configureTestingModule({
      imports: [RunSummary],
      providers: [
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) =>
            service(TheplotRuns, {
              getRunReview: () =>
                create(GetRunReviewReplySchema, replies.length > 1 ? replies.shift() : replies[0]),
              rebriefRun: ({ runId }) => {
                rebriefed.push(runId);
                return create(RebriefRunReplySchema);
              },
            }),
          ),
        },
      ],
    });
    const fixture = TestBed.createComponent(RunSummary);
    fixture.componentRef.setInput('runId', 'run-1');
    return fixture;
  }

  async function settle(fixture: ComponentFixture<RunSummary>): Promise<HTMLElement> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (element: Element) => element.textContent!.replace(/\s+/g, ' ').trim();

  it('shows the brief, its flags and each step numbered as the conversation numbers messages', async () => {
    const fixture = render({
      brief: {
        status: BriefStatus.BRIEFED,
        outcome: 'Delivered the story.',
        overview: 'The agent ran the family importer.',
        flags: [
          {
            kind: 'manuscript-specific-skill',
            detail: 'The importer hardcodes the cast.',
            sequence: 3,
          },
        ],
        modelId: 'claude-sonnet-5',
        costUsdMicros: 4_200n,
      },
      steps: [
        {
          sequence: 1,
          headline: 'Loaded built-in context',
          calls: [{ callId: 'c1', headline: 'Loaded built-in context' }],
        },
        {
          sequence: 3,
          headline: 'Ran importer@6/import-story; Wrote stage `story`: passed',
          brief: 'Ran the importer, which carries another story’s characters.',
          calls: [
            {
              callId: 'c2',
              effects: [
                { kind: 'ScriptRan', summary: 'Ran importer@6/import-story', depth: 0 },
                { kind: 'ArtifactWritten', summary: 'Wrote stage `story`: passed', depth: 1 },
              ],
            },
          ],
        },
      ],
    });
    const opened: number[] = [];
    fixture.componentInstance.openMessage.subscribe((sequence) => opened.push(sequence));
    const element = await settle(fixture);

    expect(text(element.querySelector('section')!)).toContain('Delivered the story.');
    expect(text(element.querySelector('section')!)).toContain('claude-sonnet-5');
    const steps = Array.from(element.querySelectorAll('ol > li')).map(text);
    expect(steps[0]).toContain('#2 Loaded built-in context');
    expect(steps[1]).toContain('#4 Ran importer@6/import-story ↳ Wrote stage `story`: passed');
    expect(steps[1]).toContain('carries another story’s characters');
    expect(steps[1]).toContain('⚑ The importer hardcodes the cast.');

    (element.querySelector('section li button') as HTMLButtonElement).click();
    expect(opened).toEqual([3]);
  });

  it('asks for a failed brief to be written again', async () => {
    const fixture = render(
      { brief: { status: BriefStatus.FAILED, attempts: 3, error: 'JsonException: no object' } },
      { brief: { status: BriefStatus.BRIEFING, attempts: 1 } },
    );
    const element = await settle(fixture);
    expect(text(element)).toContain('JsonException: no object');

    (element.querySelector('header button') as HTMLButtonElement).click();
    await settle(fixture);

    expect(rebriefed).toEqual(['run-1']);
    expect(text(element)).toContain('The worker is writing the brief.');
  });

  it('says a run still going is briefed once it ends', async () => {
    const element = await settle(render({ steps: [] }));

    expect(text(element)).toContain('briefed once it ends');
    expect(text(element)).toContain('No steps recorded');
  });
});

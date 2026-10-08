import { ComponentFixture, TestBed } from '@angular/core/testing';
import { create } from '@bufbuild/protobuf';
import { createRouterTransport } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../../admin-kit';
import {
  GetTranscriptReplySchema,
  TheplotRuns,
  TranscriptRole,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { RunTranscript } from './run-transcript';

describe('RunTranscript', () => {
  function render(reply: Parameters<typeof create<typeof GetTranscriptReplySchema>>[1]) {
    TestBed.configureTestingModule({
      imports: [RunTranscript],
      providers: [
        {
          provide: GRPC_TRANSPORT,
          useValue: createRouterTransport(({ service }) =>
            service(TheplotRuns, {
              getTranscript: ({ fromSequence }) =>
                fromSequence === 0
                  ? create(GetTranscriptReplySchema, reply)
                  : create(GetTranscriptReplySchema, { total: reply?.total ?? 0 }),
            }),
          ),
        },
      ],
    });
    const fixture = TestBed.createComponent(RunTranscript);
    fixture.componentRef.setInput('runId', 'run-1');
    return fixture;
  }

  async function settle(fixture: ComponentFixture<RunTranscript>): Promise<HTMLElement> {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('puts each system prompt before the first message it applied to', async () => {
    const element = await settle(
      render({
        total: 3,
        systemPrompts: [
          { fromSequence: 0, text: 'You are the discovery agent.' },
          { fromSequence: 2, text: 'You are the discovery agent.\n- split (yours)' },
        ],
        messages: [
          { sequence: 0, role: TranscriptRole.USER, text: 'Import this.' },
          { sequence: 1, role: TranscriptRole.ASSISTANT, text: 'Loading context.' },
          { sequence: 2, role: TranscriptRole.USER, text: 'Resumed.' },
        ],
      }),
    );

    const items = Array.from(element.querySelectorAll('ol > li')).map((li) =>
      li.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(items.length).toBe(5);
    expect(items[0]).toContain('System prompt');
    expect(items[1]).toContain('Import this.');
    expect(items[3]).toContain('System prompt after resuming from message #3');
    expect(items[4]).toContain('Resumed.');
    expect(element.querySelector('details pre')?.textContent).toContain(
      'You are the discovery agent.',
    );
  });

  it('says when a run predates journaling its prompt', async () => {
    const element = await settle(
      render({ total: 1, messages: [{ sequence: 0, role: TranscriptRole.USER, text: 'Go.' }] }),
    );

    expect(element.textContent).toContain('system prompt was not recorded');
  });

  it('opens a create-code call’s source in a viewer without toggling the call', async () => {
    const source = 'public static class Script\n{\n}';
    const fixture = render({
      total: 1,
      messages: [
        {
          sequence: 0,
          role: TranscriptRole.ASSISTANT,
          toolCalls: [
            {
              id: 'call-1',
              name: 'execute_code',
              inputJson: JSON.stringify({
                skill: 'create-code',
                script: 'create',
                args: { skill: 'split', script: 'cut', source },
              }),
            },
          ],
        },
      ],
    });
    const element = await settle(fixture);
    const button = element.querySelector<HTMLButtonElement>('summary button')!;

    expect(button.textContent!.trim()).toBe('View source');
    button.click();
    await settle(fixture);

    expect(element.querySelector('details')!.open).toBe(false);
    const dialog = element.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('split/cut · source');
    expect(dialog.textContent).toContain('3 lines');
  });
});

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  TranscriptRole,
  type SystemPrompt,
  type TranscriptMessage,
  type TranscriptToolCall,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { errorMessage, TheplotAdminApi } from '../theplot-admin';
import { duration, prettyJson, toDate, usd } from './format';
import { toolCallView, type LongText, type ToolCallView } from './long-text';
import { TextViewer } from './text-viewer';

const PAGE = 100;

interface Entry {
  key: string;
  prompt?: SystemPrompt;
  message?: TranscriptMessage;
  calls?: { call: TranscriptToolCall; view: ToolCallView }[];
}

/**
 * The discovery agent's conversation: the system prompt it ran under, its turns, the tools it
 * called and what they returned.
 */
@Component({
  selector: 'app-run-transcript',
  imports: [DatePipe, DecimalPipe, TextViewer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <p class="rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (!messages().length && !loading() && !error()) {
      <p
        class="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-sm text-gray-500"
      >
        No conversation was journaled for this run. Only discovery runs talk to a model directly.
      </p>
    }

    @if (messages().length && !prompts().length && !loading()) {
      <p class="mb-3 text-xs text-gray-500">
        This run’s system prompt was not recorded; runs started before prompts were journaled have
        none.
      </p>
    }

    <ol class="space-y-3">
      @for (entry of entries(); track entry.key) {
        @if (entry.prompt; as prompt) {
          <li>
            <details class="rounded-2xl border border-slate-300 bg-slate-50 text-sm shadow-sm">
              <summary
                class="flex cursor-pointer flex-wrap items-center gap-2 px-4 py-3 text-xs text-gray-500"
              >
                <span class="rounded-full bg-slate-600 px-2 py-0.5 font-medium text-white"
                  >System</span
                >
                <span class="font-medium text-gray-900">
                  {{ prompt.fromSequence === 0 ? 'System prompt' : 'System prompt after resuming' }}
                </span>
                @if (prompt.fromSequence > 0) {
                  <span>from message #{{ prompt.fromSequence + 1 }}</span>
                }
                <span>{{ toDate(prompt.recordedUnixMs) | date: 'HH:mm:ss' }}</span>
                <span class="ml-auto">{{ prompt.text.length | number }} characters</span>
              </summary>
              <pre
                class="max-h-[32rem] overflow-auto whitespace-pre-wrap break-words border-t border-slate-200 px-4 py-3 font-mono text-xs leading-relaxed text-gray-800"
                >{{ prompt.text }}</pre
              >
            </details>
          </li>
        }
        @if (entry.message; as message) {
          <li [id]="'message-' + message.sequence">
            <article
              class="rounded-2xl border p-4 text-sm shadow-sm"
              [class]="
                message.role === Role.ASSISTANT
                  ? 'border-indigo-100 bg-indigo-50/50'
                  : 'border-gray-200 bg-white'
              "
            >
              <header class="flex flex-wrap items-center gap-2 text-xs text-gray-500">
                <span
                  class="rounded-full px-2 py-0.5 font-medium"
                  [class]="
                    message.role === Role.ASSISTANT
                      ? 'bg-indigo-600 text-white'
                      : 'bg-gray-900 text-white'
                  "
                >
                  {{ message.role === Role.ASSISTANT ? 'Agent' : 'Engine' }}
                </span>
                <span>#{{ message.sequence + 1 }}</span>
                <span>{{ toDate(message.recordedUnixMs) | date: 'HH:mm:ss' }}</span>
                @if (message.spend; as spend) {
                  <span class="ml-auto"
                    >{{ usd(spend.usdMicros) }} · {{ duration(spend.durationMs) }}</span
                  >
                }
              </header>

              @if (message.text) {
                <p class="mt-2 whitespace-pre-wrap break-words text-gray-900">{{ message.text }}</p>
              }

              @for (item of entry.calls; track item.call.id) {
                <details class="mt-2 rounded-lg border border-indigo-200 bg-white">
                  <summary
                    class="flex cursor-pointer flex-wrap items-center gap-x-2 gap-y-1 px-3 py-2 font-mono text-xs text-indigo-900"
                  >
                    <span>▸ {{ item.call.name }}</span>
                    @if (item.view.skill) {
                      <span class="font-medium">{{ item.view.skill }}</span>
                    }
                    <span class="text-gray-400">{{ item.call.id }}</span>
                    @if (item.view.texts.length) {
                      <span class="ml-auto flex gap-2">
                        @for (text of item.view.texts; track text.field) {
                          <button
                            type="button"
                            class="rounded-md border border-indigo-200 bg-indigo-50 px-2 py-0.5 font-sans text-indigo-800 hover:bg-indigo-100"
                            (click)="$event.preventDefault(); viewing.set(text)"
                          >
                            View {{ text.field }}
                          </button>
                        }
                      </span>
                    }
                  </summary>
                  <pre
                    class="whitespace-pre-wrap break-words border-t border-indigo-100 px-3 py-2 font-mono text-xs text-gray-800"
                    >{{ item.view.json }}</pre
                  >
                </details>
              }

              @for (result of message.toolResults; track result.callId) {
                <details
                  class="mt-2 rounded-lg border bg-white"
                  [class]="result.isError ? 'border-red-300' : 'border-gray-200'"
                  [open]="result.isError"
                >
                  <summary
                    class="cursor-pointer px-3 py-2 font-mono text-xs"
                    [class]="result.isError ? 'text-red-800' : 'text-gray-700'"
                  >
                    ↳ {{ result.name }} {{ result.isError ? 'failed' : 'returned' }}
                    <span class="ml-2 text-gray-400">{{ result.callId }}</span>
                  </summary>
                  <pre
                    class="max-h-96 overflow-auto whitespace-pre-wrap break-words border-t border-gray-100 px-3 py-2 font-mono text-xs text-gray-800"
                    >{{ pretty(result.content) }}</pre
                  >
                </details>
              }
            </article>
          </li>
        }
      }
    </ol>

    @if (viewing(); as text) {
      <app-text-viewer [text]="text" (closed)="viewing.set(null)" />
    }

    @if (loading()) {
      <p class="mt-4 text-sm text-gray-500">
        Loading… {{ messages().length }} of {{ total() }} messages
      </p>
    }
  `,
})
export class RunTranscript {
  private readonly api = inject(TheplotAdminApi);

  readonly runId = input.required<string>();
  /** A message to scroll to once it has loaded. */
  readonly focus = input<number | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private focused = false;

  protected readonly Role = TranscriptRole;
  protected readonly messages = signal<TranscriptMessage[]>([]);
  protected readonly prompts = signal<SystemPrompt[]>([]);
  /** Each prompt sits before the first message it applied to; one with no messages after it goes last. */
  protected readonly entries = computed<Entry[]>(() => {
    const pending = [...this.prompts()];
    const entries: Entry[] = [];
    const take = (upTo: number) => {
      while (pending.length && pending[0].fromSequence <= upTo) {
        const prompt = pending.shift()!;
        entries.push({ key: `p${prompt.fromSequence}`, prompt });
      }
    };
    for (const message of this.messages()) {
      take(message.sequence);
      entries.push({
        key: `m${message.sequence}`,
        message,
        calls: message.toolCalls.map((call) => ({ call, view: toolCallView(call.inputJson) })),
      });
    }
    take(Number.MAX_SAFE_INTEGER);
    return entries;
  });
  protected readonly viewing = signal<LongText | null>(null);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly duration = duration;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  constructor() {
    afterNextRender(() => void this.loadAll());
  }

  protected pretty(text: string): string {
    return prettyJson(text) ?? text;
  }

  private scrollToFocus(loadedUpTo: number): void {
    const focus = this.focus();
    if (focus === null || this.focused || focus >= loadedUpTo) {
      return;
    }

    this.focused = true;
    // After the page just loaded has rendered.
    setTimeout(() =>
      this.host.nativeElement
        .querySelector(`#message-${focus}`)
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
    );
  }

  // Pages follow the sequence, so a long conversation streams in rather than arriving at once.
  private async loadAll(): Promise<void> {
    try {
      let from = 0;
      do {
        const reply = await this.api.runs.getTranscript({
          runId: this.runId(),
          fromSequence: from,
          take: PAGE,
        });
        this.total.set(reply.total);
        if (from === 0) {
          this.prompts.set(reply.systemPrompts);
        }
        if (!reply.messages.length) {
          break;
        }
        this.messages.update((loaded) => [...loaded, ...reply.messages]);
        from = reply.messages[reply.messages.length - 1].sequence + 1;
        this.scrollToFocus(from);
      } while (this.messages().length < this.total());
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }
}

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import {
  BriefStatus,
  type BriefFlag,
  type GetRunReviewReply,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { errorMessage, TheplotAdminApi } from '../theplot-admin';
import { toDate, usd } from './format';

const POLL_MS = 15_000;

/**
 * What a run did, for review without reading the conversation: the brief the worker writes once a
 * run ends, and each step's engine headline with a model's account where the headline is not enough.
 * Message numbers match the conversation's.
 */
@Component({
  selector: 'app-run-summary',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <p class="rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (review(); as review) {
      <section class="rounded-2xl border border-gray-200 bg-white p-5 text-sm shadow-sm">
        <header class="flex flex-wrap items-center gap-2">
          <h2 class="font-semibold text-gray-900">Brief</h2>
          @if (review.brief; as brief) {
            <span class="text-xs text-gray-500">
              @if (brief.status === Status.BRIEFED) {
                {{ brief.modelId }} · {{ usd(brief.costUsdMicros) }} ·
                {{ toDate(brief.updatedUnixMs) | date: 'medium' }}
              } @else if (brief.status === Status.BRIEFING) {
                being written · attempt {{ brief.attempts }}
              } @else {
                failed after {{ brief.attempts }}
                {{ brief.attempts === 1 ? 'attempt' : 'attempts' }}
              }
            </span>
            @if (brief.status !== Status.BRIEFING) {
              <button
                type="button"
                class="ml-auto rounded-md border border-gray-300 px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                [disabled]="rebriefing()"
                (click)="rebrief()"
              >
                Brief again
              </button>
            }
          }
        </header>

        @if (review.brief; as brief) {
          @if (brief.status === Status.BRIEFED) {
            <p class="mt-3 font-medium text-gray-900">{{ brief.outcome }}</p>
            <p class="mt-2 whitespace-pre-wrap text-gray-700">{{ brief.overview }}</p>
            @if (brief.flags.length) {
              <ul class="mt-4 space-y-2">
                @for (flag of brief.flags; track $index) {
                  <li class="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2">
                    <span
                      class="mr-2 rounded-full bg-amber-200 px-2 py-0.5 font-mono text-xs text-amber-900"
                      >{{ flag.kind }}</span
                    >
                    <span class="text-amber-950">{{ flag.detail }}</span>
                    @if (flag.sequence) {
                      <button
                        type="button"
                        class="ml-2 text-xs text-indigo-600 hover:underline"
                        (click)="openMessage.emit(flag.sequence)"
                      >
                        #{{ flag.sequence + 1 }} →
                      </button>
                    }
                  </li>
                }
              </ul>
            }
          } @else if (brief.status === Status.FAILED) {
            <p class="mt-3 text-red-800">{{ brief.error }}</p>
            <p class="mt-1 text-xs text-gray-500">
              The worker stops after its last attempt; brief again to retry.
            </p>
          } @else {
            <p class="mt-3 text-gray-500">The worker is writing the brief.</p>
          }
        } @else {
          <p class="mt-3 text-gray-500">
            {{
              ended()
                ? 'Waiting for the worker to brief this run.'
                : 'The run is still going; it is briefed once it ends.'
            }}
          </p>
        }
      </section>

      <ol class="mt-6 space-y-2">
        @for (step of review.steps; track step.sequence) {
          @let flags = flagsAt().get(step.sequence);
          <li
            class="rounded-2xl border bg-white p-4 text-sm shadow-sm"
            [class]="flags ? 'border-amber-300' : 'border-gray-200'"
          >
            <header class="flex items-start gap-3">
              <span
                class="shrink-0 rounded-full bg-indigo-600 px-2 py-0.5 text-xs font-medium text-white"
                >#{{ step.sequence + 1 }}</span
              >
              <ul class="min-w-0 flex-1 space-y-1">
                @for (call of step.calls; track call.callId) {
                  @if (call.effects.length) {
                    @for (effect of call.effects; track $index) {
                      <li
                        class="break-words"
                        [style.padding-left.rem]="effect.depth * 1.25"
                        [class]="effect.kind === 'Failed' ? 'text-red-800' : 'text-gray-900'"
                      >
                        @if (effect.depth) {
                          <span class="text-gray-400">↳ </span>
                        }
                        {{ effect.summary }}
                      </li>
                    }
                  } @else {
                    <li
                      class="break-words"
                      [class]="call.isError ? 'text-red-800' : 'text-gray-600'"
                    >
                      {{ call.headline }}
                    </li>
                  }
                }
              </ul>
              <button
                type="button"
                class="shrink-0 text-xs text-indigo-600 hover:underline"
                (click)="openMessage.emit(step.sequence)"
              >
                Conversation →
              </button>
            </header>
            @if (step.brief) {
              <p class="mt-2 border-l-2 border-indigo-200 pl-3 text-gray-700">{{ step.brief }}</p>
            }
            @for (flag of flags ?? []; track $index) {
              <p class="mt-2 text-xs text-amber-900">⚑ {{ flag.detail }}</p>
            }
          </li>
        } @empty {
          <li
            class="rounded-2xl border border-dashed border-gray-300 p-8 text-center text-sm text-gray-500"
          >
            No steps recorded. Runs from before steps were kept get them when they are briefed.
          </li>
        }
      </ol>
    } @else if (!error()) {
      <p class="text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class RunSummary {
  private readonly api = inject(TheplotAdminApi);
  private readonly destroyRef = inject(DestroyRef);
  private timer: ReturnType<typeof setTimeout> | undefined;

  readonly runId = input.required<string>();
  /** Closed or abandoned: the worker will brief it. */
  readonly ended = input(false);
  /** A transcript sequence to show in the conversation. */
  readonly openMessage = output<number>();

  protected readonly Status = BriefStatus;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  protected readonly review = signal<GetRunReviewReply | null>(null);
  protected readonly error = signal('');
  protected readonly rebriefing = signal(false);
  protected readonly flagsAt = computed(() => {
    const at = new Map<number, BriefFlag[]>();
    for (const flag of this.review()?.brief?.flags ?? []) {
      if (flag.sequence) {
        at.set(flag.sequence, [...(at.get(flag.sequence) ?? []), flag]);
      }
    }
    return at;
  });

  constructor() {
    afterNextRender(() => void this.load());
    this.destroyRef.onDestroy(() => clearTimeout(this.timer));
  }

  protected async rebrief(): Promise<void> {
    this.rebriefing.set(true);
    try {
      await this.api.runs.rebriefRun({ runId: this.runId() });
      await this.load();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.rebriefing.set(false);
    }
  }

  private async load(): Promise<void> {
    clearTimeout(this.timer);
    try {
      const review = await this.api.runs.getRunReview({ runId: this.runId() });
      this.review.set(review);
      this.error.set('');
      const waiting = !review.brief || review.brief.status === BriefStatus.BRIEFING;
      if (this.ended() && waiting) {
        this.timer = setTimeout(() => void this.load(), POLL_MS);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

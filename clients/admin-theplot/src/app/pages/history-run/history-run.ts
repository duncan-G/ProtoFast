import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import type { GetHistoryStoryRunReply } from '../../../lib/gen/Admin/Theplot/theplot_history_pb';
import { toDate, usd } from '../../engine/format';
import { parseBrief } from '../../history/brief';
import {
  EVENT_KIND_LABEL,
  isRunActive,
  RUN_STATUS_CLASS,
  RUN_STATUS_LABEL,
  runElapsed,
  runLabel,
} from '../../history/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

const POLL_MS = 5_000;

/** One story run: the request, the brief research produced, and every search, read and draft on the way. */
@Component({
  selector: 'app-history-run',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/history" class="text-sm text-indigo-600 hover:underline">← History</a>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as reply) {
      @let run = reply.run!;
      <header class="mt-4 flex flex-wrap items-center gap-3">
        <h1 class="text-xl font-bold text-gray-900">{{ runLabel(run) }}</h1>
        <span [class]="statusClass[run.status]">{{ statusLabel[run.status] }}</span>
        <span class="font-mono text-xs text-gray-500">{{ run.id }}</span>
        <div class="ml-auto flex gap-2">
          @if (isRunActive(run.status)) {
            <button
              type="button"
              class="rounded-lg border border-gray-300 px-3 py-1.5 text-sm hover:bg-gray-50 disabled:opacity-50"
              [disabled]="cancelling()"
              (click)="cancel()"
            >
              Cancel
            </button>
          }
          @if (run.storyId) {
            <a
              [routerLink]="['/stories', run.storyId]"
              class="rounded-lg bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
              >Open the story</a
            >
          }
        </div>
      </header>

      @if (run.failure) {
        <p class="mt-4 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-800">
          <span class="font-medium">Failure:</span> {{ run.failure }}
        </p>
      }

      <dl
        class="mt-6 grid gap-x-6 gap-y-4 rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm sm:grid-cols-3 lg:grid-cols-4"
      >
        <div class="sm:col-span-3 lg:col-span-4">
          <dt class="text-gray-500">Asked for</dt>
          <dd class="text-gray-900">{{ run.request || 'A random moment' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Owner</dt>
          <dd class="font-mono text-xs text-gray-900 break-all">{{ run.writerId }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Scenes</dt>
          <dd class="text-gray-900">{{ run.scenes }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Cost</dt>
          <dd class="text-gray-900">{{ usd(run.costUsdMicros) }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Attempts</dt>
          <dd class="text-gray-900">{{ run.attempts }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Requested</dt>
          <dd class="text-gray-900">{{ toDate(run.createdUnixMs) | date: 'medium' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Took</dt>
          <dd class="text-gray-900">{{ runElapsed(run) }}</dd>
        </div>
        <div class="sm:col-span-2">
          <dt class="text-gray-500">Now</dt>
          <dd class="text-gray-900">{{ run.message || '—' }}</dd>
        </div>
      </dl>

      @if (brief(); as brief) {
        <section class="mt-8 rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 class="text-lg font-semibold text-gray-900">Research brief</h2>
          <p class="mt-1 text-sm text-gray-500">{{ brief.when }} · {{ brief.where }}</p>
          <p class="mt-3 font-medium text-gray-900">{{ brief.moment }}</p>
          <p class="mt-2 text-sm text-gray-700 whitespace-pre-line">{{ brief.summary }}</p>
          @if (brief.stakes) {
            <p class="mt-2 text-sm text-gray-700">
              <span class="font-medium">Stakes:</span> {{ brief.stakes }}
            </p>
          }

          <div class="mt-6 grid gap-6 lg:grid-cols-2">
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">
                Background
              </h3>
              <ul class="mt-2 space-y-1 text-sm text-gray-700">
                @for (fact of brief.background ?? []; track $index) {
                  <li>
                    – {{ fact.text }}
                    <span class="ml-1 font-mono text-xs text-gray-400">{{
                      cite(fact.sources)
                    }}</span>
                  </li>
                } @empty {
                  <li class="text-gray-500">None given.</li>
                }
              </ul>
            </div>
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">Aftermath</h3>
              <ul class="mt-2 space-y-1 text-sm text-gray-700">
                @for (fact of brief.aftermath ?? []; track $index) {
                  <li>
                    – {{ fact.text }}
                    <span class="ml-1 font-mono text-xs text-gray-400">{{
                      cite(fact.sources)
                    }}</span>
                  </li>
                } @empty {
                  <li class="text-gray-500">None given.</li>
                }
              </ul>
            </div>
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">People</h3>
              <ul class="mt-2 space-y-3 text-sm">
                @for (person of brief.people ?? []; track $index) {
                  <li>
                    <span class="font-medium text-gray-900">{{ person.name }}</span>
                    <span class="text-gray-500"> · {{ person.role }}</span>
                    <span class="ml-1 font-mono text-xs text-gray-400">{{
                      cite(person.sources)
                    }}</span>
                    @if (person.description) {
                      <p class="text-gray-700">{{ person.description }}</p>
                    }
                    @for (fact of person.facts ?? []; track $index) {
                      <p class="text-xs text-gray-600">– {{ fact }}</p>
                    }
                  </li>
                }
              </ul>
            </div>
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">Timeline</h3>
              <ol class="mt-2 space-y-2 text-sm">
                @for (event of brief.timeline ?? []; track $index) {
                  <li>
                    <span class="font-medium text-gray-900">{{ event.when }}</span>
                    <span class="text-gray-700"> {{ event.what }}</span>
                    <span class="ml-1 font-mono text-xs text-gray-400">{{
                      cite(event.sources)
                    }}</span>
                  </li>
                }
              </ol>
            </div>
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">Places</h3>
              <ul class="mt-2 space-y-2 text-sm">
                @for (place of brief.places ?? []; track $index) {
                  <li>
                    <span class="font-medium text-gray-900">{{ place.name }}</span>
                    <span class="text-gray-700"> {{ place.description }}</span>
                  </li>
                }
              </ul>
              <h3 class="mt-5 text-xs font-semibold uppercase tracking-wide text-gray-500">
                Period details
              </h3>
              <ul class="mt-2 space-y-1 text-sm text-gray-700">
                @for (detail of brief.details ?? []; track $index) {
                  <li>– {{ detail.text }}</li>
                }
              </ul>
            </div>
            <div>
              <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">
                Recorded words
              </h3>
              @for (quote of brief.quotes ?? []; track $index) {
                <blockquote class="mt-2 border-l-2 border-gray-200 pl-3 text-sm">
                  <p class="text-gray-900">“{{ quote.words }}”</p>
                  <p class="text-xs text-gray-500">
                    {{ quote.speaker }}{{ quote.context ? ', ' + quote.context : '' }}
                    <span class="font-mono">#{{ quote.source }}</span>
                  </p>
                </blockquote>
              } @empty {
                <p class="mt-2 text-sm text-gray-500">None found.</p>
              }
              <h3 class="mt-5 text-xs font-semibold uppercase tracking-wide text-gray-500">
                Sources
              </h3>
              <ul class="mt-2 space-y-1 text-sm text-gray-700">
                @for (source of brief.sources ?? []; track $index) {
                  <li>
                    <span class="font-mono text-xs text-gray-400">#{{ source.passageId }}</span>
                    {{ source.why }}
                  </li>
                }
              </ul>
            </div>
          </div>
        </section>
      }

      <section class="mt-8">
        <h2 class="text-lg font-semibold text-gray-900">
          Trail
          <span class="ml-1 text-sm font-normal text-gray-400">{{ reply.events.length }}</span>
        </h2>
        <div class="mt-3 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
          <table class="w-full text-left text-sm">
            <thead class="border-b border-gray-200 text-gray-500">
              <tr>
                <th class="px-4 py-3 font-medium">#</th>
                <th class="px-4 py-3 font-medium">Step</th>
                <th class="px-4 py-3 font-medium">What</th>
                <th class="px-4 py-3 font-medium text-right">Cost</th>
                <th class="px-4 py-3 font-medium">At</th>
                <th class="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-100">
              @for (event of reply.events; track event.seq) {
                <tr class="hover:bg-gray-50">
                  <td class="px-4 py-3 text-gray-500">{{ event.seq }}</td>
                  <td class="px-4 py-3 whitespace-nowrap text-gray-700">
                    {{ kindLabel[event.kind] ?? event.kind }}
                  </td>
                  <td class="px-4 py-3 text-gray-900" [class.text-red-700]="event.kind === 'error'">
                    {{ event.summary }}
                  </td>
                  <td class="px-4 py-3 text-right text-gray-700 whitespace-nowrap">
                    {{ event.costUsdMicros ? usd(event.costUsdMicros) : '' }}
                  </td>
                  <td class="px-4 py-3 text-gray-600 whitespace-nowrap">
                    {{ toDate(event.atUnixMs) | date: 'mediumTime' }}
                  </td>
                  <td class="px-4 py-3 text-right">
                    @if (event.detail) {
                      <button
                        type="button"
                        class="text-xs text-indigo-600 hover:underline"
                        (click)="toggle(event.seq)"
                      >
                        {{ opened() === event.seq ? 'Hide' : 'Show' }}
                      </button>
                    }
                  </td>
                </tr>
                @if (opened() === event.seq) {
                  <tr>
                    <td colspan="6" class="bg-gray-50 px-4 py-4">
                      <pre
                        class="max-h-[32rem] overflow-auto rounded-lg border border-gray-200 bg-white p-3 text-xs whitespace-pre-wrap text-gray-800"
                        >{{ event.detail }}</pre
                      >
                    </td>
                  </tr>
                }
              } @empty {
                <tr>
                  <td colspan="6" class="px-4 py-8 text-center text-gray-500">
                    Nothing has happened yet.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class HistoryRun {
  private readonly api = inject(TheplotAdminApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly statusLabel = RUN_STATUS_LABEL;
  protected readonly statusClass = RUN_STATUS_CLASS;
  protected readonly kindLabel = EVENT_KIND_LABEL;
  protected readonly isRunActive = isRunActive;
  protected readonly runElapsed = runElapsed;
  protected readonly runLabel = runLabel;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  protected readonly reply = signal<GetHistoryStoryRunReply | null>(null);
  protected readonly brief = computed(() => parseBrief(this.reply()?.briefJson ?? ''));
  protected readonly error = signal('');
  protected readonly opened = signal<number | null>(null);
  protected readonly cancelling = signal(false);

  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    afterNextRender(() => void this.load());
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  protected cite(sources: number[] | undefined): string {
    return sources?.length ? sources.map((id) => `#${id}`).join(' ') : '';
  }

  protected toggle(seq: number): void {
    this.opened.update((open) => (open === seq ? null : seq));
  }

  protected async cancel(): Promise<void> {
    this.cancelling.set(true);
    try {
      await this.api.history.cancelStoryRun({ runId: this.id() });
      await this.load();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.cancelling.set(false);
    }
  }

  private async load(): Promise<void> {
    this.stopPolling();
    try {
      const reply = await this.api.history.getStoryRun({ runId: this.id() });
      this.reply.set(reply);
      if (reply.run && isRunActive(reply.run.status)) {
        this.timer = setTimeout(() => void this.load(), POLL_MS);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  private stopPolling(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}

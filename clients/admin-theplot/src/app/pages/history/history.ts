import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import type {
  GetHistoryCorpusReply,
  HistoryStoryRun,
  HistoryWriter,
} from '../../../lib/gen/Admin/Theplot/theplot_history_pb';
import { shortId, toDate, usd } from '../../engine/format';
import {
  isRunActive,
  RUN_STATUS_CLASS,
  RUN_STATUS_LABEL,
  runElapsed,
  runLabel,
} from '../../history/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

const PAGE_SIZE = 25;
const POLL_MS = 5_000;
const OTHER_WRITER = '';

/** Development only: write a story from the history corpus, and the runs that did. */
@Component({
  selector: 'app-history',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex items-center gap-3">
      <h1 class="text-2xl font-bold text-gray-900">History</h1>
      <span
        class="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-800 ring-1 ring-amber-200"
        >Development only</span
      >
    </div>
    <p class="mt-2 max-w-3xl text-gray-600">
      Stories written from Project Gutenberg's American history books. A model searches the corpus
      for a moment, yours or a random one, builds a research brief from what it reads, and writes it
      as a narrated documentary into the open library, where every writer can import it, or into one
      writer's library.
    </p>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <section class="mt-6 grid gap-6 lg:grid-cols-3">
      <form
        class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm lg:col-span-2"
        (submit)="start($event)"
      >
        <label for="history-request" class="block text-sm font-medium text-gray-900"
          >What should the story be about?</label
        >
        <textarea
          id="history-request"
          rows="3"
          maxlength="2000"
          class="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none"
          placeholder="The last night at Valley Forge, told from a surgeon's tent"
          [value]="request()"
          (input)="request.set($any($event.target).value)"
        ></textarea>
        <p class="mt-1 text-xs text-gray-500">
          Leave it empty for a random moment from the corpus.
        </p>

        <div class="mt-4 grid gap-4 sm:grid-cols-3">
          <div class="sm:col-span-2">
            <label for="history-writer" class="block text-sm font-medium text-gray-900"
              >Owner</label
            >
            <select
              id="history-writer"
              class="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
              (change)="pickWriter($any($event.target).value)"
            >
              @if (openLibraryId(); as open) {
                <option [value]="open" [selected]="picked() === open">
                  Open library · any writer can import it
                </option>
              }
              @for (writer of writers(); track writer.id) {
                <option [value]="writer.id" [selected]="writer.id === picked()">
                  {{ shortId(writer.id) }} · {{ writer.storyCount }}
                  {{ writer.storyCount === 1 ? 'story' : 'stories' }} ·
                  {{ writer.latestStoryTitle }}
                </option>
              }
              <option [value]="other" [selected]="picked() === other">Another writer…</option>
            </select>
            @if (picked() === other) {
              <input
                type="text"
                class="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 font-mono text-xs"
                placeholder="The writer's subject id in the theplot realm"
                [value]="typed()"
                (input)="typed.set($any($event.target).value)"
              />
            }
          </div>
          <div>
            <label for="history-scenes" class="block text-sm font-medium text-gray-900"
              >Scenes</label
            >
            <input
              id="history-scenes"
              type="number"
              min="1"
              max="20"
              class="mt-2 w-full rounded-lg border border-gray-300 px-3 py-2 text-sm"
              [value]="scenes()"
              (input)="scenes.set(+$any($event.target).value)"
            />
          </div>
        </div>

        <button
          type="submit"
          class="mt-5 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          [disabled]="!canStart()"
        >
          {{
            starting() ? 'Queuing…' : request().trim() ? 'Write the story' : 'Write a random moment'
          }}
        </button>
      </form>

      <aside class="rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm">
        <h2 class="font-semibold text-gray-900">Corpus</h2>
        @if (corpus(); as corpus) {
          @if (hasBooks()) {
            <p class="mt-2 text-gray-700">
              {{ corpus.books }} books in {{ corpus.passages }} passages.
            </p>
            <p class="mt-2 text-xs text-gray-500">
              The history-loader resource in the Aspire dashboard adds the books still missing.
            </p>
          } @else {
            <p class="mt-2 text-gray-700">The corpus is empty.</p>
            <p class="mt-2 text-xs text-gray-500">
              Start the history-loader resource in the Aspire dashboard. It downloads about 5,200
              books (a few GB, kept under ~/.cache/protofast/gutenberg) and can be stopped and
              started again; loaded books are kept.
            </p>
          }
        } @else {
          <p class="mt-2 text-gray-500">Loading…</p>
        }
      </aside>
    </section>

    <h2 class="mt-10 text-lg font-semibold text-gray-900">Runs</h2>
    <div class="mt-3 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-gray-200 text-gray-500">
          <tr>
            <th class="px-4 py-3 font-medium">Story</th>
            <th class="px-4 py-3 font-medium">Status</th>
            <th class="px-4 py-3 font-medium">Owner</th>
            <th class="px-4 py-3 font-medium text-right">Scenes</th>
            <th class="px-4 py-3 font-medium text-right">Cost</th>
            <th class="px-4 py-3 font-medium">Requested</th>
            <th class="px-4 py-3 font-medium">Took</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          @for (run of runs(); track run.id) {
            <tr class="hover:bg-gray-50">
              <td class="px-4 py-3">
                <a [routerLink]="['/history', run.id]" class="text-indigo-600 hover:underline">{{
                  runLabel(run)
                }}</a>
                @if (isRunActive(run.status)) {
                  <p class="mt-0.5 text-xs text-gray-500">{{ run.message }}</p>
                } @else if (run.failure) {
                  <p class="mt-0.5 text-xs text-red-700">{{ run.failure }}</p>
                }
              </td>
              <td class="px-4 py-3">
                <span [class]="statusClass[run.status]">{{ statusLabel[run.status] }}</span>
              </td>
              <td class="px-4 py-3 font-mono text-xs text-gray-600">{{ shortId(run.writerId) }}</td>
              <td class="px-4 py-3 text-right text-gray-900">{{ run.scenes }}</td>
              <td class="px-4 py-3 text-right text-gray-900">{{ usd(run.costUsdMicros) }}</td>
              <td class="px-4 py-3 text-gray-600 whitespace-nowrap">
                {{ toDate(run.createdUnixMs) | date: 'medium' }}
              </td>
              <td class="px-4 py-3 text-gray-600 whitespace-nowrap">{{ runElapsed(run) }}</td>
            </tr>
          } @empty {
            <tr>
              <td colspan="7" class="px-4 py-6 text-gray-500">
                {{ loading() ? 'Loading…' : 'No story has been asked for yet.' }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    @if (pages() > 1) {
      <div class="mt-4 flex items-center gap-3 text-sm text-gray-600">
        <button
          type="button"
          class="rounded-lg border border-gray-300 px-3 py-1.5 hover:bg-gray-50 disabled:opacity-50"
          [disabled]="page() === 0 || loading()"
          (click)="go(page() - 1)"
        >
          Previous
        </button>
        <span>Page {{ page() + 1 }} of {{ pages() }}</span>
        <button
          type="button"
          class="rounded-lg border border-gray-300 px-3 py-1.5 hover:bg-gray-50 disabled:opacity-50"
          [disabled]="page() + 1 >= pages() || loading()"
          (click)="go(page() + 1)"
        >
          Next
        </button>
      </div>
    }
  `,
})
export class History {
  private readonly api = inject(TheplotAdminApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statusLabel = RUN_STATUS_LABEL;
  protected readonly statusClass = RUN_STATUS_CLASS;
  protected readonly isRunActive = isRunActive;
  protected readonly runElapsed = runElapsed;
  protected readonly runLabel = runLabel;
  protected readonly shortId = shortId;
  protected readonly toDate = toDate;
  protected readonly usd = usd;
  protected readonly other = OTHER_WRITER;

  protected readonly corpus = signal<GetHistoryCorpusReply | null>(null);
  protected readonly writers = signal<HistoryWriter[]>([]);
  protected readonly openLibraryId = signal('');
  protected readonly runs = signal<HistoryStoryRun[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(true);
  protected readonly starting = signal(false);
  protected readonly error = signal('');

  protected readonly request = signal('');
  protected readonly picked = signal(OTHER_WRITER);
  protected readonly typed = signal('');
  protected readonly scenes = signal(6);

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly hasBooks = computed(() => (this.corpus()?.books ?? 0n) > 0n);
  private readonly writerId = computed(() =>
    (this.picked() === OTHER_WRITER ? this.typed() : this.picked()).trim(),
  );
  protected readonly canStart = computed(
    () =>
      !this.starting() &&
      this.writerId().length > 0 &&
      this.scenes() >= 1 &&
      this.scenes() <= 20 &&
      this.hasBooks(),
  );

  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    afterNextRender(() => void this.init());
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  protected pickWriter(id: string): void {
    this.picked.set(id);
  }

  protected async start(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.canStart()) {
      return;
    }
    this.starting.set(true);
    this.error.set('');
    try {
      const reply = await this.api.history.startStory({
        writerId: this.writerId(),
        request: this.request().trim(),
        scenes: this.scenes(),
      });
      await this.router.navigate(['/history', reply.runId]);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.starting.set(false);
    }
  }

  protected async go(page: number): Promise<void> {
    this.stopPolling();
    this.loading.set(true);
    try {
      const reply = await this.api.history.listStoryRuns({ page, pageSize: PAGE_SIZE });
      this.runs.set(reply.runs);
      this.total.set(reply.total);
      this.page.set(page);
      if (reply.runs.some((run) => isRunActive(run.status))) {
        this.timer = setTimeout(() => void this.go(this.page()), POLL_MS);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  private async init(): Promise<void> {
    try {
      const [corpus, writers] = await Promise.all([
        this.api.history.getCorpus({}),
        this.api.history.listWriters({}),
      ]);
      this.corpus.set(corpus);
      this.writers.set(writers.writers);
      this.openLibraryId.set(writers.openLibraryId);
      this.picked.set(writers.openLibraryId || writers.writers[0]?.id || OTHER_WRITER);
    } catch (err) {
      this.error.set(errorMessage(err));
    }
    await this.go(0);
  }

  private stopPolling(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}

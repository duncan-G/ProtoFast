import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import {
  RunMode,
  RunStatus,
  type FamilySummary,
  type RunHeader,
} from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import {
  elapsed,
  familyKey,
  NEUTRAL_BADGE,
  OUTCOME_CLASS,
  OUTCOME_LABEL,
  outcome,
  RUN_MODE_LABEL,
  RUN_STATUS_LABEL,
  shortId,
  toDate,
  usd,
} from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

const PAGE_SIZE = 25;

/** Every run the engine recorded, newest first, narrowed by family, mode and status. */
@Component({
  selector: 'app-runs',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Runs</h1>
        <p class="mt-2 text-gray-600">
          Every document import the engine recorded, newest first. Open a run to see each stage
          attempt, what it read and wrote, and the agent’s conversation.
        </p>
      </div>
    </div>

    <form class="mt-6 flex flex-wrap items-end gap-3 text-sm" (submit)="$event.preventDefault()">
      <label class="flex flex-col gap-1 text-gray-600">
        Family
        <select
          class="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-gray-900"
          [value]="family()"
          (change)="setFamily($any($event.target).value)"
        >
          <option value="">All families</option>
          @for (option of families(); track option.family) {
            <option [value]="option.family">{{ option.info?.displayName || option.family }}</option>
          }
          @if (family() && !knownFamily()) {
            <option [value]="family()">{{ family() }}</option>
          }
        </select>
      </label>
      <label class="flex flex-col gap-1 text-gray-600">
        Mode
        <select
          class="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-gray-900"
          [value]="mode()"
          (change)="setMode($any($event.target).value)"
        >
          <option [value]="RunMode.UNSPECIFIED">Any mode</option>
          <option [value]="RunMode.DISCOVERY">Discovery</option>
          <option [value]="RunMode.SCHEDULED">Scheduled</option>
        </select>
      </label>
      <label class="flex flex-col gap-1 text-gray-600">
        Status
        <select
          class="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-gray-900"
          [value]="status()"
          (change)="setStatus($any($event.target).value)"
        >
          @for (option of statuses; track option) {
            <option [value]="option">{{ statusLabel[option] }}</option>
          }
        </select>
      </label>
      <span class="ml-auto self-center text-gray-500">
        {{ total() }} {{ total() === 1 ? 'run' : 'runs' }}
      </span>
    </form>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <div class="mt-4 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-gray-200 text-gray-500">
          <tr>
            <th class="px-4 py-3 font-medium">Run</th>
            <th class="px-4 py-3 font-medium">Name</th>
            <th class="px-4 py-3 font-medium">Family</th>
            <th class="px-4 py-3 font-medium">Mode</th>
            <th class="px-4 py-3 font-medium">Opened</th>
            <th class="px-4 py-3 font-medium">Took</th>
            <th class="px-4 py-3 font-medium text-right">Attempts</th>
            <th class="px-4 py-3 font-medium">Outcome</th>
            <th class="px-4 py-3 font-medium text-right">Cost</th>
            <th class="px-4 py-3 font-medium">Source</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          @for (run of runs(); track run.runId) {
            <tr class="hover:bg-gray-50">
              <td class="px-4 py-3">
                <a
                  [routerLink]="['/runs', run.runId]"
                  class="font-mono text-xs text-indigo-600 hover:underline"
                  [title]="run.runId"
                  >{{ shortId(run.runId) }}</a
                >
              </td>
              <td class="max-w-xs truncate px-4 py-3 text-gray-900" [title]="run.name">
                {{ run.name || '—' }}
              </td>
              <td class="px-4 py-3">
                <a [routerLink]="['/families', run.family]" class="text-gray-900 hover:underline">{{
                  run.family
                }}</a>
                @if (run.generation > 0) {
                  <span class="ml-1 {{ neutralBadge }}">gen {{ run.generation }}</span>
                }
              </td>
              <td class="px-4 py-3 text-gray-700">{{ modeLabel[run.mode] }}</td>
              <td class="px-4 py-3 text-gray-700 whitespace-nowrap">
                {{ toDate(run.openedUnixMs) | date: 'MMM d, HH:mm:ss' }}
              </td>
              <td class="px-4 py-3 text-gray-700 whitespace-nowrap">{{ elapsed(run) }}</td>
              <td class="px-4 py-3 text-right text-gray-900">{{ run.stageAttempts }}</td>
              <td class="px-4 py-3">
                <span [class]="outcomeClass[outcome(run)]">{{ outcomeLabel[outcome(run)] }}</span>
              </td>
              <td class="px-4 py-3 text-right text-gray-900 whitespace-nowrap">
                {{ usd(run.costUsdMicros) }}
              </td>
              <td class="px-4 py-3 font-mono text-xs text-gray-500" [title]="run.sourceId">
                {{ run.sourceId ? shortId(run.sourceId) : '—' }}
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="10" class="px-4 py-8 text-center text-gray-500">
                {{ loading() ? 'Loading…' : 'No runs match.' }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

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
  `,
})
export class Runs {
  private readonly api = inject(TheplotAdminApi);
  private readonly router = inject(Router);

  /** Bound from `?family=`, so a family page can link straight to its runs. */
  readonly family = input<string>('');

  protected readonly RunMode = RunMode;
  protected readonly statuses = [
    RunStatus.UNSPECIFIED,
    RunStatus.OPEN,
    RunStatus.CLOSED,
    RunStatus.ABANDONED,
  ];
  protected readonly statusLabel = RUN_STATUS_LABEL;
  protected readonly modeLabel = RUN_MODE_LABEL;
  protected readonly outcomeLabel = OUTCOME_LABEL;
  protected readonly outcomeClass = OUTCOME_CLASS;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly outcome = outcome;
  protected readonly elapsed = elapsed;
  protected readonly shortId = shortId;
  protected readonly toDate = toDate;
  protected readonly usd = usd;
  protected readonly familyKey = familyKey;

  protected readonly mode = signal(RunMode.UNSPECIFIED);
  protected readonly status = signal(RunStatus.UNSPECIFIED);
  protected readonly runs = signal<RunHeader[]>([]);
  protected readonly families = signal<FamilySummary[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly knownFamily = computed(() =>
    this.families().some((f) => f.family === this.family()),
  );

  private rendered = false;

  constructor() {
    afterNextRender(() => {
      this.rendered = true;
      void this.loadFamilies();
      void this.go(0);
    });
    // A family change from the URL reloads the first page; before render nothing has loaded.
    effect(() => {
      this.family();
      if (this.rendered) {
        void this.go(0);
      }
    });
  }

  protected setFamily(family: string): void {
    void this.router.navigate([], { queryParams: { family: family || null } });
  }

  protected setMode(value: string): void {
    this.mode.set(Number(value) as RunMode);
    void this.go(0);
  }

  protected setStatus(value: string): void {
    this.status.set(Number(value) as RunStatus);
    void this.go(0);
  }

  protected async go(page: number): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const reply = await this.api.runs.listRuns({
        family: this.family(),
        mode: this.mode(),
        status: this.status(),
        page,
        pageSize: PAGE_SIZE,
      });
      this.runs.set(reply.runs);
      this.total.set(reply.total);
      this.page.set(page);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadFamilies(): Promise<void> {
    try {
      this.families.set((await this.api.families.listFamilies({})).families);
    } catch {
      // The filter still accepts the family in the URL; the list is a convenience.
    }
  }
}

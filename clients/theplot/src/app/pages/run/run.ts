import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { PhaseLadder } from '../../shared/phase-ladder';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import {
  PHASE_LABELS,
  isTerminal,
  phaseLabel,
  suggestedRerunPhase,
} from '../../segmentation/phases';
import { PhaseState, type Run, type RunEvent } from '../../../lib/gen/segmentation_pb';

/** Uploads expire seven days after they are taken (the bucket's `expire-uploads` lifecycle rule). */
const UPLOAD_LIFETIME_SECONDS = 7 * 24 * 60 * 60;

/**
 * Live progress for one run.
 *
 * It subscribes to `WatchRun`, which is a server-streaming tail of the run's event rows — so the
 * page shows what the worker is doing without polling, and a run that finishes in three seconds
 * looks like it finished in three seconds. Each event triggers a `GetRun` so the phase ladder
 * reflects the authoritative state rather than one assembled from event messages.
 *
 * A run that stopped can be re-queued from a chosen phase. `RerunFrom` keeps the run id and
 * resets the phase rows at and after that phase, so the page re-watches the same run rather than
 * navigating anywhere — which is also why the activity list is emptied first: `WatchRun` replays
 * a run's events from the beginning, and keeping the old ones would show each twice.
 */
@Component({
  selector: 'app-run',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, PhaseLadder, RouterLink, CurrencyPipe],
  template: `
    <app-shell>
      @if (run(); as current) {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight">{{ current.documentId }}</h1>
            <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-[var(--color-neutral-400)]">
              <span class="font-mono text-xs">{{ current.runId }}</span>
              @if (current.condition) {
                <span class="chip">{{ current.condition }}</span>
              }
              @if (current.family && current.family !== 'unknown') {
                <span class="chip">{{ current.family }}</span>
              }
              @if (current.costUsd > 0) {
                <span>{{ current.costUsd | currency: 'USD' : 'symbol' : '1.2-4' }}</span>
              }
            </p>
          </div>

          <div class="flex shrink-0 items-center gap-2">
            @if (done()) {
              <a [routerLink]="['/app/runs', current.runId, 'scenes']" class="btn btn-primary">
                Read the scenes
              </a>
              <a [routerLink]="['/app/runs', current.runId, 'tree']" class="btn btn-secondary">
                Browse the tree
              </a>
              <a [routerLink]="['/app/runs', current.runId, 'augmentations']" class="btn btn-secondary">
                Key points
              </a>
            } @else if (!stopped()) {
              <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
            }
          </div>
        </div>

        @if (actionError(); as message) {
          <p class="mt-3 text-sm text-[#f0a3a3]" role="alert">{{ message }}</p>
        }

        @if (current.reviewState === 'pending') {
          <div
            class="mt-6 rounded-[var(--radius-lg)] border border-[var(--color-accent-600)] bg-[var(--color-surface)] px-4 py-3 text-sm"
          >
            This document is waiting for a person to check its structure before it is frozen.
            <a routerLink="/app/reviews" class="ml-1 text-[var(--color-accent-300)] underline">
              Open the review queue
            </a>
          </div>
        }

        @if (stopped()) {
          <div
            class="mt-6 rounded-[var(--radius-lg)] border border-[#7a3a3a] bg-[#2a1c1c] px-4 py-3"
            role="alert"
          >
            <p class="text-sm font-medium text-[#f0a3a3]">
              {{ current.error ? 'This run stopped.' : 'This run was cancelled.' }}
            </p>
            @if (current.error) {
              <p class="mt-1 font-mono text-xs break-words text-[#f0a3a3]/85">{{ current.error }}</p>
            }

            <div class="mt-4 flex flex-wrap items-center gap-2">
              <label for="rerun-phase" class="text-sm text-[var(--color-neutral-400)]">
                Re-run from
              </label>
              <select
                id="rerun-phase"
                class="field"
                [disabled]="rerunning()"
                (change)="choosePhase($event)"
              >
                @for (phase of phases; track phase.index) {
                  <option [value]="phase.index" [selected]="phase.index === rerunPhase()">
                    {{ phase.index }} — {{ phase.name }}
                  </option>
                }
              </select>
              <button
                type="button"
                class="btn btn-primary"
                [disabled]="rerunning()"
                (click)="rerun()"
              >
                {{ rerunning() ? 'Re-queueing…' : 'Re-run' }}
              </button>
            </div>

            <p class="mt-2 text-xs text-[var(--color-neutral-500)]">{{ rerunBlurb() }}</p>

            @if (rerunPhase() === 0 && uploadExpired()) {
              <p class="mt-2 text-xs text-[#f0a3a3]/85">
                This run is more than seven days old, so its original upload has expired. Re-running
                from Ingest will fail — upload the document again instead.
              </p>
            }
          </div>
        }

        <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,22rem)]">
          <section>
            <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">Progress</h2>
            <div class="mt-3">
              <app-phase-ladder [phases]="current.phases" />
            </div>
          </section>

          <section>
            <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">Activity</h2>
            <ol class="mt-3 space-y-1.5 text-xs">
              @for (event of events(); track event.sequence) {
                <li class="flex gap-2">
                  <span class="shrink-0 font-mono text-[var(--color-neutral-600)]">
                    {{ time(event) }}
                  </span>
                  <span class="min-w-0">
                    <span class="text-[var(--color-neutral-400)]">{{ event.phase }}</span>
                    <span class="text-[var(--color-neutral-500)]"> — {{ event.message }}</span>
                  </span>
                </li>
              } @empty {
                <li class="text-[var(--color-neutral-600)]">Nothing yet.</li>
              }
            </ol>

            @if (pinned().length > 0) {
              <h2 class="mt-8 text-sm font-medium text-[var(--color-neutral-400)]">Models used</h2>
              <ul class="mt-3 space-y-1 text-xs text-[var(--color-neutral-500)]">
                @for (entry of pinned(); track entry.phase) {
                  <li><span class="text-[var(--color-neutral-400)]">{{ entry.phase }}</span> — {{ entry.model }}</li>
                }
              </ul>
            }
          </section>
        </div>
      } @else if (error(); as message) {
        <p class="text-sm text-[#f0a3a3]" role="alert">{{ message }}</p>
      } @else {
        <p class="text-sm text-[var(--color-neutral-500)]">Loading…</p>
      }
    </app-shell>
  `,
  styles: `
    .chip {
      border-radius: 999px;
      border: 1px solid var(--color-divider);
      padding: 0.0625rem 0.5rem;
      font-size: 0.75rem;
    }
  `,
})
export class RunPage {
  private readonly api = inject(SegmentationApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly run = signal<Run | null>(null);
  protected readonly events = signal<RunEvent[]>([]);
  protected readonly error = signal<string | null>(null);

  /** A failed cancel or re-run. Separate from {@link error}, which is the page failing to load. */
  protected readonly actionError = signal<string | null>(null);

  protected readonly rerunning = signal(false);
  protected readonly phases = PHASE_LABELS;

  /** The phase the person picked, or null while the suggested one still stands. */
  private readonly chosenPhase = signal<number | null>(null);

  private readonly runId = this.route.snapshot.paramMap.get('runId') ?? '';

  /** The live `WatchRun` stream, aborted on destroy and replaced on a re-run. */
  private watching: AbortController | null = null;

  constructor() {
    // The stream is a long-lived request through Envoy's /api/ route, which has no timeout — but
    // a user navigating away must not leave it open, hence the explicit abort.
    inject(DestroyRef).onDestroy(() => this.watching?.abort());

    void this.load();
  }

  protected done(): boolean {
    const current = this.run();
    return current !== null && isTerminal(current) && !current.cancelled && !current.error;
  }

  /** Stopped short of publishing — the only state a re-run is offered from. */
  protected stopped(): boolean {
    const current = this.run();
    return current !== null && (current.cancelled || current.error !== '');
  }

  /** The chosen phase, or the one {@link suggestedRerunPhase} points at until someone picks. */
  protected readonly rerunPhase = computed(
    () => this.chosenPhase() ?? suggestedRerunPhase(this.run()?.phases ?? []),
  );

  protected readonly rerunBlurb = computed(() => {
    const index = this.rerunPhase();
    const name = phaseLabel(index);

    return index === 0
      ? 'The whole document is processed again from the original upload.'
      : `Phases before ${name} keep the output they already produced; ${name} and everything after it run again.`;
  });

  /** Whether the run predates its upload's seven-day lifetime, which is what phase 0 needs. */
  protected readonly uploadExpired = computed(() => {
    const created = Number(this.run()?.createdUnixSeconds ?? 0n);
    return created > 0 && Date.now() / 1000 - created > UPLOAD_LIFETIME_SECONDS;
  });

  protected pinned(): { phase: string; model: string }[] {
    const models = this.run()?.pinnedModels ?? {};
    return Object.entries(models).map(([phase, model]) => ({ phase, model }));
  }

  protected time(event: RunEvent): string {
    return new Date(Number(event.atUnixSeconds) * 1000).toLocaleTimeString([], {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  protected choosePhase(event: Event): void {
    this.chosenPhase.set(Number((event.target as HTMLSelectElement).value));
  }

  protected async cancel(): Promise<void> {
    try {
      this.actionError.set(null);
      this.run.set(await this.api.cancelRun(this.runId));
    } catch (cause) {
      this.actionError.set(
        cause instanceof Error ? cause.message : 'The run could not be cancelled.',
      );
    }
  }

  protected async rerun(): Promise<void> {
    if (this.rerunning()) {
      return;
    }

    this.rerunning.set(true);
    this.actionError.set(null);

    try {
      await this.api.rerunFrom(this.runId, this.rerunPhase());

      // The run keeps its id and its phase rows are reset server-side, so the ladder is re-read
      // rather than patched here. The activity list starts over because WatchRun replays from the
      // first event; the re-queue note the server just wrote arrives with the rest.
      this.events.set([]);
      this.chosenPhase.set(null);
      this.run.set(await this.api.getRun(this.runId));
      this.startWatch();
    } catch (cause) {
      this.actionError.set(
        cause instanceof Error ? cause.message : 'The run could not be re-queued.',
      );
    } finally {
      this.rerunning.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.run.set(await this.api.getRun(this.runId));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'That run could not be loaded.');
      return;
    }

    this.startWatch();
  }

  /**
   * Starts tailing the run, replacing any stream already open.
   *
   * The old one is aborted rather than left to end on its own: a re-run is submitted the moment
   * the run reads as stopped, which can be a poll interval before the server closes the stream —
   * and two loops writing to the same signals would interleave the old run's tail with the new
   * run's head.
   */
  private startWatch(): void {
    this.watching?.abort();

    const abort = new AbortController();
    this.watching = abort;

    void this.watch(abort.signal);
  }

  private async watch(signal: AbortSignal): Promise<void> {
    try {
      for await (const event of this.api.watchRun(this.runId, signal)) {
        this.events.update((events) => [...events.slice(-199), event]);

        // Re-read the run on any transition, so the ladder is the server's answer rather than one
        // inferred from event text.
        if (event.state !== PhaseState.RUNNING || event.message === 'started') {
          this.run.set(await this.api.getRun(this.runId));
        }
      }
    } catch {
      // The stream ending — because the run finished, or because the page is unloading — is not
      // an error worth showing. A final read settles the display either way.
      if (!signal.aborted) {
        this.run.set(await this.api.getRun(this.runId).catch(() => this.run()));
      }
    }
  }
}

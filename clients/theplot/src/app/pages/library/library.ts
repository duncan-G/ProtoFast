import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { AuthIdentityService } from '../../auth/auth-identity';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import { isTerminal, phaseLabel } from '../../segmentation/phases';
import { PhaseState, type Run } from '../../../lib/gen/segmentation_pb';

/**
 * The user's documents and runs.
 *
 * There is no ListRuns RPC — the plan's surface (§17) is deliberately per-run — so the library
 * keeps the run ids it has seen in local storage and asks for each one. That is enough for the
 * single-user case this screen serves, and it means a user who clears storage loses a list, not
 * a document: every run is still reachable by id, and the result rows are in Postgres.
 */
@Component({
  selector: 'app-library',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, RouterLink, CurrencyPipe],
  template: `
    <app-shell [showReviews]="isReviewer()">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold tracking-tight">Your documents</h1>
          <p class="mt-1 text-sm text-[var(--color-neutral-400)]">
            Every document you have segmented, newest first.
          </p>
        </div>
        <a routerLink="/app/upload" class="btn btn-primary">Upload a document</a>
      </div>

      @if (loading()) {
        <p class="mt-10 text-sm text-[var(--color-neutral-500)]">Loading…</p>
      } @else if (runs().length === 0) {
        <div
          class="mt-10 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-divider)] px-6 py-16 text-center"
        >
          <h2 class="text-lg font-medium">Nothing here yet</h2>
          <p class="mx-auto mt-2 max-w-md text-sm text-[var(--color-neutral-400)]">
            Upload a Markdown document — clean, converted from a PDF, or a flat transcript — and
            ThePlot will give you a paragraph list and a section tree.
          </p>
          <a routerLink="/app/upload" class="btn btn-primary mt-6 inline-flex">Upload a document</a>
        </div>
      } @else {
        <ul class="mt-8 space-y-2">
          @for (run of runs(); track run.runId) {
            <li
              class="rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-4"
            >
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <a
                    [routerLink]="['/app/runs', run.runId]"
                    class="truncate text-base font-medium hover:text-[var(--color-accent-300)]"
                  >
                    {{ run.documentId }}
                  </a>
                  <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-[var(--color-neutral-500)]">
                    <span>{{ describe(run) }}</span>
                    @if (run.condition) {
                      <span class="chip">{{ run.condition }}</span>
                    }
                    @if (run.family && run.family !== 'unknown') {
                      <span class="chip">{{ run.family }}</span>
                    }
                    @if (run.costUsd > 0) {
                      <span>{{ run.costUsd | currency: 'USD' : 'symbol' : '1.2-4' }}</span>
                    }
                  </p>
                </div>

                <div class="flex shrink-0 items-center gap-2 text-sm">
                  @if (run.reviewState === 'pending') {
                    <a [routerLink]="['/app/reviews']" class="chip chip-accent">Needs review</a>
                  }
                  @if (finished(run)) {
                    <a [routerLink]="['/app/runs', run.runId, 'tree']" class="btn btn-secondary">Tree</a>
                  }
                </div>
              </div>
            </li>
          }
        </ul>
      }
    </app-shell>
  `,
  styles: `
    .chip {
      border-radius: 999px;
      border: 1px solid var(--color-divider);
      padding: 0.0625rem 0.5rem;
    }
    .chip-accent {
      border-color: var(--color-accent-600);
      color: var(--color-accent-300);
    }
  `,
})
export class Library {
  private readonly api = inject(SegmentationApi);
  private readonly auth = inject(AuthIdentityService);

  protected readonly runs = signal<Run[]>([]);
  protected readonly loading = signal(true);

  protected readonly isReviewer = computed(() =>
    this.auth.identity.roles.includes('segmentation-reviewer'),
  );

  constructor() {
    void this.load();
  }

  protected finished(run: Run): boolean {
    return isTerminal(run) && !run.cancelled && !run.error;
  }

  protected describe(run: Run): string {
    if (run.cancelled) {
      return 'Cancelled';
    }

    if (run.error) {
      return 'Failed';
    }

    const running = run.phases.find((p) => p.state === PhaseState.RUNNING);
    if (running) {
      return `${phaseLabel(running.index)}…`;
    }

    return isTerminal(run) ? 'Ready' : 'Queued';
  }

  private async load(): Promise<void> {
    try {
      const ids = RunHistory.read();
      const runs = await Promise.all(
        ids.map((id) =>
          this.api.getRun(id).catch(() => null),
        ),
      );

      // A run the server no longer knows about is dropped from history rather than shown as an
      // error: the likeliest cause is a rebuilt dev database, and a permanent broken row is worse
      // than a missing one.
      const found = runs.filter((r): r is Run => r !== null);
      RunHistory.write(found.map((r) => r.runId));

      this.runs.set(found.sort((a, b) => Number(b.createdUnixSeconds - a.createdUnixSeconds)));
    } finally {
      this.loading.set(false);
    }
  }
}

/**
 * The run ids this browser has submitted. Local to the device by design — it is a convenience
 * index, not a source of truth, and the server's ownership check is what actually protects a run.
 */
export const RunHistory = {
  key: 'theplot.runs',

  read(): string[] {
    if (typeof localStorage === 'undefined') {
      return [];
    }

    try {
      const parsed: unknown = JSON.parse(localStorage.getItem(this.key) ?? '[]');
      return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : [];
    } catch {
      return [];
    }
  },

  write(ids: string[]): void {
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(this.key, JSON.stringify(ids.slice(0, 200)));
    }
  },

  add(id: string): void {
    const ids = this.read().filter((existing) => existing !== id);
    this.write([id, ...ids]);
  },
};

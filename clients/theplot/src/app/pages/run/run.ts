import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { PhaseLadder } from '../../shared/phase-ladder';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import { isTerminal } from '../../segmentation/phases';
import { PhaseState, type Run, type RunEvent } from '../../../lib/gen/segmentation_pb';

/**
 * Live progress for one run.
 *
 * It subscribes to `WatchRun`, which is a server-streaming tail of the run's event rows — so the
 * page shows what the worker is doing without polling, and a run that finishes in three seconds
 * looks like it finished in three seconds. Each event triggers a `GetRun` so the phase ladder
 * reflects the authoritative state rather than one assembled from event messages.
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
              <a [routerLink]="['/app/runs', current.runId, 'tree']" class="btn btn-primary">
                Browse the tree
              </a>
              <a [routerLink]="['/app/runs', current.runId, 'augmentations']" class="btn btn-secondary">
                Key points
              </a>
            } @else if (!current.cancelled && !current.error) {
              <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
            }
          </div>
        </div>

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

        @if (current.error) {
          <div
            class="mt-6 rounded-[var(--radius-lg)] border border-[#7a3a3a] bg-[#2a1c1c] px-4 py-3"
            role="alert"
          >
            <p class="text-sm font-medium text-[#f0a3a3]">This run stopped.</p>
            <p class="mt-1 font-mono text-xs break-words text-[#f0a3a3]/85">{{ current.error }}</p>
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

  private readonly runId = this.route.snapshot.paramMap.get('runId') ?? '';

  constructor() {
    const abort = new AbortController();

    // The stream is a long-lived request through Envoy's /api/ route, which has no timeout — but
    // a user navigating away must not leave it open, hence the explicit abort.
    inject(DestroyRef).onDestroy(() => abort.abort());

    void this.load(abort.signal);
  }

  protected done(): boolean {
    const current = this.run();
    return current !== null && isTerminal(current) && !current.cancelled && !current.error;
  }

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

  protected async cancel(): Promise<void> {
    try {
      this.run.set(await this.api.cancelRun(this.runId));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'The run could not be cancelled.');
    }
  }

  private async load(signal: AbortSignal): Promise<void> {
    try {
      this.run.set(await this.api.getRun(this.runId));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'That run could not be loaded.');
      return;
    }

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

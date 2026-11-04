import { isPlatformBrowser } from '@angular/common';
import { computed, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { Code, ConnectError } from '@connectrpc/connect';
import { LoopApi } from './loop-api';
import { isActive, LoopRun, RunKind } from './model/loop-run';
import { RunUpdate } from './model/run-update';
import { RunWork } from './model/run-work';

const POLL_MS = 2000;
const RETRY_MS = 1000;
const RETRIES = 5;

/** What a run is about; an unset id matches only an unset one. */
export interface RunSubject {
  companyId?: string | null;
  applicationId?: string | null;
  noteId?: string | null;
}

/**
 * The user's AI runs. They're listed while any is queued or running, so the tray sees them end, and
 * a page that started one finds it again when it reopens, finished or not.
 */
@Injectable({ providedIn: 'root' })
export class LoopRuns {
  private readonly api = inject(LoopApi);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly runs = signal<readonly LoopRun[]>([]);
  readonly active = computed(() => this.runs().filter(isActive));

  private loading: Promise<void> | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Resolves once the runs have been listed, or failed to be. */
  load(): Promise<void> {
    if (!this.browser) {
      return Promise.resolve();
    }
    this.loading ??= this.refresh();
    return this.loading;
  }

  /** The newest run of this work on this thing. */
  find(kind: RunKind, subject: RunSubject): LoopRun | null {
    return (
      [...this.runs()]
        .reverse()
        .find(
          (run) =>
            run.kind === kind &&
            run.companyId === (subject.companyId ?? null) &&
            run.applicationId === (subject.applicationId ?? null) &&
            run.noteId === (subject.noteId ?? null),
        ) ?? null
    );
  }

  /** The server drops earlier runs of the same work on the same thing, so this does too. */
  async start(label: string, work: RunWork): Promise<LoopRun> {
    const run = await this.api.startRun(label, work);
    this.runs.update((runs) => [...runs.filter((r) => !sameWork(r, run)), run]);
    this.schedule();
    return run;
  }

  /**
   * The run's updates until it ends, watched again from where it got when the connection drops.
   * Leaving the loop stops the watch, not the run.
   */
  async *watch(run: LoopRun, signal: AbortSignal): AsyncIterable<RunUpdate> {
    const from = { attempt: 0, steps: 0, text: 0 };
    let failures = 0;
    while (!signal.aborted) {
      try {
        for await (const update of this.api.watchRun(run.id, from, signal)) {
          failures = 0;
          if (update.restarted) {
            from.steps = 0;
            from.text = 0;
          }
          from.steps += update.steps.length;
          from.text += update.text.length;
          if (update.run) {
            from.attempt = update.run.attempt;
            this.replace(update.run);
          }
          yield update;
          if (update.run && !isActive(update.run)) {
            return;
          }
        }
      } catch (err) {
        if (signal.aborted) {
          return;
        }
        if ((err instanceof ConnectError && err.code === Code.NotFound) || ++failures > RETRIES) {
          throw err;
        }
        await new Promise((resolve) => setTimeout(resolve, RETRY_MS * failures));
      }
    }
  }

  async cancel(runId: string): Promise<void> {
    this.replace(await this.api.cancelRun(runId));
  }

  async dismiss(runIds: readonly string[]): Promise<void> {
    const gone = new Set(runIds);
    if (gone.size === 0) {
      return;
    }
    this.runs.update((runs) => runs.filter((r) => !gone.has(r.id)));
    await this.api.dismissRuns([...gone]);
  }

  clearFinished(): Promise<void> {
    return this.dismiss(
      this.runs()
        .filter((r) => !isActive(r))
        .map((r) => r.id),
    );
  }

  private replace(run: LoopRun): void {
    this.runs.update((runs) => runs.map((r) => (r.id === run.id ? run : r)));
  }

  private schedule(): void {
    if (this.browser && this.timer === null && this.active().length > 0) {
      this.timer = setTimeout(() => {
        this.timer = null;
        void this.refresh();
      }, POLL_MS);
    }
  }

  private async refresh(): Promise<void> {
    try {
      this.runs.set(await this.api.listRuns());
    } catch {
      // The next poll tries again.
    } finally {
      this.schedule();
    }
  }
}

function sameWork(a: LoopRun, b: LoopRun): boolean {
  return (
    a.kind === b.kind &&
    a.companyId === b.companyId &&
    a.applicationId === b.applicationId &&
    a.noteId === b.noteId
  );
}

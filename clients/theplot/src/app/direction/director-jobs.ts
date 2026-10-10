import { isPlatformBrowser } from '@angular/common';
import { computed, inject, Injectable, PLATFORM_ID, signal } from '@angular/core';
import { DirectorJobsApi } from './director-jobs-api';
import { DirectorJob, isActive } from './model/director-job';
import { JobStep } from './model/job-step';
import { JobStepResult } from './model/job-step-result';

const POLL_MS = 2000;
const STARTING = 'starting-';

/** Steps a job finished since they were last handed on, with the job as it now stands. */
export interface JobProgress {
  job: DirectorJob;
  /** Step `fromStep + i`'s result; null for one this client can't read. */
  results: (JobStepResult | null)[];
  fromStep: number;
}

/**
 * The author's background jobs, polled while any is queued or running. Each finished step's result
 * is handed to listeners once, so an open editor can take it in as it lands.
 */
@Injectable({ providedIn: 'root' })
export class DirectorJobs {
  private readonly api = inject(DirectorJobsApi);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly jobs = signal<readonly DirectorJob[]>([]);
  readonly active = computed(() => this.jobs().filter(isActive));
  /** The steps queued and running jobs have still to do. */
  readonly pending = computed(() =>
    this.active().flatMap((job) => job.steps.slice(job.stepsDone).map((step) => ({ job, step }))),
  );

  /** Steps whose results listeners have had, by job id. */
  private readonly seen = new Map<string, number>();
  private readonly listeners = new Set<(progress: JobProgress) => void>();
  private timer: ReturnType<typeof setTimeout> | null = null;
  private loaded = false;
  private starts = 0;

  /** Steps done before the first load count as seen: whatever opens next reads them fresh. */
  load(): void {
    if (!this.browser || this.loaded) {
      return;
    }
    this.loaded = true;
    void this.refresh(true);
  }

  onProgress(listener: (progress: JobProgress) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /**
   * Shows the job at once, before the API has it, so its steps read as busy straight away.
   * `ready` is waited on first, such as a save the job must see.
   */
  async start(
    storyId: string,
    label: string,
    steps: JobStep[],
    followVariations = false,
    ready: Promise<unknown> = Promise.resolve(),
  ): Promise<DirectorJob> {
    const placeholder: DirectorJob = {
      id: `${STARTING}${++this.starts}`,
      storyId,
      sceneId: sceneOf(steps),
      label,
      state: 'queued',
      steps,
      stepsDone: 0,
      error: '',
      created: new Date(),
      finished: null,
      lane: '',
    };
    this.jobs.update((jobs) => [...jobs, placeholder]);
    try {
      await ready;
      const job = await this.api.startJob(storyId, label, steps, followVariations);
      this.seen.set(job.id, 0);
      // A poll may have listed it already.
      this.jobs.update((jobs) =>
        jobs.filter((j) => j.id !== job.id).map((j) => (j === placeholder ? job : j)),
      );
      this.schedule();
      return job;
    } catch (err) {
      this.jobs.update((jobs) => jobs.filter((j) => j !== placeholder));
      throw err;
    }
  }

  async cancel(id: string): Promise<void> {
    const job = await this.api.cancelJob(id);
    this.jobs.update((jobs) => jobs.map((j) => (j.id === id ? job : j)));
  }

  async dismiss(ids: readonly string[]): Promise<void> {
    const gone = new Set(ids);
    this.jobs.update((jobs) => jobs.filter((j) => !gone.has(j.id)));
    await this.api.dismissJobs([...gone]);
  }

  clearFinished(): Promise<void> {
    return this.dismiss(
      this.jobs()
        .filter((j) => !isActive(j))
        .map((j) => j.id),
    );
  }

  private schedule(): void {
    if (this.browser && this.timer === null && this.active().length > 0) {
      this.timer = setTimeout(() => {
        this.timer = null;
        void this.refresh(false);
      }, POLL_MS);
    }
  }

  private async refresh(first: boolean): Promise<void> {
    try {
      const listed = await this.api.listJobs();
      const before = new Map(this.jobs().map((j) => [j.id, j]));
      const progress: JobProgress[] = [];
      for (const job of listed) {
        const seen = this.seen.get(job.id);
        if (seen === undefined) {
          this.seen.set(job.id, first ? job.stepsDone : 0);
          if (first) {
            continue;
          }
        }
        const from = this.seen.get(job.id) ?? 0;
        const was = before.get(job.id);
        const ended = !isActive(job) && (!was || isActive(was));
        if (job.stepsDone > from || ended) {
          const { job: fresh, results } = await this.api.getJob(job.id, from);
          this.seen.set(job.id, from + results.length);
          progress.push({ job: fresh, results, fromStep: from });
        }
      }
      const fresh = new Map(progress.map((p) => [p.job.id, p.job]));
      this.jobs.set([
        ...listed.map((j) => fresh.get(j.id) ?? j),
        ...this.jobs().filter((j) => j.id.startsWith(STARTING)),
      ]);
      for (const p of progress) {
        for (const listener of this.listeners) {
          try {
            listener(p);
          } catch (err) {
            console.error('A finished job step could not be taken in', err);
          }
        }
      }
    } catch {
      // The next poll tries again.
    } finally {
      this.schedule();
    }
  }
}

function sceneOf(steps: readonly JobStep[]): string | null {
  for (const step of steps) {
    if ('sceneId' in step) {
      return step.sceneId;
    }
  }
  return null;
}

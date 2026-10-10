import { Injectable } from '@angular/core';
import { ConnectDirectorJobsApi } from './connect-director-jobs-api';
import { DirectorJob } from './model/director-job';
import { JobStep } from './model/job-step';
import { JobStepResult } from './model/job-step-result';

/**
 * Director work that waits on a model runs in the background: a job is queued at once and polled
 * until it is done. Jobs on the same scene, or on a story's library, run one at a time.
 */
@Injectable({ providedIn: 'root', useClass: ConnectDirectorJobsApi })
export abstract class DirectorJobsApi {
  /** `followVariations`: a redrawn frame also redraws the variation after it, which it staled. */
  abstract startJob(
    storyId: string,
    label: string,
    steps: JobStep[],
    followVariations?: boolean,
  ): Promise<DirectorJob>;

  /** Queued, running and undismissed finished jobs, oldest first. */
  abstract listJobs(): Promise<DirectorJob[]>;

  /** The job with the results of its finished steps from `fromStep` on. */
  abstract getJob(
    id: string,
    fromStep: number,
  ): Promise<{ job: DirectorJob; results: (JobStepResult | null)[] }>;

  abstract cancelJob(id: string): Promise<DirectorJob>;

  /** Finished jobs only; running ones are left alone. */
  abstract dismissJobs(ids: string[]): Promise<void>;
}

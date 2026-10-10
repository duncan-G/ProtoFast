import { JobStep } from './job-step';

export type JobState = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

/** Director work running in the background; jobs that share a lane run one at a time. */
export interface DirectorJob {
  id: string;
  storyId: string;
  sceneId: string | null;
  label: string;
  state: JobState;
  steps: JobStep[];
  stepsDone: number;
  /** In words for the author. */
  error: string;
  created: Date;
  finished: Date | null;
  lane: string;
}

export function isActive(job: DirectorJob): boolean {
  return job.state === 'queued' || job.state === 'running';
}

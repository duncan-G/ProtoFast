import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Character } from '../stories/model/character';
import { DirectorJobs, JobProgress } from './director-jobs';
import { DirectorJobsApi } from './director-jobs-api';
import { DirectorJob } from './model/director-job';
import { JobStep } from './model/job-step';
import { JobStepResult } from './model/job-step-result';

const STEP: JobStep = { kind: 'castVoice', characterId: 'mara' };

function job(overrides: Partial<DirectorJob> = {}): DirectorJob {
  return {
    id: 'job-1',
    storyId: 'story',
    sceneId: null,
    label: 'Cast a voice for Mara',
    state: 'queued',
    steps: [STEP, { kind: 'castVoice', characterId: 'tom' }],
    stepsDone: 0,
    error: '',
    created: new Date(),
    finished: null,
    lane: 'library:story',
    ...overrides,
  };
}

function result(id: string): JobStepResult {
  return { kind: 'castVoice', character: { id } as Character };
}

/** The API as the server would answer: `listed` is what the next list returns. */
class FakeApi implements DirectorJobsApi {
  listed: DirectorJob[] = [];
  results: JobStepResult[] = [];
  gets: number[] = [];
  started: (() => void) | null = null;

  async startJob(): Promise<DirectorJob> {
    await new Promise<void>((resolve) => (this.started = resolve));
    return job();
  }

  async listJobs(): Promise<DirectorJob[]> {
    return this.listed;
  }

  async getJob(id: string, fromStep: number) {
    this.gets.push(fromStep);
    const current = this.listed.find((j) => j.id === id)!;
    return { job: current, results: this.results.slice(fromStep, current.stepsDone) };
  }

  async cancelJob(id: string): Promise<DirectorJob> {
    return { ...this.listed.find((j) => j.id === id)!, state: 'cancelled' };
  }

  async dismissJobs(): Promise<void> {}
}

describe('DirectorJobs', () => {
  let api: FakeApi;
  let jobs: DirectorJobs;
  let progress: JobProgress[];

  beforeEach(() => {
    vi.useFakeTimers();
    api = new FakeApi();
    TestBed.configureTestingModule({
      providers: [
        { provide: DirectorJobsApi, useValue: api },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    jobs = TestBed.inject(DirectorJobs);
    progress = [];
    jobs.onProgress((p) => progress.push(p));
  });

  afterEach(() => vi.useRealTimers());

  it('shows the steps as pending before the API has the job', async () => {
    const started = jobs.start('story', 'Cast', [STEP]);

    expect(jobs.pending().map((p) => p.step)).toEqual([STEP]);
    await vi.advanceTimersByTimeAsync(0);
    api.started!();
    expect((await started).id).toBe('job-1');
    expect(jobs.jobs().map((j) => j.id)).toEqual(['job-1']);
  });

  it('hands each finished step on once, then the end of the job', async () => {
    const started = jobs.start('story', 'Cast', job().steps);
    await vi.advanceTimersByTimeAsync(0);
    api.started!();
    await started;
    api.results = [result('mara'), result('tom')];

    api.listed = [job({ state: 'running', stepsDone: 1 })];
    await vi.advanceTimersByTimeAsync(2000);
    api.listed = [job({ state: 'running', stepsDone: 1 })];
    await vi.advanceTimersByTimeAsync(2000);
    api.listed = [job({ state: 'succeeded', stepsDone: 2 })];
    await vi.advanceTimersByTimeAsync(2000);

    expect(progress.map((p) => [p.fromStep, p.results.length, p.job.state])).toEqual([
      [0, 1, 'running'],
      [1, 1, 'succeeded'],
    ]);
    expect(jobs.pending()).toEqual([]);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(api.gets).toEqual([0, 1]);
  });

  it('takes steps done before it first loads as seen', async () => {
    api.listed = [job({ state: 'running', stepsDone: 1 })];
    api.results = [result('mara'), result('tom')];

    jobs.load();
    await vi.advanceTimersByTimeAsync(0);
    api.listed = [job({ state: 'succeeded', stepsDone: 2 })];
    await vi.advanceTimersByTimeAsync(2000);

    expect(progress.map((p) => [p.fromStep, p.results.length])).toEqual([[1, 1]]);
  });
});

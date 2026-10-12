/**
 * Where a build of the story's location world model stands. The build runs in the background:
 * `queued` until a worker picks it up, `running` while it reads the scenes, `retrying` after an
 * attempt failed and before the next, then one of the three ends.
 */
export type WorldModelBuildState =
  | 'queued'
  | 'running'
  | 'retrying'
  | 'finished'
  | 'failed'
  | 'cancelled';

export interface WorldModelBuild {
  id: string;
  storyId: string;
  state: WorldModelBuildState;
  scenesTotal: number;
  scenesDone: number;
  /** What the build is doing, or why it stopped, written for the writer. */
  message: string;
  /** USD spent on model calls so far, across every attempt. */
  costUsd: number;
  requestedAt: Date;
  startedAt: Date | null;
  finishedAt: Date | null;
  /** What the graph holds, once finished. */
  partCount: number;
  objectCount: number;
  observationCount: number;
}

export const ACTIVE_BUILD_STATES: ReadonlySet<WorldModelBuildState> = new Set([
  'queued',
  'running',
  'retrying',
]);

export function isBuildActive(build: WorldModelBuild | null): build is WorldModelBuild {
  return build !== null && ACTIVE_BUILD_STATES.has(build.state);
}

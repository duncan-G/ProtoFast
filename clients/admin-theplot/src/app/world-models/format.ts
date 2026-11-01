import {
  WorldModelBuildStatus,
  WorldModelStepKind,
  WorldModelStepStatus,
  type WorldModelBuildHeader,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { duration } from '../engine/format';

const BADGE = 'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium';

export const BUILD_STATUS_LABEL: Record<WorldModelBuildStatus, string> = {
  [WorldModelBuildStatus.UNSPECIFIED]: 'Any',
  [WorldModelBuildStatus.QUEUED]: 'Queued',
  [WorldModelBuildStatus.RUNNING]: 'Running',
  [WorldModelBuildStatus.RETRYING]: 'Retrying',
  [WorldModelBuildStatus.FINISHED]: 'Finished',
  [WorldModelBuildStatus.FAILED]: 'Failed',
  [WorldModelBuildStatus.CANCELLED]: 'Cancelled',
};

export const BUILD_STATUS_CLASS: Record<WorldModelBuildStatus, string> = {
  [WorldModelBuildStatus.UNSPECIFIED]: `${BADGE} bg-gray-100 text-gray-700`,
  [WorldModelBuildStatus.QUEUED]: `${BADGE} bg-gray-100 text-gray-700 ring-1 ring-gray-200`,
  [WorldModelBuildStatus.RUNNING]: `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`,
  [WorldModelBuildStatus.RETRYING]: `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`,
  [WorldModelBuildStatus.FINISHED]: `${BADGE} bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200`,
  [WorldModelBuildStatus.FAILED]: `${BADGE} bg-red-50 text-red-800 ring-1 ring-red-200`,
  [WorldModelBuildStatus.CANCELLED]: `${BADGE} bg-gray-100 text-gray-700 ring-1 ring-gray-200`,
};

export const STEP_KIND_LABEL: Record<WorldModelStepKind, string> = {
  [WorldModelStepKind.UNSPECIFIED]: 'Step',
  [WorldModelStepKind.SEED]: 'Seed',
  [WorldModelStepKind.READ_SCENE]: 'Read scene',
  [WorldModelStepKind.FINISH]: 'Finish',
  [WorldModelStepKind.DESCRIBE_LOCATION]: 'Describe location',
};

export const STEP_STATUS_LABEL: Record<WorldModelStepStatus, string> = {
  [WorldModelStepStatus.UNSPECIFIED]: 'Unknown',
  [WorldModelStepStatus.RUNNING]: 'Running',
  [WorldModelStepStatus.FINISHED]: 'Finished',
  [WorldModelStepStatus.FAILED]: 'Failed',
  [WorldModelStepStatus.SKIPPED]: 'Skipped',
};

export const STEP_STATUS_CLASS: Record<WorldModelStepStatus, string> = {
  [WorldModelStepStatus.UNSPECIFIED]: `${BADGE} bg-gray-100 text-gray-700`,
  [WorldModelStepStatus.RUNNING]: `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`,
  [WorldModelStepStatus.FINISHED]: `${BADGE} bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200`,
  [WorldModelStepStatus.FAILED]: `${BADGE} bg-red-50 text-red-800 ring-1 ring-red-200`,
  [WorldModelStepStatus.SKIPPED]: `${BADGE} bg-gray-100 text-gray-700 ring-1 ring-gray-200`,
};

export function isBuildActive(status: WorldModelBuildStatus): boolean {
  return (
    status === WorldModelBuildStatus.QUEUED ||
    status === WorldModelBuildStatus.RUNNING ||
    status === WorldModelBuildStatus.RETRYING
  );
}

/** From the request to the end, or to now while the build is still going. */
export function buildElapsed(build: WorldModelBuildHeader, now = Date.now()): string {
  const start = Number(build.startedUnixMs || build.requestedUnixMs);
  const end = Number(build.finishedUnixMs) || now;
  return duration(end - start);
}

export function tokens(count: bigint): string {
  const n = Number(count);
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : `${n}`;
}

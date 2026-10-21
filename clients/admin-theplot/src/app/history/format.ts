import {
  HistoryStoryRunStatus,
  type HistoryStoryRun,
} from '../../lib/gen/Admin/Theplot/theplot_history_pb';
import { duration } from '../engine/format';

const BADGE = 'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium';
const WAITING = `${BADGE} bg-gray-100 text-gray-700 ring-1 ring-gray-200`;
const WORKING = `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`;

export const RUN_STATUS_LABEL: Record<HistoryStoryRunStatus, string> = {
  [HistoryStoryRunStatus.UNSPECIFIED]: 'Unknown',
  [HistoryStoryRunStatus.QUEUED]: 'Queued',
  [HistoryStoryRunStatus.RESEARCHING]: 'Researching',
  [HistoryStoryRunStatus.WRITING]: 'Writing',
  [HistoryStoryRunStatus.SAVING]: 'Saving',
  [HistoryStoryRunStatus.FINISHED]: 'Finished',
  [HistoryStoryRunStatus.FAILED]: 'Failed',
  [HistoryStoryRunStatus.CANCELLED]: 'Cancelled',
};

export const RUN_STATUS_CLASS: Record<HistoryStoryRunStatus, string> = {
  [HistoryStoryRunStatus.UNSPECIFIED]: WAITING,
  [HistoryStoryRunStatus.QUEUED]: WAITING,
  [HistoryStoryRunStatus.RESEARCHING]: WORKING,
  [HistoryStoryRunStatus.WRITING]: WORKING,
  [HistoryStoryRunStatus.SAVING]: WORKING,
  [HistoryStoryRunStatus.FINISHED]: `${BADGE} bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200`,
  [HistoryStoryRunStatus.FAILED]: `${BADGE} bg-red-50 text-red-800 ring-1 ring-red-200`,
  [HistoryStoryRunStatus.CANCELLED]: WAITING,
};

export const EVENT_KIND_LABEL: Record<string, string> = {
  random_passages: 'Random draw',
  search: 'Search',
  read: 'Read',
  note: 'Thinking',
  brief: 'Brief',
  draft: 'Script',
  repair: 'Fix',
  saved: 'Saved',
  error: 'Error',
};

export function isRunActive(status: HistoryStoryRunStatus): boolean {
  return (
    status === HistoryStoryRunStatus.QUEUED ||
    status === HistoryStoryRunStatus.RESEARCHING ||
    status === HistoryStoryRunStatus.WRITING ||
    status === HistoryStoryRunStatus.SAVING
  );
}

/** From the start (or the request, while it waits) to the end, or to now while it runs. */
export function runElapsed(run: HistoryStoryRun, now = Date.now()): string {
  const start = Number(run.startedUnixMs || run.createdUnixMs);
  const end = Number(run.finishedUnixMs) || now;
  return duration(end - start);
}

/** The title once research has named the moment, else what was asked for. */
export function runLabel(run: HistoryStoryRun): string {
  return run.title || run.request || 'A random moment';
}

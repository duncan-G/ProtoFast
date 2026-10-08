import { ImportProgress } from './document-api';
import { formatCost } from './format';

/** What the desk and the tray say about an import the server is running. */
export interface ImportStatus {
  label: string;
  /** Which of `IMPORT_STEPS` it is on, 1-based; null once it has stopped or while it waits to retry. */
  step: number | null;
}

/** Read, library, scenes, assembly, mentions, save. */
export const IMPORT_STEPS = 6;

// The screenplay workflow's stages, in the order it runs them.
const STAGES: Record<string, [step: number, label: string]> = {
  library: [2, 'Finding the characters and places'],
  scenes: [3, 'Laying out the scenes'],
  story: [4, 'Putting the story together'],
};

/** "$0.42 so far" while it runs, "$1.20 spent" once it stops; null before any model call. */
export function describeCost(progress: ImportProgress): string | null {
  if (progress.costUsd <= 0) {
    return null;
  }
  return `${formatCost(progress.costUsd)} ${isImportActive(progress) ? 'so far' : 'spent'}`;
}

export function isImportActive(progress: ImportProgress): boolean {
  return progress.state !== 'done' && progress.state !== 'failed' && progress.state !== 'cancelled';
}

export function describeImport(progress: ImportProgress): ImportStatus {
  switch (progress.state) {
    case 'queued':
      return { label: 'Waiting to be read', step: 1 };
    case 'reading':
      return { label: 'Reading the file', step: 1 };
    case 'analysing': {
      const [step, label] = STAGES[progress.stage] ?? [2, 'Working through the story'];
      return { label, step };
    }
    case 'saving':
      return progress.stage === 'mentions'
        ? { label: 'Tagging the characters, places and props', step: 5 }
        : { label: 'Saving the story', step: 6 };
    case 'retrying':
      return { label: 'Hit a snag · trying again', step: null };
    case 'failed':
      return { label: 'Couldn’t import', step: null };
    case 'done':
      return { label: 'Ready', step: null };
    case 'cancelled':
      return { label: 'Cancelled', step: null };
  }
}

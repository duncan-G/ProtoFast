import { LoopRun } from './loop-run';
import { ResearchStep } from './research-step';
import { RunResult } from './run-result';
import { RunWork } from './run-work';

/** A run with its work and all it has done. */
export interface RunDetails {
  run: LoopRun;
  /** Null for work this client doesn't know. */
  work: RunWork | null;
  steps: ResearchStep[];
  text: string;
  result: RunResult | null;
  /** A writing run's note was saved after it started, so its document no longer matches it. */
  noteChanged: boolean;
}

import { LoopRun } from './loop-run';
import { ResearchStep } from './research-step';
import { RunResult } from './run-result';

/** What a watch brings: the run when it changed, the steps and text since, and what it found, last. */
export interface RunUpdate {
  run: LoopRun | null;
  /** It started over: the steps and text start from the beginning. */
  restarted: boolean;
  steps: ResearchStep[];
  text: string;
  result: RunResult | null;
}

import {
  ExecutorTier,
  ProgressPhase,
  RunMode,
  RunStatus,
  Verdict,
  type ArtifactRef,
  type RunHeader,
} from '../../lib/gen/Admin/Theplot/theplot_engine_pb';

/** The api sends Unix milliseconds, 0 for unset. */
export function toDate(unixMs: bigint | undefined): Date | null {
  return unixMs ? new Date(Number(unixMs)) : null;
}

export function usd(micros: bigint | undefined): string {
  const amount = Number(micros ?? 0n) / 1_000_000;
  if (amount === 0) {
    return '$0';
  }
  return amount < 0.01 ? `$${amount.toFixed(4)}` : `$${amount.toFixed(2)}`;
}

export function duration(ms: bigint | number | undefined): string {
  const total = Number(ms ?? 0);
  if (total < 1000) {
    return `${total} ms`;
  }
  const seconds = total / 1000;
  if (seconds < 60) {
    return `${seconds.toFixed(1)} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ${Math.round(seconds % 60)}s`;
  }
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

/** From opening to closing or abandonment, or to now while the run is open. */
export function elapsed(run: RunHeader, now = Date.now()): string {
  const end = Number(run.abandonedUnixMs || run.closedUnixMs) || now;
  return duration(end - Number(run.openedUnixMs));
}

export function shortId(id: string): string {
  return id.length > 14 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id;
}

export function shortHash(hash: string): string {
  return hash.slice(0, 10);
}

export function familyKey(family: string, generation: number): string {
  return generation === 0 ? family : `${family}#${generation}`;
}

/**
 * `runId/stageId/hash` for the `?artifact=` deep link. The run id is part of it because a run's
 * input lives under the upload id, not the run's; a stage id may itself contain slashes.
 */
export function artifactParam(ref: ArtifactRef): string {
  return `${ref.runId}/${ref.stageId}/${ref.hash}`;
}

export function parseArtifactParam(param: string): ArtifactRef | null {
  const first = param.indexOf('/');
  const last = param.lastIndexOf('/');
  if (first <= 0 || last - first < 2 || last === param.length - 1) {
    return null;
  }
  return {
    $typeName: 'admin.theplot.ArtifactRef',
    runId: param.slice(0, first),
    stageId: param.slice(first + 1, last),
    hash: param.slice(last + 1),
  };
}

/** Pretty-printed when the text is JSON, else null. */
export function prettyJson(text: string): string | null {
  const trimmed = text.trimStart();
  if (!trimmed.startsWith('{') && !trimmed.startsWith('[')) {
    return null;
  }
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return null;
  }
}

export type Outcome = 'open' | 'passed' | 'failed' | 'abandoned';

export function outcome(run: RunHeader): Outcome {
  switch (run.status) {
    case RunStatus.ABANDONED:
      return 'abandoned';
    case RunStatus.CLOSED:
      return run.passed ? 'passed' : 'failed';
    default:
      return 'open';
  }
}

export const OUTCOME_LABEL: Record<Outcome, string> = {
  open: 'Running',
  passed: 'Passed',
  failed: 'Failed',
  abandoned: 'Abandoned',
};

const BADGE = 'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium';

export const OUTCOME_CLASS: Record<Outcome, string> = {
  open: `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`,
  passed: `${BADGE} bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200`,
  failed: `${BADGE} bg-red-50 text-red-800 ring-1 ring-red-200`,
  abandoned: `${BADGE} bg-gray-100 text-gray-700 ring-1 ring-gray-200`,
};

export const RUN_MODE_LABEL: Record<RunMode, string> = {
  [RunMode.UNSPECIFIED]: 'Unknown',
  [RunMode.DISCOVERY]: 'Discovery',
  [RunMode.SCHEDULED]: 'Scheduled',
};

export const RUN_STATUS_LABEL: Record<RunStatus, string> = {
  [RunStatus.UNSPECIFIED]: 'Any',
  [RunStatus.OPEN]: 'Running',
  [RunStatus.CLOSED]: 'Closed',
  [RunStatus.ABANDONED]: 'Abandoned',
};

export const TIER_LABEL: Record<ExecutorTier, string> = {
  [ExecutorTier.UNSPECIFIED]: 'Unknown tier',
  [ExecutorTier.ORCHESTRATOR]: 'Orchestrator',
  [ExecutorTier.DELEGATE_LARGE]: 'Large delegate',
  [ExecutorTier.DELEGATE_MEDIUM]: 'Medium delegate',
  [ExecutorTier.DELEGATE_SMALL]: 'Small delegate',
  [ExecutorTier.CODIFIED]: 'Codified',
};

export const TIER_CLASS: Record<ExecutorTier, string> = {
  [ExecutorTier.UNSPECIFIED]: `${BADGE} bg-gray-100 text-gray-700`,
  [ExecutorTier.ORCHESTRATOR]: `${BADGE} bg-violet-50 text-violet-800 ring-1 ring-violet-200`,
  [ExecutorTier.DELEGATE_LARGE]: `${BADGE} bg-indigo-50 text-indigo-800 ring-1 ring-indigo-200`,
  [ExecutorTier.DELEGATE_MEDIUM]: `${BADGE} bg-sky-50 text-sky-800 ring-1 ring-sky-200`,
  [ExecutorTier.DELEGATE_SMALL]: `${BADGE} bg-cyan-50 text-cyan-800 ring-1 ring-cyan-200`,
  [ExecutorTier.CODIFIED]: `${BADGE} bg-slate-100 text-slate-800 ring-1 ring-slate-200`,
};

export const VERDICT_LABEL: Record<Verdict, string> = {
  [Verdict.UNSPECIFIED]: 'Unknown',
  [Verdict.PASS]: 'Pass',
  [Verdict.DEGRADED]: 'Degraded',
  [Verdict.FAIL]: 'Fail',
};

export const VERDICT_CLASS: Record<Verdict, string> = {
  [Verdict.UNSPECIFIED]: `${BADGE} bg-gray-100 text-gray-700`,
  [Verdict.PASS]: `${BADGE} bg-emerald-50 text-emerald-800 ring-1 ring-emerald-200`,
  [Verdict.DEGRADED]: `${BADGE} bg-amber-50 text-amber-800 ring-1 ring-amber-200`,
  [Verdict.FAIL]: `${BADGE} bg-red-50 text-red-800 ring-1 ring-red-200`,
};

export const PHASE_LABEL: Record<ProgressPhase, string> = {
  [ProgressPhase.UNSPECIFIED]: 'Unknown',
  [ProgressPhase.PREPARING]: 'Preparing',
  [ProgressPhase.RUNNING]: 'Running',
  [ProgressPhase.FINISHING]: 'Finishing',
  [ProgressPhase.FINISHED]: 'Finished',
  [ProgressPhase.RETRYING]: 'Retrying',
  [ProgressPhase.FAILED]: 'Failed',
  [ProgressPhase.CANCELLED]: 'Cancelled',
};

export const NEUTRAL_BADGE = `${BADGE} bg-gray-100 text-gray-700`;

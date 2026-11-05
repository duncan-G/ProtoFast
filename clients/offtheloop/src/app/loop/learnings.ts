import { Learning } from './model/learning';
import { Round } from './model/round';

export interface LearningEntry extends Learning {
  /** Its place in the application's list. */
  at: number;
}

export interface LearningGroup {
  round: string;
  /** The newest entry's day, or null when none has one. */
  latest: string | null;
  entries: LearningEntry[];
}

const newer = (a: LearningEntry, b: LearningEntry) =>
  (b.on ?? '').localeCompare(a.on ?? '') || b.at - a.at;

export function newestFirst(learnings: readonly Learning[]): LearningEntry[] {
  return learnings.map((learning, at) => ({ ...learning, at })).sort(newer);
}

/** By round, the round learned from most recently first; entries from no round come last. */
export function learningGroups(learnings: readonly Learning[]): LearningGroup[] {
  const groups = new Map<string, LearningEntry[]>();
  for (const entry of newestFirst(learnings)) {
    groups.set(entry.round, [...(groups.get(entry.round) ?? []), entry]);
  }
  return [...groups]
    .map(([round, entries]) => ({ round, latest: entries[0].on, entries }))
    .sort(
      (a, b) =>
        Number(!a.round) - Number(!b.round) || (b.latest ?? '').localeCompare(a.latest ?? ''),
    );
}

/** The round under way, else the last one done. */
export function roundNow(rounds: readonly Round[]): string {
  const current = rounds.find((round) => round.state === 'current');
  const done = rounds.filter((round) => round.state === 'done').at(-1);
  return (current ?? done)?.label ?? '';
}

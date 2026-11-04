export type RunKind = 'researchCompany' | 'readPosting' | 'write';

export type RunState = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled';

/** AI work running on the server; it keeps going when the page that started it closes. */
export interface LoopRun {
  id: string;
  kind: RunKind;
  label: string;
  state: RunState;
  /** In words for the user. */
  error: string;
  companyId: string | null;
  /** The application a posting is read into. */
  applicationId: string | null;
  noteId: string | null;
  /** A run taken over after its worker stopped starts over, one attempt on. */
  attempt: number;
  created: Date;
  started: Date | null;
  finished: Date | null;
}

export function isActive(run: LoopRun): boolean {
  return run.state === 'queued' || run.state === 'running';
}

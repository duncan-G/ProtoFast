import { Application } from './application';

/** How an application reads at a glance: its dot and its colour. 'none' is not applied for yet. */
export type Status = 'none' | 'applied' | 'active' | 'offer' | 'closed';

export function statusOf(application: Pick<Application, 'stage' | 'closed' | 'appliedOn'>): Status {
  if (application.closed) {
    return 'closed';
  }
  if (application.stage === 'offer') {
    return 'offer';
  }
  if (application.stage !== 'applied') {
    return 'active';
  }
  return application.appliedOn ? 'applied' : 'none';
}

export const STATUSES: readonly { status: Status; label: string }[] = [
  { status: 'none', label: 'not applied' },
  { status: 'applied', label: 'applied' },
  { status: 'active', label: 'interviewing' },
  { status: 'offer', label: 'offer' },
  { status: 'closed', label: 'closed' },
];

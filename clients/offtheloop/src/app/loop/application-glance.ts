import { daysBetween, when } from './days';
import { plural } from './loop-summary';
import { Application } from './model/application';
import { STAGES } from './model/stage';
import { statusOf } from './model/status';

type Glanced = Pick<Application, 'stage' | 'closed' | 'appliedOn' | 'nextStep' | 'nextOn'>;

const capital = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** The stage bar in small: passed stages grey, the one under way lit, closed ones dark. */
export function stageSegments(application: Glanced): string[] {
  const status = statusOf(application);
  const at = status === 'none' ? -1 : STAGES.indexOf(application.stage);
  const lit = status === 'offer' ? 'var(--color-accent)' : 'var(--color-amber)';
  return STAGES.map((_, i) => {
    if (i > at) {
      return 'var(--color-line-2)';
    }
    if (status === 'closed') {
      return 'var(--color-edge-2)';
    }
    return i === at && status !== 'applied' ? lit : 'var(--color-text-2)';
  });
}

export function stageName(application: Glanced): string {
  return statusOf(application) === 'none' ? 'Not applied' : capital(application.stage);
}

/** What's next for an open application, or how a closed one ended. */
export function glanceLine(application: Glanced, now: string): string {
  const status = statusOf(application);
  if (status === 'closed') {
    return application.stage === 'applied' ? 'Closed' : `Closed at ${application.stage}`;
  }
  if (application.nextStep || application.nextOn) {
    const what = application.nextStep || (status === 'offer' ? 'Offer ends' : 'Next step');
    return application.nextOn ? `${what} · ${when(application.nextOn, now)}` : what;
  }
  if (status === 'none' || !application.appliedOn) {
    return 'Not applied yet';
  }
  const waited = daysBetween(application.appliedOn, now);
  return waited > 0 ? `No reply yet · ${plural(waited, 'day')}` : 'Applied today';
}

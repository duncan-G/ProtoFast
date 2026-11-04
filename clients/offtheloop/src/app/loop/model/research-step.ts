export interface ResearchStep {
  action: 'thinking' | 'searching' | 'reading';
  /** What it's working out, the search's words, or the page's address. */
  subject: string;
}

/** A page's address without the scheme or a bare trailing slash. */
export function stepSubject(step: ResearchStep): string {
  if (step.action !== 'reading') {
    return step.subject;
  }
  try {
    const url = new URL(step.subject);
    return url.host.replace(/^www\./, '') + (url.pathname === '/' ? '' : url.pathname);
  } catch {
    return step.subject;
  }
}

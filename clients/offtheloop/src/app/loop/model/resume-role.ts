import { ResumeEvidence } from './resume-evidence';

/** A job held. Months are YYYY-MM, or YYYY alone. */
export interface ResumeRole {
  company: string;
  title: string;
  location: string;
  start: string;
  /** Empty while it's current. */
  end: string;
  summary: string;
  highlights: string[];
  stack: string[];
  evidence: ResumeEvidence[];
}

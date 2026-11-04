import { ResumeEvidence } from './resume-evidence';

export interface ResumeProject {
  name: string;
  link: string;
  start: string;
  /** Empty while it's ongoing. */
  end: string;
  summary: string;
  highlights: string[];
  stack: string[];
  evidence: ResumeEvidence[];
}

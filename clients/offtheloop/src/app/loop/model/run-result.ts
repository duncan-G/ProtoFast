import { CompanyFindings } from './company-findings';
import { PostingFindings } from './posting-findings';
import { Resume } from './resume';

/** What a research or resume run found; a writing run's is its text. */
export type RunResult =
  | { kind: 'company'; findings: CompanyFindings }
  | { kind: 'posting'; findings: PostingFindings }
  | { kind: 'resume'; resume: Resume };

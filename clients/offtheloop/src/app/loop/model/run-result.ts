import { CompanyFindings } from './company-findings';
import { PostingFindings } from './posting-findings';

/** What a research run found; a writing run's is its text. */
export type RunResult =
  | { kind: 'company'; findings: CompanyFindings }
  | { kind: 'posting'; findings: PostingFindings };

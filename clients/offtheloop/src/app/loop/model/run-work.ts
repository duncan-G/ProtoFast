import { PostingRequest } from './posting-request';
import { WriteRequest } from './write-request';

/** What a run asks the AI to do. */
export type RunWork =
  | { kind: 'researchCompany'; companyId: string }
  | { kind: 'readPosting'; request: PostingRequest }
  | { kind: 'write'; request: WriteRequest };

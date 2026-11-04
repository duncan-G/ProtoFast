import { Application } from './application';
import { Company } from './company';
import { WebSource } from './web-source';

/** What a job posting says; empty fields and lists weren't in it. */
export interface PostingFindings {
  application: Pick<
    Application,
    | 'role'
    | 'postingUrl'
    | 'posting'
    | 'postedPay'
    | 'team'
    | 'reportsTo'
    | 'arrangement'
    | 'location'
    | 'hours'
    | 'sponsorsVisa'
    | 'wants'
    | 'stack'
    | 'benefits'
    | 'questions'
  >;
  company: Pick<Company, 'about' | 'values' | 'practices'>;
  /** The employer the posting names. */
  employer: string;
  sources: WebSource[];
}

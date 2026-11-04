import { Company } from './company';
import { WebSource } from './web-source';

/** What the web says of a company; empty fields and lists weren't found. Its contacts are executives. */
export interface CompanyFindings {
  company: Pick<
    Company,
    | 'about'
    | 'location'
    | 'revenue'
    | 'growth'
    | 'headcount'
    | 'funding'
    | 'lastRaised'
    | 'values'
    | 'practices'
    | 'competitors'
    | 'concerns'
    | 'contacts'
  >;
  sources: WebSource[];
}

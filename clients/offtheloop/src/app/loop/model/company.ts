import { Contact } from './contact';

export interface Company {
  id: string;
  name: string;
  /** "fintech · payments api · remote (us)" */
  about: string;
  /** "Burbank, CA" */
  location: string;
  revenue: string;
  growth: string;
  headcount: string;
  funding: string;
  /** "Mar 2025" */
  lastRaised: string;
  values: string[];
  practices: string[];
  competitors: string[];
  concerns: string[];
  contacts: Contact[];
  /** Unix seconds, as the server last sent it; ignored on writes. */
  lastModified: number;
}

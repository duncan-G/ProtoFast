import { Contact } from './contact';

export interface Company {
  id: string;
  name: string;
  /** "fintech · payments api · remote (us)" */
  about: string;
  /** "Burbank, CA" */
  location: string;
  /** In plain words: what it makes or sells, and to whom. */
  whatItDoes: string;
  /** Who pays it, for what, and how it's priced. */
  howItMakesMoney: string;
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

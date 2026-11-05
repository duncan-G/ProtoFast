import { Arrangement } from './arrangement';
import { AskedQuestion } from './asked-question';
import { Learning } from './learning';
import { Round } from './round';
import { Source } from './source';
import { Stage } from './stage';

/** Days are YYYY-MM-DD, in the user's calendar. */
export interface Application {
  id: string;
  companyId: string;
  /** The company's name, as the server last sent it; ignored on writes. */
  company: string;
  role: string;
  /** The furthest it got; a closed application keeps the stage it closed at. */
  stage: Stage;
  closed: boolean;
  /** Null until you apply. */
  appliedOn: string | null;
  source: Source | null;
  /** Who referred you or which board. */
  sourceDetail: string;
  /** The version sent, from before files; a resume file's name supersedes it. */
  resume: string;
  resumeId: string | null;
  cvId: string | null;
  postingUrl: string;
  /** The posting's text, kept in case it comes down. */
  posting: string;
  nextStep: string;
  nextOn: string | null;
  postedPay: string;
  askedPay: string;
  offeredPay: string;
  /** Equity, bonus, vesting, sign-on. */
  offerTerms: string;
  wants: string[];
  /** Questions still to ask them. */
  questions: string[];
  /** Questions put to them, moved out of questions once asked. */
  asked: AskedQuestion[];
  team: string;
  reportsTo: string;
  stack: string[];
  arrangement: Arrangement | null;
  location: string;
  /** "4h overlap with ET" */
  hours: string;
  /** Null when not known. */
  sponsorsVisa: boolean | null;
  benefits: string[];
  rounds: Round[];
  /** Oldest first. */
  learnings: Learning[];
  /** Unix seconds, as the server last sent it; ignored on writes. */
  lastModified: number;
}

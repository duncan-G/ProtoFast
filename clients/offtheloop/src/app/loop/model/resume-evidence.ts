/** A commit a resume entry rests on. */
export interface ResumeEvidence {
  repo: string;
  commit: string;
  /** YYYY-MM-DD, or empty. */
  date: string;
  what: string;
}

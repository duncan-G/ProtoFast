export interface Learning {
  text: string;
  /** The round it came from, by name; empty when it came from none. */
  round: string;
  /** YYYY-MM-DD, or null when not known. */
  on: string | null;
}

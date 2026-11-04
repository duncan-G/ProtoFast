/** Values are as typed: "200k", "staff". */
export interface Goal {
  id: string;
  /** What it measures: "salary", "title". */
  kind: string;
  target: string;
  /** Where it stands now; may be empty. */
  current: string;
}

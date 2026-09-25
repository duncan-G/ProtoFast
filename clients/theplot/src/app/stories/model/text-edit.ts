/** One contiguous change: `[start, oldEnd)` of the old text became `[start, newEnd)` of the new. */
export interface TextEdit {
  start: number;
  oldEnd: number;
  newEnd: number;
}

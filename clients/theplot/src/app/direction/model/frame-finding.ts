/** What the frame checks still found wrong with a frame the model directed. */
export interface FrameFinding {
  frameId: string;
  check: 'consistency' | 'positioning';
  issue: string;
}

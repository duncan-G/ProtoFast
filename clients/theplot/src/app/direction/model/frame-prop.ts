/** A prop a frame shows or hides against the scene, or places. */
export interface FrameProp {
  propId: string;
  inView: boolean;
  /** As on a placement; a carried prop goes where its holder is. */
  screen: string;
  depth: string;
  mark: string;
}

export function isPlacedProp(prop: FrameProp): boolean {
  return Boolean(prop.screen || prop.depth || prop.mark);
}

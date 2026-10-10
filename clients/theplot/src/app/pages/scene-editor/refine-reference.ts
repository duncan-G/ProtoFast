/** A picture of the scene a refine may take its change from. */
export interface RefineReference {
  assetId: string;
  url: string;
  /** "Mara", "Frame 3". */
  label: string;
  /** "Character", "Frame"; searched with the label. */
  kind: string;
  /** A frame's beat; searched too. */
  detail: string;
  /** Null for a library picture. */
  frameNumber: number | null;
}

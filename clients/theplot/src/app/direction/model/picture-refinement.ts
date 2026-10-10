/** What to change in a picture, and another picture of the story's to take it from. */
export interface PictureRefinement {
  instructions: string;
  referenceAssetId: string | null;
}

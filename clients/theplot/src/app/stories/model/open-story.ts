/** A story ThePlot offers every writer to copy onto their desk. */
export interface OpenStory {
  id: string;
  title: string;
  /** Its opening title card (place and date), or its first action line. */
  opening: string;
  sceneCount: number;
  characterCount: number;
  publishedAt: Date;
  /** The caller's copy, once they have imported it. */
  importedStoryId: string | null;
}

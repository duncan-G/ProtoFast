/** Where the writer left a story: a scene, and the block at the top of the view. */
export interface StorySpot {
  sceneId: string;
  /** `null` at the scene's top. May name a block no longer in the scene. */
  elementId: string | null;
}

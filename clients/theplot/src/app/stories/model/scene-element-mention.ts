/** Exactly one of the three target ids is set. */
export interface SceneElementMention {
  id: string;
  characterId: string | null;
  propId: string | null;
  locationId: string | null;
  /** Index of the `@`. */
  offset: number;
  /** `@` included. */
  length: number;
}

/** Exactly one of the three target ids is set. */
export interface SceneElementMention {
  id: string;
  characterId: string | null;
  propId: string | null;
  locationId: string | null;
  /** Index of the `@`, or of a tag's first character. */
  offset: number;
  /** `@` included. */
  length: number;
  /** Marks text as written ("him"), with no `@`; a rename leaves it alone. */
  isTag: boolean;
}

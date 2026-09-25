export interface Character {
  id: string;
  storyId: string;
  name: string;
  /** A `CharacterKind` label. */
  kind: string;
  /** OKLCH hue, 0–359. */
  hue: number;
}

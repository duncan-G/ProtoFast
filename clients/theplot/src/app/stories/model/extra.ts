/** Someone uncast who appears in pictures: one person or a group, described rather than identified. */
export interface Extra {
  id: string;
  storyId: string;
  name: string;
  /** Who they are and how many: "three teenage boys on bikes". */
  description: string | null;
  wardrobe: string | null;
}

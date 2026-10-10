/** Someone uncast who appears in pictures: one person or a group, described rather than identified. */
export interface Extra {
  id: string;
  storyId: string;
  name: string;
  /** Who they are and how many: "three teenage boys on bikes". */
  description: string | null;
  wardrobe: string | null;
  /** 1 is one person alone; null leaves the number to the description. */
  headcount: number | null;
}

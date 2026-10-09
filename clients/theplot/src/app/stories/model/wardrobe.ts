/** An outfit on a character's sheet; a scene's look names one. Labels are unique per character. */
export interface Wardrobe {
  /** Empty until the API has saved it. */
  id: string;
  label: string;
  description: string;
}

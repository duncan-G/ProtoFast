import { CharacterIdentity } from './character-identity';
import { VoiceProfile } from './voice-profile';
import { Wardrobe } from './wardrobe';

export interface Character {
  id: string;
  storyId: string;
  name: string;
  /** A `CharacterKind` label. */
  kind: string;
  /** OKLCH hue, 0–359. */
  hue: number;
  /** The sheet, written in director mode by the author or the model. */
  description: string | null;
  appearance: CharacterIdentity | null;
  voice: VoiceProfile | null;
  /** In position order. */
  wardrobes: Wardrobe[];
}

/** A sheet is empty when every part of it is. */
export function hasSheet(character: Character): boolean {
  return (
    character.description !== null ||
    character.appearance !== null ||
    character.voice !== null ||
    character.wardrobes.length > 0
  );
}

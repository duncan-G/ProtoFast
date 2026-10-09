/** What never changes about a character within the story; what does lives on the scene's look. */
export interface CharacterIdentity {
  age: string;
  gender: string;
  build: string;
  face: string;
  hair: string;
  skin: string;
  /** Scars, tattoos, glasses: anything a picture has to keep. */
  marks: string[];
}

export const EMPTY_IDENTITY: Readonly<CharacterIdentity> = {
  age: '',
  gender: '',
  build: '',
  face: '',
  hair: '',
  skin: '',
  marks: [],
};

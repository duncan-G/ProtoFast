/** One of the speech provider's voices. */
export interface SpeechVoice {
  id: string;
  name: string;
  description: string;
  /** e.g. "male · middle aged · british" */
  traits: string;
  /** A short sample to audition the voice; empty when there is none. */
  previewUrl: string;
}

export interface VoiceProfile {
  /** "late 50s, gravelly, slow, Glaswegian" */
  description: string;
  /** The delivery a line without tags falls back to, e.g. "dry, understated". */
  baseline: string;
  /** The speech provider's voice; empty takes the provider's default. */
  voiceId: string;
  /** The provider's name for the voice. */
  voiceName: string;
  /** A short sample of the voice; empty when there is none. */
  previewUrl: string;
}

export const EMPTY_VOICE: VoiceProfile = {
  description: '',
  baseline: '',
  voiceId: '',
  voiceName: '',
  previewUrl: '',
};

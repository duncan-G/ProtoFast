export interface VoiceProfile {
  /** "late 50s, gravelly, slow, Glaswegian" */
  description: string;
  /** The delivery a line without tags falls back to, e.g. "dry, understated". */
  baseline: string;
  /** The speech provider's voice; empty takes the provider's default. */
  voiceId: string;
}

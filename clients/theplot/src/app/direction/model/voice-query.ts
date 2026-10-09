/** Filters on the provider's voice library; empty means any. Values are the provider's labels. */
export interface VoiceQuery {
  search: string;
  /** e.g. "female" */
  gender: string;
  /** e.g. "middle_aged" */
  age: string;
  /** e.g. "british" */
  accent: string;
  /** Only voices made for narration. */
  narration: boolean;
}

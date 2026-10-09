/** A cue's spoken line, as the player streams it. */
export interface CueAudio {
  assetId: string;
  /** Presigned; it expires, so it comes with every load of the direction. */
  url: string;
  contentType: string;
  /** The performance or the voice changed since this was rendered. */
  stale: boolean;
  voice: string;
}

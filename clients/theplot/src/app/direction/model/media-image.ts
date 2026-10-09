/** A generated picture, as the editor shows it. */
export interface MediaImage {
  assetId: string;
  /** Presigned; it expires, so it comes with every load. */
  url: string;
  contentType: string;
  /** What it pictures has changed since, or a library picture it was composed from was redone. */
  stale: boolean;
  /** The pictures it was offered to compose from: "MARA", "Frame 2", "Frame 4 of THE DOCKS". */
  references: string[];
  /** The image model's summary of how it made the picture. */
  notes: string;
}

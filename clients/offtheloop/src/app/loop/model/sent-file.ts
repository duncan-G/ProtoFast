import { SentFileKind } from './sent-file-kind';

/** A resume or CV file; one can go with any number of applications. */
export interface SentFile {
  id: string;
  kind: SentFileKind;
  /** The version it is: "v4-backend". */
  name: string;
  fileName: string;
  mediaType: string;
  sizeBytes: number;
  /** YYYY-MM-DD, in the user's calendar. */
  uploadedOn: string;
}

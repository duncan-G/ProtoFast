/** A link to read, or the posting's text when its page can't be read. */
export interface PostingRequest {
  url: string;
  text: string;
  companyId: string;
  /** The application it's read into; empty for a new one. */
  applicationId: string;
}

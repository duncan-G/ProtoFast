import { inject, Injectable } from '@angular/core';
import { Code, ConnectError, createClient } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { Documents, type Document as DocumentMessage } from '../../lib/gen/document_pb';
import { DocumentUpload } from '../../lib/gen/document_upload_pb';

/** One accepted source format, as the API's table lists it. */
export interface SourceFormatInfo {
  /** With the leading dot: ".docx". */
  extension: string;
  mediaType: string;
  /** "Word", "PDF", "Markdown". */
  label: string;
}

/** What the import dialog needs to check a file before anything is sent. */
export interface AcceptedFormats {
  formats: SourceFormatInfo[];
  maxBytes: number;
  /** The `accept` attribute for the file picker: every extension and media type. */
  accept: string;
  /** Distinct labels, in table order, for the chips under the drop zone. */
  labels: string[];
}

/** A document on the desk: an upload the API has confirmed landed in storage. */
export interface DocumentSummary {
  id: string;
  name: string;
  fileName: string;
  sizeBytes: number;
  mediaType: string;
  fileExtension: string;
  createdAt: Date;
  lastModifiedAt: Date;
}

/** A presigned POST, ready to be replayed as multipart/form-data with the file part last. */
export interface UploadTicket {
  uploadId: string;
  postUrl: string;
  /** In signing order. */
  fields: [name: string, value: string][];
  maxBytes: number;
  expiresAt: Date;
}

/**
 * The document endpoints on the api service, over gRPC-Web through the edge (`/api/…`). The
 * session cookie is the credential; the edge turns it into the internal JWT the api enforces.
 *
 * Browser only: nothing here runs during SSR, because the cookie never reaches the render.
 */
@Injectable({ providedIn: 'root' })
export class DocumentApi {
  private readonly transport = inject(GRPC_TRANSPORT);
  private readonly uploads = createClient(DocumentUpload, this.transport);
  private readonly documents = createClient(Documents, this.transport);

  async listSourceFormats(): Promise<AcceptedFormats> {
    const reply = await this.uploads.listSourceFormats({});
    const formats = reply.formats.map((f) => ({
      extension: f.extension,
      mediaType: f.mediaType,
      label: f.label,
    }));
    const mediaTypes = [...new Set(formats.map((f) => f.mediaType))];
    return {
      formats,
      maxBytes: Number(reply.maxBytes),
      accept: [...formats.map((f) => f.extension), ...mediaTypes].join(','),
      labels: [...new Set(formats.map((f) => f.label))],
    };
  }

  /** Step 1: the API validates name, type and size, records the upload and signs a POST for it. */
  async createUploadUrl(file: File): Promise<UploadTicket> {
    const reply = await this.uploads.createDocumentUploadUrl({
      fileName: file.name,
      sizeBytes: BigInt(file.size),
      contentType: file.type,
    });
    return {
      uploadId: reply.uploadId,
      postUrl: reply.postUrl,
      fields: Object.entries(reply.fields),
      maxBytes: Number(reply.maxBytes),
      expiresAt: new Date(Number(reply.expiresUnixSeconds) * 1000),
    };
  }

  /** Step 3: after the POST to storage returned 201, the API confirms the object and records the document. */
  async completeUpload(uploadId: string): Promise<DocumentSummary> {
    const reply = await this.uploads.completeDocumentUpload({ uploadId });
    if (!reply.document) {
      throw new Error('The import was recorded but the API returned no document.');
    }
    return toSummary(reply.document);
  }

  /** The caller's documents, newest first. */
  async listDocuments(): Promise<DocumentSummary[]> {
    const reply = await this.documents.listDocuments({});
    return reply.documents.map(toSummary);
  }
}

function toSummary(message: DocumentMessage): DocumentSummary {
  return {
    id: message.id,
    name: message.name,
    fileName: message.fileName,
    sizeBytes: Number(message.sizeBytes),
    mediaType: message.mediaType,
    fileExtension: message.fileExtension,
    createdAt: new Date(Number(message.createdUnixSeconds) * 1000),
    lastModifiedAt: new Date(Number(message.lastModifiedUnixSeconds) * 1000),
  };
}

/**
 * The message to show for a failed call: the API's own status message when it sent one that
 * means something to a person, the caller's fallback otherwise. An expired session is its own
 * case, since the only useful answer is to sign in again.
 */
export function describeError(err: unknown, fallback: string): string {
  if (err instanceof ConnectError) {
    switch (err.code) {
      case Code.Unauthenticated:
      case Code.PermissionDenied:
        return 'Your session has expired. Sign in again to continue.';
      case Code.InvalidArgument:
      case Code.AlreadyExists:
      case Code.FailedPrecondition:
      case Code.NotFound:
      case Code.ResourceExhausted:
        return err.rawMessage || fallback;
      case Code.Unavailable:
      case Code.DeadlineExceeded:
        return 'ThePlot could not be reached. Check your connection and try again.';
      default:
        return fallback;
    }
  }
  if (err instanceof Error && err.message) {
    return err.message;
  }
  return fallback;
}

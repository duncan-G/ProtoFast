import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../grpc-transport';
import {
  Segmentation,
  type CreateUploadReply,
  type ListModelsReply,
  type ListReviewsReply,
  type ListSourceFormatsReply,
  type ParagraphEdit,
  type Priority,
  type Result,
  type Run,
  type RunEvent,
  type Sensitivity,
  type SourceFormat,
} from '../../lib/gen/segmentation_pb';

export type { Result, Run, RunEvent, ListReviewsReply, ListModelsReply, SourceFormat };

/** What `EntityTooLarge` and its siblings mean to a person (ingest plan §7.4). */
export class UploadRejectedError extends Error {
  constructor(
    message: string,
    readonly code: string,
  ) {
    super(message);
    this.name = 'UploadRejectedError';
  }
}

/** What the upload page collects before a run can be submitted. */
export interface SubmitOptions {
  uploadId: string;
  documentId: string;
  sensitivity: Sensitivity;
  familyHint: string;
  augmentations: string[];
  priority: Priority;
  requireReview: boolean;
}

/**
 * The Segmentation service on `api`, over gRPC-Web through the edge.
 *
 * Nothing here runs during SSR. The identity that authorizes these calls is the ext_authz
 * annotation on the browser's own request; a server-side render has no session to present, so
 * every page fetches after hydration rather than rendering a signed-out shape of itself.
 */
@Injectable({ providedIn: 'root' })
export class SegmentationApi {
  private readonly client = createClient(Segmentation, inject(GRPC_TRANSPORT));

  /**
   * The formats the server will actually sign an upload for, and the size cap it signs into the
   * policy (ingest plan C9).
   *
   * The `accept` attribute and the "larger than N MB" message are both built from this, so the
   * page cannot promise a format `CreateUpload` would refuse or quote a limit S3 would not
   * enforce.
   */
  listSourceFormats(): Promise<ListSourceFormatsReply> {
    return this.client.listSourceFormats({});
  }

  /**
   * Uploads the document straight to S3 with a presigned POST, then submits the run.
   *
   * The file never crosses gRPC (plan §18.3) — a 10 MB document would be a 10 MB gRPC-Web frame
   * through Envoy and into api's memory for no reason. `api` only ever sees the upload id.
   *
   * POST rather than PUT because a POST carries a signed *policy*, and the policy's
   * `content-length-range` is what makes the size limit S3's to enforce rather than this code's to
   * respect (ingest plan §7).
   */
  async upload(file: File, onProgress?: (fraction: number) => void): Promise<string> {
    const reply = await this.client.createUpload({
      fileName: file.name,
      sizeBytes: BigInt(file.size),
      contentType: file.type,
    });

    // The same refusal S3 would produce, produced instantly and worded identically — so the
    // common case does not cost an upload the bucket is going to reject anyway.
    if (reply.maxBytes > 0n && BigInt(file.size) > reply.maxBytes) {
      throw new UploadRejectedError(tooLargeMessage(reply.maxBytes), 'EntityTooLarge');
    }

    await this.postToS3(reply, file, onProgress);

    return reply.uploadId;
  }

  async submitRun(options: SubmitOptions): Promise<string> {
    const reply = await this.client.submitRun({
      uploadId: options.uploadId,
      documentId: options.documentId,
      sensitivity: options.sensitivity,
      familyHint: options.familyHint,
      augmentations: options.augmentations,
      priority: options.priority,
      // The key is derived from the upload rather than generated per click, so a double-submit
      // (or a retry after a dropped response) returns the same run instead of billing twice.
      idempotencyKey: `upload:${options.uploadId}`,
      requireReview: options.requireReview,
    });

    return reply.runId;
  }

  getRun(runId: string): Promise<Run> {
    return this.client.getRun({ runId });
  }

  /** Server-streaming progress. The stream ends when the run reaches a terminal state. */
  watchRun(runId: string, signal?: AbortSignal): AsyncIterable<RunEvent> {
    return this.client.watchRun({ runId }, { signal });
  }

  getResult(runId: string): Promise<Result> {
    return this.client.getResult({ runId });
  }

  cancelRun(runId: string): Promise<Run> {
    return this.client.cancelRun({ runId });
  }

  rerunFrom(runId: string, fromPhase: number): Promise<{ runId: string }> {
    return this.client.rerunFrom({ runId, fromPhase });
  }

  async artifactUrl(runId: string, artifactKey: string): Promise<string> {
    const reply = await this.client.getArtifact({ runId, artifactKey });
    return reply.getUrl;
  }

  listReviews(status = 'pending', pageToken = ''): Promise<ListReviewsReply> {
    return this.client.listReviews({ status, pageSize: 50, pageToken });
  }

  submitReviewDecision(
    reviewId: string,
    decision: 'approve' | 'approve_with_edits' | 'reject',
    notes: string,
    edits: ParagraphEdit[] = [],
  ): Promise<Run> {
    return this.client.submitReviewDecision({ reviewId, decision, notes, edits });
  }

  listModels(): Promise<ListModelsReply> {
    return this.client.listModels({});
  }

  /**
   * The presigned POST, via XHR rather than fetch, purely for upload progress: `fetch` cannot
   * report how much of a request body has been sent, and a user uploading a 10 MB scan needs to
   * see that something is happening.
   */
  private postToS3(
    reply: CreateUploadReply,
    file: File,
    onProgress?: (fraction: number) => void,
  ): Promise<void> {
    const form = new FormData();

    for (const [name, value] of Object.entries(reply.fields)) {
      form.append(name, value);
    }

    // MUST be last: S3 stops reading the multipart body at the file part, so any field written
    // after it is never seen and the upload fails the policy it was signed against.
    form.append('file', file);

    return new Promise((resolve, reject) => {
      const request = new XMLHttpRequest();
      request.open('POST', reply.postUrl, true);

      // No Content-Type header is set on purpose: the browser has to generate the multipart
      // boundary itself, and setting the header would replace it with one that has none.

      if (onProgress) {
        request.upload.onprogress = (event) => {
          if (event.lengthComputable) {
            onProgress(event.loaded / event.total);
          }
        };
      }

      request.onload = () =>
        request.status >= 200 && request.status < 300
          ? resolve()
          : reject(rejection(request, reply.maxBytes));

      request.onerror = () =>
        reject(new Error('The upload could not reach storage. Check your connection and retry.'));

      request.send(form);
    });
  }
}

/**
 * Maps S3's own refusal to something a person can act on (ingest plan §7.4).
 *
 * S3 answers a too-large POST with 400 and an XML body carrying `<Code>EntityTooLarge</Code>`.
 * Reading the code rather than guessing from the status matters because an expired policy is also
 * a 4xx, and "try again" is the right advice for exactly one of the two.
 */
function rejection(request: XMLHttpRequest, maxBytes: bigint): UploadRejectedError {
  const code = /<Code>([^<]+)<\/Code>/.exec(request.responseText ?? '')?.[1] ?? '';

  if (code === 'EntityTooLarge') {
    return new UploadRejectedError(tooLargeMessage(maxBytes), code);
  }

  if (code === 'AccessDenied' || code === 'ExpiredToken' || request.status === 403) {
    return new UploadRejectedError('The upload window expired. Try again.', code || 'AccessDenied');
  }

  return new UploadRejectedError(
    `The upload failed (HTTP ${request.status}). Try again.`,
    code || 'Unknown',
  );
}

function tooLargeMessage(maxBytes: bigint): string {
  const megabytes = Number(maxBytes / 1024n / 1024n);
  return `That file is larger than ${megabytes} MB. Try splitting it or exporting a smaller version.`;
}

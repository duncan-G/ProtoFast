import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../grpc-transport';
import {
  Segmentation,
  type CreateUploadReply,
  type ListModelsReply,
  type ListReviewsReply,
  type ParagraphEdit,
  type Priority,
  type Result,
  type Run,
  type RunEvent,
  type Sensitivity,
} from '../../lib/gen/segmentation_pb';

export type { Result, Run, RunEvent, ListReviewsReply, ListModelsReply };

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
   * Uploads the document straight to S3 with a presigned PUT, then submits the run.
   *
   * The file never crosses gRPC (plan §18.3) — a 60 MB document would be a 60 MB gRPC-Web frame
   * through Envoy and into api's memory for no reason. `api` only ever sees the upload id.
   */
  async upload(
    file: File,
    layout: File | null,
    onProgress?: (fraction: number) => void,
  ): Promise<string> {
    const reply = await this.client.createUpload({
      fileName: file.name,
      sizeBytes: BigInt(file.size),
      withLayout: layout !== null,
    });

    await this.putToS3(reply.markdownPutUrl, file, requiredHeaders(reply), onProgress);

    if (layout && reply.layoutPutUrl) {
      await this.putToS3(reply.layoutPutUrl, layout, { 'content-type': 'application/json' });
    }

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
   * A presigned PUT via XHR rather than fetch, purely for upload progress: `fetch` cannot report
   * how much of a request body has been sent, and a user uploading a large scan needs to see that
   * something is happening.
   */
  private putToS3(
    url: string,
    file: File,
    headers: Record<string, string>,
    onProgress?: (fraction: number) => void,
  ): Promise<void> {
    return new Promise((resolve, reject) => {
      const request = new XMLHttpRequest();
      request.open('PUT', url, true);

      for (const [name, value] of Object.entries(headers)) {
        request.setRequestHeader(name, value);
      }

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
          : reject(
              new Error(
                `The upload was rejected by storage (HTTP ${request.status}). ` +
                  'Presigned URLs expire after 15 minutes — try again.',
              ),
            );

      request.onerror = () =>
        reject(new Error('The upload could not reach storage. Check your connection and retry.'));

      request.send(file);
    });
  }
}

/**
 * The signature covers Content-Type, so the PUT has to send back exactly what was signed.
 * Falling back to text/markdown matches what api signs when it returns no header map.
 */
function requiredHeaders(reply: CreateUploadReply): Record<string, string> {
  const headers = { ...reply.requiredHeaders };
  return Object.keys(headers).length > 0 ? headers : { 'content-type': 'text/markdown' };
}

import { inject, Injectable } from '@angular/core';
import type { MessageInitShape } from '@bufbuild/protobuf';
import { timestampDate } from '@bufbuild/protobuf/wkt';
import { createClient } from '@connectrpc/connect';
import { type DirectRequest } from '../../lib/gen/direction_pb';
import {
  DirectorJobs,
  type Job as JobMessage,
  type JobStep as JobStepMessage,
  JobStepSchema,
  type JobStepResult as JobStepResultMessage,
  JobStatus,
} from '../../lib/gen/director_jobs_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { toCharacter, toExtra } from '../stories/connect-story-api';
import {
  fromTarget,
  toDescribed,
  toDirection,
  toEntrySubject,
  toLibraryImage,
  toRefine,
  toSubject,
} from './connect-direction-api';
import type { DirectorJobsApi } from './director-jobs-api';
import { DescribeTarget } from './model/describe-target';
import { DirectTarget } from './model/direct-target';
import { DirectorJob, JobState } from './model/director-job';
import { JobStep } from './model/job-step';
import { JobStepResult } from './model/job-step-result';
import { PictureRefinement } from './model/picture-refinement';
import { PictureTarget } from './model/picture-target';

/** The director job endpoints on the api service, over gRPC-Web through the edge. */
@Injectable({ providedIn: 'root' })
export class ConnectDirectorJobsApi implements DirectorJobsApi {
  private readonly client = createClient(DirectorJobs, inject(GRPC_TRANSPORT));

  async startJob(
    storyId: string,
    label: string,
    steps: JobStep[],
    followVariations = false,
  ): Promise<DirectorJob> {
    const reply = await this.client.startJob({
      storyId,
      label,
      steps: steps.map(fromStep),
      followVariations,
    });
    return toJob(required(reply.job));
  }

  async listJobs(): Promise<DirectorJob[]> {
    const reply = await this.client.listJobs({});
    return reply.jobs.map(toJob);
  }

  async getJob(
    id: string,
    fromStep: number,
  ): Promise<{ job: DirectorJob; results: (JobStepResult | null)[] }> {
    const reply = await this.client.getJob({ jobId: id, fromStep });
    return { job: toJob(required(reply.job)), results: reply.results.map(toResult) };
  }

  async cancelJob(id: string): Promise<DirectorJob> {
    const reply = await this.client.cancelJob({ jobId: id });
    return toJob(required(reply.job));
  }

  async dismissJobs(ids: string[]): Promise<void> {
    await this.client.dismissJobs({ jobIds: ids });
  }
}

function required<T>(message: T | undefined): T {
  if (message === undefined) {
    throw new Error('The API returned an empty reply.');
  }
  return message;
}

const STATES: Record<JobStatus, JobState> = {
  [JobStatus.UNSPECIFIED]: 'queued',
  [JobStatus.QUEUED]: 'queued',
  [JobStatus.RUNNING]: 'running',
  [JobStatus.SUCCEEDED]: 'succeeded',
  [JobStatus.FAILED]: 'failed',
  [JobStatus.CANCELLED]: 'cancelled',
};

function toJob(message: JobMessage): DirectorJob {
  return {
    id: message.id,
    storyId: message.storyId,
    sceneId: message.sceneId ?? null,
    label: message.label,
    state: STATES[message.status],
    steps: message.steps.flatMap(toStep),
    stepsDone: message.stepsDone,
    error: message.error,
    created: message.created ? timestampDate(message.created) : new Date(),
    finished: message.finished ? timestampDate(message.finished) : null,
    lane: message.lane,
  };
}

function refinement(refine: string, referenceAssetId: string): PictureRefinement | null {
  return refine ? { instructions: refine, referenceAssetId: referenceAssetId || null } : null;
}

function refineFields(value: PictureRefinement | null) {
  return value ? toRefine(value) : {};
}

function fromStep(step: JobStep): MessageInitShape<typeof JobStepSchema> {
  switch (step.kind) {
    case 'direct':
      return {
        work: {
          case: 'direct',
          value: {
            sceneId: step.sceneId,
            replaceEdited: step.replaceEdited,
            target: fromTarget(step.target),
            instructions: step.target.kind === 'frame' ? (step.target.instructions ?? '') : '',
          },
        },
      };
    case 'describe':
      return {
        work: { case: 'describe', value: { subject: toEntrySubject(step.target) } },
      };
    case 'castVoice':
      return {
        work: { case: 'castVoice', value: { characterId: step.characterId } },
      };
    case 'audio':
      return {
        work: {
          case: 'audio',
          value: { sceneId: step.sceneId, elementId: step.elementId, regenerate: step.regenerate },
        },
      };
    case 'frameImage':
      return {
        work: {
          case: 'frameImage',
          value: {
            sceneId: step.sceneId,
            frameId: step.frameId,
            regenerate: step.regenerate,
            ...refineFields(step.refinement),
          },
        },
      };
    case 'lookImage':
      return {
        work: {
          case: 'lookImage',
          value: {
            sceneId: step.sceneId,
            characterId: step.characterId,
            elementId: step.elementId ?? undefined,
            regenerate: step.regenerate,
            ...refineFields(step.refinement),
          },
        },
      };
    case 'libraryImage':
      return {
        work: {
          case: 'libraryImage',
          value: {
            subject: toSubject(step.target),
            regenerate: step.regenerate,
            ...refineFields(step.refinement),
          },
        },
      };
  }
}

function toStep(message: JobStepMessage): JobStep[] {
  const work = message.work;
  switch (work.case) {
    case 'direct': {
      const target = toDirectTarget(work.value);
      return target
        ? [
            {
              kind: 'direct',
              sceneId: work.value.sceneId,
              target,
              replaceEdited: work.value.replaceEdited,
            },
          ]
        : [];
    }
    case 'describe': {
      const target = toEntryTarget(work.value.subject);
      return target ? [{ kind: 'describe', target }] : [];
    }
    case 'castVoice':
      return [{ kind: 'castVoice', characterId: work.value.characterId }];
    case 'audio':
      return [
        {
          kind: 'audio',
          sceneId: work.value.sceneId,
          elementId: work.value.elementId ?? '',
          regenerate: work.value.regenerate,
        },
      ];
    case 'frameImage':
      return [
        {
          kind: 'frameImage',
          sceneId: work.value.sceneId,
          frameId: work.value.frameId,
          regenerate: work.value.regenerate,
          refinement: refinement(work.value.refine, work.value.refineReferenceAssetId),
        },
      ];
    case 'lookImage':
      return [
        {
          kind: 'lookImage',
          sceneId: work.value.sceneId,
          characterId: work.value.characterId,
          elementId: work.value.elementId ?? null,
          regenerate: work.value.regenerate,
          refinement: refinement(work.value.refine, work.value.refineReferenceAssetId),
        },
      ];
    case 'libraryImage': {
      const subject = work.value.subject;
      const target: PictureTarget | null =
        subject.case === 'locationVariationId'
          ? { kind: 'locationVariation', id: subject.value }
          : toEntryTarget(subject);
      return target
        ? [
            {
              kind: 'libraryImage',
              target,
              regenerate: work.value.regenerate,
              refinement: refinement(work.value.refine, work.value.refineReferenceAssetId),
            },
          ]
        : [];
    }
    default:
      return [];
  }
}

function toEntryTarget(subject: { case?: string; value?: string }): DescribeTarget | null {
  const id = subject.value ?? '';
  switch (subject.case) {
    case 'characterId':
      return { kind: 'character', id };
    case 'locationId':
      return { kind: 'location', id };
    case 'propId':
      return { kind: 'prop', id };
    case 'extraId':
      return { kind: 'extra', id };
    default:
      return null;
  }
}

function toDirectTarget(request: DirectRequest): DirectTarget | null {
  const target = request.target;
  switch (target.case) {
    case 'scene':
      return { kind: 'scene' };
    case 'storyDay':
      return { kind: 'storyDay' };
    case 'extras':
      return { kind: 'extras' };
    case 'continuity':
      return { kind: 'continuity' };
    case 'appearanceCharacterId':
      return { kind: 'appearance', characterId: target.value };
    case 'propStatePropId':
      return { kind: 'propState', propId: target.value };
    case 'cues':
      return { kind: 'cues' };
    case 'cueElementId':
      return { kind: 'cue', elementId: target.value };
    case 'frames':
      return { kind: 'frames' };
    case 'frameId':
      return { kind: 'frame', frameId: target.value, instructions: request.instructions };
    default:
      return null;
  }
}

/** Null for a reply this client doesn't know, so results stay in step with their steps. */
function toResult(message: JobStepResultMessage): JobStepResult | null {
  const reply = message.reply;
  switch (reply.case) {
    case 'direct':
      return {
        kind: 'direct',
        direction: reply.value.direction ? toDirection(reply.value.direction) : null,
        extras: reply.value.extras.map(toExtra),
        findings: reply.value.findings.map((f) => ({
          frameId: f.frameId,
          check: f.check === 'consistency' ? 'consistency' : 'positioning',
          issue: f.issue,
        })),
      };
    case 'describe':
      return { kind: 'describe', entry: toDescribed(reply.value) };
    case 'castVoice':
      return reply.value.character
        ? { kind: 'castVoice', character: toCharacter(reply.value.character) }
        : null;
    case 'audio':
    case 'frameImage':
    case 'lookImage':
      return {
        kind: reply.case,
        direction: reply.value.direction ? toDirection(reply.value.direction) : null,
      };
    case 'libraryImage':
      return {
        kind: 'libraryImage',
        image: reply.value.image ? toLibraryImage(reply.value.image) : null,
      };
    default:
      return null;
  }
}

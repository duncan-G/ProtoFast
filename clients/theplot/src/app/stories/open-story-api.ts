import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { OpenStories, type OpenStory as OpenStoryMessage } from '../../lib/gen/open_story_pb';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { toStorySummary } from './connect-story-api';
import { OpenStory } from './model/open-story';
import { StorySummary } from './model/story-summary';

export interface OpenStoryImport {
  story: StorySummary;
  /** False when the writer already had a copy, which is the one returned. */
  created: boolean;
}

@Injectable({ providedIn: 'root' })
export class OpenStoryApi {
  private readonly client = createClient(OpenStories, inject(GRPC_TRANSPORT));

  /** Newest first. */
  async listOpenStories(): Promise<OpenStory[]> {
    const reply = await this.client.listOpenStories({});
    return reply.stories.map(toOpenStory);
  }

  async importOpenStory(openStoryId: string): Promise<OpenStoryImport> {
    const reply = await this.client.importOpenStory({ openStoryId });
    if (!reply.story) {
      throw new Error('The api returned no story.');
    }
    return { story: toStorySummary(reply.story), created: reply.created };
  }
}

function toOpenStory(message: OpenStoryMessage): OpenStory {
  return {
    id: message.id,
    title: message.title,
    opening: message.opening,
    sceneCount: message.sceneCount,
    characterCount: message.characterCount,
    publishedAt: new Date(Number(message.publishedUnixSeconds) * 1000),
    importedStoryId: message.importedStoryId || null,
  };
}

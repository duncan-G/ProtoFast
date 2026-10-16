import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { StoryFiles } from '../../lib/gen/story_pb';
import { extensionOf } from '../documents/format';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { toStorySummary } from './connect-story-api';
import { StorySummary } from './model/story-summary';

export const STORY_FILE_EXTENSION = '.theplot';

/** The API refuses a larger file. */
export const STORY_FILE_MAX_BYTES = 4 * 1024 * 1024;

export function isStoryFile(fileName: string): boolean {
  return extensionOf(fileName) === STORY_FILE_EXTENSION;
}

/**
 * Whole stories as `.theplot` files. Importing one is a single call that creates the story as it
 * was exported: nothing is uploaded to storage or read by a model, unlike a document's import.
 */
@Injectable({ providedIn: 'root' })
export class StoryFileApi {
  private readonly client = createClient(StoryFiles, inject(GRPC_TRANSPORT));

  async exportStory(storyId: string): Promise<File> {
    const reply = await this.client.exportStory({ storyId });
    return new File([reply.content.slice()], reply.fileName, { type: 'application/json' });
  }

  async importStory(file: File): Promise<StorySummary> {
    const reply = await this.client.importStory({
      content: new Uint8Array(await file.arrayBuffer()),
    });
    if (!reply.story) {
      throw new Error('The API returned an empty reply.');
    }
    return toStorySummary(reply.story);
  }
}

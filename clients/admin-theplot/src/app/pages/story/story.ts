import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import type { GetStoryReply } from '../../../lib/gen/Admin/Theplot/theplot_admin_pb';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

@Component({
  selector: 'app-story',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/stories" class="text-sm text-indigo-600 hover:underline">← Stories</a>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as reply) {
      <h1 class="mt-4 text-2xl font-bold text-gray-900">{{ reply.story?.title }}</h1>
      <dl
        class="mt-6 grid gap-4 rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm sm:grid-cols-2"
      >
        <div>
          <dt class="text-gray-500">Writer</dt>
          <dd class="font-mono text-xs text-gray-900">{{ reply.story?.ownerId }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Created</dt>
          <dd class="text-gray-900">
            {{ toDate(reply.story?.createdUnixSeconds) | date: 'medium' }}
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Last changed</dt>
          <dd class="text-gray-900">
            {{ toDate(reply.story?.lastModifiedUnixSeconds) | date: 'medium' }}
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Scenes</dt>
          <dd class="text-gray-900">{{ reply.story?.sceneCount }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Characters</dt>
          <dd class="text-gray-900">{{ reply.characterCount }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Locations · props</dt>
          <dd class="text-gray-900">{{ reply.locationCount }} · {{ reply.propCount }}</dd>
        </div>
      </dl>
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class Story {
  private readonly api = inject(TheplotAdminApi);

  readonly id = input.required<string>();

  protected readonly reply = signal<GetStoryReply | null>(null);
  protected readonly error = signal('');

  constructor() {
    afterNextRender(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.reply.set(await this.api.theplot.getStory({ storyId: this.id() }));
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  protected toDate(unixSeconds: bigint | undefined): Date {
    return new Date(Number(unixSeconds ?? 0n) * 1000);
  }
}

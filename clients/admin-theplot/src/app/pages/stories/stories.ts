import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import type { StorySummary } from '../../../lib/gen/Admin/Theplot/theplot_admin_pb';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

const PAGE_SIZE = 25;

@Component({
  selector: 'app-stories',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="text-2xl font-bold text-gray-900">Stories</h1>
    <p class="mt-2 text-gray-600">Every writer's stories, most recently changed first.</p>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <div class="mt-6 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-gray-200 text-gray-500">
          <tr>
            <th class="px-4 py-3 font-medium">Title</th>
            <th class="px-4 py-3 font-medium">Writer</th>
            <th class="px-4 py-3 font-medium">Scenes</th>
            <th class="px-4 py-3 font-medium">Last changed</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          @for (story of stories(); track story.id) {
            <tr>
              <td class="px-4 py-3">
                <a [routerLink]="['/stories', story.id]" class="text-indigo-600 hover:underline">{{
                  story.title
                }}</a>
              </td>
              <td class="px-4 py-3 font-mono text-xs text-gray-600">{{ story.ownerId }}</td>
              <td class="px-4 py-3 text-gray-900">{{ story.sceneCount }}</td>
              <td class="px-4 py-3 text-gray-600">
                {{ toDate(story.lastModifiedUnixSeconds) | date: 'medium' }}
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="4" class="px-4 py-6 text-gray-500">
                {{ loading() ? 'Loading…' : 'No stories yet.' }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    <div class="mt-4 flex items-center gap-3 text-sm text-gray-600">
      <button
        type="button"
        class="rounded-lg border border-gray-300 px-3 py-1.5 hover:bg-gray-50 disabled:opacity-50"
        [disabled]="page() === 0 || loading()"
        (click)="go(page() - 1)"
      >
        Previous
      </button>
      <span>Page {{ page() + 1 }} of {{ pages() }}</span>
      <button
        type="button"
        class="rounded-lg border border-gray-300 px-3 py-1.5 hover:bg-gray-50 disabled:opacity-50"
        [disabled]="page() + 1 >= pages() || loading()"
        (click)="go(page() + 1)"
      >
        Next
      </button>
    </div>
  `,
})
export class Stories {
  private readonly api = inject(TheplotAdminApi);

  protected readonly stories = signal<StorySummary[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  constructor() {
    afterNextRender(() => void this.go(0));
  }

  protected async go(page: number): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const reply = await this.api.theplot.listStories({ page, pageSize: PAGE_SIZE });
      this.stories.set(reply.stories);
      this.total.set(reply.total);
      this.page.set(page);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }

  protected toDate(unixSeconds: bigint): Date {
    return new Date(Number(unixSeconds) * 1000);
  }
}

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
import {
  WorldModelBuildStatus,
  type WorldModelBuildHeader,
} from '../../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { shortId, toDate, usd } from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';
import { BUILD_STATUS_CLASS, BUILD_STATUS_LABEL, buildElapsed } from '../../world-models/format';

const PAGE_SIZE = 25;

/** Every world model build a writer asked for, newest first, narrowed by status. */
@Component({
  selector: 'app-world-models',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">World models</h1>
        <p class="mt-2 text-gray-600">
          Every build of a story’s location world model, newest first. Open one to see each scene
          the model read, what it was asked and answered, and what each step cost.
        </p>
      </div>
    </div>

    <form class="mt-6 flex flex-wrap items-end gap-3 text-sm" (submit)="$event.preventDefault()">
      <label class="flex flex-col gap-1 text-gray-600">
        Status
        <select
          class="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-gray-900"
          [value]="status()"
          (change)="setStatus($any($event.target).value)"
        >
          @for (option of statuses; track option) {
            <option [value]="option">{{ statusLabel[option] }}</option>
          }
        </select>
      </label>
      <span class="ml-auto self-center text-gray-500">
        {{ total() }} {{ total() === 1 ? 'build' : 'builds' }}
      </span>
    </form>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <div class="mt-4 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-gray-200 text-gray-500">
          <tr>
            <th class="px-4 py-3 font-medium">Build</th>
            <th class="px-4 py-3 font-medium">Story</th>
            <th class="px-4 py-3 font-medium">Writer</th>
            <th class="px-4 py-3 font-medium">Requested</th>
            <th class="px-4 py-3 font-medium">Took</th>
            <th class="px-4 py-3 font-medium text-right">Scenes</th>
            <th class="px-4 py-3 font-medium text-right">Steps</th>
            <th class="px-4 py-3 font-medium">Status</th>
            <th class="px-4 py-3 font-medium text-right">Cost</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          @for (build of builds(); track build.id) {
            <tr class="hover:bg-gray-50">
              <td class="px-4 py-3">
                <a
                  [routerLink]="['/world-models', build.id]"
                  class="font-mono text-xs text-indigo-600 hover:underline"
                  [title]="build.id"
                  >{{ shortId(build.id) }}</a
                >
              </td>
              <td class="max-w-xs truncate px-4 py-3" [title]="build.storyTitle">
                <a
                  [routerLink]="['/stories', build.storyId]"
                  class="text-gray-900 hover:underline"
                  >{{ build.storyTitle || '—' }}</a
                >
              </td>
              <td class="px-4 py-3 font-mono text-xs text-gray-500" [title]="build.ownerId">
                {{ shortId(build.ownerId) }}
              </td>
              <td class="px-4 py-3 text-gray-700 whitespace-nowrap">
                {{ toDate(build.requestedUnixMs) | date: 'MMM d, HH:mm:ss' }}
              </td>
              <td class="px-4 py-3 text-gray-700 whitespace-nowrap">{{ buildElapsed(build) }}</td>
              <td class="px-4 py-3 text-right text-gray-900 whitespace-nowrap">
                {{ build.scenesDone }} / {{ build.scenesTotal }}
              </td>
              <td class="px-4 py-3 text-right text-gray-900">{{ build.steps }}</td>
              <td class="px-4 py-3">
                <span [class]="statusClass[build.status]">{{ statusLabel[build.status] }}</span>
              </td>
              <td class="px-4 py-3 text-right text-gray-900 whitespace-nowrap">
                {{ usd(build.costUsdMicros) }}
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="9" class="px-4 py-8 text-center text-gray-500">
                {{ loading() ? 'Loading…' : 'No builds match.' }}
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
export class WorldModels {
  private readonly api = inject(TheplotAdminApi);

  protected readonly statuses = [
    WorldModelBuildStatus.UNSPECIFIED,
    WorldModelBuildStatus.QUEUED,
    WorldModelBuildStatus.RUNNING,
    WorldModelBuildStatus.RETRYING,
    WorldModelBuildStatus.FINISHED,
    WorldModelBuildStatus.FAILED,
    WorldModelBuildStatus.CANCELLED,
  ];
  protected readonly statusLabel = BUILD_STATUS_LABEL;
  protected readonly statusClass = BUILD_STATUS_CLASS;
  protected readonly buildElapsed = buildElapsed;
  protected readonly shortId = shortId;
  protected readonly toDate = toDate;
  protected readonly usd = usd;

  protected readonly status = signal(WorldModelBuildStatus.UNSPECIFIED);
  protected readonly builds = signal<WorldModelBuildHeader[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));

  constructor() {
    afterNextRender(() => void this.go(0));
  }

  protected setStatus(value: string): void {
    this.status.set(Number(value) as WorldModelBuildStatus);
    void this.go(0);
  }

  protected async go(page: number): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const reply = await this.api.worldModels.listBuilds({
        status: this.status(),
        page,
        pageSize: PAGE_SIZE,
      });
      this.builds.set(reply.builds);
      this.total.set(reply.total);
      this.page.set(page);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }
}

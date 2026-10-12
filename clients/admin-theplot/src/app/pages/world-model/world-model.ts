import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import type { GetWorldModelBuildReply } from '../../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { duration, toDate, usd } from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';
import {
  BUILD_STATUS_CLASS,
  BUILD_STATUS_LABEL,
  buildElapsed,
  isBuildActive,
  STEP_KIND_LABEL,
  STEP_STATUS_CLASS,
  STEP_STATUS_LABEL,
  tokens,
} from '../../world-models/format';

const POLL_MS = 10_000;

/**
 * One build: how it went, and every step in order. A scene read opens to show the prompt the
 * model was sent and the reply it gave. While the build is still going the page polls.
 */
@Component({
  selector: 'app-world-model',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/world-models" class="text-sm text-indigo-600 hover:underline">← World models</a>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as reply) {
      @let build = reply.build!;
      <header class="mt-4 flex flex-wrap items-center gap-3">
        <h1 class="text-xl font-bold text-gray-900">
          <a [routerLink]="['/stories', build.storyId]" class="hover:underline">{{
            build.storyTitle || build.storyId
          }}</a>
        </h1>
        <span [class]="statusClass[build.status]">{{ statusLabel[build.status] }}</span>
        <span class="font-mono text-xs text-gray-500">{{ build.id }}</span>
        <a
          [routerLink]="['/stories', build.storyId, 'world']"
          class="ml-auto rounded-lg bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700"
          >View the world</a
        >
      </header>

      @if (build.failure) {
        <p class="mt-4 rounded-lg bg-red-50 border border-red-200 p-4 text-sm text-red-800">
          <span class="font-medium">Failure:</span> {{ build.failure }}
        </p>
      }

      <dl
        class="mt-6 grid gap-x-6 gap-y-4 rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm sm:grid-cols-3 lg:grid-cols-4"
      >
        <div>
          <dt class="text-gray-500">Writer</dt>
          <dd class="font-mono text-xs text-gray-900">{{ build.ownerId }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Requested</dt>
          <dd class="text-gray-900">{{ toDate(build.requestedUnixMs) | date: 'medium' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Started</dt>
          <dd class="text-gray-900">
            @if (toDate(build.startedUnixMs); as at) {
              {{ at | date: 'medium' }}
            } @else {
              not yet
            }
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Ended</dt>
          <dd class="text-gray-900">
            @if (toDate(build.finishedUnixMs); as at) {
              {{ at | date: 'medium' }}
            } @else {
              still going
            }
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Took</dt>
          <dd class="text-gray-900">{{ buildElapsed(build) }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Scenes</dt>
          <dd class="text-gray-900">{{ build.scenesDone }} of {{ build.scenesTotal }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Cost</dt>
          <dd class="text-gray-900">{{ usd(build.costUsdMicros) }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Deliveries</dt>
          <dd class="text-gray-900">{{ build.deliveries }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Graph</dt>
          <dd class="text-gray-900">
            {{ build.partCount }} parts · {{ build.objectCount }} objects ·
            {{ build.observationCount }} observations
          </dd>
        </div>
        <div>
          <dt class="text-gray-500">Trace</dt>
          <dd class="font-mono text-xs text-gray-900">{{ build.traceId || '—' }}</dd>
        </div>
        <div class="sm:col-span-2">
          <dt class="text-gray-500">Writer sees</dt>
          <dd class="text-gray-900">{{ build.message || '—' }}</dd>
        </div>
      </dl>

      <section class="mt-8">
        <h2 class="text-lg font-semibold text-gray-900">
          Steps <span class="ml-1 text-sm font-normal text-gray-400">{{ reply.steps.length }}</span>
        </h2>
        <div class="mt-3 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
          <table class="w-full text-left text-sm">
            <thead class="border-b border-gray-200 text-gray-500">
              <tr>
                <th class="px-4 py-3 font-medium">#</th>
                <th class="px-4 py-3 font-medium">Step</th>
                <th class="px-4 py-3 font-medium">Status</th>
                <th class="px-4 py-3 font-medium">Model</th>
                <th class="px-4 py-3 font-medium text-right">Tokens</th>
                <th class="px-4 py-3 font-medium text-right">Cost</th>
                <th class="px-4 py-3 font-medium">Took</th>
                <th class="px-4 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-100">
              @for (step of reply.steps; track step.id) {
                <tr class="hover:bg-gray-50">
                  <td class="px-4 py-3 text-gray-500">{{ step.sequence }}</td>
                  <td class="px-4 py-3">
                    <span class="text-gray-900">{{ step.title }}</span>
                    <span class="ml-2 text-xs text-gray-500">{{ kindLabel[step.kind] }}</span>
                    @if (step.summary) {
                      <p class="mt-0.5 text-xs text-gray-600">{{ step.summary }}</p>
                    }
                    @if (step.failure) {
                      <p class="mt-0.5 text-xs text-red-700">{{ step.failure }}</p>
                    }
                  </td>
                  <td class="px-4 py-3">
                    <span [class]="stepStatusClass[step.status]">{{
                      stepStatusLabel[step.status]
                    }}</span>
                  </td>
                  <td class="px-4 py-3 font-mono text-xs text-gray-700">
                    {{ step.modelId || '—' }}
                  </td>
                  <td class="px-4 py-3 text-right text-gray-700 whitespace-nowrap">
                    @if (step.modelId) {
                      {{ tokens(step.inputTokens) }} in · {{ tokens(step.outputTokens) }} out
                    } @else {
                      —
                    }
                  </td>
                  <td class="px-4 py-3 text-right text-gray-900 whitespace-nowrap">
                    {{ usd(step.costUsdMicros) }}
                  </td>
                  <td class="px-4 py-3 text-gray-700 whitespace-nowrap">
                    {{ took(step.startedUnixMs, step.finishedUnixMs) }}
                  </td>
                  <td class="px-4 py-3 text-right">
                    @if (step.prompt || step.reply) {
                      <button
                        type="button"
                        class="text-xs text-indigo-600 hover:underline"
                        (click)="toggle(step.id)"
                      >
                        {{ opened() === step.id ? 'Hide' : 'Show' }} exchange
                      </button>
                    }
                  </td>
                </tr>
                @if (opened() === step.id) {
                  <tr>
                    <td colspan="8" class="bg-gray-50 px-4 py-4">
                      <div class="grid gap-4 lg:grid-cols-2">
                        <div>
                          <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">
                            Prompt
                          </h3>
                          <pre
                            class="mt-2 max-h-96 overflow-auto rounded-lg border border-gray-200 bg-white p-3 text-xs whitespace-pre-wrap text-gray-800"
                            >{{ step.prompt }}</pre
                          >
                        </div>
                        <div>
                          <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-500">
                            Reply
                          </h3>
                          <pre
                            class="mt-2 max-h-96 overflow-auto rounded-lg border border-gray-200 bg-white p-3 text-xs whitespace-pre-wrap text-gray-800"
                            >{{ step.reply }}</pre
                          >
                        </div>
                      </div>
                    </td>
                  </tr>
                }
              } @empty {
                <tr>
                  <td colspan="8" class="px-4 py-8 text-center text-gray-500">
                    No step has been recorded yet.
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class WorldModel {
  private readonly api = inject(TheplotAdminApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly statusLabel = BUILD_STATUS_LABEL;
  protected readonly statusClass = BUILD_STATUS_CLASS;
  protected readonly kindLabel = STEP_KIND_LABEL;
  protected readonly stepStatusLabel = STEP_STATUS_LABEL;
  protected readonly stepStatusClass = STEP_STATUS_CLASS;
  protected readonly buildElapsed = buildElapsed;
  protected readonly toDate = toDate;
  protected readonly usd = usd;
  protected readonly tokens = tokens;

  protected readonly reply = signal<GetWorldModelBuildReply | null>(null);
  protected readonly error = signal('');
  protected readonly opened = signal<string | null>(null);

  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    afterNextRender(() => void this.load());
    this.destroyRef.onDestroy(() => {
      if (this.timer !== null) {
        clearTimeout(this.timer);
      }
    });
  }

  protected toggle(stepId: string): void {
    this.opened.update((open) => (open === stepId ? null : stepId));
  }

  protected took(started: bigint, finished: bigint): string {
    return finished ? duration(Number(finished) - Number(started)) : '…';
  }

  private async load(): Promise<void> {
    try {
      const reply = await this.api.worldModels.getBuild({ buildId: this.id() });
      this.reply.set(reply);
      if (reply.build && isBuildActive(reply.build.status)) {
        this.timer = setTimeout(() => void this.load(), POLL_MS);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

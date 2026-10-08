import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ExecutorOrigin,
  type GetExecutorReply,
} from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import {
  artifactParam,
  NEUTRAL_BADGE,
  shortHash,
  TIER_CLASS,
  TIER_LABEL,
} from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

const ORIGIN_LABEL: Record<ExecutorOrigin, string> = {
  [ExecutorOrigin.UNSPECIFIED]: 'unknown origin',
  [ExecutorOrigin.SEED]: 'seeded',
  [ExecutorOrigin.AGENT_DEFINED]: 'defined by the agent',
  [ExecutorOrigin.DISTILLED]: 'distilled',
};

/** An executor's spec and the playbook it runs with. Executors are engine-wide, not a family's. */
@Component({
  selector: 'app-executor',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/runs" class="text-sm text-indigo-600 hover:underline">← Runs</a>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as spec) {
      <header class="mt-4 flex flex-wrap items-center gap-3">
        <h1 class="font-mono text-xl font-bold text-gray-900">
          {{ spec.id }}&#64;{{ spec.version }}
        </h1>
        <span [class]="tierClass[spec.tier]">{{ tierLabel[spec.tier] }}</span>
        <span class="{{ neutralBadge }}">{{ originLabel[spec.origin] }}</span>
        <span
          class="{{ neutralBadge }}"
          [class.bg-emerald-50]="spec.promoted"
          [class.text-emerald-800]="spec.promoted"
        >
          {{ spec.promoted ? 'promoted' : 'not promoted' }}
        </span>
      </header>

      <dl
        class="mt-6 grid gap-4 rounded-2xl border border-gray-200 bg-white p-6 text-sm shadow-sm sm:grid-cols-3"
      >
        <div>
          <dt class="text-gray-500">Model class</dt>
          <dd class="text-gray-900">{{ spec.modelClass || '— (code)' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Tools</dt>
          <dd class="text-gray-900">{{ spec.tools.length ? spec.tools.join(', ') : 'none' }}</dd>
        </div>
        <div>
          <dt class="text-gray-500">Code</dt>
          <dd class="font-mono text-xs text-gray-900">
            {{ spec.codeAssembly ? shortHash(spec.codeAssembly) : '—' }}
          </dd>
        </div>
      </dl>

      @if (spec.playbook; as playbook) {
        <section class="mt-6 rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 class="font-semibold text-gray-900">
            Playbook
            <span class="font-mono text-sm font-normal text-gray-500"
              >{{ playbook.id }}&#64;{{ playbook.version }}</span
            >
          </h2>
          <pre
            class="mt-3 whitespace-pre-wrap break-words rounded-lg bg-gray-50 p-4 text-sm leading-relaxed text-gray-900"
            >{{ playbook.instructions }}</pre
          >

          @if (rules().length) {
            <h3 class="mt-5 text-sm font-medium text-gray-700">Learned rules</h3>
            <dl class="mt-2 divide-y divide-gray-100 text-sm">
              @for (rule of rules(); track rule[0]) {
                <div class="py-2">
                  <dt class="font-mono text-xs text-gray-500">{{ rule[0] }}</dt>
                  <dd class="text-gray-900">{{ rule[1] }}</dd>
                </div>
              }
            </dl>
          }

          @if (playbook.examples.length) {
            <h3 class="mt-5 text-sm font-medium text-gray-700">Examples</h3>
            <ul class="mt-2 space-y-1 text-sm">
              @for (example of playbook.examples; track $index) {
                <li class="flex flex-wrap items-center gap-2 font-mono text-xs">
                  @if (example.input; as input) {
                    <a
                      [routerLink]="['/runs', input.runId]"
                      [queryParams]="{ artifact: artifactParam(input) }"
                      class="rounded-lg border border-gray-300 bg-gray-50 px-2 py-1 text-gray-800 hover:border-indigo-400"
                      >{{ input.stageId }} · {{ shortHash(input.hash) }}</a
                    >
                  }
                  <span class="text-gray-400">→</span>
                  @if (example.output; as output) {
                    <a
                      [routerLink]="['/runs', output.runId]"
                      [queryParams]="{ artifact: artifactParam(output) }"
                      class="rounded-lg border border-indigo-300 bg-indigo-50 px-2 py-1 text-indigo-900 hover:bg-indigo-100"
                      >{{ output.stageId }} · {{ shortHash(output.hash) }}</a
                    >
                  }
                </li>
              }
            </ul>
          }
        </section>
      } @else {
        <p class="mt-6 text-sm text-gray-600">
          This executor has no playbook: it is code, or the engine's own stage agent.
        </p>
      }
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class Executor {
  private readonly api = inject(TheplotAdminApi);

  readonly id = input.required<string>();
  readonly version = input.required<string>();

  protected readonly tierClass = TIER_CLASS;
  protected readonly tierLabel = TIER_LABEL;
  protected readonly originLabel = ORIGIN_LABEL;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly shortHash = shortHash;
  protected readonly artifactParam = artifactParam;

  protected readonly reply = signal<GetExecutorReply | null>(null);
  protected readonly error = signal('');
  protected readonly rules = computed(() => Object.entries(this.reply()?.playbook?.rules ?? {}));

  constructor() {
    afterNextRender(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.reply.set(
        await this.api.runs.getExecutor({ id: this.id(), version: Number(this.version()) }),
      );
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

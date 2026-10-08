import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type { GetFamilyReply } from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import {
  familyKey,
  NEUTRAL_BADGE,
  RUN_MODE_LABEL,
  TIER_CLASS,
  TIER_LABEL,
  toDate,
} from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

interface RemovedSkill {
  id: string;
  removedUnixMs: bigint;
  reason: string;
  versions: number[];
}

/** One family: what an operator wrote about it, its generations, and everything its runs learned. */
@Component({
  selector: 'app-family',
  imports: [DatePipe, DecimalPipe, FormsModule, PercentPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/families" class="text-sm text-indigo-600 hover:underline"
      >← Document families</a
    >

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as reply) {
      <header class="mt-4">
        <h1 class="text-2xl font-bold text-gray-900">
          {{ reply.info?.displayName || reply.family }}
        </h1>
        <p class="font-mono text-sm text-gray-500">{{ reply.family }}</p>
        @if (reply.info?.description) {
          <p class="mt-2 max-w-2xl text-gray-700">{{ reply.info?.description }}</p>
        }
      </header>

      <div class="mt-6 grid gap-6 lg:grid-cols-2">
        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <div class="flex items-center justify-between">
            <h2 class="font-semibold text-gray-900">About</h2>
            @if (!editing()) {
              <button
                type="button"
                class="text-sm text-indigo-600 hover:underline"
                (click)="startEditing(reply)"
              >
                {{ reply.info ? 'Edit' : 'Describe this family' }}
              </button>
            }
          </div>
          @if (editing()) {
            <form class="mt-4 grid gap-3 text-sm" (ngSubmit)="save()">
              <label class="flex flex-col gap-1 text-gray-700">
                Display name
                <input
                  name="displayName"
                  class="rounded-lg border border-gray-300 px-3 py-2 text-gray-900"
                  [(ngModel)]="form.displayName"
                  required
                  maxlength="200"
                />
              </label>
              <label class="flex flex-col gap-1 text-gray-700">
                Description
                <textarea
                  name="description"
                  rows="4"
                  class="rounded-lg border border-gray-300 px-3 py-2 text-gray-900"
                  [(ngModel)]="form.description"
                ></textarea>
              </label>
              @if (saveError()) {
                <p class="rounded-lg bg-red-50 border border-red-200 p-3 text-red-800">
                  {{ saveError() }}
                </p>
              }
              <div class="flex gap-2">
                <button
                  type="submit"
                  class="rounded-lg bg-indigo-600 px-4 py-2 font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
                  [disabled]="saving() || !form.displayName"
                >
                  {{ saving() ? 'Saving…' : 'Save' }}
                </button>
                <button
                  type="button"
                  class="rounded-lg border border-gray-300 px-4 py-2 hover:bg-gray-50"
                  (click)="editing.set(false)"
                >
                  Cancel
                </button>
              </div>
            </form>
          } @else if (reply.info; as info) {
            <dl class="mt-4 grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt class="text-gray-500">Registered by</dt>
                <dd class="font-mono text-xs text-gray-900">{{ info.createdBy || '—' }}</dd>
              </div>
              <div>
                <dt class="text-gray-500">Registered</dt>
                <dd class="text-gray-900">{{ toDate(info.createdUnixMs) | date: 'medium' }}</dd>
              </div>
              <div>
                <dt class="text-gray-500">Last edited</dt>
                <dd class="text-gray-900">{{ toDate(info.updatedUnixMs) | date: 'medium' }}</dd>
              </div>
            </dl>
          } @else {
            <p class="mt-4 text-sm text-gray-600">
              Nobody has described this family yet, so the classifier cannot sort documents into it.
            </p>
          }
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 class="font-semibold text-gray-900">Generation {{ reply.currentGeneration }}</h2>
          <p class="mt-1 text-sm text-gray-600">
            @if (toDate(reply.resetUnixMs); as at) {
              Reset {{ at | date: 'medium' }}. Earlier generations keep their runs and skills but no
              new run sees them.
            } @else {
              Never reset: the family still runs on everything it has learned since its first
              import.
            }
          </p>
          <div class="mt-4 flex flex-wrap items-center gap-3 text-sm">
            <label class="flex items-center gap-2 text-gray-700">
              Showing
              <select
                class="rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-gray-900"
                [value]="reply.generation"
                (change)="view(Number($any($event.target).value))"
              >
                @for (generation of generations(); track generation) {
                  <option [value]="generation">
                    generation {{ generation
                    }}{{ generation === reply.currentGeneration ? ' (current)' : '' }}
                  </option>
                }
              </select>
            </label>
            <span class="ml-auto"></span>
            @if (confirmingReset()) {
              <span class="text-gray-700"
                >Start generation {{ reply.currentGeneration + 1 }} from scratch?</span
              >
              <button
                type="button"
                class="rounded-lg bg-red-600 px-3 py-1.5 font-medium text-white hover:bg-red-500 disabled:opacity-50"
                [disabled]="resetting()"
                (click)="reset()"
              >
                {{ resetting() ? 'Resetting…' : 'Yes, reset' }}
              </button>
              <button
                type="button"
                class="rounded-lg border border-gray-300 px-3 py-1.5 hover:bg-gray-50"
                (click)="confirmingReset.set(false)"
              >
                Cancel
              </button>
            } @else {
              <button
                type="button"
                class="rounded-lg border border-red-300 px-3 py-1.5 font-medium text-red-700 hover:bg-red-50"
                (click)="confirmingReset.set(true)"
              >
                Reset family
              </button>
            }
          </div>
          @if (resetError()) {
            <p class="mt-3 rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800">
              {{ resetError() }}
            </p>
          }
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 class="font-semibold text-gray-900">Policy</h2>
          <dl class="mt-4 grid gap-3 text-sm sm:grid-cols-3">
            <div>
              <dt class="text-gray-500">Mode</dt>
              <dd class="text-gray-900">{{ modeLabel[reply.mode] }}</dd>
            </div>
            <div>
              <dt class="text-gray-500">Workflow</dt>
              <dd class="font-mono text-xs text-gray-900">
                {{
                  reply.workflowId
                    ? reply.workflowId + '@' + reply.workflowVersion
                    : 'none mined yet'
                }}
              </dd>
            </div>
            <div>
              <dt class="text-gray-500">Pass rate</dt>
              <dd class="text-gray-900">
                {{ reply.confidenceMean | percent: '1.0-1' }}
                <span class="text-xs text-gray-500"
                  >over {{ reply.confidenceObservations | number: '1.0-1' }} observations</span
                >
              </dd>
            </div>
          </dl>
          @if (reply.minedWorkflows.length) {
            <h3 class="mt-4 text-sm font-medium text-gray-700">Mined workflows</h3>
            <ul class="mt-1 space-y-1 text-sm">
              @for (workflow of reply.minedWorkflows; track workflow.id + workflow.version) {
                <li class="flex flex-wrap items-center gap-2">
                  <span class="font-mono text-xs text-gray-900"
                    >{{ workflow.id }}&#64;{{ workflow.version }}</span
                  >
                  <span class="text-gray-600">{{ workflow.stages }} stages</span>
                  <span class="{{ neutralBadge }}">{{
                    workflow.promoted ? 'promoted' : 'draft'
                  }}</span>
                  <span class="text-xs text-gray-500">{{
                    toDate(workflow.minedUnixMs) | date: 'medium'
                  }}</span>
                </li>
              }
            </ul>
          }
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h2 class="font-semibold text-gray-900">Runs</h2>
          @if (reply.runsByGeneration.length) {
            <table class="mt-3 w-full text-left text-sm">
              <thead class="text-gray-500">
                <tr>
                  <th class="py-1 font-medium">Generation</th>
                  <th class="py-1 font-medium text-right">Runs</th>
                  <th class="py-1 font-medium text-right">Running</th>
                  <th class="py-1 font-medium">Last</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-100">
                @for (row of reply.runsByGeneration; track row.generation) {
                  <tr>
                    <td class="py-1.5 text-gray-900">{{ row.generation }}</td>
                    <td class="py-1.5 text-right text-gray-900">{{ row.runs }}</td>
                    <td class="py-1.5 text-right text-gray-900">{{ row.openRuns }}</td>
                    <td class="py-1.5 text-gray-600">
                      {{ toDate(row.lastRunUnixMs) | date: 'MMM d, HH:mm' }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="mt-3 text-sm text-gray-600">No run has used this family yet.</p>
          }
          <a
            routerLink="/runs"
            [queryParams]="{ family: reply.family }"
            class="mt-3 inline-block text-sm text-indigo-600 hover:underline"
            >All runs for this family →</a
          >
        </section>
      </div>

      <h2 class="mt-10 text-lg font-semibold text-gray-900">
        What generation {{ reply.generation }} has learned
      </h2>
      <p class="mt-1 text-sm text-gray-600">
        The agent grows these as it imports documents of this family; a reset starts a generation
        with none.
      </p>

      <div class="mt-4 grid gap-6 lg:grid-cols-2">
        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h3 class="font-semibold text-gray-900">
            Skills <span class="text-gray-400">{{ liveSkills().length }}</span>
          </h3>
          <ul class="mt-3 divide-y divide-gray-100 text-sm">
            @for (skill of liveSkills(); track skill.id + skill.version) {
              <li class="flex items-center justify-between py-2">
                <a
                  [routerLink]="['/skills', skill.id, skill.version]"
                  class="font-mono text-indigo-600 hover:underline"
                >
                  {{ skill.id }}&#64;{{ skill.version }}
                </a>
                <span class="text-xs text-gray-500">{{
                  toDate(skill.addedUnixMs) | date: 'MMM d, HH:mm'
                }}</span>
              </li>
            } @empty {
              <li class="py-2 text-gray-500">None yet.</li>
            }
          </ul>
          @if (removedSkills().length) {
            <h4 class="mt-5 text-xs font-semibold uppercase tracking-wide text-gray-500">
              Removed <span class="text-gray-400">{{ removedSkills().length }}</span>
            </h4>
            <p class="mt-1 text-xs text-gray-500">No run lists or loads these any more.</p>
            <ul class="mt-2 divide-y divide-gray-100 text-sm">
              @for (removed of removedSkills(); track removed.id + removed.removedUnixMs) {
                <li class="py-2">
                  <div class="flex items-center justify-between gap-3">
                    <span class="font-mono text-gray-500 line-through">{{ removed.id }}</span>
                    <span class="text-xs text-gray-500">{{
                      toDate(removed.removedUnixMs) | date: 'MMM d, HH:mm'
                    }}</span>
                  </div>
                  <p class="mt-1 text-gray-600">{{ removed.reason }}</p>
                  <p class="mt-1 flex flex-wrap gap-x-2 font-mono text-xs">
                    @for (version of removed.versions; track version) {
                      <a
                        [routerLink]="['/skills', removed.id, version]"
                        class="text-indigo-600 hover:underline"
                        >&#64;{{ version }}</a
                      >
                    }
                  </p>
                </li>
              }
            </ul>
          }
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h3 class="font-semibold text-gray-900">
            Executors <span class="text-gray-400">{{ reply.executors.length }}</span>
          </h3>
          <ul class="mt-3 divide-y divide-gray-100 text-sm">
            @for (executor of reply.executors; track executor.id + executor.version) {
              <li class="flex items-center justify-between py-2">
                <a
                  [routerLink]="['/executors', executor.id, executor.version]"
                  class="font-mono text-indigo-600 hover:underline"
                >
                  {{ executor.id }}&#64;{{ executor.version }}
                </a>
                <span class="text-xs text-gray-500">{{
                  toDate(executor.addedUnixMs) | date: 'MMM d, HH:mm'
                }}</span>
              </li>
            } @empty {
              <li class="py-2 text-gray-500">None yet; the agent does every stage itself.</li>
            }
          </ul>
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h3 class="font-semibold text-gray-900">
            Verifiers <span class="text-gray-400">{{ reply.verifiers.length }}</span>
          </h3>
          <ul class="mt-3 divide-y divide-gray-100 text-sm">
            @for (verifier of reply.verifiers; track verifier.id) {
              <li class="py-2">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="font-mono text-gray-900">{{ verifier.id }}</span>
                  <span class="{{ neutralBadge }}">stage {{ verifier.stageId }}</span>
                  <span class="ml-auto text-xs text-gray-500">{{
                    toDate(verifier.addedUnixMs) | date: 'MMM d, HH:mm'
                  }}</span>
                </div>
                <p class="mt-1 whitespace-pre-wrap text-gray-600">{{ verifier.rubric }}</p>
              </li>
            } @empty {
              <li class="py-2 text-gray-500">None yet.</li>
            }
          </ul>
        </section>

        <section class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <h3 class="font-semibold text-gray-900">
            Stage policies <span class="text-gray-400">{{ reply.stagePolicies.length }}</span>
          </h3>
          <ul class="mt-3 divide-y divide-gray-100 text-sm">
            @for (policy of reply.stagePolicies; track policy.stageId) {
              <li class="py-2">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="font-medium text-gray-900">{{ policy.stageId }}</span>
                  <span class="text-gray-500">primary</span>
                  <span [class]="tierClass[policy.primary]">{{ tierLabel[policy.primary] }}</span>
                  <span class="text-gray-600">{{ policy.confidenceMean | percent: '1.0-1' }}</span>
                  @if (policy.shadow !== undefined) {
                    <span class="text-gray-500">shadow</span>
                    <span [class]="tierClass[policy.shadow]">{{ tierLabel[policy.shadow] }}</span>
                    <span class="text-gray-600">{{ policy.shadowMean | percent: '1.0-1' }}</span>
                  }
                </div>
                <ul class="mt-1 flex flex-wrap gap-1.5">
                  @for (rung of policy.ladder; track rung.tier) {
                    <li
                      class="rounded-lg border border-gray-200 bg-gray-50 px-2 py-0.5 font-mono text-xs text-gray-700"
                    >
                      {{ tierLabel[rung.tier] }}: {{ rung.executorId }}&#64;{{
                        rung.executorVersion
                      }}
                    </li>
                  }
                </ul>
              </li>
            } @empty {
              <li class="py-2 text-gray-500">None yet; every stage runs at the orchestrator.</li>
            }
          </ul>
        </section>
      </div>
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class Family {
  private readonly api = inject(TheplotAdminApi);

  readonly family = input.required<string>();

  protected readonly Number = Number;
  protected readonly modeLabel = RUN_MODE_LABEL;
  protected readonly tierClass = TIER_CLASS;
  protected readonly tierLabel = TIER_LABEL;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly toDate = toDate;
  protected readonly familyKey = familyKey;

  protected readonly reply = signal<GetFamilyReply | null>(null);
  protected readonly error = signal('');
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly saveError = signal('');
  protected readonly confirmingReset = signal(false);
  protected readonly resetting = signal(false);
  protected readonly resetError = signal('');
  protected readonly form = { displayName: '', description: '' };
  protected readonly liveSkills = computed(() =>
    (this.reply()?.skills ?? []).filter((s) => !s.removedUnixMs),
  );
  protected readonly removedSkills = computed(() => {
    const groups = new Map<string, RemovedSkill>();
    for (const skill of this.reply()?.skills ?? []) {
      if (!skill.removedUnixMs) continue;
      const key = `${skill.id}:${skill.removedUnixMs}`;
      const group = groups.get(key) ?? {
        id: skill.id,
        removedUnixMs: skill.removedUnixMs,
        reason: skill.removalReason,
        versions: [],
      };
      group.versions.push(skill.version);
      groups.set(key, group);
    }
    return [...groups.values()].sort((a, b) => Number(b.removedUnixMs - a.removedUnixMs));
  });
  protected readonly generations = computed(() => {
    const current = this.reply()?.currentGeneration ?? 0;
    return Array.from({ length: current + 1 }, (_, i) => current - i);
  });

  constructor() {
    afterNextRender(() => void this.load());
  }

  protected startEditing(reply: GetFamilyReply): void {
    this.form.displayName = reply.info?.displayName ?? reply.family;
    this.form.description = reply.info?.description ?? '';
    this.saveError.set('');
    this.editing.set(true);
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    this.saveError.set('');
    try {
      await this.api.families.updateFamily({ family: this.family(), ...this.form });
      this.editing.set(false);
      await this.load(this.reply()?.generation);
    } catch (err) {
      this.saveError.set(errorMessage(err));
    } finally {
      this.saving.set(false);
    }
  }

  protected async reset(): Promise<void> {
    this.resetting.set(true);
    this.resetError.set('');
    try {
      await this.api.families.resetFamily({ family: this.family() });
      this.confirmingReset.set(false);
      await this.load();
    } catch (err) {
      this.resetError.set(errorMessage(err));
    } finally {
      this.resetting.set(false);
    }
  }

  protected view(generation: number): void {
    void this.load(generation);
  }

  private async load(generation?: number): Promise<void> {
    try {
      this.reply.set(await this.api.families.getFamily({ family: this.family(), generation }));
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

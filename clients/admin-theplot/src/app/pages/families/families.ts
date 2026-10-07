import { afterNextRender, ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import type { FamilySummary } from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { NEUTRAL_BADGE, RUN_MODE_LABEL, toDate } from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

/** Every document family the engine knows, and a form to register one ahead of its first run. */
@Component({
  selector: 'app-families',
  imports: [DatePipe, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Document families</h1>
        <p class="mt-2 max-w-2xl text-gray-600">
          A family is a kind of document similar enough to share what the engine learns: the skills,
          executors and verifiers its runs define. The classifier sorts each document by these
          descriptions and opens a family when none fits; register one here to describe it ahead
          of time.
        </p>
      </div>
      <button
        type="button"
        class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-500"
        (click)="adding.set(!adding())"
      >
        {{ adding() ? 'Cancel' : 'Add family' }}
      </button>
    </div>

    @if (adding()) {
      <form
        class="mt-6 grid gap-4 rounded-2xl border border-indigo-200 bg-indigo-50/40 p-6 text-sm sm:grid-cols-2"
        (ngSubmit)="create()"
      >
        <label class="flex flex-col gap-1 text-gray-700">
          Name
          <input
            name="family"
            class="rounded-lg border border-gray-300 bg-white px-3 py-2 font-mono text-gray-900"
            [(ngModel)]="form.family"
            required
            pattern="[a-z][a-z0-9_-]*"
            maxlength="64"
            placeholder="screenplay"
            autocomplete="off"
          />
          <span class="text-xs text-gray-500">
            The key every store uses: lowercase letters, digits, - and _, starting with a letter.
          </span>
        </label>
        <label class="flex flex-col gap-1 text-gray-700">
          Display name
          <input
            name="displayName"
            class="rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900"
            [(ngModel)]="form.displayName"
            required
            maxlength="200"
            placeholder="Screenplays"
          />
        </label>
        <label class="flex flex-col gap-1 text-gray-700 sm:col-span-2">
          Description
          <textarea
            name="description"
            rows="3"
            class="rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900"
            [(ngModel)]="form.description"
            placeholder="What these documents look like and what the import should produce from them."
          ></textarea>
        </label>
        @if (createError()) {
          <p class="rounded-lg bg-red-50 border border-red-200 p-3 text-red-800 sm:col-span-2">
            {{ createError() }}
          </p>
        }
        <div class="flex gap-2 sm:col-span-2">
          <button
            type="submit"
            class="rounded-lg bg-indigo-600 px-4 py-2 font-medium text-white hover:bg-indigo-500 disabled:opacity-50"
            [disabled]="creating() || !form.family || !form.displayName"
          >
            {{ creating() ? 'Registering…' : 'Register family' }}
          </button>
        </div>
      </form>
    }

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <div class="mt-6 overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-gray-200 text-gray-500">
          <tr>
            <th class="px-4 py-3 font-medium">Family</th>
            <th class="px-4 py-3 font-medium">Generation</th>
            <th class="px-4 py-3 font-medium">Mode</th>
            <th class="px-4 py-3 font-medium text-right">Runs</th>
            <th class="px-4 py-3 font-medium">Learned</th>
            <th class="px-4 py-3 font-medium">Last run</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100">
          @for (family of families(); track family.family) {
            <tr class="hover:bg-gray-50">
              <td class="px-4 py-3">
                <a
                  [routerLink]="['/families', family.family]"
                  class="font-medium text-indigo-600 hover:underline"
                >
                  {{ family.info?.displayName || family.family }}
                </a>
                <div class="font-mono text-xs text-gray-500">
                  {{ family.family }}
                  @if (!family.info) {
                    <span class="ml-1 text-gray-400">· not described</span>
                  }
                </div>
              </td>
              <td class="px-4 py-3 text-gray-900">{{ family.generation }}</td>
              <td class="px-4 py-3 text-gray-700">
                {{ modeLabel[family.mode] }}
                @if (family.workflowId) {
                  <span class="block font-mono text-xs text-gray-500"
                    >{{ family.workflowId }}&#64;{{ family.workflowVersion }}</span
                  >
                }
              </td>
              <td class="px-4 py-3 text-right text-gray-900">
                {{ family.runs }}
                @if (family.openRuns) {
                  <span class="{{ neutralBadge }} ml-1 bg-amber-50 text-amber-800"
                    >{{ family.openRuns }} running</span
                  >
                }
              </td>
              <td class="px-4 py-3 text-gray-700 whitespace-nowrap">
                {{ family.skills }} skills · {{ family.executors }} executors ·
                {{ family.verifiers }} verifiers
              </td>
              <td class="px-4 py-3 text-gray-600 whitespace-nowrap">
                @if (toDate(family.lastRunUnixMs); as at) {
                  {{ at | date: 'medium' }}
                } @else {
                  —
                }
              </td>
            </tr>
          } @empty {
            <tr>
              <td colspan="6" class="px-4 py-8 text-center text-gray-500">
                {{
                  loading() ? 'Loading…' : 'No document families yet. The first import creates one.'
                }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class Families {
  private readonly api = inject(TheplotAdminApi);
  private readonly router = inject(Router);

  protected readonly modeLabel = RUN_MODE_LABEL;
  protected readonly neutralBadge = NEUTRAL_BADGE;
  protected readonly toDate = toDate;

  protected readonly families = signal<FamilySummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly adding = signal(false);
  protected readonly creating = signal(false);
  protected readonly createError = signal('');
  protected readonly form = { family: '', displayName: '', description: '' };

  constructor() {
    afterNextRender(() => void this.load());
  }

  protected async create(): Promise<void> {
    this.creating.set(true);
    this.createError.set('');
    try {
      await this.api.families.createFamily(this.form);
      await this.router.navigate(['/families', this.form.family]);
    } catch (err) {
      this.createError.set(errorMessage(err));
    } finally {
      this.creating.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.families.set((await this.api.families.listFamilies({})).families);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }
}

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import type { GetSkillReply } from '../../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { shortHash } from '../../engine/format';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';

/** A skill the agent wrote for a family: its instructions and the scripts' C# source. */
@Component({
  selector: 'app-skill',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/families" class="text-sm text-indigo-600 hover:underline"
      >← Document families</a
    >

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    @if (reply(); as skill) {
      <header class="mt-4">
        <p class="text-xs uppercase tracking-wide text-gray-500">Skill</p>
        <h1 class="font-mono text-xl font-bold text-gray-900">
          {{ skill.id }}&#64;{{ skill.version }}
        </h1>
        @if (skill.description) {
          <p class="mt-2 max-w-2xl text-gray-700">{{ skill.description }}</p>
        }
      </header>

      <section class="mt-6 rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
        <h2 class="font-semibold text-gray-900">Instructions</h2>
        <pre
          class="mt-3 whitespace-pre-wrap break-words rounded-lg bg-gray-50 p-4 text-sm leading-relaxed text-gray-900"
          >{{ skill.instructions }}</pre
        >
      </section>

      <section class="mt-6">
        <h2 class="font-semibold text-gray-900">
          Scripts <span class="text-gray-400">{{ skill.scripts.length }}</span>
        </h2>
        <div class="mt-3 space-y-3">
          @for (script of skill.scripts; track script.codeHash) {
            <details
              class="rounded-2xl border border-gray-200 bg-white shadow-sm"
              [open]="skill.scripts.length === 1"
            >
              <summary class="cursor-pointer px-5 py-3">
                <span class="font-mono text-sm font-medium text-gray-900">{{ script.name }}</span>
                <span class="ml-2 text-sm text-gray-600">{{ script.description }}</span>
                <span class="ml-2 font-mono text-xs text-gray-400">{{
                  shortHash(script.codeHash)
                }}</span>
              </summary>
              @if (script.source) {
                <pre
                  class="overflow-x-auto border-t border-gray-100 bg-gray-50 px-5 py-4 font-mono text-xs leading-relaxed text-gray-900"
                  >{{ script.source }}</pre
                >
              } @else {
                <p class="border-t border-gray-100 px-5 py-3 text-sm text-gray-500">
                  The source is no longer in the registry.
                </p>
              }
            </details>
          } @empty {
            <p class="text-sm text-gray-600">This skill is instructions only.</p>
          }
        </div>
      </section>
    } @else if (!error()) {
      <p class="mt-6 text-sm text-gray-500">Loading…</p>
    }
  `,
})
export class Skill {
  private readonly api = inject(TheplotAdminApi);

  readonly id = input.required<string>();
  readonly version = input.required<string>();

  protected readonly shortHash = shortHash;
  protected readonly reply = signal<GetSkillReply | null>(null);
  protected readonly error = signal('');

  constructor() {
    afterNextRender(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.reply.set(
        await this.api.runs.getSkill({ id: this.id(), version: Number(this.version()) }),
      );
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

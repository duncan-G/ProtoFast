import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FieldsForm } from '../../editors/fields-form';
import { Resume } from '../../loop/model/resume';
import { ResumeGroup } from '../../loop/model/resume-group';
import { inOrder } from '../../loop/resume-text';
import { Icon } from '../../shared/icon';
import { BLANK_ENTRY, Entry, ENTRY_FIELDS, EntryKind, ResumeEntry } from './resume-entry';
import {
  addedEntry,
  ENTRY_LISTS,
  ENTRY_NOUNS,
  entryChange,
  HEAD_FIELDS,
  headChanges,
  headValue,
  SUMMARY_FIELDS,
  trimmedChanges,
} from './resume-fields';
import { ResumeGroups } from './resume-groups';

type Section = 'head' | 'summary';

/** A resume, every part edited where it's shown; each change saves the whole resume. */
@Component({
  selector: 'app-resume-editor',
  imports: [FieldsForm, Icon, ResumeEntry, ResumeGroups],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope flex flex-col gap-8' },
  template: `
    @let r = resume();
    @if (editing() === 'head') {
      <app-fields-form
        heading="about you"
        [fields]="headFields"
        [value]="headValue()"
        [save]="saveHead"
        (closed)="editing.set(null)"
      />
    } @else {
      <header class="flex items-start justify-between gap-4">
        <div class="min-w-0">
          <h2
            class="m-0 text-[clamp(26px,3vw,36px)] leading-[1.08] font-semibold tracking-[-0.04em]"
            [style.color]="r.name ? null : 'var(--color-faint)'"
          >
            {{ r.name || 'Your name' }}
          </h2>
          @if (r.headline) {
            <p class="mt-1.5 mb-0 text-[16px] text-[var(--color-text-2)]">{{ r.headline }}</p>
          }
          <div class="mt-2 flex flex-wrap items-baseline gap-x-5 gap-y-1">
            @for (part of contact(); track $index) {
              <span class="label text-[13px]">{{ part }}</span>
            }
            @for (link of r.links; track link.url) {
              <a class="label text-[13px]" [href]="link.url" target="_blank" rel="noopener">{{
                link.label
              }}</a>
            }
            @if (!contact().length && !r.links.length) {
              <button type="button" class="btn-link" (click)="editing.set('head')">
                + how to reach you
              </button>
            }
          </div>
        </div>
        <button
          type="button"
          class="btn btn-icon h-10 min-h-10 w-10 flex-none"
          aria-label="Edit name and contact details"
          (click)="editing.set('head')"
        >
          <app-icon name="edit" [size]="15" />
        </button>
      </header>
    }

    <section aria-label="Summary" class="flex flex-col gap-2">
      @if (editing() === 'summary') {
        <app-fields-form
          heading="summary"
          [fields]="summaryFields"
          [value]="r"
          [save]="saveFields"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="section-head">
          <h3 class="caption">summary</h3>
          <button type="button" class="btn-link" (click)="editing.set('summary')">
            {{ r.summary ? 'edit' : '+ summary' }}
          </button>
        </div>
        @if (r.summary) {
          <p class="m-0 max-w-[760px] text-[15.5px] leading-[1.55] text-[var(--color-text-2)]">
            {{ r.summary }}
          </p>
        }
      }
    </section>

    <div class="company-grid dim-scope">
      <div class="dim-scope flex min-w-0 flex-col gap-10">
        @for (part of mainParts; track part.kind) {
          <section [attr.aria-label]="part.label" class="dim-scope flex flex-col gap-4">
            <div class="section-head">
              <h3 class="caption">{{ part.label }}</h3>
              <button type="button" class="btn-link" (click)="adding.set(part.kind)">
                + {{ part.noun }}
              </button>
            </div>
            @for (entry of entries(part.kind); track $index) {
              <app-resume-entry
                [kind]="part.kind"
                [entry]="entry"
                [save]="saveEntry(part.kind, $index)"
              />
            } @empty {
              <p class="m-0 text-[14px] text-[var(--color-dim)]">{{ part.empty }}</p>
            }
          </section>
        }
      </div>

      <aside class="card company-card dim-scope" aria-label="Skills, education and the rest">
        <app-resume-groups
          label="skills"
          noun="skills"
          look="tags"
          placeholder="Go"
          example="languages: Go, Rust, TypeScript&#10;infrastructure: AWS, Terraform"
          empty="Languages, frameworks and tools, in groups."
          [groups]="r.skills"
          [save]="saveSkills"
        />

        <section aria-label="Education" class="card-rule dim-scope flex flex-col gap-4">
          <div class="section-head">
            <h3 class="caption">education</h3>
            <button type="button" class="btn-link" (click)="adding.set('school')">+ school</button>
          </div>
          @for (entry of entries('school'); track $index) {
            <app-resume-entry kind="school" [entry]="entry" [save]="saveEntry('school', $index)" />
          }
        </section>

        <app-resume-groups
          class="card-rule"
          label="other"
          noun="a list"
          look="rows"
          separator=";"
          placeholder="AWS Solutions Architect (2024)"
          example="certifications: AWS Solutions Architect (2024); CKA (2023)&#10;talks: Queues at scale, GopherCon (2025)"
          empty="Certifications, awards, talks, open source."
          [groups]="r.extras"
          [save]="saveExtras"
        />
      </aside>
    </div>

    @if (adding(); as kind) {
      <app-fields-form
        sheet
        [heading]="'new ' + nouns[kind]"
        [fields]="entryFields[kind]"
        [value]="blank[kind]()"
        [save]="addEntry(kind)"
        [saveLabel]="'Add ' + nouns[kind]"
        note="highlights and the rest go on it after"
        (closed)="adding.set(null)"
      />
    }
  `,
})
export class ResumeEditor {
  readonly resume = input.required<Resume>();
  readonly save = input.required<(resume: Resume) => Promise<unknown>>();

  protected readonly headFields = HEAD_FIELDS;
  protected readonly summaryFields = SUMMARY_FIELDS;
  protected readonly entryFields = ENTRY_FIELDS;
  protected readonly blank = BLANK_ENTRY;
  protected readonly nouns = ENTRY_NOUNS;
  protected readonly mainParts = [
    { kind: 'role', label: 'experience', noun: 'job', empty: 'No jobs yet.' },
    { kind: 'project', label: 'projects', noun: 'project', empty: 'No projects yet.' },
  ] as const;

  protected readonly editing = signal<Section | null>(null);
  protected readonly adding = signal<EntryKind | null>(null);

  protected readonly contact = computed(() => {
    const r = this.resume();
    return [r.location, r.email, r.phone].filter(Boolean);
  });

  protected readonly headValue = computed(() => headValue(this.resume()));

  protected entries(kind: EntryKind): readonly Entry[] {
    return this.resume()[ENTRY_LISTS[kind]];
  }

  private change(change: Partial<Resume>): Promise<unknown> {
    return this.save()(inOrder({ ...this.resume(), ...change }));
  }

  protected readonly saveFields = (changes: Record<string, unknown>) =>
    this.change(trimmedChanges(changes));

  protected readonly saveHead = (changes: Record<string, unknown>) =>
    this.change(headChanges(changes));

  protected readonly saveSkills = (skills: ResumeGroup[]) => this.change({ skills });

  protected readonly saveExtras = (extras: ResumeGroup[]) => this.change({ extras });

  protected saveEntry(kind: EntryKind, index: number): (entry: Entry | null) => Promise<unknown> {
    return (entry) => this.change(entryChange(this.resume(), kind, index, entry));
  }

  protected addEntry(kind: EntryKind): (changes: Record<string, unknown>) => Promise<unknown> {
    return (changes) => this.change(addedEntry(this.resume(), kind, changes));
  }
}

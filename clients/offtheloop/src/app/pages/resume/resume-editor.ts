import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { LoopError } from '../../loop/errors';
import { Resume } from '../../loop/model/resume';
import { ResumeGroup } from '../../loop/model/resume-group';
import { ResumeLink } from '../../loop/model/resume-link';
import { inOrder } from '../../loop/resume-text';
import { Icon } from '../../shared/icon';
import {
  BLANK_ENTRY,
  Entry,
  ENTRY_FIELDS,
  entryChanges,
  EntryKind,
  ResumeEntry,
} from './resume-entry';
import { ResumeGroups } from './resume-groups';

type Section = 'head' | 'summary';

type ListKey = 'experience' | 'projects' | 'education';

const LISTS: Record<EntryKind, ListKey> = {
  role: 'experience',
  project: 'projects',
  school: 'education',
};

const HEAD: readonly FieldSpec[] = [
  { key: 'name', label: 'name', placeholder: 'Ada Park', look: 'lead', wide: true },
  {
    key: 'headline',
    label: 'headline',
    hint: 'one line under your name',
    placeholder: 'Backend engineer · payments',
    wide: true,
  },
  { key: 'email', label: 'email', placeholder: 'ada@example.com' },
  { key: 'phone', label: 'phone', placeholder: '+1 555 0100' },
  { key: 'location', label: 'based in', placeholder: 'Austin, TX' },
  {
    key: 'links',
    label: 'links',
    hint: 'one per line: label then address',
    type: 'textarea',
    look: 'mono',
    placeholder: 'GitHub https://github.com/ada\nportfolio https://ada.dev',
  },
];

const SUMMARY: readonly FieldSpec[] = [
  {
    key: 'summary',
    label: 'summary',
    hint: 'who you are as an engineer',
    type: 'textarea',
    placeholder: 'Backend engineer who…',
  },
];

/** "GitHub https://github.com/ada" per line; a bare address takes its site as the label. */
function readLinks(text: string): ResumeLink[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const url = line.split(/\s+/).find((part) => /^https?:\/\/\S+$/i.test(part));
      if (!url) {
        throw new LoopError(`“${line}” has no web address starting https://.`);
      }
      const label = line
        .replace(url, '')
        .replace(/[\s:·—-]+$/, '')
        .trim();
      return { label: label || new URL(url).hostname.replace(/^www\./, ''), url };
    });
}

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

  protected readonly headFields = HEAD;
  protected readonly summaryFields = SUMMARY;
  protected readonly entryFields = ENTRY_FIELDS;
  protected readonly blank = BLANK_ENTRY;
  protected readonly nouns: Record<EntryKind, string> = {
    role: 'job',
    project: 'project',
    school: 'school',
  };
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

  protected readonly headValue = computed(() => {
    const r = this.resume();
    return { ...r, links: r.links.map((l) => `${l.label} ${l.url}`).join('\n') };
  });

  protected entries(kind: EntryKind): readonly Entry[] {
    return this.resume()[LISTS[kind]];
  }

  private change(change: Partial<Resume>): Promise<unknown> {
    return this.save()(inOrder({ ...this.resume(), ...change }));
  }

  protected readonly saveFields = (changes: Record<string, unknown>) =>
    this.change(
      Object.fromEntries(Object.entries(changes).map(([k, v]) => [k, String(v ?? '').trim()])),
    );

  protected readonly saveHead = (changes: Record<string, unknown>) => {
    const { links, ...fields } = changes;
    return this.change({
      ...Object.fromEntries(Object.entries(fields).map(([k, v]) => [k, String(v ?? '').trim()])),
      links: readLinks(String(links ?? '')),
    });
  };

  protected readonly saveSkills = (skills: ResumeGroup[]) => this.change({ skills });

  protected readonly saveExtras = (extras: ResumeGroup[]) => this.change({ extras });

  protected saveEntry(kind: EntryKind, index: number): (entry: Entry | null) => Promise<unknown> {
    const key = LISTS[kind];
    return (entry) => {
      const list = [...this.resume()[key]] as Entry[];
      if (entry) {
        list[index] = entry;
      } else {
        list.splice(index, 1);
      }
      return this.change({ [key]: list } as Partial<Resume>);
    };
  }

  protected addEntry(kind: EntryKind): (changes: Record<string, unknown>) => Promise<unknown> {
    const key = LISTS[kind];
    return (changes) => {
      const entry = entryChanges(BLANK_ENTRY[kind](), changes);
      return this.change({ [key]: [...this.resume()[key], entry] } as Partial<Resume>);
    };
  }
}

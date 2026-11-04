import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListField } from '../../editors/list-field';
import { LoopError, messageOf } from '../../loop/errors';
import { plural } from '../../loop/loop-summary';
import { ResumeEvidence } from '../../loop/model/resume-evidence';
import { ResumeProject } from '../../loop/model/resume-project';
import { ResumeRole } from '../../loop/model/resume-role';
import { ResumeSchool } from '../../loop/model/resume-school';
import { rangeLabel, readMonth } from '../../loop/resume-text';
import { Icon } from '../../shared/icon';

export type EntryKind = 'role' | 'project' | 'school';

export type Entry = ResumeRole | ResumeProject | ResumeSchool;

const START: FieldSpec = { key: 'start', label: 'from', hint: 'month', placeholder: '2021-03' };

export const ENTRY_FIELDS: Record<EntryKind, readonly FieldSpec[]> = {
  role: [
    {
      key: 'title',
      label: 'title',
      placeholder: 'Senior Software Engineer',
      look: 'lead',
      wide: true,
    },
    { key: 'company', label: 'company', placeholder: 'Halcyon Pay', required: true },
    { key: 'location', label: 'where', placeholder: 'Remote' },
    START,
    { key: 'end', label: 'to', hint: 'blank while current', placeholder: '2024-06' },
    {
      key: 'summary',
      label: 'the team',
      hint: 'one line',
      type: 'textarea',
      placeholder: 'Payments Core: the ledger and payouts.',
    },
  ],
  project: [
    {
      key: 'name',
      label: 'project',
      placeholder: 'tinyurl-rs',
      look: 'lead',
      required: true,
      wide: true,
    },
    {
      key: 'link',
      label: 'link',
      type: 'url',
      placeholder: 'https://github.com/you/tinyurl-rs',
      wide: true,
    },
    START,
    { key: 'end', label: 'to', hint: 'blank while ongoing', placeholder: '2024-06' },
    {
      key: 'summary',
      label: 'what it is',
      hint: 'one line',
      type: 'textarea',
      placeholder: 'A URL shortener in Rust.',
    },
  ],
  school: [
    {
      key: 'school',
      label: 'school',
      placeholder: 'State University',
      look: 'lead',
      required: true,
      wide: true,
    },
    { key: 'degree', label: 'degree', placeholder: 'BSc Computer Science', wide: true },
    { ...START, placeholder: '2014' },
    { key: 'end', label: 'to', hint: 'month', placeholder: '2018' },
  ],
};

export const BLANK_ENTRY: Record<EntryKind, () => Entry> = {
  role: () => ({
    company: '',
    title: '',
    location: '',
    start: '',
    end: '',
    summary: '',
    highlights: [],
    stack: [],
    evidence: [],
  }),
  project: () => ({
    name: '',
    link: '',
    start: '',
    end: '',
    summary: '',
    highlights: [],
    stack: [],
    evidence: [],
  }),
  school: () => ({ school: '', degree: '', start: '', end: '', details: [] }),
};

const NOUNS: Record<EntryKind, string> = { role: 'job', project: 'project', school: 'school' };

/** A form's fields onto the entry, months read as typed. */
export function entryChanges(entry: Entry, changes: Record<string, unknown>): Entry {
  const next: Record<string, unknown> = { ...entry };
  for (const [key, value] of Object.entries(changes)) {
    const text = String(value ?? '').trim();
    if (key === 'start' || key === 'end') {
      const month = readMonth(text);
      if (month === null) {
        throw new LoopError(`“${text}” isn’t a month. Write it like 2021-03, or Mar 2021.`);
      }
      next[key] = month;
    } else {
      next[key] = text;
    }
  }
  return next as unknown as Entry;
}

interface EntryList {
  key: 'highlights' | 'stack' | 'details';
  label: string;
  placeholder: string;
  look: 'rows' | 'tags';
}

/** One job, project or school: its lists edited in place, the rest in a panel. */
@Component({
  selector: 'app-resume-entry',
  imports: [FieldsForm, Icon, ListField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'resume-entry dim-scope' },
  template: `
    @let view = shown();
    @if (editing()) {
      <app-fields-form
        [heading]="noun()"
        [fields]="fields()"
        [value]="entry()"
        [save]="saveFields"
        (closed)="editing.set(false)"
      />
    } @else {
      <div class="entry-head">
        <div class="min-w-0">
          <h4 class="entry-title">
            @if (view.link) {
              <a [href]="view.link" target="_blank" rel="noopener">{{ view.title }}</a>
            } @else {
              {{ view.title }}
            }
            @if (view.subtitle) {
              <span class="entry-at">· {{ view.subtitle }}</span>
            }
          </h4>
          @if (view.meta) {
            <p class="meta m-0">{{ view.meta }}</p>
          }
        </div>
        <button
          type="button"
          class="btn btn-icon h-9 min-h-9 w-9 flex-none"
          [attr.aria-label]="'Edit ' + view.title"
          (click)="editing.set(true)"
        >
          <app-icon name="edit" [size]="14" />
        </button>
      </div>
      @if (view.summary) {
        <p class="entry-summary">{{ view.summary }}</p>
      }
    }

    @for (list of lists(); track list.key) {
      <app-list-field
        [label]="list.label"
        [items]="listItems(list.key)"
        [placeholder]="list.placeholder"
        [look]="list.look"
        [save]="saveList(list.key)"
      />
    }

    @if (evidence().length) {
      <div class="entry-evidence">
        <button
          type="button"
          class="btn-link"
          [attr.aria-expanded]="showEvidence()"
          (click)="showEvidence.set(!showEvidence())"
        >
          from git · {{ evidenceLine() }}
        </button>
        @if (showEvidence()) {
          <ul class="evidence-list">
            @for (commit of evidence(); track commit.commit) {
              <li>
                <span class="mono">{{ commit.commit.slice(0, 7) }}</span>
                <span class="meta"
                  >{{ commit.repo }}{{ commit.date ? ' · ' + commit.date : '' }}</span
                >
                @if (commit.what) {
                  <span>{{ commit.what }}</span>
                }
              </li>
            }
          </ul>
        }
      </div>
    }

    @if (!editing()) {
      @if (confirmingDelete()) {
        <div class="flex flex-wrap items-center gap-2.5">
          <span class="text-[13px] text-[var(--color-muted)]">Take {{ shown().title }} off?</span>
          <button
            type="button"
            class="btn btn-sm btn-danger"
            [disabled]="busy()"
            (click)="remove()"
          >
            Remove
          </button>
          <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">keep</button>
        </div>
      } @else {
        <button
          type="button"
          class="btn-link entry-remove self-start"
          (click)="confirmingDelete.set(true)"
        >
          remove {{ noun() }}
        </button>
      }
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
    }
  `,
})
export class ResumeEntry {
  readonly kind = input.required<EntryKind>();
  readonly entry = input.required<Entry>();
  /** Saves the entry changed, or takes it off when given null. */
  readonly save = input.required<(entry: Entry | null) => Promise<unknown>>();

  protected readonly editing = signal(false);
  protected readonly showEvidence = signal(false);
  protected readonly confirmingDelete = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly noun = computed(() => NOUNS[this.kind()]);
  protected readonly fields = computed(() => ENTRY_FIELDS[this.kind()]);

  protected readonly shown = computed(() => {
    const entry = this.entry();
    switch (this.kind()) {
      case 'role': {
        const role = entry as ResumeRole;
        return {
          title: role.title || role.company,
          subtitle: role.title ? role.company : '',
          link: '',
          meta: [rangeLabel(role.start, role.end), role.location].filter(Boolean).join(' · '),
          summary: role.summary,
        };
      }
      case 'project': {
        const project = entry as ResumeProject;
        return {
          title: project.name,
          subtitle: '',
          link: project.link,
          meta: rangeLabel(project.start, project.end),
          summary: project.summary,
        };
      }
      default: {
        const school = entry as ResumeSchool;
        return {
          title: school.degree || school.school,
          subtitle: school.degree ? school.school : '',
          link: '',
          meta: rangeLabel(school.start, school.end, null),
          summary: '',
        };
      }
    }
  });

  protected readonly lists = computed((): EntryList[] =>
    this.kind() === 'school'
      ? [{ key: 'details', label: 'details', placeholder: 'First-class honours', look: 'rows' }]
      : [
          {
            key: 'highlights',
            label: 'highlights',
            placeholder: 'Moved billing onto a queue, cutting runs from 5 h to 20 min',
            look: 'rows',
          },
          { key: 'stack', label: 'stack', placeholder: 'Go', look: 'tags' },
        ],
  );

  protected readonly evidence = computed(
    (): ResumeEvidence[] => (this.entry() as Partial<ResumeRole>).evidence ?? [],
  );

  protected readonly evidenceLine = computed(() => {
    const commits = this.evidence();
    const repos = [...new Set(commits.map((c) => c.repo).filter(Boolean))];
    return [plural(commits.length, 'commit'), repos.join(', ')].filter(Boolean).join(' · ');
  });

  protected listItems(key: EntryList['key']): string[] {
    return (this.entry() as unknown as Record<string, string[]>)[key] ?? [];
  }

  protected saveList(key: EntryList['key']): (items: string[]) => Promise<unknown> {
    return (items) => this.save()({ ...this.entry(), [key]: items } as Entry);
  }

  protected readonly saveFields = (changes: Record<string, unknown>) =>
    this.save()(entryChanges(this.entry(), changes));

  protected async remove(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.save()(null);
    } catch (err) {
      this.error.set(messageOf(err, `The ${this.noun()} couldn’t be removed.`));
      this.busy.set(false);
    }
  }
}

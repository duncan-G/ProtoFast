import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  signal,
} from '@angular/core';
import { FieldsForm } from '../../editors/fields-form';
import { plural } from '../../loop/loop-summary';
import { Resume } from '../../loop/model/resume';
import { ResumeGroup } from '../../loop/model/resume-group';
import {
  entryMeta,
  findKey,
  hasText,
  linkLabel,
  readEntries,
  readGroups,
  ReadingEntry,
  trailingLink,
} from '../../loop/resume-reading';
import { inOrder, rangeLabel } from '../../loop/resume-text';
import { Icon } from '../../shared/icon';
import { MarkedText } from './marked-text';
import { BLANK_ENTRY, Entry, ENTRY_FIELDS, EntryKind, ResumeEntry } from './resume-entry';
import {
  addedEntry,
  ENTRY_NOUNS,
  entryChange,
  HEAD_FIELDS,
  headChanges,
  headValue,
  SUMMARY_FIELDS,
  trimmedChanges,
} from './resume-fields';
import { ResumeGroups } from './resume-groups';

const PARTS = [
  { kind: 'role', id: 'experience', title: 'Experience', empty: 'No jobs yet.' },
  { kind: 'project', id: 'projects', title: 'Projects', empty: 'No projects yet.' },
] as const;

/**
 * The general resume laid out to read, all of it: an outline with a find beside it, jobs open and
 * projects folded. Each part opens its editor in place from its own edit button.
 */
@Component({
  selector: 'app-resume-reader',
  imports: [FieldsForm, Icon, MarkedText, ResumeEntry, ResumeGroups],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let r = resume();
    @let find = key();
    <div class="resume-page">
      <aside class="resume-outline" aria-label="Resume outline">
        <ng-content select="[readerTop]" />

        <div class="outline-find">
          <label class="caption" for="resume-find">find in resume</label>
          <div class="find-field">
            <app-icon name="search" [size]="16" />
            <input
              id="resume-find"
              type="search"
              autocomplete="off"
              placeholder="Postgres, billing, on-call…"
              [value]="query()"
              (input)="query.set($any($event.target).value)"
            />
          </div>
          <div class="find-result">
            <span role="status">{{ result() }}</span>
            @if (find) {
              <button type="button" class="read-edit" (click)="query.set('')">clear</button>
            }
          </div>
        </div>

        <nav class="outline-nav" aria-label="Resume sections">
          <a class="outline-head" href="/app/resume#profile" (click)="jump($event, 'profile')"
            >profile and summary</a
          >
          @for (part of parts(); track part.kind) {
            <div class="outline-group">
              <a
                class="outline-head"
                [href]="'/app/resume#' + part.id"
                (click)="jump($event, part.id)"
              >
                <span>{{ part.id }}</span>
                <span class="outline-count">{{ part.entries.length }}</span>
              </a>
              @for (entry of part.entries; track entry.key) {
                <a
                  class="outline-entry"
                  [class.is-out]="find && !entry.shown"
                  [href]="'/app/resume#' + entry.key"
                  (click)="jump($event, entry.key)"
                >
                  <span class="outline-label">{{ entry.label }}</span>
                  <span class="outline-years">{{ entry.years }}</span>
                  <span class="outline-count" [class.is-hit]="find && entry.hits">{{
                    find ? entry.hits : entry.total
                  }}</span>
                </a>
              }
            </div>
          }
          <a class="outline-head" href="/app/resume#skills" (click)="jump($event, 'skills')">
            <span>skills</span>
            <span class="outline-count">{{ skillCount() }}</span>
          </a>
          <a class="outline-head" href="/app/resume#education" (click)="jump($event, 'education')">
            <span>education and other</span>
            <span class="outline-count">{{ r.education.length + r.extras.length || '' }}</span>
          </a>
        </nav>

        <ng-content />
      </aside>

      <div class="resume-read dim-scope">
        <ng-content select="[readerStatus]" />

        <header id="profile" class="reading-profile" tabindex="-1">
          @if (editing() === 'head') {
            <app-fields-form
              heading="about you"
              [fields]="headFields"
              [value]="headForm()"
              [save]="saveHead"
              (closed)="editing.set(null)"
            />
          } @else {
            <div class="reading-head">
              <h1 class="reading-name" [class.is-blank]="!r.name">{{ r.name || 'Your name' }}</h1>
              <button
                type="button"
                class="read-edit"
                aria-label="Edit name and contact details"
                (click)="editing.set('head')"
              >
                <app-icon name="edit" [size]="14" />edit
              </button>
            </div>
            @if (r.headline) {
              <p class="reading-headline">{{ r.headline }}</p>
            }
            <div class="reading-contact">
              @if (r.location) {
                <span>{{ r.location }}</span>
              }
              @if (r.email) {
                <a [href]="'mailto:' + r.email">{{ r.email }}</a>
              }
              @if (r.phone) {
                <a [href]="'tel:' + dialable(r.phone)">{{ r.phone }}</a>
              }
              @for (link of r.links; track link.url) {
                <a class="is-link" [href]="link.url" target="_blank" rel="noopener">{{
                  link.label
                }}</a>
              }
              @if (!r.location && !r.email && !r.phone && !r.links.length) {
                <button type="button" class="read-edit" (click)="editing.set('head')">
                  <app-icon name="plus" [size]="14" />how to reach you
                </button>
              }
            </div>
            @if (stats().length) {
              <dl class="reading-stats">
                @for (stat of stats(); track stat.label) {
                  <div>
                    <dt>{{ stat.label }}</dt>
                    <dd>{{ stat.value }}</dd>
                  </div>
                }
              </dl>
            }
          }
        </header>

        @if (!find || hasText(r.summary, find)) {
          <section class="reading-section dim-scope" aria-labelledby="read-summary">
            <div class="reading-section-head">
              <h2 id="read-summary">Summary</h2>
              <span class="reading-rule" aria-hidden="true"></span>
              @if (editing() !== 'summary') {
                <button type="button" class="read-edit" (click)="editing.set('summary')">
                  @if (r.summary) {
                    <app-icon name="edit" [size]="14" />edit
                  } @else {
                    <app-icon name="plus" [size]="14" />summary
                  }
                </button>
              }
            </div>
            @if (editing() === 'summary') {
              <app-fields-form
                heading="summary"
                [fields]="summaryFields"
                [value]="r"
                [save]="saveText"
                (closed)="editing.set(null)"
              />
            } @else if (r.summary) {
              <p class="reading-summary"><app-marked [text]="r.summary" [find]="find" /></p>
            }
          </section>
        }

        @for (part of parts(); track part.kind) {
          <section
            class="reading-section dim-scope"
            tabindex="-1"
            [id]="part.id"
            [attr.aria-labelledby]="'read-' + part.id"
          >
            <div class="reading-section-head">
              <h2 [id]="'read-' + part.id">{{ part.title }}</h2>
              @if (part.entries.length) {
                <span class="reading-count">{{ part.count }}</span>
              }
              <span class="reading-rule" aria-hidden="true"></span>
              <button type="button" class="read-edit" (click)="adding.set(part.kind)">
                <app-icon name="plus" [size]="14" />{{ nouns[part.kind] }}
              </button>
            </div>
            @for (entry of part.shown; track entry.key) {
              @if (editing() === entry.key) {
                <div class="reading-editing dim-scope" [id]="entry.key">
                  <div class="reading-editing-bar">
                    <span class="caption">editing {{ nouns[part.kind] }}</span>
                    <button type="button" class="btn btn-sm" (click)="editing.set(null)">Done</button>
                  </div>
                  <app-resume-entry
                    [kind]="part.kind"
                    [entry]="entry.source"
                    [save]="saveEntry(part.kind, entry.index)"
                  />
                </div>
              } @else {
                <article
                  class="reading-entry"
                  tabindex="-1"
                  [id]="entry.key"
                  [attr.aria-labelledby]="entry.key + '-title'"
                >
                  <p class="reading-when">
                    <span>{{ entry.dates }}</span>
                    @if (entry.span) {
                      <span class="meta">{{ entry.span }}</span>
                    }
                  </p>
                  <div class="reading-body">
                    <div class="reading-entry-head">
                      <div class="reading-entry-name">
                        <h3 class="reading-entry-title" [id]="entry.key + '-title'">
                          <app-marked [text]="entry.title" [find]="find" />
                          @if (entry.at) {
                            <span class="entry-at">
                              · <app-marked [text]="entry.at" [find]="find" />
                            </span>
                          }
                        </h3>
                        @if (entry.link) {
                          <a class="reading-link" [href]="entry.link" target="_blank" rel="noopener">{{
                            linkLabel(entry.link)
                          }}</a>
                        }
                      </div>
                      <button
                        type="button"
                        class="read-edit"
                        [attr.aria-label]="'Edit ' + entry.label"
                        (click)="editing.set(entry.key)"
                      >
                        <app-icon name="edit" [size]="14" />edit
                      </button>
                    </div>
                    @if (entry.summary) {
                      <p class="reading-entry-summary">
                        <app-marked [text]="entry.summary" [find]="find" />
                      </p>
                    }
                    @if (entry.total || entry.stack.length || entry.evidence.length) {
                      <div class="reading-meta-row">
                        <span class="meta">{{ meta(entry) }}</span>
                        @if (!find) {
                          <button
                            type="button"
                            class="read-toggle"
                            [attr.aria-expanded]="isOpen(entry)"
                            [attr.aria-controls]="entry.key + '-more'"
                            (click)="toggle(entry)"
                          >
                            <app-icon name="down" [size]="14" [class.is-open]="isOpen(entry)" />{{
                              isOpen(entry) ? 'hide' : 'show all'
                            }}
                          </button>
                        }
                      </div>
                      @if (isOpen(entry)) {
                        <div class="reading-more" [id]="entry.key + '-more'">
                          @if (entry.highlights.length) {
                            <ol class="reading-highlights" aria-label="Highlights">
                              @for (highlight of entry.highlights; track highlight.n) {
                                <li>
                                  <span class="reading-n" aria-hidden="true">{{ highlight.n }}</span>
                                  <span><app-marked [text]="highlight.text" [find]="find" /></span>
                                </li>
                              }
                            </ol>
                          }
                          @if (entry.stack.length) {
                            <div class="reading-stack">
                              <span class="caption">stack</span>
                              <p class="reading-inline">
                                @for (item of entry.stack; track $index) {
                                  <app-marked [text]="item" [find]="find" />
                                  @if (!$last) {
                                    <span class="reading-sep" aria-hidden="true"> · </span>
                                  }
                                }
                              </p>
                            </div>
                          }
                          @if (entry.evidence.length) {
                            <details class="reading-commits">
                              <summary>
                                <span
                                  >{{ plural(entry.evidence.length, 'commit') }} from git{{
                                    entry.repos ? ' · ' + entry.repos : ''
                                  }}</span
                                >
                                <app-icon name="down" [size]="14" />
                              </summary>
                              <ol aria-label="Commits">
                                @for (commit of entry.evidence; track $index) {
                                  <li>
                                    <span class="meta">{{ commit.date }}</span>
                                    <span class="meta">{{ commit.commit.slice(0, 7) }}</span>
                                    <span><app-marked [text]="commit.what" [find]="find" /></span>
                                  </li>
                                }
                              </ol>
                            </details>
                          }
                        </div>
                      }
                    }
                  </div>
                </article>
              }
            } @empty {
              <p class="reading-empty">{{ find ? 'Nothing here matches.' : part.empty }}</p>
            }
          </section>
        }

        @if (!find || skillsShown()) {
          <section
            id="skills"
            class="reading-section dim-scope"
            tabindex="-1"
            aria-labelledby="read-skills"
          >
            <div class="reading-section-head">
              <h2 id="read-skills">Skills</h2>
              @if (r.skills.length) {
                <span class="reading-count">{{ skillCount() }}</span>
              }
              <span class="reading-rule" aria-hidden="true"></span>
              @if (editing() !== 'skills') {
                <button type="button" class="read-edit" (click)="editing.set('skills')">
                  @if (r.skills.length) {
                    <app-icon name="edit" [size]="14" />edit
                  } @else {
                    <app-icon name="plus" [size]="14" />skills
                  }
                </button>
              }
            </div>
            @if (editing() === 'skills') {
              <div class="reading-editing dim-scope">
                <div class="reading-editing-bar">
                  <span class="caption">editing skills</span>
                  <button type="button" class="btn btn-sm" (click)="editing.set(null)">Done</button>
                </div>
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
              </div>
            } @else {
              @for (group of skills(); track $index) {
                @if (group.shown) {
                  <div class="reading-row">
                    <p class="reading-when">
                      <span class="reading-group">{{ group.name }}</span>
                      <span class="meta">{{ group.items.length }}</span>
                    </p>
                    <p class="reading-inline reading-skills">
                      @for (item of group.items; track $index) {
                        <app-marked [text]="item" [find]="find" />
                        @if (!$last) {
                          <span class="reading-sep" aria-hidden="true"> · </span>
                        }
                      }
                    </p>
                  </div>
                }
              } @empty {
                <p class="reading-empty">Languages, frameworks and tools, in groups.</p>
              }
            }
          </section>
        }

        @if (!find || schoolsShown()) {
          <section
            id="education"
            class="reading-section dim-scope"
            tabindex="-1"
            aria-labelledby="read-education"
          >
            <div class="reading-section-head">
              <h2 id="read-education">Education</h2>
              <span class="reading-rule" aria-hidden="true"></span>
              <button type="button" class="read-edit" (click)="adding.set('school')">
                <app-icon name="plus" [size]="14" />school
              </button>
            </div>
            @for (school of schools(); track school.key) {
              @if (school.shown) {
                @if (editing() === school.key) {
                  <div class="reading-editing dim-scope">
                    <div class="reading-editing-bar">
                      <span class="caption">editing school</span>
                      <button type="button" class="btn btn-sm" (click)="editing.set(null)">
                        Done
                      </button>
                    </div>
                    <app-resume-entry
                      kind="school"
                      [entry]="school.source"
                      [save]="saveEntry('school', school.index)"
                    />
                  </div>
                } @else {
                  <article class="reading-entry" [attr.aria-labelledby]="school.key + '-title'">
                    <p class="reading-when"><span>{{ school.dates }}</span></p>
                    <div class="reading-body">
                      <div class="reading-entry-head">
                        <div class="reading-entry-name">
                          <h3 class="reading-entry-title" [id]="school.key + '-title'">
                            <app-marked [text]="school.title" [find]="find" />
                            @if (school.at) {
                              <span class="entry-at">
                                · <app-marked [text]="school.at" [find]="find" />
                              </span>
                            }
                          </h3>
                        </div>
                        <button
                          type="button"
                          class="read-edit"
                          [attr.aria-label]="'Edit ' + school.title"
                          (click)="editing.set(school.key)"
                        >
                          <app-icon name="edit" [size]="14" />edit
                        </button>
                      </div>
                      @if (school.details.length) {
                        <ul class="reading-details">
                          @for (detail of school.details; track $index) {
                            <li><app-marked [text]="detail" [find]="find" /></li>
                          }
                        </ul>
                      }
                    </div>
                  </article>
                }
              }
            } @empty {
              <p class="reading-empty">No schools yet.</p>
            }
          </section>
        }

        @if (!find || othersShown()) {
          <section class="reading-section dim-scope" aria-labelledby="read-other">
            <div class="reading-section-head">
              <h2 id="read-other">Other</h2>
              <span class="reading-rule" aria-hidden="true"></span>
              @if (editing() !== 'other') {
                <button type="button" class="read-edit" (click)="editing.set('other')">
                  @if (r.extras.length) {
                    <app-icon name="edit" [size]="14" />edit
                  } @else {
                    <app-icon name="plus" [size]="14" />a list
                  }
                </button>
              }
            </div>
            @if (editing() === 'other') {
              <div class="reading-editing dim-scope">
                <div class="reading-editing-bar">
                  <span class="caption">editing other lists</span>
                  <button type="button" class="btn btn-sm" (click)="editing.set(null)">Done</button>
                </div>
                <app-resume-groups
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
              </div>
            } @else {
              @for (group of others(); track $index) {
                @if (group.shown) {
                  <div class="reading-row">
                    <p class="reading-when">
                      <span class="reading-group">{{ group.name }}</span>
                      <span class="meta">{{ group.lines.length }}</span>
                    </p>
                    <ul class="reading-details">
                      @for (line of group.lines; track $index) {
                        <li>
                          <app-marked [text]="line.text" [find]="find" />
                          @if (line.url) {
                            <a class="reading-link" [href]="line.url" target="_blank" rel="noopener">{{
                              linkLabel(line.url)
                            }}</a>
                          }
                        </li>
                      }
                    </ul>
                  </div>
                }
              } @empty {
                <p class="reading-empty">Certifications, awards, talks, open source.</p>
              }
            }
          </section>
        }
      </div>
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
export class ResumeReader {
  readonly resume = input.required<Resume>();
  readonly save = input.required<(resume: Resume) => Promise<unknown>>();
  readonly stories = input(0);

  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly today = new Date();

  protected readonly headFields = HEAD_FIELDS;
  protected readonly summaryFields = SUMMARY_FIELDS;
  protected readonly entryFields = ENTRY_FIELDS;
  protected readonly blank = BLANK_ENTRY;
  protected readonly nouns = ENTRY_NOUNS;
  protected readonly hasText = hasText;
  protected readonly linkLabel = linkLabel;
  protected readonly plural = plural;

  protected readonly query = signal('');
  protected readonly key = computed(() => findKey(this.query()));
  /** An entry's key: what's being edited, or 'head', 'summary', 'skills', 'other'. */
  protected readonly editing = signal<string | null>(null);
  protected readonly adding = signal<EntryKind | null>(null);
  /** Entries opened or folded by hand; the rest follow their kind. */
  private readonly folds = signal<Record<string, boolean>>({});

  protected readonly headForm = computed(() => headValue(this.resume()));

  protected readonly parts = computed(() => {
    const r = this.resume();
    const key = this.key();
    return PARTS.map((part) => {
      const entries = readEntries(
        part.kind,
        part.kind === 'role' ? r.experience : r.projects,
        key,
        this.today,
      );
      const shown = entries.filter((e) => e.shown);
      return {
        ...part,
        entries,
        shown,
        count: key ? `${shown.length} of ${entries.length}` : String(entries.length),
      };
    });
  });

  protected readonly skills = computed(() => readGroups(this.resume().skills, this.key()));

  protected readonly skillsShown = computed(() => this.skills().some((g) => g.shown));

  protected readonly skillCount = computed(() => {
    const groups = this.resume().skills;
    const items = groups.reduce((n, g) => n + g.items.length, 0);
    return groups.length ? `${plural(groups.length, 'group')} · ${items}` : '';
  });

  protected readonly others = computed(() =>
    readGroups(this.resume().extras, this.key()).map((group) => ({
      ...group,
      lines: group.items.map(trailingLink),
    })),
  );

  protected readonly othersShown = computed(() => this.others().some((g) => g.shown));

  protected readonly schools = computed(() => {
    const key = this.key();
    return this.resume().education.map((school, index) => ({
      key: `school-${index}`,
      index,
      source: school,
      title: school.degree || school.school,
      at: school.degree ? school.school : '',
      dates: rangeLabel(school.start, school.end, null),
      details: school.details,
      shown:
        !key || [school.school, school.degree, ...school.details].some((t) => hasText(t, key)),
    }));
  });

  protected readonly schoolsShown = computed(() => this.schools().some((s) => s.shown));

  protected readonly stats = computed(() => {
    const r = this.resume();
    const work = [...r.experience, ...r.projects];
    const counts: [number, string, string][] = [
      [r.experience.length, 'job', 'jobs'],
      [r.projects.length, 'project', 'projects'],
      [work.reduce((n, w) => n + w.highlights.length, 0), 'highlight', 'highlights'],
      [r.skills.reduce((n, g) => n + g.items.length, 0), 'skill', 'skills'],
      [work.reduce((n, w) => n + w.evidence.length, 0), 'commit from git', 'commits from git'],
      [this.stories(), 'story', 'stories'],
    ];
    return counts
      .filter(([value]) => value > 0)
      .map(([value, one, many]) => ({ value, label: value === 1 ? one : many }));
  });

  protected readonly result = computed(() => {
    const key = this.key();
    if (!key) {
      return '';
    }
    const entries = this.parts().flatMap((part) => part.shown);
    const highlights = entries.reduce((n, e) => n + e.highlights.length, 0);
    const skills = this.skills().reduce((n, g) => n + g.hits, 0);
    if (!entries.length && !skills && !this.schoolsShown() && !this.othersShown()) {
      return `Nothing matches “${this.query().trim()}”`;
    }
    return [
      entries.length
        ? `${plural(highlights, 'highlight')} in ${plural(entries.length, 'entry', 'entries')}`
        : '',
      skills ? plural(skills, 'skill') : '',
    ]
      .filter(Boolean)
      .join(' · ');
  });

  protected meta(entry: ReadingEntry): string {
    return entryMeta(entry, this.key());
  }

  protected isOpen(entry: ReadingEntry): boolean {
    return !!this.key() || (this.folds()[entry.key] ?? entry.kind === 'role');
  }

  protected toggle(entry: ReadingEntry): void {
    const open = this.isOpen(entry);
    this.folds.update((folds) => ({ ...folds, [entry.key]: !open }));
  }

  protected dialable(phone: string): string {
    return phone.replace(/[^\d+]/g, '');
  }

  /** In-page links: the app's base href would otherwise take #id to the site's root. */
  protected jump(event: Event, id: string): void {
    event.preventDefault();
    if (this.document.getElementById(id)) {
      this.scrollTo(id);
    } else if (this.query()) {
      this.query.set('');
      afterNextRender(() => this.scrollTo(id), { injector: this.injector });
    }
  }

  private scrollTo(id: string): void {
    const target = this.document.getElementById(id);
    if (!target) {
      return;
    }
    const still = this.document.defaultView?.matchMedia('(prefers-reduced-motion: reduce)').matches;
    target.scrollIntoView({ behavior: still ? 'auto' : 'smooth', block: 'start' });
    target.focus({ preventScroll: true });
  }

  private change(change: Partial<Resume>): Promise<unknown> {
    return this.save()(inOrder({ ...this.resume(), ...change }));
  }

  protected readonly saveText = (changes: Record<string, unknown>) =>
    this.change(trimmedChanges(changes));

  protected readonly saveHead = (changes: Record<string, unknown>) =>
    this.change(headChanges(changes));

  protected readonly saveSkills = (skills: ResumeGroup[]) => this.change({ skills });

  protected readonly saveExtras = (extras: ResumeGroup[]) => this.change({ extras });

  protected saveEntry(kind: EntryKind, index: number): (entry: Entry | null) => Promise<unknown> {
    return async (entry) => {
      await this.change(entryChange(this.resume(), kind, index, entry));
      if (!entry) {
        this.editing.set(null);
      }
    };
  }

  protected addEntry(kind: EntryKind): (changes: Record<string, unknown>) => Promise<unknown> {
    return (changes) => this.change(addedEntry(this.resume(), kind, changes));
  }
}

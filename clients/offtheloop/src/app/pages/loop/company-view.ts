import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListField, ListSpec } from '../../editors/list-field';
import { shortDate, today } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { changesFrom, Finding } from '../../loop/findings';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { LoopTray } from '../../loop/loop-tray';
import { plural, stageLabel } from '../../loop/loop-summary';
import { Application } from '../../loop/model/application';
import { Company } from '../../loop/model/company';
import { isActive } from '../../loop/model/loop-run';
import { STAGES } from '../../loop/model/stage';
import { statusOf } from '../../loop/model/status';
import { Figure } from '../../shared/figure';
import { Icon } from '../../shared/icon';
import { StageDot } from '../../shared/stage-dot';
import { ApplicationView, SOURCE_FIELDS } from './application-view';
import { CompanyNotes } from './company-notes';
import { People } from './people';
import { PostingReader } from './posting-reader';

type Section = 'head' | 'business' | 'numbers';

export const COMPANY_FIELDS: readonly FieldSpec[] = [
  {
    key: 'name',
    label: 'company',
    placeholder: 'Halcyon Pay',
    required: true,
    look: 'lead',
    wide: true,
  },
  {
    key: 'about',
    label: 'about',
    hint: 'industry, product, how they work',
    placeholder: 'fintech · payments api · remote (us)',
    wide: true,
  },
];

const HEAD: readonly FieldSpec[] = [
  ...COMPANY_FIELDS,
  { key: 'location', label: 'based in', placeholder: 'Burbank, CA' },
];

const BUSINESS: readonly FieldSpec[] = [
  {
    key: 'whatItDoes',
    label: 'what it does',
    hint: 'in plain words',
    type: 'textarea',
    placeholder: 'Runs card payments for online shops and pays them out the next day.',
  },
  {
    key: 'howItMakesMoney',
    label: 'how it makes money',
    hint: 'who pays, for what',
    type: 'textarea',
    placeholder: 'Takes 2.9% + 30¢ of each payment; big shops pay a monthly fee instead.',
  },
];

const NUMBERS: readonly FieldSpec[] = [
  { key: 'revenue', label: 'revenue', placeholder: '$140M', look: 'figure' },
  { key: 'growth', label: 'growth target', placeholder: '2× by ’27', look: 'figure' },
  { key: 'headcount', label: 'people', placeholder: '~400', look: 'figure' },
  { key: 'funding', label: 'funding', placeholder: 'Series C', look: 'figure' },
  { key: 'lastRaised', label: 'last raised', placeholder: 'Mar 2025', look: 'figure' },
];

const NEW_APPLICATION: readonly FieldSpec[] = [
  {
    key: 'role',
    label: 'role',
    placeholder: 'Senior SWE, Payments Core',
    look: 'lead',
    wide: true,
  },
  { key: 'appliedOn', label: 'applied', type: 'date', picks: 'past', none: 'not yet', wide: true },
  ...SOURCE_FIELDS,
  {
    key: 'stage',
    label: 'already at',
    hint: "for one you're logging late",
    type: 'choice',
    options: STAGES.map((value) => ({ value, label: value })),
    required: true,
  },
];

const FIGURES = [
  ['revenue', 'revenue'],
  ['growth', 'growth target'],
  ['headcount', 'people'],
  ['funding', 'funding'],
] as const;

const LISTS = [
  { key: 'competitors', label: 'competes with', placeholder: 'Stripe', look: 'tags' },
  { key: 'concerns', label: 'concerns', placeholder: 'Layoffs in March', marker: '!' },
  { key: 'values', label: 'values', placeholder: 'Default to writing' },
  { key: 'practices', label: 'how it works', placeholder: 'On-call one week in six' },
] as const satisfies readonly ListSpec<Company>[];

/** A company: its applications beside what you know about it, each part edited in place. */
@Component({
  selector: 'app-company-view',
  imports: [
    ApplicationView,
    CompanyNotes,
    FieldsForm,
    Figure,
    Icon,
    ListField,
    People,
    PostingReader,
    RouterLink,
    StageDot,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope mx-auto flex w-full max-w-[1120px] flex-col gap-8' },
  template: `
    @let co = company();
    <a
      routerLink="/app/loop"
      [queryParams]="{ id: null, application: null, note: null }"
      queryParamsHandling="merge"
      class="-mb-4 inline-flex min-h-[40px] items-center gap-1.5 self-start text-[14px] text-[var(--color-muted)] no-underline"
      ><app-icon name="back" [size]="14" />The loop</a
    >

    @if (editing() === 'head') {
      <app-fields-form
        heading="company"
        [fields]="headFields"
        [value]="co"
        [save]="saveChanges"
        (closed)="editing.set(null)"
      />
    } @else {
      <header class="flex items-end justify-between gap-4">
        <div class="min-w-0">
          <h2
            class="m-0 text-[clamp(28px,3.4vw,40px)] leading-[1.05] font-semibold tracking-[-0.045em]"
          >
            {{ co.name }}
          </h2>
          <div class="mt-2 flex flex-wrap items-baseline gap-x-5 gap-y-1">
            @if (co.about) {
              <p class="label m-0 text-[13px]">{{ co.about }}</p>
            } @else {
              <button type="button" class="btn-link" (click)="editing.set('head')">+ about</button>
            }
            @if (co.location) {
              <p class="label m-0 inline-flex items-center gap-1.5 self-center text-[13px]">
                <app-icon name="pin" [size]="13" />{{ co.location }}
              </p>
            } @else {
              <button type="button" class="btn-link" (click)="editing.set('head')">
                + based in
              </button>
            }
          </div>
        </div>
        <div class="flex flex-none items-center gap-2">
          <button
            type="button"
            class="btn btn-sm"
            [disabled]="startingResearch()"
            (click)="research()"
          >
            @switch (researchState()) {
              @case ('going') {
                <span class="spinner h-3 w-3" aria-hidden="true"></span>Researching
              }
              @case ('done') {
                <span class="research-ready" aria-hidden="true"></span>Review research
              }
              @default {
                @if (startingResearch()) {
                  <span class="spinner h-3 w-3" aria-hidden="true"></span>
                } @else {
                  <app-icon name="search" [size]="14" />
                }
                Research
              }
            }
          </button>
          <button
            type="button"
            class="btn btn-icon h-10 min-h-10 w-10"
            aria-label="Edit company"
            (click)="editing.set('head')"
          >
            <app-icon name="edit" [size]="15" />
          </button>
        </div>
      </header>
    }

    @if (researchError()) {
      <p class="alert -mt-4" role="alert">{{ researchError() }}</p>
    }

    <div class="company-grid dim-scope">
      <div class="dim-scope flex min-w-0 flex-col gap-10">
        <section aria-label="Applications" class="dim-scope flex min-w-0 flex-col gap-3">
          <div class="section-head">
            <h3 class="caption">
              {{ applications().length > 1 ? 'applications' : 'application' }}
            </h3>
            @if (applications().length) {
              <div class="flex gap-4">
                <button type="button" class="btn-link" (click)="readingPosting.set(true)">
                  + from a posting
                </button>
                <button type="button" class="btn-link" (click)="adding.set(true)">
                  + application
                </button>
              </div>
            }
          </div>
          @if (readingPosting() || postingRun()) {
            <app-posting-reader
              [company]="co"
              [existing]="postingRun()"
              [keep]="keepPosting"
              applyLabel="Next: when you applied"
              (closed)="readingPosting.set(false)"
            />
          }
          @if (adding()) {
            <app-fields-form
              sheet
              [heading]="'new application · ' + co.name"
              [note]="
                fromPosting()
                  ? 'what the posting said goes in with it'
                  : 'pay, the job and the rest go on the page after'
              "
              [fields]="newApplicationFields"
              [value]="{ ...blankApplication, role: fromPosting()?.role ?? '' }"
              [save]="addApplication"
              saveLabel="Add application"
              failure="The application could not be added."
              (closed)="adding.set(false); fromPosting.set(null)"
            />
          }
          @for (app of applications(); track app.id) {
            @if (applications().length > 1) {
              <a
                class="row-pick"
                routerLink="/app/loop"
                [queryParams]="{ application: app.id }"
                queryParamsHandling="merge"
                [replaceUrl]="true"
                [attr.aria-current]="app.id === selected()?.id"
              >
                <app-stage-dot [status]="statusOf(app)" />
                <span class="flex-1 truncate font-medium">{{ app.role || 'role not set' }}</span>
                <span class="meta w-[60px]">{{ stageLabel(app) }}</span>
                <span class="meta w-[60px] text-right">{{
                  app.appliedOn ? shortDate(app.appliedOn) : ''
                }}</span>
              </a>
            }
            @if (app.id === selected()?.id) {
              <app-application-view
                [application]="app"
                [class.pb-6]="applications().length > 1"
                [class.pt-2]="applications().length > 1"
              />
            }
          } @empty {
            @if (!readingPosting() && !postingRun()) {
              <p class="m-0 text-[var(--color-dim)]">
                No application yet.
                <button
                  type="button"
                  class="btn-link text-[15px] text-[var(--color-accent)]"
                  (click)="adding.set(true)"
                >
                  Add one</button
                >, or
                <button
                  type="button"
                  class="btn-link text-[15px] text-[var(--color-accent)]"
                  (click)="readingPosting.set(true)"
                >
                  read its posting</button
                >.
              </p>
            }
          }
        </section>

        <app-company-notes class="section-rule" [company]="co" />
      </div>

      <aside class="card company-card dim-scope" [attr.aria-label]="'About ' + co.name">
        <section aria-label="The business" class="flex flex-col gap-3.5">
          @if (editing() === 'business') {
            <app-fields-form
              heading="the business"
              [fields]="businessFields"
              [value]="co"
              [save]="saveChanges"
              (closed)="editing.set(null)"
            />
          } @else {
            <div class="section-head">
              <h3 class="caption">the business</h3>
              <button type="button" class="btn-link" (click)="editing.set('business')">edit</button>
            </div>
            @for (part of business(); track part.label) {
              <div class="flex flex-col gap-1">
                <h4 class="figure-label m-0 font-normal">{{ part.label }}</h4>
                <p
                  class="m-0 text-[15px] leading-[1.5]"
                  [style.color]="part.text ? 'var(--color-text-2)' : 'var(--color-edge-2)'"
                >
                  {{ part.text || '—' }}
                </p>
              </div>
            }
          }
        </section>

        <section aria-label="Company numbers" class="card-rule flex flex-col gap-3.5">
          @if (editing() === 'numbers') {
            <app-fields-form
              heading="numbers"
              [fields]="numberFields"
              [value]="co"
              [save]="saveChanges"
              (closed)="editing.set(null)"
            />
          } @else {
            <div class="section-head">
              <h3 class="caption">numbers</h3>
              <button type="button" class="btn-link" (click)="editing.set('numbers')">edit</button>
            </div>
            <div class="grid grid-cols-2 gap-x-5 gap-y-[18px]">
              @for (figure of figures(); track figure.label) {
                <app-figure small [value]="figure.value" [label]="figure.label" />
              }
            </div>
          }
        </section>

        <app-people [company]="co" />

        <div class="card-rule flex flex-col gap-6">
          @for (list of aboutLists; track list.key) {
            <app-list-field
              [label]="list.label"
              [items]="co[list.key]"
              [placeholder]="list.placeholder"
              [look]="list.look"
              [marker]="list.marker"
              [save]="list.save"
            />
          }
        </div>

        <div class="card-rule flex flex-col gap-6">
          @for (list of cultureLists; track list.key) {
            <app-list-field
              [label]="list.label"
              [items]="co[list.key]"
              [placeholder]="list.placeholder"
              [look]="list.look"
              [marker]="list.marker"
              [save]="list.save"
            />
          }
        </div>

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
        <div class="flex flex-wrap items-center gap-2.5">
          @if (confirmingDelete()) {
            <span class="text-[13px] text-[var(--color-muted)]">{{ deleteLine() }}</span>
            <button
              type="button"
              class="btn btn-sm btn-danger"
              [disabled]="busy()"
              (click)="remove()"
            >
              Delete
            </button>
            <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">
              keep
            </button>
          } @else {
            <button type="button" class="btn-link" (click)="confirmingDelete.set(true)">
              delete company
            </button>
          }
        </div>
      </aside>
    </div>
  `,
})
export class CompanyView {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly tray = inject(LoopTray);
  private readonly router = inject(Router);

  readonly company = input.required<Company>();
  /** The application to open; the lead one when unset or gone. */
  readonly applicationId = input<string | null>(null);

  protected readonly statusOf = statusOf;
  protected readonly stageLabel = stageLabel;
  protected readonly shortDate = shortDate;
  protected readonly headFields = HEAD;
  protected readonly businessFields = BUSINESS;
  protected readonly numberFields = NUMBERS;
  protected readonly newApplicationFields = NEW_APPLICATION;
  protected readonly blankApplication = {
    role: '',
    appliedOn: today(),
    source: null,
    sourceDetail: '',
    stage: 'applied',
  };
  protected readonly editing = signal<Section | null>(null);
  protected readonly adding = signal(false);
  protected readonly startingResearch = signal(false);
  protected readonly researchError = signal('');
  protected readonly readingPosting = signal(false);
  /** What a posting said, for the application being added. */
  protected readonly fromPosting = signal<Partial<Application> | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);

  protected readonly saveChanges = (changes: Record<string, unknown>) =>
    this.store.saveCompany({ ...this.company(), ...changes });

  /** Research under way, or done and not yet reviewed, which the tray shows. */
  protected readonly researchRun = computed(() =>
    this.runs.find('researchCompany', { companyId: this.company().id }),
  );
  protected readonly researchState = computed(() => {
    const run = this.researchRun();
    return run && isActive(run) ? 'going' : run?.state === 'succeeded' ? 'done' : 'none';
  });
  /** A posting being read for a new application. */
  protected readonly postingRun = computed(() =>
    this.runs.find('readPosting', { companyId: this.company().id }),
  );

  protected async research(): Promise<void> {
    const run = this.researchRun();
    if (this.researchState() === 'going') {
      this.tray.open();
      return;
    }
    if (run && this.researchState() === 'done') {
      this.tray.review(run.id);
      return;
    }
    this.startingResearch.set(true);
    this.researchError.set('');
    try {
      await this.runs.start(`Research · ${this.company().name}`, {
        kind: 'researchCompany',
        companyId: this.company().id,
      });
      this.tray.open();
    } catch (err) {
      this.researchError.set(messageOf(err, 'The research couldn’t start.'));
    } finally {
      this.startingResearch.set(false);
    }
  }

  /** The company's facts save now; the job waits for the add sheet. */
  protected readonly keepPosting = async (findings: Finding[]) => {
    const changes = changesFrom(this.company(), findings, 'company');
    if (Object.keys(changes).length) {
      await this.saveChanges(changes);
    }
    this.fromPosting.set(changesFrom<Partial<Application>>({}, findings, 'application'));
    this.adding.set(true);
  };

  protected readonly addApplication = async (fields: Record<string, unknown>) => {
    const saved = await this.store.addApplication(this.company().id, {
      ...this.fromPosting(),
      ...fields,
    });
    void this.router.navigate(['/app/loop'], {
      queryParams: { id: this.company().id, application: saved.id },
      replaceUrl: true,
    });
  };

  private readonly lists = LISTS.map((list) => ({
    look: 'rows' as const,
    marker: '·' as const,
    ...list,
    save: (items: string[]) => this.saveChanges({ [list.key]: items }),
  }));
  protected readonly aboutLists = this.lists.slice(0, 2);
  protected readonly cultureLists = this.lists.slice(2);

  protected readonly applications = computed(() =>
    this.store.applications().filter((a) => a.companyId === this.company().id),
  );

  protected readonly selected = computed(() => {
    const id = this.applicationId();
    const list = this.applications();
    return list.find((a) => a.id === id) ?? list[0] ?? null;
  });

  protected readonly business = computed(() => {
    const co = this.company();
    return [
      { label: 'what it does', text: co.whatItDoes },
      { label: 'how it makes money', text: co.howItMakesMoney },
    ];
  });

  protected readonly figures = computed(() => {
    const co = this.company();
    return FIGURES.map(([key, label]) => ({
      label: key === 'funding' && co.lastRaised ? `${label} · ${co.lastRaised}` : label,
      value: co[key],
    }));
  });

  protected readonly deleteLine = computed(() => {
    const { id, name } = this.company();
    const applications = this.applications().length;
    const notes = this.store.notes().filter((note) => note.companyId === id).length;
    const its = [
      applications && plural(applications, 'application'),
      notes && plural(notes, 'note'),
    ].filter(Boolean);
    return its.length
      ? `Delete ${name} with its ${its.join(' and ')} for good?`
      : `Delete ${name} for good?`;
  });

  protected async remove(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.deleteCompany(this.company().id);
      void this.router.navigate(['/app/loop'], { replaceUrl: true });
    } catch (err) {
      this.error.set(messageOf(err, 'The company could not be deleted.'));
      this.busy.set(false);
    }
  }
}

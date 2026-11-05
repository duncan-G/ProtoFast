import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListCard } from '../../editors/list-card';
import { ListField } from '../../editors/list-field';
import { fromNow, shortDate, today, whenWithDate } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { changesFrom, Finding } from '../../loop/findings';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { ARRANGEMENTS } from '../../loop/model/arrangement';
import { Source, SOURCES } from '../../loop/model/source';
import { Stage, STAGES } from '../../loop/model/stage';
import { statusOf } from '../../loop/model/status';
import { figureParts } from '../../shared/figure';
import { Icon } from '../../shared/icon';
import { MenuItem, MoreMenu } from '../../shared/more-menu';
import { LearningsCard } from './learnings-card';
import { PostingReader } from './posting-reader';
import { QuestionsCard } from './questions-card';
import { Rounds } from './rounds';
import { SentFiles } from './sent-files';

type Section = 'basics' | 'next' | 'pay' | 'job' | 'posting';

const VIA: Partial<Record<Source, string>> = {
  referral: 'referred by',
  recruiter: 'which recruiter',
  'job board': 'which board',
  other: 'how you found it',
};

export const SOURCE_FIELDS: readonly FieldSpec[] = [
  {
    key: 'source',
    label: 'came from',
    type: 'choice',
    options: SOURCES.map((value) => ({ value, label: value })),
  },
  {
    key: 'sourceDetail',
    label: (draft) => VIA[draft['source'] as Source] ?? 'via',
    hint: (draft) => (draft['source'] ? '' : 'who referred you, which board'),
    placeholder: 'Sam Okafor',
  },
];

const BASICS: readonly FieldSpec[] = [
  {
    key: 'role',
    label: 'role',
    placeholder: 'Senior SWE, Payments Core',
    look: 'lead',
    wide: true,
  },
  { key: 'appliedOn', label: 'applied', type: 'date', picks: 'past', none: 'not yet', wide: true },
  ...SOURCE_FIELDS,
];

const PAY: readonly FieldSpec[] = [
  { key: 'postedPay', label: 'posted range', placeholder: '$180–220k', look: 'figure' },
  { key: 'askedPay', label: 'you asked', placeholder: '$215k', look: 'figure' },
  { key: 'offeredPay', label: 'offer', placeholder: '$205k', look: 'figure' },
  {
    key: 'offerTerms',
    label: 'offer terms',
    hint: 'equity, bonus, vesting, sign-on',
    placeholder: '0.1% over 4y, 1y cliff · 10% bonus · $20k sign-on',
    wide: true,
  },
];

const JOB: readonly FieldSpec[] = [
  {
    key: 'postingUrl',
    label: 'posting link',
    type: 'url',
    placeholder: 'https://boards.greenhouse.io/halcyon/4021',
    look: 'mono',
    wide: true,
  },
  {
    key: 'posting',
    label: 'saved copy',
    hint: 'in case the posting comes down',
    type: 'textarea',
    look: 'mono',
  },
  {
    key: 'team',
    label: 'team',
    hint: "what you'd work on",
    placeholder: 'Payments Core, ledger + retries',
  },
  { key: 'reportsTo', label: 'reports to', placeholder: 'Priya Raman, eng manager' },
  {
    key: 'arrangement',
    label: 'setup',
    type: 'choice',
    options: ARRANGEMENTS.map((value) => ({ value, label: value })),
  },
  { key: 'location', label: 'where', placeholder: 'SF, 3 days' },
  { key: 'hours', label: 'hours', placeholder: '4h overlap ET' },
  {
    key: 'sponsorsVisa',
    label: 'visa',
    type: 'choice',
    options: [
      { value: true, label: 'sponsors' },
      { value: false, label: "doesn't sponsor" },
      { value: null, label: 'not sure' },
    ],
  },
];

const PAY_FIGURES = [
  ['postedPay', 'Posted range'],
  ['askedPay', 'You asked'],
  ['offeredPay', 'Offer'],
] as const;

/** One application as cards, each edited where it is shown. */
@Component({
  selector: 'app-application-view',
  imports: [
    FieldsForm,
    Icon,
    LearningsCard,
    ListCard,
    ListField,
    MoreMenu,
    PostingReader,
    QuestionsCard,
    Rounds,
    SentFiles,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope flex flex-col gap-6' },
  template: `
    @let app = application();
    <section class="card app-card dim-scope" aria-label="Where it stands">
      @if (editing() === 'basics') {
        <app-fields-form
          heading="role and source"
          [fields]="basics"
          [value]="app"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h2 class="app-role flex flex-wrap items-center gap-x-3 gap-y-1">
              {{ app.role || 'Role not set' }}
              @if (app.closed) {
                <span class="badge font-sans text-[12.5px] font-medium">Closed</span>
              }
            </h2>
            <p class="mt-2 mb-0 text-[14px] text-[var(--color-muted)]">{{ appliedLine() }}</p>
          </div>
          <div class="card-actions">
            <button type="button" class="btn btn-xs" (click)="editing.set('basics')">
              <app-icon name="edit" [size]="13" />Edit
            </button>
            <app-more-menu small label="More application actions" [items]="menu()" />
          </div>
        </div>
      }
      @if (confirmingDelete()) {
        <div class="confirm-bar" role="alert">
          <span>Delete this application for good?</span>
          <button type="button" class="btn btn-xs" (click)="confirmingDelete.set(false)">
            Keep it
          </button>
          <button
            type="button"
            class="btn btn-sm btn-danger"
            [disabled]="busy()"
            (click)="remove()"
          >
            Delete
          </button>
        </div>
      }

      <div>
        <div class="stage-head">
          <h3 class="field-label text-[13px] font-medium">Stage</h3>
          <p class="hint">Click a stage to move it</p>
        </div>
        <div role="radiogroup" aria-label="Stage" class="stage-bar">
          @for (step of track(); track step.stage) {
            <button
              type="button"
              role="radio"
              class="stage-step"
              [attr.aria-checked]="step.here"
              [attr.aria-label]="step.stage"
              [disabled]="busy()"
              (click)="setStage(step.stage)"
            >
              <span
                class="stage-seg"
                [style.background]="step.seg"
                [style.box-shadow]="step.glow"
              ></span>
              <span
                class="stage-label"
                [class.font-semibold]="step.here"
                [style.color]="step.color"
                >{{ step.label }}</span
              >
            </button>
          }
        </div>
      </div>

      <div class="app-split dim-scope">
        <div class="flex flex-col gap-2.5">
          @if (editing() === 'next') {
            <app-fields-form
              heading="next step"
              [fields]="nextFields()"
              [value]="app"
              [save]="saveChanges"
              (closed)="editing.set(null)"
            />
          } @else {
            <div class="card-head">
              <h3 class="field-label text-[13px] font-medium">
                {{ app.stage === 'offer' ? 'Offer ends' : 'Next step' }}
              </h3>
              @if (app.nextStep || app.nextOn) {
                <button type="button" class="btn btn-xs" (click)="editing.set('next')">
                  <app-icon name="edit" [size]="13" />Edit
                </button>
              }
            </div>
            @if (app.nextStep || app.nextOn) {
              <div class="next-box" [style.--next-color]="hot()">
                @if (app.nextOn) {
                  <div class="flex flex-wrap items-baseline gap-x-3">
                    <span class="next-day" [style.color]="hot()">{{ nextWhen() }}</span>
                    <span class="meta">{{ nextIn() }}</span>
                  </div>
                }
                @if (app.nextStep) {
                  <p class="next-what">{{ app.nextStep }}</p>
                }
              </div>
            } @else {
              <button type="button" class="add-tile" (click)="editing.set('next')">
                <span class="add-tile-mark"><app-icon name="plus" [size]="13" [weight]="2" /></span>
                <span class="add-tile-text"
                  >Add the next step<small>When it is and what happens</small></span
                >
              </button>
            }
          }
        </div>
        <app-rounds [application]="app" [hot]="hot()" />
      </div>
    </section>

    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }

    <div class="cards dim-scope">
      <section class="card card-pad dim-scope" aria-label="Pay">
        @if (editing() === 'pay') {
          <app-fields-form
            heading="pay"
            layout="figures"
            [fields]="payFields"
            [value]="app"
            [save]="saveChanges"
            (closed)="editing.set(null)"
          />
        } @else {
          <div class="card-head">
            <h3 class="card-title">Pay</h3>
            <button type="button" class="btn btn-xs" (click)="editing.set('pay')">
              <app-icon name="edit" [size]="13" />Edit
            </button>
          </div>
          <div class="figures-row">
            @for (figure of pay(); track figure.label) {
              <div class="flex min-w-0 flex-col gap-1.5">
                <p class="field-label">{{ figure.label }}</p>
                @if (figure.value) {
                  <p class="figure m-0" [style.color]="figure.color">{{ figure.value }}</p>
                  @if (figure.context) {
                    <p class="tile-context m-0">{{ figure.context }}</p>
                  }
                } @else if (figure.addable) {
                  <button type="button" class="btn-add self-start" (click)="editing.set('pay')">
                    <app-icon name="plus" [size]="11" [weight]="2" />Add
                  </button>
                } @else {
                  <p class="quiet pt-1.5">None yet</p>
                }
              </div>
            }
          </div>
          @if (app.offerTerms) {
            <p class="m-0 text-[14.5px] text-[var(--color-text-2)]">{{ app.offerTerms }}</p>
          }
        }
      </section>

      <app-sent-files [application]="app" />
    </div>

    <section class="card card-pad dim-scope" aria-label="The job">
      @if (editing() === 'job') {
        <app-fields-form
          heading="the job"
          [fields]="jobFields"
          [value]="app"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else if ((editing() === 'posting' || postingRun()) && company(); as co) {
        <app-posting-reader
          [company]="co"
          [application]="app"
          [existing]="postingRun()"
          [keep]="keepPosting"
          applyLabel="Fill in the job"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="card-head">
          <h3 class="card-title">The job</h3>
          <div class="card-actions">
            @if (app.posting) {
              <button
                type="button"
                class="btn btn-xs"
                [attr.aria-expanded]="readingSaved()"
                (click)="readingSaved.set(!readingSaved())"
              >
                <app-icon name="file" [size]="13" />{{
                  readingSaved() ? 'Hide the saved posting' : 'Read the saved posting'
                }}
              </button>
            }
            @if (app.postingUrl) {
              <a
                class="btn btn-xs"
                [href]="app.postingUrl"
                target="_blank"
                rel="noopener noreferrer"
                >{{ postingHost() }} ↗</a
              >
            }
            <button type="button" class="btn btn-xs" (click)="editing.set('posting')">
              <app-icon name="search" [size]="13" />Fill from a posting
            </button>
            <button type="button" class="btn btn-xs" (click)="editing.set('job')">
              <app-icon name="edit" [size]="13" />Edit
            </button>
          </div>
        </div>
        @if (readingSaved()) {
          <p
            class="m-0 max-h-[420px] overflow-y-auto rounded-[12px] bg-[var(--color-bg)] p-4 text-[14.5px] whitespace-pre-line text-[var(--color-text-2)]"
          >
            {{ app.posting }}
          </p>
        }
        <dl class="facts-grid">
          @for (fact of facts(); track fact.label) {
            <div class="min-w-0">
              <dt class="field-label">{{ fact.label }}</dt>
              <dd>
                @if (fact.value) {
                  {{ fact.value }}
                } @else {
                  <button type="button" class="btn-add min-h-8" (click)="editing.set('job')">
                    <app-icon name="plus" [size]="11" [weight]="2" />Add
                  </button>
                }
              </dd>
            </div>
          }
        </dl>
        <div class="card-split flex flex-col gap-2.5">
          <h4 class="field-label">Stack</h4>
          <app-list-field
            label="Stack"
            look="tags"
            placeholder="Postgres"
            [heading]="false"
            [items]="app.stack"
            [save]="saveList('stack')"
          />
        </div>
        <div class="card-split flex flex-col gap-2.5">
          <h4 class="field-label">Benefits</h4>
          <app-list-field
            label="Benefits"
            look="tags"
            placeholder="16 weeks parental leave"
            [heading]="false"
            [items]="app.benefits"
            [save]="saveList('benefits')"
          />
        </div>
      }
    </section>

    <app-list-card
      heading="What they want"
      split
      placeholder="Go or Java"
      empty="Nothing noted yet. Fill the job from its posting, or add what they ask for."
      [subtitle]="subtitle()"
      [items]="app.wants"
      [save]="saveList('wants')"
    />

    <div class="cards">
      <app-questions-card [application]="app" />
      <app-learnings-card [application]="app" />
    </div>
  `,
})
export class ApplicationView {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly application = input.required<Application>();

  protected readonly basics = BASICS;
  protected readonly payFields = PAY;
  protected readonly jobFields = JOB;
  protected readonly editing = signal<Section | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly readingSaved = signal(false);

  protected readonly saveChanges = (changes: Record<string, unknown>) =>
    this.store.saveApplication({ ...this.application(), ...changes });

  /** Its posting being read, or read and not yet reviewed. */
  protected readonly postingRun = computed(() =>
    this.runs.find('readPosting', {
      companyId: this.application().companyId,
      applicationId: this.application().id,
    }),
  );
  protected readonly company = computed(() =>
    this.store.companiesById().get(this.application().companyId),
  );

  protected readonly keepPosting = async (findings: Finding[]) => {
    const app = this.application();
    await this.store.saveApplication({ ...app, ...changesFrom(app, findings, 'application') });
    const company = this.company();
    const changes = company ? changesFrom(company, findings, 'company') : {};
    if (company && Object.keys(changes).length) {
      await this.store.saveCompany({ ...company, ...changes });
    }
  };

  protected readonly saveList =
    (key: 'wants' | 'stack' | 'benefits') =>
    (items: string[]): Promise<unknown> =>
      this.saveChanges({ [key]: items });

  protected readonly subtitle = computed(() => {
    const app = this.application();
    return `${app.role || 'Role not set'} · ${app.company}`;
  });

  protected readonly menu = computed((): MenuItem[] => [
    {
      label: this.application().closed ? 'Reopen' : 'Mark closed',
      run: () => void this.setClosed(!this.application().closed),
    },
    { label: 'Delete application', danger: true, run: () => this.confirmingDelete.set(true) },
  ]);

  protected readonly nextFields = computed((): FieldSpec[] => [
    {
      key: 'nextStep',
      label: 'what',
      placeholder: 'Virtual onsite, 4 rounds',
      look: 'large',
      wide: true,
    },
    {
      key: 'nextOn',
      label: this.application().stage === 'offer' ? 'offer ends' : 'when',
      type: 'date',
      picks: 'ahead',
      none: 'no date',
      wide: true,
    },
  ]);

  protected readonly hot = computed(() =>
    this.application().stage === 'offer' ? 'var(--color-accent)' : 'var(--color-amber)',
  );

  protected readonly nextWhen = computed(() => {
    const nextOn = this.application().nextOn;
    return nextOn ? whenWithDate(nextOn, this.store.today()) : '';
  });

  protected readonly nextIn = computed(() => {
    const nextOn = this.application().nextOn;
    return nextOn ? fromNow(nextOn, this.store.today()) : '';
  });

  protected readonly appliedLine = computed(() => {
    const app = this.application();
    const source = app.source === 'other' && app.sourceDetail ? null : app.source;
    const via =
      source && app.sourceDetail ? `${source} (${app.sourceDetail})` : source || app.sourceDetail;
    const applied = app.appliedOn
      ? `Applied ${shortDate(app.appliedOn)}`
      : statusOf(app) === 'none' && 'Not applied yet';
    const line = [applied, via && `via ${via}`].filter(Boolean).join(' · ');
    return line.charAt(0).toUpperCase() + line.slice(1);
  });

  /** An offer shows in the offer colour, and can't be added before there is one. */
  protected readonly pay = computed(() => {
    const app = this.application();
    return PAY_FIGURES.map(([key, label]) => {
      const { figure, context } = figureParts(app[key]);
      return {
        label,
        value: figure,
        context,
        color: key === 'offeredPay' ? 'var(--color-accent)' : 'var(--color-text)',
        addable: key !== 'offeredPay' || app.stage === 'offer',
      };
    });
  });

  protected readonly postingHost = computed(() => {
    const url = this.application().postingUrl;
    try {
      return new URL(url).host.replace(/^www\./, '');
    } catch {
      return url;
    }
  });

  protected readonly facts = computed(() => {
    const app = this.application();
    const setup = [app.arrangement ?? '', app.location, app.hours].filter(Boolean).join(' · ');
    const visa =
      app.sponsorsVisa === null ? '' : app.sponsorsVisa ? 'Sponsors visas' : 'No visa sponsorship';
    return [
      { label: 'Team', value: app.team },
      { label: 'Reports to', value: app.reportsTo },
      { label: 'Setup', value: setup.charAt(0).toUpperCase() + setup.slice(1) },
      { label: 'Visa', value: visa },
    ];
  });

  /** Passed stages grey, the one it's at lit unless only applied or closed; none before applying. */
  protected readonly track = computed(() => {
    const app = this.application();
    const status = statusOf(app);
    const at = status === 'none' ? -1 : STAGES.indexOf(app.stage);
    const lit = status === 'active' || status === 'offer';
    const hot = this.hot();
    return STAGES.map((stage, i) => {
      const here = i === at;
      const glows = here && lit;
      let seg = 'var(--color-line-2)';
      let color = 'var(--color-dim)';
      if (i < at) {
        seg = app.closed ? 'var(--color-edge)' : 'var(--color-faint)';
        color = 'var(--color-muted)';
      } else if (here) {
        seg = app.closed ? 'var(--color-edge-2)' : lit ? hot : 'var(--color-muted)';
        color = app.closed ? 'var(--color-muted)' : lit ? hot : 'var(--color-text)';
      }
      return {
        stage,
        here,
        label: here && app.closed ? 'closed' : stage,
        seg,
        color,
        glow: glows ? `0 0 0 4px color-mix(in srgb, ${hot} 14%, transparent)` : 'none',
      };
    });
  });

  /** Pressing applied before there's a day marks it applied today. */
  protected setStage(stage: Stage): Promise<void> {
    const app = this.application();
    if (stage === 'applied' && !app.appliedOn) {
      return this.act({ stage, appliedOn: today() }, 'The stage could not be saved.');
    }
    return stage === app.stage
      ? Promise.resolve()
      : this.act({ stage }, 'The stage could not be saved.');
  }

  protected setClosed(closed: boolean): Promise<void> {
    return this.act({ closed }, 'The application could not be saved.');
  }

  protected async remove(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.deleteApplication(this.application().id);
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { application: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    } catch (err) {
      this.error.set(messageOf(err, 'The application could not be deleted.'));
      this.busy.set(false);
    }
  }

  private async act(changes: Partial<Application>, failure: string): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.saveChanges(changes);
    } catch (err) {
      this.error.set(messageOf(err, failure));
    } finally {
      this.busy.set(false);
    }
  }
}

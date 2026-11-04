import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListField, ListSpec } from '../../editors/list-field';
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
import { Figure } from '../../shared/figure';
import { PostingReader } from './posting-reader';
import { Rounds } from './rounds';
import { SentFiles } from './sent-files';

type Section = 'basics' | 'next' | 'pay' | 'job' | 'posting' | 'learned';

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

const LEARNED: readonly FieldSpec[] = [
  {
    key: 'learned',
    label: 'learned',
    type: 'textarea',
    placeholder: 'Tech screen was all idempotency and retries.',
  },
];

const PAY_FIGURES = [
  ['postedPay', 'posted range'],
  ['askedPay', 'you asked'],
  ['offeredPay', 'offer'],
] as const;

const LISTS = [
  { key: 'questions', label: 'to ask', placeholder: 'Who owns on-call?', marker: '?' },
  { key: 'stack', label: 'stack', placeholder: 'Postgres', look: 'tags' },
  { key: 'wants', label: 'they want', placeholder: 'Go or Java' },
  { key: 'benefits', label: 'benefits', placeholder: '16 weeks parental leave' },
] as const satisfies readonly ListSpec<Application>[];

/** One application, every section edited where it is shown. */
@Component({
  selector: 'app-application-view',
  imports: [Figure, FieldsForm, ListField, PostingReader, Rounds, SentFiles],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope flex flex-col gap-10' },
  template: `
    @let app = application();
    <div class="card app-card dim-scope">
      @if (editing() === 'basics') {
        <app-fields-form
          heading="role and source"
          [fields]="basics"
          [value]="app"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0">
            <h3 class="app-role">{{ app.role || 'Role not set' }}</h3>
            <p class="mono mt-2 mb-0 text-[12.5px] text-[var(--color-dim)]">{{ appliedLine() }}</p>
          </div>
          <div class="flex flex-none gap-4 pt-1">
            <button
              type="button"
              class="btn-link"
              [disabled]="busy()"
              (click)="setClosed(!app.closed)"
            >
              {{ app.closed ? 'reopen' : 'mark closed' }}
            </button>
            <button type="button" class="btn-link" (click)="editing.set('basics')">edit</button>
          </div>
        </div>
      }

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

      <div class="app-split dim-scope">
        <div class="flex flex-col gap-1.5">
          @if (editing() === 'next') {
            <app-fields-form
              heading="next step"
              [fields]="nextFields()"
              [value]="app"
              [save]="saveChanges"
              (closed)="editing.set(null)"
            />
          } @else {
            <div class="section-head">
              <h4 class="caption">{{ app.stage === 'offer' ? 'offer ends' : 'next' }}</h4>
              @if (app.nextStep || app.nextOn) {
                <button type="button" class="btn-link" (click)="editing.set('next')">edit</button>
              }
            </div>
            @if (app.nextStep || app.nextOn) {
              @if (app.nextOn) {
                <div class="mt-1 flex items-baseline gap-3">
                  <span class="next-day" [style.color]="hot()">{{ nextWhen() }}</span>
                  <span class="meta">{{ nextIn() }}</span>
                </div>
              }
              @if (app.nextStep) {
                <p class="next-what">{{ app.nextStep }}</p>
              }
            } @else {
              <button type="button" class="btn-link self-start" (click)="editing.set('next')">
                + next step
              </button>
            }
          }
        </div>
        <app-rounds [application]="app" [hot]="hot()" />
      </div>
    </div>

    <section aria-label="Pay" class="flex flex-col gap-3.5">
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
        <div class="section-head">
          <h4 class="caption">pay</h4>
          <button type="button" class="btn-link" (click)="editing.set('pay')">edit</button>
        </div>
        <div class="grid grid-cols-3 gap-5">
          @for (figure of pay(); track figure.label) {
            <app-figure [value]="figure.value" [label]="figure.label" [color]="figure.color" />
          }
        </div>
        @if (app.offerTerms) {
          <p class="m-0 text-[14.5px] text-[var(--color-text-2)]">{{ app.offerTerms }}</p>
        }
      }
    </section>

    <app-sent-files [application]="app" />

    <section aria-label="The job" class="section-rule flex flex-col gap-3.5">
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
        <div class="section-head">
          <h4 class="caption">the job</h4>
          <div class="flex gap-4">
            <button type="button" class="btn-link" (click)="editing.set('posting')">
              read posting
            </button>
            @if (hasJob()) {
              <button type="button" class="btn-link" (click)="editing.set('job')">edit</button>
            }
          </div>
        </div>
        @if (hasJob()) {
          <dl class="facts">
            @if (app.postingUrl || app.posting) {
              <dt class="term">posting</dt>
              <dd class="flex min-w-0 flex-col items-start">
                @if (app.postingUrl) {
                  <a
                    class="max-w-full truncate"
                    [href]="app.postingUrl"
                    target="_blank"
                    rel="noopener noreferrer"
                    >{{ postingHost() }} ↗</a
                  >
                }
                @if (app.posting) {
                  <details class="w-full">
                    <summary
                      class="btn-link inline-flex list-none items-center [&::-webkit-details-marker]:hidden"
                    >
                      saved copy
                    </summary>
                    <p
                      class="m-0 mt-1 whitespace-pre-line text-[14.5px] text-[var(--color-text-2)]"
                    >
                      {{ app.posting }}
                    </p>
                  </details>
                }
              </dd>
            }
            @if (app.team) {
              <dt class="term">team</dt>
              <dd>{{ app.team }}</dd>
            }
            @if (app.reportsTo) {
              <dt class="term">reports to</dt>
              <dd>{{ app.reportsTo }}</dd>
            }
            @if (setup().length) {
              <dt class="term">setup</dt>
              <dd class="tags">
                @for (part of setup(); track part) {
                  <span class="tag-soft">{{ part }}</span>
                }
              </dd>
            }
          </dl>
        } @else {
          <button type="button" class="btn-link self-start" (click)="editing.set('job')">
            + posting, team, setup
          </button>
        }
      }
    </section>

    <div class="section-rule flex flex-col gap-8">
      @for (list of lists; track list.key) {
        <app-list-field
          [label]="list.label"
          [items]="app[list.key]"
          [placeholder]="list.placeholder"
          [look]="list.look"
          [marker]="list.marker"
          [save]="list.save"
        />
      }
    </div>

    <section aria-label="Learned" class="section-rule flex flex-col gap-2">
      @if (editing() === 'learned') {
        <app-fields-form
          heading="learned"
          [fields]="learnedFields"
          [value]="app"
          [save]="saveChanges"
          (closed)="editing.set(null)"
        />
      } @else {
        <div class="section-head">
          <h4 class="caption">learned</h4>
          @if (app.learned) {
            <button type="button" class="btn-link" (click)="editing.set('learned')">edit</button>
          }
        </div>
        @if (app.learned) {
          <p class="m-0 max-w-[60ch] text-[16px] whitespace-pre-line text-[var(--color-text-2)]">
            {{ app.learned }}
          </p>
        } @else {
          <button type="button" class="btn-link self-start" (click)="editing.set('learned')">
            + what you learned
          </button>
        }
      }
    </section>

    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
    <div class="-mt-4 flex flex-wrap items-center gap-2.5">
      @if (confirmingDelete()) {
        <span class="text-[13px] text-[var(--color-muted)]">Delete this application for good?</span>
        <button type="button" class="btn btn-danger" [disabled]="busy()" (click)="remove()">
          Delete
        </button>
        <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">keep</button>
      } @else {
        <button type="button" class="btn-link" (click)="confirmingDelete.set(true)">
          delete application
        </button>
      }
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
  protected readonly learnedFields = LEARNED;
  protected readonly editing = signal<Section | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);

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

  protected readonly lists = LISTS.map((list) => ({
    look: 'rows' as const,
    marker: '·' as const,
    ...list,
    save: (items: string[]) => this.saveChanges({ [list.key]: items }),
  }));

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
      ? `applied ${shortDate(app.appliedOn)}`
      : statusOf(app) === 'none' && 'not applied yet';
    return [applied, via && `via ${via}`].filter(Boolean).join(' · ');
  });

  /** An offer shows in the offer colour. */
  protected readonly pay = computed(() => {
    const app = this.application();
    return PAY_FIGURES.map(([key, label]) => ({
      label,
      value: app[key],
      color: key === 'offeredPay' ? 'var(--color-accent)' : undefined,
    }));
  });

  protected readonly postingHost = computed(() => {
    const url = this.application().postingUrl;
    try {
      return new URL(url).host.replace(/^www\./, '');
    } catch {
      return url;
    }
  });

  protected readonly setup = computed(() => {
    const app = this.application();
    const visa =
      app.sponsorsVisa === null ? '' : app.sponsorsVisa ? 'sponsors visas' : 'no visa sponsorship';
    return [app.arrangement ?? '', app.location, app.hours, visa].filter(Boolean);
  });

  protected readonly hasJob = computed(() => {
    const app = this.application();
    return !!(app.postingUrl || app.posting || app.team || app.reportsTo || this.setup().length);
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

import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { glanceLine, stageSegments } from '../../loop/application-glance';
import { today } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { changesFrom, Finding } from '../../loop/findings';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { LoopTray } from '../../loop/loop-tray';
import { plural } from '../../loop/loop-summary';
import { Application } from '../../loop/model/application';
import { Company } from '../../loop/model/company';
import { isActive } from '../../loop/model/loop-run';
import { STAGES } from '../../loop/model/stage';
import { statusOf } from '../../loop/model/status';
import { Icon } from '../../shared/icon';
import { MenuItem, MoreMenu } from '../../shared/more-menu';
import { StageDot } from '../../shared/stage-dot';
import { ApplicationList } from './application-list';
import { ApplicationView, SOURCE_FIELDS } from './application-view';
import { CompanyAbout } from './company-about';
import { CompanyNotes } from './company-notes';
import { LearnedLog } from './learned-log';
import { PostingReader } from './posting-reader';

export type CompanyTab = 'applications' | 'company' | 'notes';

/** From this many on, applications are picked from a list rather than from cards. */
const MANY = 4;

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

/** A company: its applications, what you know about it, and its notes, a tab each. */
@Component({
  selector: 'app-company-view',
  imports: [
    ApplicationList,
    ApplicationView,
    CompanyAbout,
    CompanyNotes,
    FieldsForm,
    Icon,
    LearnedLog,
    MoreMenu,
    PostingReader,
    RouterLink,
    StageDot,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dim-scope mx-auto flex w-full max-w-[1120px] flex-col gap-6' },
  template: `
    @let co = company();
    @if (view() === 'learned' && selected(); as app) {
      <app-learned-log [application]="app" />
    } @else {
      <a
        routerLink="/app/loop"
        [queryParams]="{ id: null, application: null, note: null, tab: null, view: null }"
        queryParamsHandling="merge"
        class="-mb-2 inline-flex min-h-[40px] items-center gap-1.5 self-start text-[14px] text-[var(--color-muted)] no-underline"
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
        <header class="company-head">
          <div class="min-w-0">
            <h1 class="company-name">{{ co.name }}</h1>
            <div class="company-meta">
              @if (co.about) {
                <span>{{ about() }}</span>
              }
              @if (co.location) {
                <span class="inline-flex items-center gap-1.5"
                  ><app-icon name="pin" [size]="13" />{{ co.location }}</span
                >
              }
              @if (!co.about || !co.location) {
                <button type="button" class="btn-add min-h-8" (click)="editing.set('head')">
                  <app-icon name="plus" [size]="11" [weight]="2" />{{
                    co.about ? 'Where it’s based' : 'What it is'
                  }}
                </button>
              }
            </div>
          </div>
          <div class="card-actions">
            @if (notApplied()) {
              <button
                type="button"
                class="btn btn-icon btn-raised star-toggle"
                aria-label="Star company"
                title="Starred companies come first among those not applied for"
                [attr.aria-pressed]="co.starred"
                [disabled]="starring()"
                (click)="toggleStar()"
              >
                <app-icon name="star" [size]="16" />
              </button>
            }
            <button
              type="button"
              class="btn btn-raised"
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
            <button type="button" class="btn btn-raised" (click)="editing.set('head')">
              <app-icon name="edit" [size]="14" />Edit company
            </button>
            <app-more-menu label="More company actions" [items]="menu()" />
          </div>
        </header>
      }

      @if (researchError() || starError() || error()) {
        <p class="alert" role="alert">{{ researchError() || starError() || error() }}</p>
      }
      @if (confirmingDelete()) {
        <div class="confirm-bar" role="alert">
          <span>{{ deleteLine() }}</span>
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

      <div class="page-tabs">
        <nav [attr.aria-label]="co.name">
          <a
            class="page-tab"
            routerLink="/app/loop"
            [queryParams]="{ tab: null }"
            queryParamsHandling="merge"
            [attr.aria-current]="tab() === 'applications' ? 'page' : null"
          >
            @if (applications().length === 1) {
              <app-stage-dot [status]="statusOf(applications()[0])" />Application
            } @else {
              Applications
              @if (applications().length) {
                <span class="badge">{{ applications().length }}</span>
              }
            }
          </a>
          <a
            class="page-tab"
            routerLink="/app/loop"
            [queryParams]="{ tab: 'company' }"
            queryParamsHandling="merge"
            [attr.aria-current]="tab() === 'company' ? 'page' : null"
            >Company</a
          >
          <a
            class="page-tab"
            routerLink="/app/loop"
            [queryParams]="{ tab: 'notes' }"
            queryParamsHandling="merge"
            [attr.aria-current]="tab() === 'notes' ? 'page' : null"
          >
            Notes
            @if (notes()) {
              <span class="badge">{{ notes() }}</span>
            }
          </a>
        </nav>
        @if (tab() === 'applications' && applications().length) {
          <div class="page-tabs-actions">
            <button type="button" class="btn btn-xs" (click)="readingPosting.set(true)">
              <app-icon name="search" [size]="13" />Read a posting
            </button>
            <button type="button" class="btn btn-xs" (click)="adding.set(true)">
              <app-icon name="plus" [size]="12" [weight]="2" />New application
            </button>
          </div>
        }
      </div>

      @switch (tab()) {
        @case ('company') {
          <app-company-about [company]="co" />
        }
        @case ('notes') {
          <app-company-notes class="card card-pad" [company]="co" />
        }
        @default {
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

          @if (!applications().length) {
            @if (!readingPosting() && !postingRun()) {
              <section class="card card-pad items-start" aria-label="No application yet">
                <h2 class="card-title">No application yet</h2>
                <p class="quiet">
                  Read the posting to fill in the job, or add the application by hand.
                </p>
                <div class="card-actions">
                  <button
                    type="button"
                    class="btn btn-sm btn-primary"
                    (click)="readingPosting.set(true)"
                  >
                    <app-icon name="search" [size]="14" />Read its posting
                  </button>
                  <button type="button" class="btn btn-sm btn-raised" (click)="adding.set(true)">
                    Add by hand
                  </button>
                </div>
              </section>
            }
          } @else if (!selected()) {
            <app-application-list [applications]="applications()" />
          } @else {
            @if (stepper(); as step) {
              <div class="app-stepper">
                <a
                  class="btn btn-xs"
                  routerLink="/app/loop"
                  [queryParams]="{ application: null }"
                  queryParamsHandling="merge"
                  ><app-icon name="back" [size]="13" />All
                  {{ applications().length }} applications</a
                >
                <div class="flex items-center gap-2">
                  <span class="text-[13px] text-[var(--color-muted)]">{{ step.label }}</span>
                  @if (step.previous; as previous) {
                    <a
                      class="btn btn-icon btn-xs"
                      routerLink="/app/loop"
                      [queryParams]="{ application: previous.id }"
                      queryParamsHandling="merge"
                      [attr.aria-label]="'Previous: ' + previous.role"
                      ><app-icon name="back" [size]="14"
                    /></a>
                  } @else {
                    <button
                      type="button"
                      class="btn btn-icon btn-xs"
                      disabled
                      aria-label="Previous"
                    >
                      <app-icon name="back" [size]="14" />
                    </button>
                  }
                  @if (step.next; as next) {
                    <a
                      class="btn btn-icon btn-xs"
                      routerLink="/app/loop"
                      [queryParams]="{ application: next.id }"
                      queryParamsHandling="merge"
                      [attr.aria-label]="'Next: ' + next.role"
                      ><app-icon name="next" [size]="14"
                    /></a>
                  } @else {
                    <button type="button" class="btn btn-icon btn-xs" disabled aria-label="Next">
                      <app-icon name="next" [size]="14" />
                    </button>
                  }
                </div>
              </div>
            } @else if (applications().length > 1) {
              <div class="app-picks" role="group" aria-label="Pick an application">
                @for (pick of picks(); track pick.id) {
                  <a
                    class="app-pick"
                    routerLink="/app/loop"
                    [queryParams]="{ application: pick.id }"
                    queryParamsHandling="merge"
                    [replaceUrl]="true"
                    [attr.aria-current]="pick.id === selected()?.id"
                    [attr.data-closed]="pick.closed"
                  >
                    <span class="app-pick-role">{{ pick.role }}</span>
                    <span class="app-pick-line"
                      ><app-stage-dot [status]="pick.status" />{{ pick.line }}</span
                    >
                    <span class="mini-stages" aria-hidden="true">
                      @for (seg of pick.segments; track $index) {
                        <span [style.background]="seg"></span>
                      }
                    </span>
                  </a>
                }
              </div>
            }
            @if (selected(); as app) {
              <!-- One view per application, so a panel open on one never shows on the next. -->
              @for (key of [app.id]; track key) {
                <app-application-view [application]="app" />
              }
            }
          }
        }
      }
    }
  `,
})
export class CompanyView {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly tray = inject(LoopTray);
  private readonly router = inject(Router);

  readonly company = input.required<Company>();
  /** The application to open; the lead one when unset or gone, unless there are many. */
  readonly applicationId = input<string | null>(null);
  readonly tab = input<CompanyTab>('applications');
  /** 'learned' shows the open application's log of what was learned in place of the page. */
  readonly view = input<string | null>(null);

  protected readonly statusOf = statusOf;
  protected readonly headFields = HEAD;
  protected readonly newApplicationFields = NEW_APPLICATION;
  protected readonly blankApplication = {
    role: '',
    appliedOn: today(),
    source: null,
    sourceDetail: '',
    stage: 'applied',
  };
  protected readonly editing = signal<'head' | null>(null);
  protected readonly adding = signal(false);
  protected readonly startingResearch = signal(false);
  protected readonly researchError = signal('');
  protected readonly readingPosting = signal(false);
  /** What a posting said, for the application being added. */
  protected readonly fromPosting = signal<Partial<Application> | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly starring = signal(false);
  protected readonly starError = signal('');

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

  protected readonly applications = computed(() =>
    this.store.applications().filter((a) => a.companyId === this.company().id),
  );

  /** Matches the loop list, which goes by the lead application. */
  protected readonly notApplied = computed(() => {
    const lead = this.applications()[0];
    return !lead || statusOf(lead) === 'none';
  });

  protected async toggleStar(): Promise<void> {
    this.starring.set(true);
    this.starError.set('');
    try {
      await this.saveChanges({ starred: !this.company().starred });
    } catch (err) {
      this.starError.set(messageOf(err, 'The star couldn’t be saved.'));
    } finally {
      this.starring.set(false);
    }
  }

  /** Many applications open on their list instead. */
  protected readonly selected = computed(() => {
    const id = this.applicationId();
    const list = this.applications();
    const picked = list.find((a) => a.id === id);
    return picked ?? (list.length >= MANY ? null : (list[0] ?? null));
  });

  protected readonly picks = computed(() => {
    const now = this.store.today();
    return this.applications().map((app) => ({
      id: app.id,
      role: app.role || 'Role not set',
      closed: app.closed,
      status: statusOf(app),
      line: glanceLine(app, now),
      segments: stageSegments(app),
    }));
  });

  /** With many, step through the open ones, or the closed ones, without the list. */
  protected readonly stepper = computed(() => {
    const app = this.selected();
    const all = this.applications();
    if (!app || all.length < MANY) {
      return null;
    }
    const kin = all.filter((other) => other.closed === app.closed);
    const at = kin.findIndex((other) => other.id === app.id);
    const near = (i: number) => {
      const other = kin[i];
      return other ? { id: other.id, role: other.role || 'Role not set' } : null;
    };
    return {
      label: `${at + 1} of ${kin.length} ${app.closed ? 'closed' : 'open'}`,
      previous: near(at - 1),
      next: near(at + 1),
    };
  });

  protected readonly about = computed(() => {
    const about = this.company().about;
    return about.charAt(0).toUpperCase() + about.slice(1);
  });

  protected readonly notes = computed(
    () => this.store.notes().filter((note) => note.companyId === this.company().id).length,
  );

  protected readonly menu = computed((): MenuItem[] => [
    { label: 'Delete company', danger: true, run: () => this.confirmingDelete.set(true) },
  ]);

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

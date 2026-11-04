import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Finding, otherEmployer, postingFindings, Review } from '../../loop/findings';
import { LoopApi } from '../../loop/loop-api';
import { LoopRuns } from '../../loop/loop-runs';
import { Application } from '../../loop/model/application';
import { Company } from '../../loop/model/company';
import { LoopRun } from '../../loop/model/loop-run';
import { RunResult } from '../../loop/model/run-result';
import { ResearchPanel } from './research-panel';

let nextId = 0;

/**
 * Asks for a posting's link, or its text when the page can't be read, then reads it for review. A
 * reading goes on when this closes, and opens here again.
 */
@Component({
  selector: 'app-posting-reader',
  imports: [FormsModule, ResearchPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    @if (reading()) {
      <app-research-panel
        [heading]="'reading the posting · ' + company().name"
        [existing]="attached()"
        [start]="start"
        [review]="review"
        [apply]="keep()"
        [applyLabel]="applyLabel()"
        failure="The posting couldn’t be read."
        (closed)="closed.emit()"
      >
        @if (!pasting()) {
          <button failed type="button" class="btn-link self-start" (click)="paste()">
            paste the posting’s text instead
          </button>
        }
      </app-research-panel>
    } @else {
      <form
        class="edit-form edit-panel"
        [attr.aria-labelledby]="id + '-heading'"
        (ngSubmit)="read()"
        (keydown.escape)="cancel()"
      >
        <div class="edit-head">
          <h3 class="edit-heading" [id]="id + '-heading'">read a posting · {{ company().name }}</h3>
        </div>
        <div class="edit-body">
          @if (pasting()) {
            <div class="field">
              <div class="field-head">
                <label [for]="id + '-text'">the posting</label>
                <span class="field-hint">its whole text, as copied</span>
              </div>
              <textarea
                class="input input-box"
                rows="8"
                name="text"
                [id]="id + '-text'"
                [(ngModel)]="text"
              ></textarea>
            </div>
            <button type="button" class="btn-link self-start" (click)="pasting.set(false)">
              use a link instead
            </button>
          } @else {
            <div class="field">
              <div class="field-head">
                <label [for]="id + '-url'">posting link</label>
                <span class="field-hint">a job board or careers page</span>
              </div>
              <input
                class="input mono"
                type="url"
                name="url"
                autocomplete="off"
                placeholder="https://boards.greenhouse.io/halcyon/4021"
                [id]="id + '-url'"
                [(ngModel)]="url"
              />
            </div>
            <button type="button" class="btn-link self-start" (click)="pasting.set(true)">
              paste its text instead
            </button>
          }
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </div>
        <div class="edit-foot">
          <span class="edit-note wide-only">you pick what to keep before anything saves</span>
          <button type="button" class="btn btn-sm" (click)="cancel()">Cancel</button>
          <button type="submit" class="btn btn-sm btn-primary">Read posting</button>
        </div>
      </form>
    }
  `,
})
export class PostingReader implements OnInit {
  private readonly api = inject(LoopApi);
  private readonly runs = inject(LoopRuns);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly company = input.required<Company>();
  /** The application to fill in; a new one when unset. */
  readonly application = input<Application | null>(null);
  /** A reading already under way or done, shown instead of the form. */
  readonly existing = input<LoopRun | null>(null);
  /** Gets the ticked findings. */
  readonly keep = input.required<(findings: Finding[]) => Promise<void>>();
  readonly applyLabel = input('Keep ticked');
  readonly closed = output<void>();

  protected readonly id = `posting-${nextId++}`;
  protected readonly pasting = signal(false);
  protected readonly reading = signal(false);
  /** The run the panel opens on; a new reading starts its own. */
  protected readonly attached = signal<LoopRun | null>(null);
  protected readonly error = signal('');
  protected url = '';
  protected text = '';
  private runId: string | null = null;

  protected readonly start = async (): Promise<LoopRun> => {
    const url = this.url.trim();
    const run = await this.runs.start(`Read the posting · ${this.company().name}`, {
      kind: 'readPosting',
      request: {
        url: /^https?:\/\/\S+$/i.test(url) ? url : '',
        text: this.pasting() ? this.text.trim() : '',
        companyId: this.company().id,
        applicationId: this.application()?.id ?? '',
      },
    });
    this.runId = run.id;
    return run;
  };

  protected readonly review = (result: RunResult): Review => {
    if (result.kind !== 'posting') {
      return { findings: [], sources: [] };
    }
    const found = result.findings;
    const company = this.company();
    return {
      findings: postingFindings(this.application(), company, found),
      sources: found.sources,
      note: otherEmployer(found.employer, company.name)
        ? `This posting is for ${found.employer}, not ${company.name}.`
        : undefined,
    };
  };

  constructor() {
    afterNextRender(() => this.focus());
  }

  ngOnInit(): void {
    this.url = this.application()?.postingUrl ?? '';
    const existing = this.existing();
    if (existing) {
      this.runId = existing.id;
      this.attached.set(existing);
      this.reading.set(true);
      // Trying again, or pasting instead, starts from what this run was given.
      this.api
        .getRun(existing.id)
        .then(({ work }) => {
          if (work?.kind === 'readPosting') {
            this.url = work.request.url;
            this.text = work.request.text;
            this.pasting.set(!work.request.url && !!work.request.text);
          }
        })
        .catch(() => undefined);
    }
  }

  protected read(): void {
    const url = this.url.trim();
    if (this.pasting() ? !this.text.trim() : !url) {
      this.error.set(this.pasting() ? 'Paste the posting’s text.' : 'Paste the posting’s link.');
      return;
    }
    if (!this.pasting() && !/^https?:\/\/\S+$/i.test(url)) {
      this.error.set('That link isn’t a web address.');
      return;
    }
    this.error.set('');
    this.attached.set(null);
    this.reading.set(true);
  }

  protected paste(): void {
    this.pasting.set(true);
    this.reading.set(false);
    setTimeout(() => this.focus());
  }

  /** Backing out of the form is done with any reading it came from. */
  protected cancel(): void {
    if (this.runId) {
      this.runs.dismiss([this.runId]).catch(() => undefined);
    }
    this.closed.emit();
  }

  private focus(): void {
    this.host.nativeElement
      .querySelector<HTMLElement>('.edit-body input, .edit-body textarea')
      ?.focus();
  }
}

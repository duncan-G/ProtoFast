import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { shortDate, toDay } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { isActive } from '../../loop/model/loop-run';
import { isEmptyResume } from '../../loop/model/resume';
import { SentFile } from '../../loop/model/sent-file';
import { SENT_FILE_KINDS, SentFileKind } from '../../loop/model/sent-file-kind';
import { Icon } from '../../shared/icon';
import { MenuItem, MoreMenu } from '../../shared/more-menu';

/** What the server takes, told by extension; it checks again. */
const ACCEPT = ['.pdf', '.docx', '.doc', '.odt', '.rtf', '.txt', '.md'];
const MAX_BYTES = 10 * 1024 * 1024;

const NOUNS: Record<SentFileKind, string> = { resume: 'resume', cv: 'CV' };
const LABELS: Record<SentFileKind, string> = { resume: 'Resume', cv: 'CV' };

/** A resume file's name supersedes the version typed before files. */
function slotChanges(kind: SentFileKind, fileId: string | null): Partial<Application> {
  return kind === 'resume' ? { resumeId: fileId, resume: '' } : { cvId: fileId };
}

interface Upload {
  kind: SentFileKind;
  fileName: string;
  fraction: number;
}

/** The resume and CV an application went with: the tailored resume, a file, or one sent before. */
@Component({
  selector: 'app-sent-files',
  imports: [FormsModule, Icon, MoreMenu, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card card-pad dim-scope', role: 'region', 'aria-label': 'What you sent' },
  template: `
    <input
      #chooser
      type="file"
      class="sr-only"
      tabindex="-1"
      aria-hidden="true"
      [accept]="accept"
      (change)="chosen($event)"
    />
    @if (picking(); as kind) {
      <div
        class="edit-form edit-panel"
        role="group"
        aria-labelledby="sent-pick-heading"
        [class.is-over]="over() === kind"
        (keydown.escape)="close()"
        (dragover)="dragOver($event, kind)"
        (dragleave)="over.set(null)"
        (drop)="dropped($event, kind)"
      >
        <div class="edit-head">
          <h4 id="sent-pick-heading" class="edit-heading">{{ nouns[kind] }} sent</h4>
          <span class="keys"
            ><span><kbd>esc</kbd> close</span></span
          >
        </div>
        <div class="edit-body">
          <button #uploadButton type="button" class="sent-drop" (click)="choose(kind)">
            <app-icon name="upload" [size]="18" />
            <span class="font-medium">Upload a new {{ nouns[kind] }}</span>
            <span class="meta">or drop it here · PDF, Word, ODT, RTF, text · up to 10 MB</span>
          </button>

          @if (choices().length) {
            <div class="flex flex-col gap-1">
              <h5 class="caption mb-1">or one you’ve sent before</h5>
              @for (file of choices(); track file.id) {
                @if (renaming() === file.id) {
                  <form
                    class="item sent-choice"
                    (ngSubmit)="rename(file)"
                    (keydown.escape)="$event.stopPropagation(); renaming.set(null)"
                  >
                    <app-icon name="file" [size]="16" />
                    <label class="sr-only" [for]="'sent-rename-' + file.id"
                      >Name for {{ file.fileName }}</label
                    >
                    <input
                      class="item-input"
                      name="name"
                      autocomplete="off"
                      maxlength="80"
                      placeholder="v4-backend"
                      [id]="'sent-rename-' + file.id"
                      [(ngModel)]="draftName"
                    />
                    <button
                      type="submit"
                      class="btn btn-xs"
                      [disabled]="busy() || !draftName.trim()"
                    >
                      Save
                    </button>
                  </form>
                } @else if (confirmingDelete() === file.id) {
                  <div class="item sent-choice">
                    <span class="flex-1 text-[13.5px] text-[var(--color-muted)]">
                      Delete {{ file.name }} for good?{{ usedBy(file) }}
                    </span>
                    <button
                      type="button"
                      class="btn btn-sm btn-danger"
                      [disabled]="busy()"
                      (click)="remove(file)"
                    >
                      Delete
                    </button>
                    <button type="button" class="btn btn-xs" (click)="confirmingDelete.set(null)">
                      Keep
                    </button>
                  </div>
                } @else {
                  <div class="item sent-choice">
                    <button
                      type="button"
                      class="row-pick min-w-0 flex-1"
                      [attr.aria-current]="file.id === attachedId(kind)"
                      [disabled]="busy()"
                      (click)="attach(kind, file.id)"
                    >
                      <app-icon name="file" [size]="16" />
                      <span class="truncate font-medium">{{ file.name }}</span>
                      <span class="meta truncate max-[559px]:hidden">{{ describe(file) }}</span>
                    </button>
                    <button type="button" class="btn btn-xs" (click)="startRename(file)">
                      Rename
                    </button>
                    <button
                      type="button"
                      class="btn btn-xs"
                      (click)="confirmingDelete.set(file.id)"
                    >
                      Delete
                    </button>
                  </div>
                }
              }
            </div>
          }
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </div>
        <div class="edit-foot">
          <button type="button" class="btn btn-sm" (click)="close()">Done</button>
        </div>
      </div>
    } @else {
      <div class="card-head">
        <h3 class="card-title">What you sent</h3>
      </div>
      @for (slot of slots(); track slot.kind) {
        <div
          class="flex flex-col gap-1.5"
          role="group"
          [attr.aria-label]="slot.label"
          (dragover)="dragOver($event, slot.kind)"
          (dragleave)="over.set(null)"
          (drop)="dropped($event, slot.kind)"
        >
          <p class="field-label">{{ slot.label }}</p>
          @if (uploading()?.kind === slot.kind) {
            <div class="slot" aria-live="polite">
              <span class="slot-mark"><span class="spinner"></span></span>
              <div class="slot-text">
                <p class="slot-name truncate">{{ uploading()?.fileName }}</p>
                <div class="sent-progress" role="progressbar" [attr.aria-valuenow]="percent()">
                  <span [style.width.%]="percent()"></span>
                </div>
              </div>
            </div>
          } @else if (slot.file; as file) {
            <div class="slot" [class.is-over]="over() === slot.kind">
              <span class="slot-mark"><app-icon name="file" [size]="18" /></span>
              <div class="slot-text">
                <p class="slot-name truncate">{{ file.name }}</p>
                <p class="meta truncate">{{ file.fileName }} · {{ size(file) }}</p>
              </div>
              <button type="button" class="btn btn-xs" (click)="open(file)">Open ↗</button>
              <app-more-menu
                small
                [label]="'More ' + slot.noun + ' actions'"
                [items]="fileMenu(slot.kind)"
              />
            </div>
          } @else if (slot.kind === 'resume' && tailoring() !== 'none') {
            @switch (tailoring()) {
              @case ('going') {
                <a
                  class="slot no-underline"
                  routerLink="/app/resume"
                  [queryParams]="{ for: app().id }"
                >
                  <span class="slot-mark is-tailored"><span class="spinner"></span></span>
                  <span class="slot-text">
                    <span class="slot-name block">Tailoring to this job</span>
                    <span class="meta block">About a minute. You can leave this page.</span>
                  </span>
                </a>
              }
              @case ('done') {
                <div class="slot is-ready">
                  <div class="slot-text">
                    <p class="slot-name flex items-center gap-2">
                      <span class="research-ready" aria-hidden="true"></span>Tailored resume ready
                    </p>
                    <p class="meta">Keep it or throw it away</p>
                  </div>
                  <a
                    class="btn btn-xs btn-primary"
                    routerLink="/app/resume"
                    [queryParams]="{ for: app().id }"
                    >Review</a
                  >
                </div>
              }
              @case ('failed') {
                <p class="alert" role="alert">
                  {{ run()?.error || 'The tailoring stopped before it finished.' }}
                </p>
                <button
                  type="button"
                  class="btn btn-xs self-start"
                  [disabled]="starting()"
                  (click)="tailor()"
                >
                  Try again
                </button>
              }
              @default {
                <div class="slot" [class.is-over]="over() === slot.kind">
                  <span class="slot-mark is-tailored"><app-icon name="resume" [size]="18" /></span>
                  <div class="slot-text">
                    <p class="slot-name">Tailored resume</p>
                    <p class="meta">Made for this role · updated {{ tailoredOn() }}</p>
                  </div>
                  <a class="btn btn-xs" routerLink="/app/resume" [queryParams]="{ for: app().id }"
                    >Open</a
                  >
                  <app-more-menu small label="More resume actions" [items]="tailoredMenu()" />
                </div>
              }
            }
          } @else if (slot.legacy) {
            <div class="slot">
              <span class="slot-mark"><app-icon name="file" [size]="18" /></span>
              <div class="slot-text">
                <p class="slot-name truncate">{{ slot.legacy }}</p>
                <p class="meta">No file kept</p>
              </div>
              <button type="button" class="btn-add" (click)="pick(slot.kind)">
                <app-icon name="upload" [size]="12" />Attach the file
              </button>
            </div>
          } @else if (slot.kind === 'resume' && !generalEmpty()) {
            <div class="slot-empty" [class.is-over]="over() === slot.kind">
              <button
                type="button"
                class="btn btn-sm btn-primary"
                [disabled]="starting()"
                (click)="tailor()"
              >
                @if (starting()) {
                  <span class="spinner h-3 w-3" aria-hidden="true"></span>
                }
                Tailor my resume to this job
              </button>
              <button type="button" class="btn btn-sm btn-raised" (click)="pick(slot.kind)">
                <app-icon name="upload" [size]="14" />Upload a file
              </button>
              <p class="hint text-center">or drop one here</p>
            </div>
          } @else {
            <button
              type="button"
              class="add-tile"
              [class.is-over]="over() === slot.kind"
              (click)="pick(slot.kind)"
            >
              <span class="add-tile-mark"><app-icon name="upload" [size]="14" /></span>
              <span class="add-tile-text"
                >Add {{ slot.kind === 'cv' ? 'a CV' : 'a resume'
                }}<small>or drop a file here</small></span
              >
            </button>
            @if (slot.kind === 'resume') {
              <p class="hint">
                Fill in <a routerLink="/app/resume">your resume</a> and it can be tailored to this
                job.
              </p>
            }
          }
        </div>
      }
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
    }
  `,
})
export class SentFiles {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly chooser = viewChild.required<ElementRef<HTMLInputElement>>('chooser');
  private readonly uploadButton = viewChild<ElementRef<HTMLButtonElement>>('uploadButton');
  private chooserKind: SentFileKind = 'resume';

  readonly application = input.required<Application>();

  protected readonly app = this.application;
  protected readonly accept = ACCEPT.join(',');
  protected readonly nouns = NOUNS;
  protected readonly picking = signal<SentFileKind | null>(null);
  protected readonly uploading = signal<Upload | null>(null);
  protected readonly over = signal<SentFileKind | null>(null);
  protected readonly renaming = signal<string | null>(null);
  protected readonly confirmingDelete = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected draftName = '';

  protected readonly slots = computed(() => {
    const app = this.application();
    const files = this.store.filesById();
    return SENT_FILE_KINDS.map((kind) => {
      const id = kind === 'resume' ? app.resumeId : app.cvId;
      return {
        kind,
        noun: NOUNS[kind],
        label: LABELS[kind],
        file: (id && files.get(id)) || null,
        legacy: kind === 'resume' ? app.resume : '',
      };
    });
  });

  protected readonly choices = computed(() => {
    const kind = this.picking();
    return this.store.files().filter((file) => file.kind === kind);
  });

  protected readonly starting = signal(false);
  protected readonly tailored = computed(
    () => this.store.tailoredResumes().get(this.application().id) ?? null,
  );
  protected readonly run = computed(() => {
    const app = this.application();
    return this.runs.find('tailorResume', { companyId: app.companyId, applicationId: app.id });
  });
  protected readonly generalEmpty = computed(() => isEmptyResume(this.store.generalResume()));

  /** A tailored resume fills the resume slot until a file is put in it. */
  protected readonly tailoring = computed(() => {
    const run = this.run();
    if (run && isActive(run)) {
      return 'going';
    }
    if (run?.state === 'succeeded') {
      return 'done';
    }
    if (run) {
      return 'failed';
    }
    return this.tailored() ? 'saved' : 'none';
  });

  protected readonly tailoredOn = computed(() => {
    const saved = this.tailored();
    return saved ? shortDate(toDay(new Date(saved.lastModified * 1000))) : '';
  });

  protected readonly tailoredMenu = computed((): MenuItem[] => [
    { label: 'Tailor again', run: () => void this.tailor() },
    { label: 'Send a file instead', run: () => this.pick('resume') },
  ]);

  protected fileMenu(kind: SentFileKind): MenuItem[] {
    return [
      { label: 'Use another file', run: () => this.pick(kind) },
      {
        label: `Take it off this application`,
        danger: true,
        run: () => void this.detach(kind),
      },
    ];
  }

  protected async tailor(): Promise<void> {
    const app = this.application();
    this.starting.set(true);
    this.error.set('');
    try {
      await this.runs.start(`Tailor · ${app.company}`, {
        kind: 'tailorResume',
        applicationId: app.id,
        instructions: '',
      });
      void this.router.navigate(['/app/resume'], { queryParams: { for: app.id } });
    } catch (err) {
      this.error.set(messageOf(err, 'The tailoring couldn’t start.'));
    } finally {
      this.starting.set(false);
    }
  }

  protected readonly percent = computed(() => Math.round((this.uploading()?.fraction ?? 0) * 100));

  protected attachedId(kind: SentFileKind): string | null {
    const app = this.application();
    return kind === 'resume' ? app.resumeId : app.cvId;
  }

  /** Straight to the file chooser when there's nothing sent before to pick from. */
  protected pick(kind: SentFileKind): void {
    this.error.set('');
    if (!this.store.files().some((file) => file.kind === kind)) {
      this.choose(kind);
      return;
    }
    this.picking.set(kind);
    afterNextRender(() => this.uploadButton()?.nativeElement.focus(), { injector: this.injector });
  }

  protected close(): void {
    this.picking.set(null);
    this.renaming.set(null);
    this.confirmingDelete.set(null);
    this.error.set('');
  }

  protected choose(kind: SentFileKind): void {
    this.chooserKind = kind;
    this.chooser().nativeElement.click();
  }

  protected chosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      void this.upload(this.chooserKind, file);
    }
  }

  protected dragOver(event: DragEvent, kind: SentFileKind): void {
    if (event.dataTransfer?.types.includes('Files')) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
      this.over.set(kind);
    }
  }

  protected dropped(event: DragEvent, kind: SentFileKind): void {
    event.preventDefault();
    this.over.set(null);
    const file = event.dataTransfer?.files[0];
    if (file) {
      void this.upload(kind, file);
    }
  }

  /** The tab opens on the click, so no popup blocker stops it while the link is signed. */
  protected async open(file: SentFile): Promise<void> {
    this.error.set('');
    const tab = window.open('', '_blank');
    try {
      const url = await this.store.openFile(file.id);
      if (tab) {
        tab.opener = null;
        tab.location.href = url;
      } else {
        window.location.assign(url);
      }
    } catch (err) {
      tab?.close();
      this.error.set(messageOf(err, 'The file could not be opened.'));
    }
  }

  protected attach(kind: SentFileKind, fileId: string): Promise<void> {
    return this.act(
      () => this.save(slotChanges(kind, fileId)),
      'The file could not be attached.',
      true,
    );
  }

  protected detach(kind: SentFileKind): Promise<void> {
    return this.act(() => this.save(slotChanges(kind, null)), 'The file could not be removed.');
  }

  protected startRename(file: SentFile): void {
    this.confirmingDelete.set(null);
    this.draftName = file.name;
    this.renaming.set(file.id);
    afterNextRender(() => document.getElementById(`sent-rename-${file.id}`)?.focus(), {
      injector: this.injector,
    });
  }

  protected rename(file: SentFile): Promise<void> {
    const name = this.draftName.trim();
    if (!name || name === file.name) {
      this.renaming.set(null);
      return Promise.resolve();
    }
    return this.act(async () => {
      await this.store.renameFile(file.id, name);
      this.renaming.set(null);
    }, 'The file could not be renamed.');
  }

  protected remove(file: SentFile): Promise<void> {
    return this.act(async () => {
      await this.store.deleteFile(file.id);
      this.confirmingDelete.set(null);
    }, 'The file could not be deleted.');
  }

  protected describe(file: SentFile): string {
    const uses = this.store.fileUses().get(file.id) ?? 0;
    const sent = uses === 1 ? 'sent once' : uses ? `sent ${uses}×` : 'not sent yet';
    return `${file.fileName} · ${shortDate(file.uploadedOn)} · ${sent}`;
  }

  protected usedBy(file: SentFile): string {
    const uses = this.store.fileUses().get(file.id) ?? 0;
    return uses ? ` It comes off ${uses === 1 ? 'one application' : `${uses} applications`}.` : '';
  }

  protected size(file: SentFile): string {
    return file.sizeBytes < 1024 * 1024
      ? `${Math.max(1, Math.round(file.sizeBytes / 1024))} KB`
      : `${(file.sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  private async upload(kind: SentFileKind, file: File): Promise<void> {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ACCEPT.includes(extension)) {
      this.error.set('Send a PDF, Word, OpenDocument, RTF, text or Markdown file.');
      return;
    }
    if (file.size === 0 || file.size > MAX_BYTES) {
      this.error.set('The file must be under 10 MB.');
      return;
    }
    if (this.uploading()) {
      return;
    }

    const app = this.application();
    const name = kind === 'resume' && !app.resumeId ? app.resume : '';
    this.close();
    this.uploading.set({ kind, fileName: file.name, fraction: 0 });
    try {
      const saved = await this.store.uploadFile(kind, file, name, (fraction) =>
        this.uploading.update((u) => u && { ...u, fraction }),
      );
      await this.save(slotChanges(kind, saved.id));
    } catch (err) {
      this.error.set(messageOf(err, err instanceof Error ? err.message : 'The upload failed.'));
    } finally {
      this.uploading.set(null);
    }
  }

  private async save(changes: Partial<Application>): Promise<void> {
    await this.store.saveApplication({ ...this.application(), ...changes });
  }

  private async act(work: () => Promise<void>, failure: string, closes = false): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await work();
      if (closes) {
        this.close();
      }
    } catch (err) {
      this.error.set(messageOf(err, failure));
    } finally {
      this.busy.set(false);
    }
  }
}

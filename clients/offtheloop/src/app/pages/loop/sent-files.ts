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
import { shortDate } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { SentFile } from '../../loop/model/sent-file';
import { SENT_FILE_KINDS, SentFileKind } from '../../loop/model/sent-file-kind';
import { Icon } from '../../shared/icon';

/** What the server takes, told by extension; it checks again. */
const ACCEPT = ['.pdf', '.docx', '.doc', '.odt', '.rtf', '.txt', '.md'];
const MAX_BYTES = 10 * 1024 * 1024;

const NOUNS: Record<SentFileKind, string> = { resume: 'resume', cv: 'CV' };

/** A resume file's name supersedes the version typed before files. */
function slotChanges(kind: SentFileKind, fileId: string | null): Partial<Application> {
  return kind === 'resume' ? { resumeId: fileId, resume: '' } : { cvId: fileId };
}

interface Upload {
  kind: SentFileKind;
  fileName: string;
  fraction: number;
}

/** The resume and CV an application went with: upload one, or pick one sent before. */
@Component({
  selector: 'app-sent-files',
  imports: [FormsModule, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'section-rule flex flex-col gap-3.5', role: 'region', 'aria-label': 'Sent' },
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
                    <button type="submit" class="btn-link" [disabled]="busy() || !draftName.trim()">
                      save
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
                    <button type="button" class="btn-link" (click)="confirmingDelete.set(null)">
                      keep
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
                    <button type="button" class="btn-link" (click)="startRename(file)">
                      rename
                    </button>
                    <button type="button" class="btn-link" (click)="confirmingDelete.set(file.id)">
                      delete
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
      <h4 class="caption">sent</h4>
      <div class="sent-grid">
        @for (slot of slots(); track slot.kind) {
          <div
            class="sent-slot"
            role="group"
            [attr.aria-label]="slot.noun"
            [class.is-over]="over() === slot.kind"
            (dragover)="dragOver($event, slot.kind)"
            (dragleave)="over.set(null)"
            (drop)="dropped($event, slot.kind)"
          >
            <span class="figure-label">{{ slot.noun }}</span>
            @if (uploading()?.kind === slot.kind) {
              <div class="sent-file" aria-live="polite">
                <span class="spinner"></span>
                <div class="min-w-0 flex-1">
                  <p class="sent-name truncate">{{ uploading()?.fileName }}</p>
                  <div class="sent-progress" role="progressbar" [attr.aria-valuenow]="percent()">
                    <span [style.width.%]="percent()"></span>
                  </div>
                </div>
              </div>
            } @else if (slot.file; as file) {
              <div class="sent-file">
                <app-icon name="file" [size]="20" [weight]="1.4" />
                <div class="min-w-0 flex-1">
                  <button
                    type="button"
                    class="sent-name sent-open"
                    [attr.aria-label]="'Open ' + file.name"
                    (click)="open(file)"
                  >
                    <span class="truncate">{{ file.name }}</span>
                    <span aria-hidden="true">↗</span>
                  </button>
                  <p class="meta m-0 truncate">{{ file.fileName }} · {{ size(file) }}</p>
                </div>
              </div>
              <div class="flex gap-4">
                <button type="button" class="btn-link" (click)="pick(slot.kind)">change</button>
                <button
                  type="button"
                  class="btn-link"
                  [disabled]="busy()"
                  (click)="detach(slot.kind)"
                >
                  remove
                </button>
              </div>
            } @else if (slot.legacy) {
              <div class="sent-file">
                <app-icon name="file" [size]="20" [weight]="1.4" />
                <div class="min-w-0 flex-1">
                  <p class="sent-name truncate">{{ slot.legacy }}</p>
                  <p class="meta m-0">no file kept</p>
                </div>
              </div>
              <button type="button" class="btn-link self-start" (click)="pick(slot.kind)">
                + attach the file
              </button>
            } @else {
              <button type="button" class="sent-empty" (click)="pick(slot.kind)">
                <app-icon name="plus" [size]="12" [weight]="1.8" />
                <span>add {{ slot.noun }}</span>
                <span class="meta ml-auto max-[719px]:hidden">or drop a file</span>
              </button>
            }
          </div>
        }
      </div>
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
    }
  `,
})
export class SentFiles {
  private readonly store = inject(LoopStore);
  private readonly injector = inject(Injector);
  private readonly chooser = viewChild.required<ElementRef<HTMLInputElement>>('chooser');
  private readonly uploadButton = viewChild<ElementRef<HTMLButtonElement>>('uploadButton');
  private chooserKind: SentFileKind = 'resume';

  readonly application = input.required<Application>();

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
        file: (id && files.get(id)) || null,
        legacy: kind === 'resume' ? app.resume : '',
      };
    });
  });

  protected readonly choices = computed(() => {
    const kind = this.picking();
    return this.store.files().filter((file) => file.kind === kind);
  });

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

import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import { RunHistory } from '../library/library';
import { Priority, Sensitivity } from '../../../lib/gen/segmentation_pb';

/**
 * Drag-and-drop → presigned PUT → SubmitRun (plan §18.3).
 *
 * The file goes straight from the browser to S3; `api` only ever sees an upload id. That is why
 * the progress bar here reports the S3 PUT rather than an RPC: the upload is the slow part, and
 * it does not touch the platform at all.
 */
@Component({
  selector: 'app-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, FormsModule],
  template: `
    <app-shell>
      <h1 class="text-2xl font-semibold tracking-tight">Upload a document</h1>
      <p class="mt-1 max-w-2xl text-sm text-[var(--color-neutral-400)]">
        Markdown in any condition — clean, converted from a PDF, or a flat transcript. If your
        converter also produced per-line layout metadata, add it: ThePlot uses font sizes, spacing
        and line widths to tell headings from body text.
      </p>

      <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div>
          <label
            class="flex cursor-pointer flex-col items-center justify-center rounded-[var(--radius-lg)] border-2 border-dashed px-6 py-14 text-center transition"
            [class]="
              dragging()
                ? 'border-[var(--color-accent)] bg-[var(--color-surface)]'
                : 'border-[var(--color-divider)] hover:border-[var(--color-neutral-600)]'
            "
            (dragover)="onDragOver($event)"
            (dragleave)="dragging.set(false)"
            (drop)="onDrop($event)"
          >
            <input
              type="file"
              class="sr-only"
              accept=".md,.markdown,.txt,text/markdown,text/plain"
              (change)="onFileChosen($event)"
            />

            @if (file(); as chosen) {
              <span class="text-base font-medium">{{ chosen.name }}</span>
              <span class="mt-1 text-sm text-[var(--color-neutral-500)]">
                {{ formatSize(chosen.size) }}
              </span>
            } @else {
              <span class="text-base font-medium">Drop a Markdown file here</span>
              <span class="mt-1 text-sm text-[var(--color-neutral-500)]">or click to choose one</span>
            }
          </label>

          <label class="mt-4 flex items-center gap-3 text-sm">
            <input
              type="file"
              class="sr-only"
              accept=".json,application/json"
              (change)="onLayoutChosen($event)"
            />
            <span class="btn btn-secondary cursor-pointer">Add layout metadata</span>
            <span class="text-[var(--color-neutral-500)]">
              {{ layout()?.name ?? 'optional — a .layout.json from your converter' }}
            </span>
          </label>

          @if (progress() !== null) {
            <div class="mt-6">
              <div class="h-1.5 overflow-hidden rounded-full bg-[var(--color-neutral-800)]">
                <div
                  class="h-full bg-[var(--color-accent)] transition-[width]"
                  [style.width.%]="(progress() ?? 0) * 100"
                ></div>
              </div>
              <p class="mt-2 text-xs text-[var(--color-neutral-500)]">
                Uploading… {{ ((progress() ?? 0) * 100).toFixed(0) }}%
              </p>
            </div>
          }

          @if (error(); as message) {
            <p
              class="mt-6 rounded-[var(--radius-md)] border border-[#7a3a3a] bg-[#2a1c1c] px-4 py-3 text-sm text-[#f0a3a3]"
              role="alert"
            >
              {{ message }}
            </p>
          }
        </div>

        <aside class="space-y-5">
          <div>
            <label for="document-id" class="block text-sm font-medium">Name</label>
            <input
              id="document-id"
              type="text"
              class="field mt-1.5"
              [(ngModel)]="documentId"
              placeholder="Taken from the filename if left blank"
            />
          </div>

          <div>
            <label for="sensitivity" class="block text-sm font-medium">Sensitivity</label>
            <select id="sensitivity" class="field mt-1.5" [(ngModel)]="sensitivity">
              <option [value]="Sensitivity.PUBLIC">Public</option>
              <option [value]="Sensitivity.INTERNAL">Internal</option>
              <option [value]="Sensitivity.CONFIDENTIAL">Confidential</option>
              <option [value]="Sensitivity.RESTRICTED">Restricted</option>
            </select>
            <p class="mt-1.5 text-xs text-[var(--color-neutral-500)]">
              Decides which model providers may see this document. Restricted documents always go
              to a person before their structure is frozen.
            </p>
          </div>

          <div>
            <label for="family" class="block text-sm font-medium">Kind of document</label>
            <select id="family" class="field mt-1.5" [(ngModel)]="familyHint">
              <option value="">Detect automatically</option>
              <option value="scanned-book">Scanned book</option>
              <option value="legal-filing">Legal filing</option>
              <option value="slide-export">Slide export</option>
              <option value="transcript">Transcript</option>
            </select>
          </div>

          <div>
            <label for="priority" class="block text-sm font-medium">Priority</label>
            <select id="priority" class="field mt-1.5" [(ngModel)]="priority">
              <option [value]="Priority.REALTIME">Now</option>
              <option [value]="Priority.BULK">Whenever — cheaper</option>
            </select>
          </div>

          <label class="flex items-start gap-2.5 text-sm">
            <input type="checkbox" class="mt-0.5" [(ngModel)]="keyPoints" />
            <span>
              Extract key points
              <span class="block text-xs text-[var(--color-neutral-500)]">
                One set of claims, terms and a question per paragraph.
              </span>
            </span>
          </label>

          <label class="flex items-start gap-2.5 text-sm">
            <input type="checkbox" class="mt-0.5" [(ngModel)]="requireReview" />
            <span>
              Review before freezing
              <span class="block text-xs text-[var(--color-neutral-500)]">
                Hold the run so you can check the structure yourself.
              </span>
            </span>
          </label>

          <button
            type="button"
            class="btn btn-primary w-full"
            [disabled]="busy() || file() === null"
            (click)="submit()"
          >
            {{ busy() ? 'Working…' : 'Segment this document' }}
          </button>
        </aside>
      </div>
    </app-shell>
  `,
  styles: `
    .field {
      display: block;
      width: 100%;
      border-radius: var(--radius-md);
      border: 1px solid var(--color-divider);
      background-color: var(--color-surface);
      padding: 0.5rem 0.75rem;
      font-size: 0.875rem;
      color: var(--color-text);
    }
    .field:focus {
      outline: 2px solid var(--color-accent);
      outline-offset: -1px;
    }
  `,
})
export class Upload {
  private readonly api = inject(SegmentationApi);
  private readonly router = inject(Router);

  protected readonly Sensitivity = Sensitivity;
  protected readonly Priority = Priority;

  protected readonly file = signal<File | null>(null);
  protected readonly layout = signal<File | null>(null);
  protected readonly dragging = signal(false);
  protected readonly busy = signal(false);
  protected readonly progress = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  protected documentId = '';
  protected sensitivity: Sensitivity = Sensitivity.INTERNAL;
  protected familyHint = '';
  protected priority: Priority = Priority.REALTIME;
  protected keyPoints = true;
  protected requireReview = false;

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);

    const dropped = event.dataTransfer?.files?.[0];
    if (dropped) {
      this.choose(dropped);
    }
  }

  protected onFileChosen(event: Event): void {
    const chosen = (event.target as HTMLInputElement).files?.[0];
    if (chosen) {
      this.choose(chosen);
    }
  }

  protected onLayoutChosen(event: Event): void {
    this.layout.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected formatSize(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${(bytes / 1024).toFixed(0)} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected async submit(): Promise<void> {
    const chosen = this.file();
    if (!chosen || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.progress.set(0);

    try {
      const uploadId = await this.api.upload(chosen, this.layout(), (fraction) =>
        this.progress.set(fraction),
      );

      const runId = await this.api.submitRun({
        uploadId,
        documentId: this.documentId.trim() || chosen.name,
        sensitivity: Number(this.sensitivity),
        familyHint: this.familyHint,
        augmentations: this.keyPoints ? ['key-points'] : [],
        priority: Number(this.priority),
        requireReview: this.requireReview,
      });

      RunHistory.add(runId);
      await this.router.navigate(['/app/runs', runId]);
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'The document could not be submitted.');
      this.progress.set(null);
    } finally {
      this.busy.set(false);
    }
  }

  private choose(file: File): void {
    this.file.set(file);
    this.error.set(null);

    if (!this.documentId) {
      this.documentId = file.name.replace(/\.(md|markdown|txt)$/i, '');
    }
  }
}

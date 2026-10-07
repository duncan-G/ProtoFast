import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import type { ArtifactRef, GetArtifactReply } from '../../lib/gen/Admin/Theplot/theplot_engine_pb';
import { errorMessage, TheplotAdminApi } from '../theplot-admin';
import { prettyJson } from './format';

const PREVIEW_BYTES = 1 << 20;

/** A side sheet showing one artifact: its contract, size and content, with copy and download. */
@Component({
  selector: 'app-artifact-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="fixed inset-0 z-40 bg-gray-900/30" (click)="closed.emit()"></div>
    <aside
      class="fixed inset-y-0 right-0 z-50 flex w-full max-w-3xl flex-col border-l border-gray-200 bg-white shadow-2xl"
      role="dialog"
      aria-labelledby="artifact-title"
    >
      <header class="flex items-start gap-3 border-b border-gray-200 px-5 py-4">
        <div class="min-w-0 flex-1">
          <p class="text-xs uppercase tracking-wide text-gray-500">Artifact</p>
          <h2
            id="artifact-title"
            class="truncate font-mono text-sm text-gray-900"
            [title]="ref().hash"
          >
            {{ ref().stageId }} · {{ ref().hash }}
          </h2>
          @if (reply(); as reply) {
            <p class="mt-1 text-xs text-gray-500">
              {{ reply.contract?.schemaId }}&#64;{{ reply.contract?.version }} ·
              {{ size(reply.size) }}
              @if (reply.truncated) {
                · showing the first {{ size(preview) }}
              }
            </p>
          }
        </div>
        <button
          type="button"
          class="rounded-lg border border-gray-300 px-2.5 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          [disabled]="!text()"
          (click)="copy()"
        >
          {{ copied() ? 'Copied' : 'Copy' }}
        </button>
        <button
          type="button"
          class="rounded-lg border border-gray-300 px-2.5 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          [disabled]="!reply()"
          (click)="download()"
        >
          Download
        </button>
        <button
          type="button"
          class="rounded-lg px-2 py-1 text-xl leading-none text-gray-500 hover:bg-gray-100"
          aria-label="Close"
          (click)="closed.emit()"
        >
          ×
        </button>
      </header>

      <div class="min-h-0 flex-1 overflow-auto bg-gray-50 p-5">
        @if (error()) {
          <p class="rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
        } @else if (!reply()) {
          <p class="text-sm text-gray-500">Loading…</p>
        } @else if (text() === null) {
          <p class="text-sm text-gray-500">Binary content; download it to inspect.</p>
        } @else {
          <pre
            class="whitespace-pre-wrap break-words font-mono text-xs leading-relaxed text-gray-900"
            >{{ text() }}</pre
          >
        }
      </div>
    </aside>
  `,
})
export class ArtifactPanel {
  private readonly api = inject(TheplotAdminApi);

  readonly ref = input.required<ArtifactRef>();
  readonly closed = output<void>();

  protected readonly preview = PREVIEW_BYTES;
  protected readonly reply = signal<GetArtifactReply | null>(null);
  protected readonly error = signal('');
  protected readonly copied = signal(false);
  /** Decoded text, pretty-printed when it is JSON; null when the bytes are not UTF-8. */
  protected readonly text = computed(() => {
    const reply = this.reply();
    if (!reply) {
      return '';
    }
    try {
      const decoded = new TextDecoder('utf-8', { fatal: true }).decode(reply.content);
      return (!reply.truncated && prettyJson(decoded)) || decoded;
    } catch {
      return null;
    }
  });

  constructor() {
    effect(() => {
      const ref = this.ref();
      this.reply.set(null);
      this.error.set('');
      this.copied.set(false);
      void this.load(ref);
    });
  }

  protected size(bytes: bigint | number): string {
    const n = Number(bytes);
    return n < 1024
      ? `${n} B`
      : n < 1 << 20
        ? `${(n / 1024).toFixed(1)} KiB`
        : `${(n / (1 << 20)).toFixed(2)} MiB`;
  }

  protected async copy(): Promise<void> {
    const text = this.text();
    if (text) {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
    }
  }

  protected download(): void {
    const reply = this.reply();
    if (!reply) {
      return;
    }
    const ref = this.ref();
    const url = URL.createObjectURL(new Blob([reply.content as BlobPart]));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${ref.runId}-${ref.stageId.replaceAll('/', '_')}-${ref.hash.slice(0, 12)}`;
    link.click();
    URL.revokeObjectURL(url);
  }

  private async load(ref: ArtifactRef): Promise<void> {
    try {
      const reply = await this.api.runs.getArtifact({ artifact: ref, maxBytes: PREVIEW_BYTES });
      if (this.ref() === ref) {
        this.reply.set(reply);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}

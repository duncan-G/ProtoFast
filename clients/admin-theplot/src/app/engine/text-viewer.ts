import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
  ViewEncapsulation,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { highlightCSharp, renderMarkdown } from './highlight';
import type { LongText, TextLanguage } from './long-text';

const LANGUAGE_LABEL: Record<TextLanguage, string> = {
  csharp: 'C#',
  markdown: 'Markdown',
  text: 'Text',
};

/** A modal showing one long tool-call argument: C# highlighted, markdown rendered. */
@Component({
  selector: 'app-text-viewer',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Rendered HTML carries no view attributes, so its styles cannot be scoped to the component.
  encapsulation: ViewEncapsulation.None,
  host: { '(document:keydown.escape)': 'closed.emit()' },
  template: `
    <div class="fixed inset-0 z-40 bg-gray-900/30" (click)="closed.emit()"></div>
    <div
      class="fixed inset-4 z-50 mx-auto flex max-w-5xl flex-col overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-2xl sm:inset-8"
      role="dialog"
      aria-modal="true"
      aria-labelledby="text-viewer-title"
    >
      <header class="flex flex-wrap items-start gap-3 border-b border-gray-200 px-5 py-4">
        <div class="min-w-0 flex-1">
          <p class="text-xs uppercase tracking-wide text-gray-500">{{ label() }}</p>
          <h2 id="text-viewer-title" class="truncate font-mono text-sm text-gray-900">
            {{ text().subject }} · {{ text().field }}
          </h2>
          <p class="mt-1 text-xs text-gray-500">
            {{ lineCount() | number }} lines · {{ text().text.length | number }} characters
          </p>
        </div>
        @if (text().language === 'markdown') {
          <div class="flex rounded-lg border border-gray-300 text-sm" role="group">
            <button
              type="button"
              class="rounded-l-lg px-2.5 py-1"
              [class]="raw() ? 'text-gray-700 hover:bg-gray-50' : 'bg-gray-900 text-white'"
              [attr.aria-pressed]="!raw()"
              (click)="raw.set(false)"
            >
              Rendered
            </button>
            <button
              type="button"
              class="rounded-r-lg px-2.5 py-1"
              [class]="raw() ? 'bg-gray-900 text-white' : 'text-gray-700 hover:bg-gray-50'"
              [attr.aria-pressed]="raw()"
              (click)="raw.set(true)"
            >
              Raw
            </button>
          </div>
        }
        <button
          type="button"
          class="rounded-lg border border-gray-300 px-2.5 py-1 text-sm text-gray-700 hover:bg-gray-50"
          (click)="copy()"
        >
          {{ copied() ? 'Copied' : 'Copy' }}
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

      <div class="min-h-0 flex-1 overflow-auto bg-gray-50">
        @if (error()) {
          <p class="m-5 rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">
            {{ error() }}
          </p>
        }
        @if (text().language === 'text' || raw() || error()) {
          <pre
            class="whitespace-pre-wrap break-words px-5 py-4 font-mono text-xs leading-relaxed text-gray-900"
            >{{ text().text }}</pre
          >
        } @else if (html() === null) {
          <p class="px-5 py-4 text-sm text-gray-500">Formatting…</p>
        } @else if (text().language === 'csharp') {
          <div class="inline-flex min-w-full font-mono text-xs leading-5">
            <pre
              class="sticky left-0 select-none border-r border-gray-200 bg-gray-100 px-3 py-4 text-right text-gray-400"
              aria-hidden="true"
              >{{ lineNumbers() }}</pre
            >
            <pre class="text-viewer-code flex-1 px-4 py-4 text-gray-900" [innerHTML]="html()"></pre>
          </div>
        } @else {
          <div class="text-viewer-md px-8 py-6 text-sm" [innerHTML]="html()"></div>
        }
      </div>
    </div>
  `,
  styles: `
    .text-viewer-md {
      color: #1f2937;
      line-height: 1.65;
      max-width: 52rem;
    }
    .text-viewer-md > * + * {
      margin-top: 0.85em;
    }
    .text-viewer-md :is(h1, h2, h3, h4) {
      color: #111827;
      font-weight: 600;
      line-height: 1.3;
      margin-top: 1.5em;
    }
    .text-viewer-md > :first-child {
      margin-top: 0;
    }
    .text-viewer-md h1 {
      font-size: 1.5em;
    }
    .text-viewer-md h2 {
      font-size: 1.25em;
      border-bottom: 1px solid #e5e7eb;
      padding-bottom: 0.3em;
    }
    .text-viewer-md h3 {
      font-size: 1.1em;
    }
    .text-viewer-md ul {
      list-style: disc;
      padding-left: 1.5em;
    }
    .text-viewer-md ol {
      list-style: decimal;
      padding-left: 1.5em;
    }
    .text-viewer-md li + li,
    .text-viewer-md li > :is(ul, ol) {
      margin-top: 0.25em;
    }
    .text-viewer-md a {
      color: #4338ca;
      text-decoration: underline;
    }
    .text-viewer-md strong {
      font-weight: 600;
      color: #111827;
    }
    .text-viewer-md :not(pre) > code {
      background: #f3f4f6;
      border-radius: 0.25rem;
      font-size: 0.875em;
      padding: 0.1em 0.35em;
    }
    .text-viewer-md pre {
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 0.5rem;
      font-size: 0.8rem;
      line-height: 1.5;
      overflow-x: auto;
      padding: 0.75rem 1rem;
    }
    .text-viewer-md blockquote {
      border-left: 3px solid #d1d5db;
      color: #4b5563;
      padding-left: 1em;
    }
    .text-viewer-md hr {
      border-color: #e5e7eb;
    }
    .text-viewer-md table {
      border-collapse: collapse;
      display: block;
      overflow-x: auto;
    }
    .text-viewer-md :is(th, td) {
      border: 1px solid #e5e7eb;
      padding: 0.35em 0.75em;
      text-align: left;
    }
    .text-viewer-md th {
      background: #f3f4f6;
      font-weight: 600;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-keyword, .hljs-built_in, .hljs-literal) {
      color: #cf222e;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-title, .hljs-title.function_) {
      color: #8250df;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-type, .hljs-title.class_) {
      color: #953800;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-string, .hljs-regexp) {
      color: #0a3069;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-number, .hljs-attr) {
      color: #0550ae;
    }
    :is(.text-viewer-code, .text-viewer-md) :is(.hljs-comment, .hljs-meta) {
      color: #6e7781;
      font-style: italic;
    }
  `,
})
export class TextViewer {
  readonly text = input.required<LongText>();
  readonly closed = output<void>();

  protected readonly raw = signal(false);
  protected readonly copied = signal(false);
  protected readonly html = signal<string | null>(null);
  protected readonly error = signal('');
  protected readonly label = computed(() => LANGUAGE_LABEL[this.text().language]);
  protected readonly lineCount = computed(() => this.text().text.split('\n').length);
  protected readonly lineNumbers = computed(() =>
    Array.from({ length: this.lineCount() }, (_, i) => i + 1).join('\n'),
  );

  constructor() {
    effect((onCleanup) => {
      const { text, language } = this.text();
      let current = true;
      onCleanup(() => (current = false));
      this.html.set(null);
      this.error.set('');
      this.copied.set(false);
      if (language === 'text') {
        return;
      }
      (language === 'csharp' ? highlightCSharp(text) : renderMarkdown(text)).then(
        (html) => current && this.html.set(html),
        () => current && this.error.set('Could not format this text; showing it as written.'),
      );
    });
  }

  protected async copy(): Promise<void> {
    await navigator.clipboard.writeText(this.text().text);
    this.copied.set(true);
  }
}

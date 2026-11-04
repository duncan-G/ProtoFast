import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  signal,
  viewChild,
} from '@angular/core';

/** A prompt for the user's own AI agent: copied in one click, or read and selected by hand. */
@Component({
  selector: 'app-agent-prompt',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-2.5' },
  styles: `
    .agent-prompt {
      max-height: 240px;
      margin: 0;
      padding: 14px 16px;
      overflow: auto;
      border: 1px solid var(--color-line-2);
      border-radius: 12px;
      background: var(--color-bg);
      color: var(--color-text-2);
      font-family: var(--font-mono);
      font-size: 12px;
      line-height: 1.55;
      white-space: pre-wrap;
    }
  `,
  template: `
    <div class="flex flex-wrap items-center gap-4">
      <button type="button" class="btn btn-sm" (click)="copy()">
        {{ copied() ? 'Copied' : 'Copy prompt' }}
      </button>
      <button
        type="button"
        class="btn-link"
        [attr.aria-expanded]="showPrompt()"
        [attr.aria-controls]="id"
        (click)="toggle()"
      >
        {{ showPrompt() ? 'hide it' : 'read it first' }}
      </button>
    </div>
    @if (copyFailed()) {
      <p class="alert m-0" role="alert">Your browser wouldn’t copy it. Select it below instead.</p>
    }
    @if (showPrompt()) {
      <pre #text class="agent-prompt" tabindex="0" [id]="id">{{ prompt() }}</pre>
    }
  `,
})
export class AgentPrompt {
  private static next = 0;
  private readonly text = viewChild<ElementRef<HTMLElement>>('text');

  readonly prompt = input.required<string>();

  protected readonly id = `agent-prompt-${AgentPrompt.next++}`;
  protected readonly copied = signal(false);
  protected readonly copyFailed = signal(false);
  protected readonly showPrompt = signal(false);

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.prompt());
      this.copyFailed.set(false);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.copyFailed.set(true);
      this.showPrompt.set(true);
      setTimeout(() => {
        const text = this.text()?.nativeElement;
        if (text) {
          getSelection()?.selectAllChildren(text);
          text.focus();
        }
      });
    }
  }

  protected toggle(): void {
    this.showPrompt.set(!this.showPrompt());
    this.copyFailed.set(false);
  }
}

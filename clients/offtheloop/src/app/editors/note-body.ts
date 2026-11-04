import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { ConnectError } from '@connectrpc/connect';
import type { AIPromptContext, AIProvider } from '@milkdown/crepe/feature/ai';
import { messageOf } from '../loop/errors';
import { LoopApi } from '../loop/loop-api';
import { LoopEditors } from '../loop/loop-editors';
import { LoopRuns } from '../loop/loop-runs';
import { LoopStore } from '../loop/loop-store';
import { isActive, LoopRun } from '../loop/model/loop-run';
import { Note } from '../loop/model/note';
import type { NotePage } from './page/note-page';
import { placeMarkers } from './page/write-markers';

/** A write to show again in a reopened note, and the note marked where its text goes. */
interface Resume {
  run: LoopRun;
  markdown: string;
  instruction: string;
}

const SAVE_AFTER_MS = 800;

let stylesheet: Promise<void> | undefined;

/** The editor's own bundle (angular.json), so only a note's page pays for it. */
function loadStylesheet(): Promise<void> {
  stylesheet ??= new Promise((resolve, reject) => {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'note-page.css';
    link.onload = () => resolve();
    link.onerror = () => reject(new Error('The editor’s styles could not be loaded.'));
    document.head.append(link);
  });
  return stylesheet;
}

/** A note's page, written in place and saved as it changes. */
@Component({
  selector: 'app-note-body',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (broken()) {
      <p class="m-0 whitespace-pre-wrap text-[var(--color-text-2)]">{{ note().body }}</p>
    }
    <div #root [class.hidden]="broken()"></div>
    @if (problem()) {
      <p class="alert mt-2" role="alert">{{ problem() }}</p>
    }
    <p class="sr-only" aria-live="polite">{{ saving() ? 'Saving' : '' }}</p>
  `,
})
export class NoteBody {
  private readonly store = inject(LoopStore);
  private readonly api = inject(LoopApi);
  private readonly runs = inject(LoopRuns);
  private readonly editors = inject(LoopEditors);
  private readonly injector = inject(Injector);
  private readonly root = viewChild.required<ElementRef<HTMLElement>>('root');

  readonly note = input.required<Note>();
  /** The note's page is ready to be seen: the editor, or its text if the editor failed. */
  readonly opened = output<string>();

  protected readonly broken = signal(false);
  protected readonly saving = signal(false);
  protected readonly problem = signal('');

  private page: NotePage | null = null;
  /** The note asked for, and the one the editor holds, which lags it while the editor loads. */
  private wanted = '';
  private shown = '';
  private saved = '';
  private opening = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private queue: Promise<void> = Promise.resolve();
  /** The run the next AI write shows instead of starting one. */
  private resuming: LoopRun | null = null;
  /** The run whose text waits to be kept or thrown away. */
  private reviewing: string | null = null;
  /** The editor is being let go, so a stopped write is the page closing, not the user. */
  private leaving = false;
  /** Settles once the AI's write has stopped watching its run. */
  private written: Promise<void> = Promise.resolve();

  constructor() {
    afterRenderEffect(() => {
      const id = this.note().id;
      if (id !== this.wanted) {
        this.wanted = id;
        untracked(() => void this.open(this.note()));
      }
    });

    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const hidden = () => document.visibilityState === 'hidden' && void this.flush();
      document.addEventListener('visibilitychange', hidden);
      destroyRef.onDestroy(() => document.removeEventListener('visibilitychange', hidden));
    });
    destroyRef.onDestroy(() => {
      this.opening++;
      void this.close();
    });
  }

  private async open(note: Note): Promise<void> {
    const opening = ++this.opening;
    await this.close();
    this.broken.set(false);
    this.problem.set('');

    let page: NotePage;
    let resume: Resume | null;
    try {
      const [{ createNotePage }, found] = await Promise.all([
        import('./page/note-page'),
        this.resumable(note),
        loadStylesheet().catch(() => undefined),
      ]);
      if (opening !== this.opening) {
        return;
      }
      resume = found;
      const root = this.root().nativeElement;
      root.replaceChildren();
      this.shown = note.id;
      page = await createNotePage({
        root,
        markdown: resume?.markdown ?? note.body,
        changed: () => this.schedule(),
        write: this.write,
        failed: (err) => this.problem.set(messageOf(err, 'The AI couldn’t finish. Try again.')),
        reviewed: () => this.reviewed(),
      });
    } catch {
      if (opening === this.opening) {
        this.broken.set(true);
        this.problem.set('The editor could not be loaded. Reload to write in this note.');
        this.opened.emit(note.id);
      }
      return;
    }
    if (opening !== this.opening) {
      await page.destroy();
      return;
    }
    this.page = page;
    if (resume) {
      this.resuming = resume.run;
      try {
        page.resume(resume.instruction);
      } catch {
        this.resuming = null;
        this.problem.set('What the AI wrote couldn’t be shown here. Ask it again.');
        void this.runs.dismiss([resume.run.id]).catch(() => undefined);
      }
    }
    // The editor writes markdown its own way; only a change from that is the user's.
    this.saved = page.markdown();
    this.opened.emit(note.id);
    if (this.editors.created() === note.id) {
      this.editors.created.set(null);
      afterNextRender(() => page.focus(), { injector: this.injector });
    }
  }

  /**
   * Saves what's left of the open note, then lets the editor go. What the AI has streamed is thrown
   * away first: its run goes on, and the note offers all of it when it reopens.
   */
  private async close(): Promise<void> {
    this.leaving = true;
    this.page?.stopWriting();
    this.leaving = false;
    // Crepe tidies its AI session after the write stops, so the editor has to outlive that.
    await this.written;
    await new Promise((resolve) => setTimeout(resolve));
    await this.flush();
    const page = this.page;
    this.page = null;
    this.reviewing = null;
    await page?.destroy();
  }

  /**
   * A write on this note that wasn't kept or thrown away, with the note marked where its text goes:
   * its own document if the note hasn't been saved since, else the note as it is now.
   */
  private async resumable(note: Note): Promise<Resume | null> {
    await this.runs.load();
    const run = this.runs.find('write', { noteId: note.id });
    if (!run) {
      return null;
    }
    try {
      const { run: now, work, text, noteChanged } = await this.api.getRun(run.id);
      if (
        work?.kind !== 'write' ||
        now.state === 'cancelled' ||
        (now.state === 'failed' && !text)
      ) {
        if (now.state === 'failed') {
          this.problem.set(now.error || 'The AI couldn’t finish. Try again.');
        }
        void this.runs.dismiss([run.id]).catch(() => undefined);
        return null;
      }
      const { document, instruction } = work.request;
      return {
        run: now,
        instruction,
        markdown: noteChanged ? placeMarkers(note.body, document) : document,
      };
    } catch {
      return null;
    }
  }

  private readonly write: AIProvider = (context, signal) => {
    const resuming = this.resuming;
    this.resuming = null;
    return this.writeRun(context, signal, resuming);
  };

  /**
   * The AI writes as a run on the server, so it finishes when the note closes part-way. Stopping it
   * in the editor stops the run; what it wrote is done with once it's kept or thrown away.
   */
  private async *writeRun(
    context: AIPromptContext,
    signal: AbortSignal,
    resuming: LoopRun | null,
  ): AsyncIterable<string> {
    this.problem.set('');
    let done = () => {};
    this.written = new Promise((resolve) => (done = resolve));
    const { id, title, kind } = this.note();
    const run =
      resuming ??
      (await this.runs.start(`Write · ${title || 'a note'}`, {
        kind: 'write',
        request: { ...context, noteId: id, title, kind },
      }));
    const watching = new AbortController();
    let stopped = false;
    const abort = () => {
      stopped = !this.leaving;
      watching.abort();
    };
    if (signal.aborted) {
      abort();
    } else {
      signal.addEventListener('abort', abort);
    }

    let ended: LoopRun | null = null;
    let wrote = false;
    try {
      for await (const update of this.runs.watch(run, watching.signal)) {
        if (update.text) {
          wrote = true;
          yield update.text;
        }
        if (update.run && !isActive(update.run)) {
          ended = update.run;
        }
      }
    } finally {
      signal.removeEventListener('abort', abort);
      if (stopped) {
        void this.runs.dismiss([run.id]).catch(() => undefined);
      }
      done();
    }
    if (watching.signal.aborted) {
      return;
    }
    // A write that failed part-way still offers what it wrote.
    if (wrote && (ended?.state === 'succeeded' || ended?.state === 'failed')) {
      if (ended.state === 'failed') {
        this.problem.set(ended.error || 'The AI stopped part-way through.');
      }
      this.reviewing = run.id;
      return;
    }
    void this.runs.dismiss([run.id]).catch(() => undefined);
    if (ended?.state !== 'succeeded') {
      throw new ConnectError(ended?.error || 'The AI couldn’t finish. Try again.');
    }
  }

  /** What the AI wrote was kept or thrown away, so its run is done with. */
  private reviewed(): void {
    const id = this.reviewing;
    this.reviewing = null;
    if (id) {
      void this.runs.dismiss([id]).catch(() => undefined);
    }
  }

  private schedule(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.flush(), SAVE_AFTER_MS);
  }

  private flush(): Promise<void> {
    clearTimeout(this.timer);
    this.queue = this.queue.then(() => this.save());
    return this.queue;
  }

  private async save(): Promise<void> {
    const page = this.page;
    if (!page) {
      return;
    }
    // What the AI wrote is saved once it's kept or thrown away.
    if (page.writing()) {
      this.schedule();
      return;
    }
    const body = page.markdown();
    const note = this.store.notes().find((n) => n.id === this.shown);
    if (body === this.saved || !note) {
      return;
    }
    this.saving.set(true);
    try {
      await this.store.saveNote({ ...note, body });
      this.saved = body;
      this.problem.set('');
    } catch (err) {
      this.problem.set(messageOf(err, 'The note could not be saved.'));
    } finally {
      this.saving.set(false);
    }
  }
}

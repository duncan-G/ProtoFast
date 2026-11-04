import { Crepe } from '@milkdown/crepe';
import {
  type AIProvider,
  type AISuggestionsBuilder,
  defaultAIIcon,
  useAIInstructionTooltipAPI,
} from '@milkdown/crepe/feature/ai';
import { setCodeMirrorTheme } from '@milkdown/crepe/feature/code-mirror';
import { commandsCtx, editorViewCtx } from '@milkdown/kit/core';
import type { Ctx } from '@milkdown/kit/ctx';
import { diffPluginKey } from '@milkdown/kit/plugin/diff';
import { streamingPluginKey } from '@milkdown/kit/plugin/streaming';
import { uploadConfig } from '@milkdown/kit/plugin/upload';
import {
  addBlockTypeCommand,
  clearTextInCurrentBlockCommand,
  codeBlockSchema,
} from '@milkdown/kit/preset/commonmark';
import { keymap } from '@milkdown/kit/prose/keymap';
import type { Node as ProseNode } from '@milkdown/kit/prose/model';
import { Plugin, Selection, TextSelection } from '@milkdown/kit/prose/state';
import { $prose } from '@milkdown/kit/utils';
import { codeTheme } from './code-theme';
import { displayMath } from './display-math';
import { drawn, mermaidPreview } from './mermaid-preview';
import { writeContext } from './write-context';
import { CURSOR, SELECTION_END, SELECTION_START } from './write-markers';

/**
 * Crepe's AI commands, by name: `@milkdown/crepe/feature/ai` is bundled apart from Crepe, so its
 * exported command keys aren't the ones the editor registered.
 */
const RUN_AI = 'RunAI';
const ABORT_AI = 'AbortAI';

/** How long a page waits for its diagrams before showing anyway, as mermaid loads on first use. */
const DRAWN_WITHIN_MS = 400;

const DIAGRAM_ICON = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="5" rx="1"/><rect x="14" y="16" width="7" height="5" rx="1"/><path d="M6.5 8v5.5h11V16"/></svg>`;

const DIAGRAM_STARTER =
  'flowchart LR\n  A[Request] --> B{Seen the key?}\n  B -- yes --> C[Stored result]\n  B -- no --> D[Run it]';

export interface NotePage {
  markdown(): string;
  /** The AI is writing, or what it wrote waits to be kept or thrown away. */
  writing(): boolean;
  /**
   * Takes the write marks out of the page and has the AI write there again, as if asked with this
   * instruction; the provider decides where the text comes from.
   */
  resume(instruction: string): void;
  /** Throws away what the AI has streamed so far, and stops reading it. */
  stopWriting(): void;
  focus(): void;
  destroy(): Promise<void>;
}

export interface NotePageOptions {
  root: HTMLElement;
  markdown: string;
  changed: (markdown: string) => void;
  write: AIProvider;
  failed: (error: unknown) => void;
  /** What the AI wrote was kept or thrown away. */
  reviewed: () => void;
}

/** The Notion-like editor a note's page is written in. Browser only. */
export async function createNotePage(options: NotePageOptions): Promise<NotePage> {
  const crepe = new Crepe({
    root: options.root,
    defaultValue: options.markdown,
    features: {
      [Crepe.Feature.ImageBlock]: false,
      [Crepe.Feature.AI]: true,
    },
    featureConfigs: {
      [Crepe.Feature.Placeholder]: {
        text: 'Write, type / for blocks, or press space to ask AI',
        mode: 'block',
      },
      [Crepe.Feature.CodeMirror]: {
        renderPreview: mermaidPreview,
        previewLabel: 'Preview',
      },
      [Crepe.Feature.BlockEdit]: {
        buildMenu: (builder) => {
          builder.addGroup('ai', 'AI').addItem('ask', {
            label: 'Ask AI',
            icon: defaultAIIcon,
            onRun: (ctx) => {
              ctx.get(commandsCtx).call(clearTextInCurrentBlockCommand.key);
              askAI(ctx);
            },
          });
          builder.getGroup('advanced').addItem('diagram', {
            label: 'Diagram',
            icon: DIAGRAM_ICON,
            onRun: (ctx) => {
              const commands = ctx.get(commandsCtx);
              const codeBlock = codeBlockSchema.type(ctx);
              commands.call(clearTextInCurrentBlockCommand.key);
              commands.call(addBlockTypeCommand.key, {
                nodeType: codeBlock.create(
                  { language: 'mermaid' },
                  ctx.get(editorViewCtx).state.schema.text(DIAGRAM_STARTER),
                ),
              });
            },
          });
        },
      },
      [Crepe.Feature.AI]: {
        provider: options.write,
        buildContext: writeContext,
        onError: (error) => options.failed(error.cause ?? error),
        instructionPlaceholder: 'Ask AI to write, explain, or draw…',
        buildAISuggestions: suggestions,
      },
    },
  });

  crepe.editor
    // Crepe's defaultsDeep merges oneDark into a theme config, corrupting lezer's tags.
    .config(setCodeMirrorTheme(codeTheme))
    .use(displayMath)
    .use(askAIKeys)
    .use(reviewEnds(options.reviewed))
    // Pasted images would be kept as this tab's blob: URLs.
    .config((ctx) =>
      ctx.update(uploadConfig.key, (prev) => ({ ...prev, uploader: async () => [] })),
    );
  crepe.on((listener) =>
    listener.markdownUpdated((_ctx, markdown, previous) => {
      if (markdown !== previous) {
        options.changed(markdown);
      }
    }),
  );

  await crepe.create();
  await Promise.race([
    diagramsInView(),
    new Promise((resolve) => setTimeout(resolve, DRAWN_WITHIN_MS)),
  ]);

  // A rendered block shows its source when clicked, until focus leaves it.
  options.root.addEventListener('click', (event) => {
    const target = event.target as Element;
    const block = target.closest<HTMLElement>('.milkdown-code-block');
    if (block && target.closest('.preview-panel')) {
      block.dataset['editing'] = '';
      block.querySelector<HTMLElement>('.cm-content')?.focus();
    }
  });
  options.root.addEventListener('focusout', (event) => {
    const block = (event.target as Element).closest<HTMLElement>('.milkdown-code-block');
    if (block && !block.contains(event.relatedTarget as Node | null)) {
      delete block.dataset['editing'];
    }
  });

  return {
    markdown: () => crepe.getMarkdown(),
    writing: () =>
      crepe.editor.action((ctx) => {
        const { state } = ctx.get(editorViewCtx);
        return !!(
          streamingPluginKey.getState(state)?.active || diffPluginKey.getState(state)?.active
        );
      }),
    resume: (instruction) =>
      crepe.editor.action((ctx) => {
        const view = ctx.get(editorViewCtx);
        const tr = view.state.tr;
        const cursor = find(tr.doc, CURSOR);
        const start = find(tr.doc, SELECTION_START);
        const end = find(tr.doc, SELECTION_END);
        let selection: Selection;
        if (start !== undefined && end !== undefined && end > start) {
          tr.delete(end, end + SELECTION_END.length).delete(start, start + SELECTION_START.length);
          selection = TextSelection.create(tr.doc, start, end - SELECTION_START.length);
        } else if (cursor !== undefined) {
          tr.delete(cursor, cursor + CURSOR.length);
          selection = TextSelection.create(tr.doc, cursor);
        } else {
          selection = Selection.atEnd(tr.doc);
        }
        view.dispatch(tr.setSelection(selection));
        ctx.get(commandsCtx).call(RUN_AI, { instruction });
      }),
    stopWriting: () =>
      crepe.editor.action((ctx) => ctx.get(commandsCtx).call(ABORT_AI, { keep: false })),
    focus: () => crepe.editor.action((ctx) => ctx.get(editorViewCtx).focus()),
    destroy: async () => {
      await crepe.destroy();
    },
  };
}

/** Where the text first occurs in the document's text nodes. */
function find(doc: ProseNode, text: string): number | undefined {
  let found: number | undefined;
  doc.descendants((node, pos) => {
    const at = found === undefined && node.isText ? (node.text ?? '').indexOf(text) : -1;
    if (at >= 0) {
      found = pos + at;
    }
    return found === undefined;
  });
  return found;
}

/** Calls back each time a diff review closes, by keeping, throwing away or retrying. */
function reviewEnds(reviewed: () => void) {
  return $prose(
    () =>
      new Plugin({
        view: () => {
          let active = false;
          return {
            update: (view) => {
              const now = !!diffPluginKey.getState(view.state)?.active;
              if (active && !now) {
                reviewed();
              }
              active = now;
            },
          };
        },
      }),
  );
}

/** A code block starts, and asks for its diagram, once an IntersectionObserver sees it. */
async function diagramsInView(): Promise<void> {
  await nextFrame();
  await nextFrame();
  await drawn();
}

function nextFrame(): Promise<number> {
  return new Promise((resolve) => requestAnimationFrame(resolve));
}

function askAI(ctx: Ctx): void {
  const { from, to } = ctx.get(editorViewCtx).state.selection;
  useAIInstructionTooltipAPI(ctx).show(from, to);
}

/** Space on an empty line, or Mod-j anywhere, asks AI, as in Notion. */
const askAIKeys = $prose((ctx) =>
  keymap({
    'Mod-j': () => {
      askAI(ctx);
      return true;
    },
    Space: (state) => {
      const { $from, empty } = state.selection;
      if (
        !empty ||
        $from.depth !== 1 ||
        $from.parent.type.name !== 'paragraph' ||
        $from.parent.content.size
      ) {
        return false;
      }
      askAI(ctx);
      return true;
    },
  }),
);

/** What an engineer preparing for interviews asks for, ahead of Crepe's rewrites. */
function suggestions(builder: AISuggestionsBuilder): void {
  const rewrites = ['improve', 'grammar', 'shorter', 'longer'].flatMap((id) => {
    const item = builder.getItem(id);
    return item ? [{ id, item }] : [];
  });
  builder
    .clear()
    .addItem('continue', {
      icon: defaultAIIcon,
      label: 'Continue writing',
      streamingLabel: 'Writing',
      prompt: 'Continue the note from where it is marked.',
    })
    .addItem('explain', {
      icon: defaultAIIcon,
      label: 'Explain it',
      streamingLabel: 'Explaining',
      prompt: 'Explain this the way I would in an interview: the idea first, then why it works.',
    })
    .addItem('example', {
      icon: defaultAIIcon,
      label: 'Add a worked example',
      streamingLabel: 'Working an example',
      prompt: 'Add a short worked example, with code where it helps.',
    })
    .addItem('complexity', {
      icon: defaultAIIcon,
      label: 'Add time and space complexity',
      streamingLabel: 'Working out the complexity',
      prompt: 'Give the time and space complexity in inline math, with a line on why.',
    })
    .addItem('diagram', {
      icon: DIAGRAM_ICON,
      label: 'Draw a diagram',
      streamingLabel: 'Drawing',
      prompt: 'Draw this as a mermaid diagram.',
    });
  for (const { id, item } of rewrites) {
    builder.addItem(id, item);
  }
}

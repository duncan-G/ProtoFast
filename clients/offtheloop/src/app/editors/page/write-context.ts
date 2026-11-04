import type { AIPromptContext } from '@milkdown/crepe/feature/ai';
import { editorViewCtx, serializerCtx } from '@milkdown/kit/core';
import type { Ctx } from '@milkdown/kit/ctx';
import { CURSOR, SELECTION_END, SELECTION_START } from './write-markers';

/** The note as markdown with the cursor or the selection marked, so the model knows where it writes. */
export function writeContext(ctx: Ctx, instruction: string): AIPromptContext {
  const { state } = ctx.get(editorViewCtx);
  const serialize = ctx.get(serializerCtx);
  const { from, to, empty } = state.selection;

  const marked = state.tr;
  if (empty) {
    marked.insertText(CURSOR, from);
  } else {
    marked.insertText(SELECTION_END, to).insertText(SELECTION_START, from);
  }
  const selection = empty ? '' : serialize(state.doc.cut(from, to)).trim();
  return { document: serialize(marked.doc), selection, instruction };
}

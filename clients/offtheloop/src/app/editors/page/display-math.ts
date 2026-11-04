import { inputRulesCtx, SchemaReady } from '@milkdown/kit/core';
import type { MilkdownPlugin } from '@milkdown/kit/ctx';
import { codeBlockSchema } from '@milkdown/kit/preset/commonmark';
import { InputRule } from '@milkdown/kit/prose/inputrules';

/**
 * A paragraph typed as `$$x$$` becomes a display math block. The rules go ahead of Crepe's, whose
 * inline rule would otherwise turn the `$x$` of `$$x$` into inline math.
 */
export const displayMath: MilkdownPlugin = (ctx) => async () => {
  await ctx.wait(SchemaReady);

  const open = new InputRule(/(?:^|[^$])\$\$[^$\s][^$]*\$$/, (state, _match, _start, end) =>
    state.tr.insertText('$', end),
  );
  const close = new InputRule(/^\$\$([^$]+)\$\$$/, (state, match, start, end) => {
    const $start = state.doc.resolve(start);
    if ($start.parent.type.name !== 'paragraph' || end !== $start.end()) {
      return null;
    }
    const block = codeBlockSchema
      .type(ctx)
      .create({ language: 'LaTeX' }, state.schema.text(match[1].trim()));
    return state.tr.replaceWith($start.before(), $start.after(), block);
  });

  ctx.update(inputRulesCtx, (rules) => [open, close, ...rules]);
  return () => ctx.update(inputRulesCtx, (rules) => rules.filter((r) => r !== open && r !== close));
};

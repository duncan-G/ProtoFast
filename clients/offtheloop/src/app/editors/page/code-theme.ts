import { HighlightStyle, language, syntaxHighlighting } from '@codemirror/language';
import {
  Decoration,
  type DecorationSet,
  EditorView,
  MatchDecorator,
  ViewPlugin,
  type ViewUpdate,
} from '@codemirror/view';
import { tags } from '@lezer/highlight';

const KEYWORDS = new MatchDecorator({
  regexp:
    /\b(?:def|return|if|elif|else|for|while|in|not|and|or|is|None|True|False|class|import|from|as|with|try|except|finally|raise|lambda|yield|pass|break|continue|function|const|let|var|new|this|null|undefined|true|false|switch|case|default|do|typeof|instanceof|async|await|public|private|static|void|func|go|defer|struct|interface|type|package|nil|fn|match|mut|impl|self)\b/g,
  decoration: Decoration.mark({ class: 'cm-plain-keyword' }),
});

/** A fence without a language, as every migrated note's code is, still gets its keywords coloured. */
const plainKeywords = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet;

    constructor(view: EditorView) {
      this.decorations = view.state.facet(language) ? Decoration.none : KEYWORDS.createDeco(view);
    }

    update(update: ViewUpdate) {
      if (update.state.facet(language)) {
        this.decorations = Decoration.none;
      } else if (update.startState.facet(language)) {
        this.decorations = KEYWORDS.createDeco(update.view);
      } else {
        this.decorations = KEYWORDS.updateDeco(update, this.decorations);
      }
    }
  },
  { decorations: (plugin) => plugin.decorations },
);

/** Code on the note's panel: the body's grey, with each language's tokens in the Quiet code colours. */
export const codeTheme = [
  EditorView.theme(
    {
      '&': { color: 'var(--color-text-2)', backgroundColor: 'transparent' },
      '.cm-content': { padding: '0', caretColor: 'var(--color-accent)' },
      '.cm-line': { padding: '0' },
      '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--color-accent)' },
      '.cm-activeLine': { backgroundColor: 'transparent' },
      '.cm-gutters': { display: 'none' },
      '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': {
        backgroundColor: 'color-mix(in srgb, var(--color-accent), transparent 78%)',
      },
      '.cm-matchingBracket': {
        backgroundColor: 'transparent',
        outline: '1px solid var(--color-edge-2)',
      },
      '.cm-plain-keyword': { color: 'var(--color-keyword)' },
    },
    { dark: true },
  ),
  syntaxHighlighting(
    HighlightStyle.define([
      {
        tag: [
          tags.keyword,
          tags.controlKeyword,
          tags.definitionKeyword,
          tags.moduleKeyword,
          tags.operatorKeyword,
          tags.modifier,
          tags.self,
          tags.tagName,
        ],
        color: 'var(--color-keyword)',
      },
      {
        tag: [tags.string, tags.special(tags.string), tags.character, tags.inserted],
        color: 'var(--color-code-string)',
      },
      {
        tag: [
          tags.number,
          tags.bool,
          tags.null,
          tags.atom,
          tags.regexp,
          tags.escape,
          tags.constant(tags.variableName),
          tags.standard(tags.variableName),
        ],
        color: 'var(--color-code-number)',
      },
      {
        tag: [
          tags.function(tags.variableName),
          tags.function(tags.propertyName),
          tags.macroName,
          tags.attributeName,
        ],
        color: 'var(--color-code-function)',
      },
      {
        tag: [tags.typeName, tags.className, tags.namespace, tags.labelName],
        color: 'var(--color-code-type)',
      },
      {
        tag: [tags.propertyName, tags.heading, tags.strong],
        color: 'var(--color-text)',
      },
      {
        tag: [tags.comment, tags.meta, tags.processingInstruction],
        color: 'var(--color-code-comment)',
      },
      { tag: [tags.operator, tags.punctuation], color: 'var(--color-muted)' },
      { tag: [tags.link, tags.url], color: 'var(--color-keyword)', textDecoration: 'underline' },
      { tag: [tags.invalid, tags.deleted], color: 'var(--color-danger)' },
      { tag: tags.emphasis, fontStyle: 'italic' },
    ]),
  ),
  plainKeywords,
];

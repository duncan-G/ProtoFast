import type { HLJSApi } from 'highlight.js';
import type { Marked } from 'marked';

// Imported on first use so neither library weighs on the console's initial bundle.
let loaded: Promise<{ hljs: HLJSApi; marked: Marked }> | undefined;

function load() {
  loaded ??= (async () => {
    const [{ default: hljs }, { default: csharp }, { default: json }, { Marked }] =
      await Promise.all([
        import('highlight.js/lib/core'),
        import('highlight.js/lib/languages/csharp'),
        import('highlight.js/lib/languages/json'),
        import('marked'),
      ]);
    hljs.registerLanguage('csharp', csharp);
    hljs.registerLanguage('json', json);
    const marked = new Marked({
      gfm: true,
      renderer: {
        code({ text, lang }) {
          const language = lang && hljs.getLanguage(lang) ? lang : null;
          const body = language ? hljs.highlight(text, { language }).value : escape(text);
          return `<pre><code class="hljs">${body}</code></pre>\n`;
        },
      },
    });
    return { hljs, marked };
  })();
  return loaded;
}

/** Highlighted HTML; callers bind it with `[innerHTML]` so Angular sanitizes it. */
export async function highlightCSharp(source: string): Promise<string> {
  const { hljs } = await load();
  return hljs.highlight(source, { language: 'csharp' }).value;
}

/** Rendered HTML; callers bind it with `[innerHTML]` so Angular sanitizes it. */
export async function renderMarkdown(text: string): Promise<string> {
  const { marked } = await load();
  return marked.parse(text, { async: false });
}

function escape(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

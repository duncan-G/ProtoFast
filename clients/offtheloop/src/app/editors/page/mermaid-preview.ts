import type { Mermaid } from 'mermaid';

let mermaid: Promise<Mermaid> | undefined;
/** Mermaid keeps one render in flight at a time, and a block's latest text must land last. */
let queue: Promise<unknown> = Promise.resolve();
let renders = 0;
let scratch: HTMLElement | undefined;

/** Where mermaid draws a diagram to measure it; by default it's the body, which grows meanwhile. */
function scratchpad(): HTMLElement {
  if (!scratch?.isConnected) {
    scratch = document.createElement('div');
    scratch.setAttribute('aria-hidden', 'true');
    scratch.style.cssText =
      'position: fixed; inset: 0; overflow: hidden; visibility: hidden; pointer-events: none;';
    document.body.append(scratch);
  }
  return scratch;
}

function load(): Promise<Mermaid> {
  mermaid ??= import('mermaid').then(({ default: m }) => {
    const styles = getComputedStyle(document.documentElement);
    const token = (name: string) => styles.getPropertyValue(name).trim();
    m.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'base',
      fontFamily: token('--font-sans'),
      themeVariables: {
        darkMode: true,
        background: token('--color-panel'),
        primaryColor: token('--color-raise'),
        primaryBorderColor: token('--color-edge-2'),
        primaryTextColor: token('--color-text'),
        secondaryColor: token('--color-line-2'),
        tertiaryColor: token('--color-panel'),
        lineColor: token('--color-dim'),
        edgeLabelBackground: token('--color-bg'),
        textColor: token('--color-text-2'),
        noteBkgColor: token('--color-line-2'),
        noteTextColor: token('--color-text-2'),
        noteBorderColor: token('--color-edge'),
      },
    });
    return m;
  });
  return mermaid;
}

/** Settles once every diagram asked for so far is drawn. */
export function drawn(): Promise<unknown> {
  return queue;
}

/** A code block's preview: the diagram for ```mermaid, nothing for other languages. */
export function mermaidPreview(
  language: string,
  content: string,
  apply: (value: null | string | HTMLElement) => void,
): void | null {
  if (language.toLowerCase() !== 'mermaid' || !content.trim()) {
    return null;
  }
  const id = `note-diagram-${++renders}`;
  queue = queue.then(async () => {
    const host = scratchpad();
    try {
      const m = await load();
      const { svg } = await m.render(id, content, host);
      apply(svg);
    } catch (err) {
      host.replaceChildren();
      const problem = document.createElement('p');
      problem.className = 'note-diagram-error';
      problem.textContent = err instanceof Error ? err.message : String(err);
      apply(problem);
    }
  });
}

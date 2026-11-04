/** Agents often wrap the JSON in a code fence or a sentence; the outermost braces are the file. */
export function parseAgentJson(text: string): { value: unknown } | { error: string } {
  const trimmed = text.trim();
  if (!trimmed) {
    return { error: 'It’s empty.' };
  }
  const fenced = /```(?:json)?\s*\n([\s\S]*?)```/.exec(trimmed)?.[1];
  const start = trimmed.search(/[[{]/);
  const end = Math.max(trimmed.lastIndexOf('}'), trimmed.lastIndexOf(']'));
  const candidates = [trimmed, fenced, start >= 0 ? trimmed.slice(start, end + 1) : undefined];
  let error = '';
  for (const candidate of candidates) {
    if (candidate === undefined) {
      continue;
    }
    try {
      return { value: JSON.parse(candidate) };
    } catch (err) {
      error ||= err instanceof Error ? err.message : String(err);
    }
  }
  return { error: `That isn’t JSON: ${error}` };
}

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

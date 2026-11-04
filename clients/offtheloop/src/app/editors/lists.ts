/** Splits typed text on a separator, dropping blank entries. */
export function splitList(text: string, separator: string): string[] {
  return text
    .split(separator)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

export function lines(items: string[]): string {
  return items.join('\n');
}

const KB = 1024;
const MB = KB * 1024;

/** "2.4 MB", "312 KB", "80 B" — one decimal for megabytes, none below. */
export function formatBytes(bytes: number): string {
  if (bytes >= MB) {
    return `${(bytes / MB).toFixed(1)} MB`;
  }
  if (bytes >= KB) {
    return `${Math.round(bytes / KB)} KB`;
  }
  return `${bytes} B`;
}

/** The whole-megabyte figure a limit is quoted as: 10 MB, not 10.0 MB. */
export function formatLimit(bytes: number): string {
  return `${Math.round(bytes / MB)} MB`;
}

/** The extension after the last dot, lowercased, with the dot — or '' when there is none. */
export function extensionOf(fileName: string): string {
  const base = fileName.split(/[\\/]/).pop() ?? '';
  const dot = base.lastIndexOf('.');
  return dot > 0 && dot < base.length - 1 ? base.slice(dot).toLowerCase() : '';
}

/** "DOCX" for the little file glyph; "FILE" when the name has no extension. */
export function extensionLabel(fileName: string): string {
  const ext = extensionOf(fileName).slice(1).toUpperCase();
  return ext || 'FILE';
}

/**
 * The title the desk shows for a file, the same way the API derives it: the extension dropped,
 * dashes and underscores read as spaces, each word capitalised.
 */
export function titleFromFileName(fileName: string): string {
  const base = fileName.split(/[\\/]/).pop() ?? fileName;
  const ext = extensionOf(base);
  const stem = ext ? base.slice(0, -ext.length) : base;
  const words = stem
    .split(/[\s\-_.]+/)
    .filter((word) => word.length > 0)
    .map((word) => word[0].toUpperCase() + word.slice(1));
  return words.length > 0 ? words.join(' ') : fileName.trim();
}

/** "Just now", "5 min ago", "3 h ago", "2 d ago", then a short date. */
export function relativeTime(date: Date, now: Date = new Date()): string {
  const seconds = Math.max(0, Math.round((now.getTime() - date.getTime()) / 1000));
  if (seconds < 45) {
    return 'Just now';
  }
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours} h ago`;
  }
  const days = Math.round(hours / 24);
  if (days < 7) {
    return `${days} d ago`;
  }
  const sameYear = date.getFullYear() === now.getFullYear();
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    ...(sameYear ? {} : { year: 'numeric' }),
  });
}

/**
 * A stable hue for a document's placeholder cover, so the same document keeps the same stripe
 * between renders and sessions without anyone having to store a colour.
 */
export function coverHue(key: string): number {
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = (hash * 31 + key.charCodeAt(i)) | 0;
  }
  return Math.abs(hash) % 360;
}

export function coverStripe(key: string): string {
  const hue = coverHue(key);
  return `repeating-linear-gradient(135deg, oklch(0.27 0.025 ${hue}) 0 7px, oklch(0.23 0.02 ${hue}) 7px 14px)`;
}

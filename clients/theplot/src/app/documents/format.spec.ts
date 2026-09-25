import {
  coverHue,
  extensionLabel,
  extensionOf,
  formatBytes,
  formatLimit,
  relativeTime,
  titleFromFileName,
} from './format';

describe('formatBytes', () => {
  it('quotes megabytes to one decimal and smaller sizes whole', () => {
    expect(formatBytes(2.4 * 1024 * 1024)).toBe('2.4 MB');
    expect(formatBytes(312 * 1024)).toBe('312 KB');
    expect(formatBytes(80)).toBe('80 B');
  });

  it('quotes a limit as whole megabytes', () => {
    expect(formatLimit(10 * 1024 * 1024)).toBe('10 MB');
  });
});

describe('extensionOf', () => {
  it('lowercases the last extension and keeps the dot', () => {
    expect(extensionOf('The Quiet Year.DOCX')).toBe('.docx');
    expect(extensionOf('archive.tar.gz')).toBe('.gz');
  });

  it('only looks at the last path segment and tolerates no extension', () => {
    expect(extensionOf('C:\\drafts\\notes.md')).toBe('.md');
    expect(extensionOf('README')).toBe('');
    expect(extensionOf('.gitignore')).toBe('');
    expect(extensionLabel('README')).toBe('FILE');
    expect(extensionLabel('notes.pages')).toBe('PAGES');
  });
});

describe('titleFromFileName', () => {
  it('drops the extension, reads dashes and underscores as spaces, and capitalises', () => {
    expect(titleFromFileName('the-quiet_year.docx')).toBe('The Quiet Year');
    expect(titleFromFileName('  harbor notes.md ')).toBe('Harbor Notes');
  });

  it('falls back to the file name when nothing is left', () => {
    expect(titleFromFileName('---.txt')).toBe('---.txt');
  });
});

describe('relativeTime', () => {
  const now = new Date('2026-09-24T12:00:00Z');

  it('steps from just now through minutes, hours and days', () => {
    expect(relativeTime(new Date('2026-09-24T11:59:40Z'), now)).toBe('Just now');
    expect(relativeTime(new Date('2026-09-24T11:55:00Z'), now)).toBe('5 min ago');
    expect(relativeTime(new Date('2026-09-24T09:00:00Z'), now)).toBe('3 h ago');
    expect(relativeTime(new Date('2026-09-22T12:00:00Z'), now)).toBe('2 d ago');
  });

  it('falls back to a short date after a week', () => {
    expect(relativeTime(new Date('2026-09-01T12:00:00Z'), now)).toMatch(/Sep/);
  });
});

describe('coverHue', () => {
  it('is stable for an id and within the hue circle', () => {
    const hue = coverHue('01j8x4m2c9k7p1q3r5s7t9v1w3');
    expect(hue).toBe(coverHue('01j8x4m2c9k7p1q3r5s7t9v1w3'));
    expect(hue).toBeGreaterThanOrEqual(0);
    expect(hue).toBeLessThan(360);
  });
});

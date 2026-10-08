import { ImportProgress } from './document-api';
import { describeCost, describeImport, isImportActive } from './import-progress';

function progress(overrides: Partial<ImportProgress>): ImportProgress {
  return {
    uploadId: 'u',
    state: 'queued',
    stage: '',
    message: '',
    storyId: null,
    costUsd: 0,
    ...overrides,
  };
}

describe('describeCost', () => {
  it('says nothing before the first model call', () => {
    expect(describeCost(progress({ state: 'reading' }))).toBeNull();
  });

  it('is a running total while the import works and a sum once it stops', () => {
    expect(describeCost(progress({ state: 'analysing', costUsd: 0.4167 }))).toBe('$0.42 so far');
    expect(describeCost(progress({ state: 'done', costUsd: 1.2 }))).toBe('$1.20 spent');
    expect(describeCost(progress({ state: 'failed', costUsd: 0.004 }))).toBe('<$0.01 spent');
  });
});

describe('describeImport', () => {
  it('numbers each screenplay stage as a step of the import', () => {
    expect(describeImport(progress({ state: 'reading' }))).toEqual({
      label: 'Reading the file',
      step: 1,
    });
    expect(describeImport(progress({ state: 'analysing', stage: 'scenes' }))).toEqual({
      label: 'Laying out the scenes',
      step: 3,
    });
    expect(describeImport(progress({ state: 'saving', stage: 'mentions' })).step).toBe(5);
    expect(describeImport(progress({ state: 'saving' })).step).toBe(6);
  });

  it('falls back to a general label for a stage it does not know', () => {
    expect(describeImport(progress({ state: 'analysing', stage: 'translate' })).label).toBe(
      'Working through the story',
    );
  });

  it('counts only done and failed as finished', () => {
    expect(isImportActive(progress({ state: 'retrying' }))).toBe(true);
    expect(isImportActive(progress({ state: 'failed' }))).toBe(false);
    expect(isImportActive(progress({ state: 'done' }))).toBe(false);
  });
});

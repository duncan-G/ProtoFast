import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ImportTray } from './import-tray';
import { ImportJob } from '../../documents/document-import';
import { DocumentSummary, ImportProgress } from '../../documents/document-api';

const DOCUMENT_ID = '01j8x4m2c9k7p1q3r5s7t9v1w3';

function progress(overrides: Partial<ImportProgress>): ImportProgress {
  return {
    uploadId: DOCUMENT_ID,
    state: 'queued',
    stage: '',
    message: '',
    storyId: null,
    costUsd: 0,
    ...overrides,
  };
}

function uploaded(): ImportJob {
  const file = new File(['x'], 'The_Tide_Clock.pdf');
  const document: DocumentSummary = {
    id: DOCUMENT_ID,
    name: 'The Tide Clock',
    fileName: file.name,
    sizeBytes: 97 * 1024,
    mediaType: 'application/pdf',
    fileExtension: '.pdf',
    createdAt: new Date(),
    lastModifiedAt: new Date(),
    import: progress({}),
  };
  return {
    id: 1,
    file,
    fileName: file.name,
    sizeBytes: file.size,
    extension: 'PDF',
    phase: 'uploaded',
    progress: 100,
    loadedBytes: file.size,
    failedAt: null,
    error: null,
    document,
    startedAt: new Date(),
  };
}

describe('ImportTray', () => {
  let fixture: ComponentFixture<ImportTray>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ImportTray] }).compileComponents();
    fixture = TestBed.createComponent(ImportTray);
    fixture.componentRef.setInput('jobs', [uploaded()]);
    fixture.componentRef.setInput('expanded', true);
  });

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const withProgress = (p: ImportProgress) =>
    fixture.componentRef.setInput('progress', new Map([[p.uploadId, p]]));

  it('keeps an uploaded file importing until the server says the story exists', async () => {
    withProgress(progress({ state: 'analysing', stage: 'scenes', costUsd: 0.4167 }));
    await fixture.whenStable();
    expect(text()).toContain('Laying out the scenes · 3 of 5');
    expect(text()).toContain('$0.42 so far');
    expect(text()).toContain('Importing 1 file');
    expect(text()).not.toContain('Open');
    expect(text()).not.toContain('ready');
  });

  it('offers Open once the import is done', async () => {
    withProgress(progress({ state: 'done', storyId: 'story-1', costUsd: 1.2 }));
    await fixture.whenStable();
    expect(text()).toContain('Ready · in Write');
    expect(text()).toContain('$1.20 spent');
    expect(text()).toContain('1 import ready');
    expect(text()).toContain('Open');
  });

  it('shows why the server gave up on a file', async () => {
    withProgress(progress({ state: 'failed', message: 'This file couldn’t be read.' }));
    await fixture.whenStable();
    expect(text()).toContain('Couldn’t import');
    expect(text()).toContain('This file couldn’t be read.');
    expect(text()).toContain('1 import needs attention');
  });
});

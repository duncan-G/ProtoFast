import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ImportDialog } from './import-dialog';
import { ImportJob } from '../../documents/document-import';
import { AcceptedFormats } from '../../documents/document-api';

const FORMATS: AcceptedFormats = {
  formats: [
    { extension: '.docx', mediaType: 'application/x-docx', label: 'Word' },
    { extension: '.md', mediaType: 'text/markdown', label: 'Markdown' },
  ],
  maxBytes: 10 * 1024 * 1024,
  accept: '.docx,.md,application/x-docx,text/markdown',
  labels: ['Word', 'Markdown'],
};

function job(overrides: Partial<ImportJob>): ImportJob {
  const file = new File(['x'], 'the-quiet-year.docx');
  return {
    id: 1,
    file,
    fileName: file.name,
    sizeBytes: 2.4 * 1024 * 1024,
    extension: 'DOCX',
    phase: 'presign',
    progress: 0,
    loadedBytes: 0,
    failedAt: null,
    error: null,
    document: null,
    startedAt: new Date(),
    ...overrides,
  };
}

describe('ImportDialog', () => {
  let fixture: ComponentFixture<ImportDialog>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ImportDialog] }).compileComponents();
    fixture = TestBed.createComponent(ImportDialog);
  });

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('opens on the drop zone with the accepted types and the limit', async () => {
    fixture.componentRef.setInput('formats', FORMATS);
    await fixture.whenStable();
    expect(text()).toContain('Drop a file here');
    expect(text()).toContain('Word');
    expect(text()).toContain('Up to 10 MB');
  });

  it('shows a picked file as checked and offers to start', async () => {
    fixture.componentRef.setInput('formats', FORMATS);
    fixture.componentRef.setInput('draft', {
      file: new File([new Uint8Array(1024)], 'the-quiet-year.docx'),
      rejection: null,
    });
    await fixture.whenStable();
    expect(text()).toContain('the-quiet-year.docx');
    expect(text()).toContain('Supported format');
    expect(text()).toContain('Start import');
  });

  it('names the rejection and offers another file', async () => {
    fixture.componentRef.setInput('formats', FORMATS);
    fixture.componentRef.setInput('draft', {
      file: new File([new Uint8Array(1024)], 'notes.pages'),
      rejection: 'type',
    });
    await fixture.whenStable();
    expect(text()).toContain('This file type isn’t supported');
    expect(text()).toContain('accepted: Word, Markdown');
    expect(text()).toContain('Choose another file');
  });

  it('draws the upload progress with the bytes so far', async () => {
    fixture.componentRef.setInput(
      'job',
      job({ phase: 'uploading', progress: 64, loadedBytes: 1.5 * 1024 * 1024 }),
    );
    await fixture.whenStable();
    expect(text()).toContain('1.5 MB of 2.4 MB');
    expect(text()).toContain('64%');
    expect(text()).toContain('Continue in background');
    const bar = fixture.nativeElement.querySelector('.progress-bar') as HTMLElement;
    expect(bar.style.width).toBe('64%');
  });

  it('marks every step done and names the document once it is on the desk', async () => {
    fixture.componentRef.setInput(
      'job',
      job({
        phase: 'done',
        progress: 100,
        document: {
          id: '01j8x4m2c9k7p1q3r5s7t9v1w3',
          name: 'The Quiet Year',
          fileName: 'the-quiet-year.docx',
          sizeBytes: 2.4 * 1024 * 1024,
          mediaType: 'application/x-docx',
          fileExtension: '.docx',
          createdAt: new Date(),
          lastModifiedAt: new Date(),
        },
      }),
    );
    await fixture.whenStable();
    expect(text()).toContain('On your desk');
    expect(text()).toContain('“The Quiet Year” is uploaded');
    expect(fixture.nativeElement.querySelectorAll('.step.is-done').length).toBe(3);
    expect(text()).toContain('Continue working');
  });

  it('shows why an import failed and offers a retry', async () => {
    fixture.componentRef.setInput(
      'job',
      job({ phase: 'failed', failedAt: 'uploading', error: 'Storage refused the upload (HTTP 403).' }),
    );
    await fixture.whenStable();
    expect(text()).toContain('The import didn’t finish');
    expect(text()).toContain('Storage refused the upload (HTTP 403).');
    expect(text()).toContain('Try again');
  });
});

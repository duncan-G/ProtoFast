import { create } from '@bufbuild/protobuf';
import { ListSourceFormatsReplySchema, SourceFormatSchema } from '../../../lib/gen/segmentation_pb';
import { acceptAttribute, familiesLabel, stripExtension } from './upload';

/**
 * The upload page's accept list is the server's, not the page's (ingest plan C9, criterion 7).
 *
 * Every case below starts from a real `ListSourceFormatsReply` rather than a hand-written array,
 * so the assertion is against the wire shape `api` actually sends — which is the only way this
 * test can catch the drift it exists to catch.
 */
describe('upload', () => {
  const reply = create(ListSourceFormatsReplySchema, {
    maxBytes: 10n * 1024n * 1024n,
    formats: [
      create(SourceFormatSchema, {
        extension: '.md',
        mediaType: 'text/markdown',
        label: 'Markdown',
      }),
      create(SourceFormatSchema, {
        extension: '.pdf',
        mediaType: 'application/pdf',
        label: 'PDF',
        producesLayout: true,
        ocrCapable: true,
      }),
      create(SourceFormatSchema, {
        extension: '.jpg',
        mediaType: 'image/jpeg',
        label: 'JPEG image',
        producesLayout: true,
        ocrCapable: true,
      }),
      create(SourceFormatSchema, {
        extension: '.jpeg',
        mediaType: 'image/jpeg',
        label: 'JPEG image',
        producesLayout: true,
        ocrCapable: true,
      }),
    ],
  });

  it('builds the accept attribute from the reply and nothing else', () => {
    const accept = acceptAttribute(reply.formats).split(',');

    expect(accept).toContain('.pdf');
    expect(accept).toContain('application/pdf');

    // A format the server did not advertise must never be offered: the page would be promising an
    // upload CreateUpload would refuse.
    expect(accept).not.toContain('.zip');
    expect(accept).not.toContain('.mp3');
  });

  it('offers a media type shared by two extensions only once', () => {
    const accept = acceptAttribute(reply.formats).split(',');

    // .jpg and .jpeg are both image/jpeg; a repeated entry is not wrong but it is noise in a
    // string the browser shows in its own file dialog.
    expect(accept.filter((entry) => entry === 'image/jpeg')).toHaveLength(1);
    expect(accept).toContain('.jpg');
    expect(accept).toContain('.jpeg');
  });

  it('accepts nothing until the reply arrives', () => {
    // Better an empty accept for a moment than one written here that outlives a server change.
    expect(acceptAttribute([])).toBe('');
  });

  it('names the families and the cap from the same reply the policy is signed with', () => {
    const label = familiesLabel(reply.formats, reply.maxBytes);

    expect(label).toContain('PDF');
    expect(label).toContain('up to 10 MB');
    // De-duplicated: ".jpg" and ".jpeg" are one family to a reader.
    expect(label.match(/JPEG image/g)).toHaveLength(1);
  });

  it('falls back to a plain prompt when the server list is unavailable', () => {
    expect(familiesLabel([], 0n)).toBe('or click to choose one');
  });

  it('derives the document name by stripping an advertised extension', () => {
    expect(stripExtension('annual-report.pdf', reply.formats)).toBe('annual-report');
    expect(stripExtension('ANNUAL-REPORT.PDF', reply.formats)).toBe('ANNUAL-REPORT');

    // An extension the server does not know is left alone rather than guessed at.
    expect(stripExtension('archive.tar.gz', reply.formats)).toBe('archive.tar.gz');
    expect(stripExtension('no-extension', reply.formats)).toBe('no-extension');
  });
});

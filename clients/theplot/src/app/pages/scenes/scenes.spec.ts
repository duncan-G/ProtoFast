import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { create } from '@bufbuild/protobuf';
import type { MessageInitShape } from '@bufbuild/protobuf';
import {
  SceneItemSchema,
  SceneResultSchema,
  type SceneResult,
} from '../../../lib/gen/segmentation_pb';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import { routes } from '../../app.routes';
import { ScenesPage } from './scenes';

/**
 * The scene page rendered, rather than its helpers called.
 *
 * What this catches that `scenes.spec.ts` cannot: the template. A scene's coordinates, its cast and
 * its items are the whole point of the page, and a binding that silently renders nothing is the
 * failure mode a view-model test cannot see.
 */
type SceneItemInit = MessageInitShape<typeof SceneItemSchema>;

const defaultItems: SceneItemInit[] = [
  {
    itemId: 'it_1',
    kind: 'speech',
    text: '“You came back,”',
    speech: { speakerName: 'Mara', addressee: 'in_scene', voiced: true },
  },
  { itemId: 'it_2', kind: 'action', text: 'she said from the doorway.' },
];

describe('ScenesPage', () => {
  /** A one-scene document, with the scene's items swappable so each case states its own. */
  const novel = (items: SceneItemInit[] = defaultItems): SceneResult =>
    create(SceneResultSchema, {
      runId: 'run_1',
      treeHash: 'abcdef0123456789',
      totalScenes: 1,
      root: {
        sectionId: 'sec_root',
        title: 'The Novel',
        children: [{ sectionId: 'sec_ch', title: 'Chapter One' }],
      },
      personas: [
        { personaId: 'pe_mara', canonicalName: 'Mara', kind: 'individual', scope: 'persistent' },
        {
          personaId: 'pe_crowd',
          canonicalName: 'the neighbours',
          kind: 'group',
          scope: 'persistent',
        },
      ],
      scenes: [
        {
          sceneId: 'sc_1',
          sectionId: 'sec_ch',
          ordinal: 0,
          situation: {
            placeId: 'pl_1',
            placeName: 'The kitchen',
            settingSource: 'inherited',
            timeAnchor: 'the next morning',
            timeRelation: 'gap',
            mode: 'enacted',
            subject: 'a return',
            cast: [{ personaId: 'pe_mara', name: 'Mara', role: 'speaking' }],
          },
          items,
          flags: [{ kind: 'oversized-scene', message: 'nine paragraphs' }],
        },
      ],
    });

  async function render(result: SceneResult) {
    TestBed.configureTestingModule({
      imports: [ScenesPage],
      providers: [
        provideRouter(routes),
        { provide: SegmentationApi, useValue: { getScenes: () => Promise.resolve(result) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ runId: 'run_1' }) } },
        },
      ],
    });

    const fixture = TestBed.createComponent(ScenesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('renders a scene with its coordinates, its cast and its items', async () => {
    const page = await render(novel());
    const text = page.textContent ?? '';

    // The heading: an untitled scene is numbered by its document-wide ordinal.
    expect(text).toContain('Scene 1');
    expect(text).toContain('Chapter One');

    // Every coordinate, and the fact that the setting was carried rather than stated (C7).
    expect(text).toContain('The kitchen');
    expect(text).toContain('(carried)');
    expect(text).toContain('the next morning · after a gap');
    expect(text).toContain('Mara');
    expect(text).toContain('(speaking)');
    expect(text).toContain('a return');
    expect(text).toContain('enacted');

    // The derived review signal, which is a signal and not a gate — so it shows without blocking.
    expect(text).toContain('oversized-scene');

    // Both items, in the order the scene names them.
    const items = [...page.querySelectorAll('[data-item-id]')].map((el) =>
      el.getAttribute('data-item-id'),
    );
    expect(items).toEqual(['it_1', 'it_2']);
    expect(text).toContain('“You came back,”');
    expect(text).toContain('she said from the doorway.');

    // The cast list in the margin, including what kind of persona a group is.
    expect(text).toContain('the neighbours');
    expect(text).toContain('group');
  });

  it('marks staged text as ThePlot’s rather than the document’s', async () => {
    const page = await render(
      novel([{ itemId: 'it_1', kind: 'action', text: '— he said', renderText: 'Tom spoke.' }]),
    );

    const text = page.textContent ?? '';

    // §3.6: the staging text is shown, the span it replaced is not, and the reader is told which
    // they are looking at.
    expect(text).toContain('Tom spoke.');
    expect(text).not.toContain('— he said');
    expect(text).toContain('staged');
  });

  it('explains an empty scene stream instead of showing an empty page', async () => {
    const page = await render(
      create(SceneResultSchema, {
        runId: 'run_1',
        treeHash: 'abcdef0123456789',
        root: { sectionId: 'sec_root', title: 'The Novel' },
      }),
    );

    const text = page.textContent ?? '';

    expect(text).toContain('This run has no scenes.');
    // The two reasons need different answers, so the copy names both.
    expect(text).toContain('before the scene phases existed');
    expect(text).toContain('nothing displayable to cut');
  });

  it('reports a failure to load rather than sitting on “Loading…”', async () => {
    TestBed.configureTestingModule({
      imports: [ScenesPage],
      providers: [
        provideRouter(routes),
        {
          provide: SegmentationApi,
          useValue: {
            getScenes: () => Promise.reject(new Error('This run has not published a result yet.')),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ runId: 'run_1' }) } },
        },
      ],
    });

    const fixture = TestBed.createComponent(ScenesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('has not published a result yet');
  });
});

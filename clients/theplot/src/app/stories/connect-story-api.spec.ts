import { TestBed } from '@angular/core/testing';
import { Code, ConnectError, createRouterTransport, type ServiceImpl } from '@connectrpc/connect';
import {
  AvatarShape,
  LocationSetting,
  SceneElementType,
  Stories,
  type SaveSceneRequest,
  type SaveVocabularyRequest,
  type UpdateLocationRequest,
} from '../../lib/gen/story_pb';
import { describeError } from '../documents/document-api';
import { GRPC_TRANSPORT } from '../grpc-transport';
import { ConnectStoryApi } from './connect-story-api';
import { Scene } from './model/scene';

function apiWith(impl: Partial<ServiceImpl<typeof Stories>>): ConnectStoryApi {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: GRPC_TRANSPORT,
        useValue: createRouterTransport(({ service }) => service(Stories, impl)),
      },
    ],
  });
  return TestBed.inject(ConnectStoryApi);
}

describe('ConnectStoryApi', () => {
  it('maps a story’s outline, library and vocabulary, leaving unset fields null', async () => {
    const api = apiWith({
      getStory: ({ storyId }) => ({
        story: {
          id: storyId,
          title: 'The Signal',
          vocabulary: {
            timesOfDay: ['GOLDEN HOUR'],
            characterKinds: [{ label: 'Ghost', avatarShape: AvatarShape.STAR }],
          },
          containers: [
            {
              id: 'act',
              storyId,
              position: 0,
              label: 'Act I',
              scenes: [
                {
                  id: 'scene',
                  containerId: 'act',
                  position: 0,
                  title: 'Three Winters',
                  openingLocationId: 'docks',
                },
              ],
            },
          ],
          characters: [{ id: 'mara', storyId, name: 'Mara', kind: 'Human', hue: 25 }],
          locations: [
            { id: 'docks', storyId, name: 'Docks', setting: LocationSetting.EXTERIOR, hue: 200 },
          ],
          props: [{ id: 'radio', storyId, name: 'Radio' }],
        },
      }),
    });

    expect(await api.getStory('story')).toEqual({
      id: 'story',
      title: 'The Signal',
      vocabulary: {
        timesOfDay: ['GOLDEN HOUR'],
        transitions: [],
        characterKinds: [{ label: 'Ghost', avatarShape: 'Star' }],
      },
      containers: [
        {
          id: 'act',
          storyId: 'story',
          position: 0,
          label: 'Act I',
          scenes: [
            {
              id: 'scene',
              containerId: 'act',
              position: 0,
              title: 'Three Winters',
              openingLocationId: 'docks',
              openingTimeOfDay: null,
            },
          ],
        },
      ],
      characters: [{ id: 'mara', storyId: 'story', name: 'Mara', kind: 'Human', hue: 25 }],
      locations: [{ id: 'docks', storyId: 'story', name: 'Docks', setting: 'Exterior', hue: 200 }],
      props: [{ id: 'radio', storyId: 'story', name: 'Radio' }],
    });
  });

  it('maps a scene’s rows and turns each mention’s target into its id field', async () => {
    const api = apiWith({
      getScene: ({ sceneId }) => ({
        scene: {
          id: sceneId,
          containerId: 'act',
          position: 2,
          title: 'Bolt Wakes',
          elements: [
            {
              id: 'h',
              sceneId,
              position: 0,
              type: SceneElementType.HEADING,
              timeOfDay: 'NIGHT',
            },
            {
              id: 'a',
              sceneId,
              position: 1,
              type: SceneElementType.ACTION,
              text: '@Bolt finds the @Map.',
              mentions: [
                { id: 'm1', target: { case: 'characterId', value: 'bolt' }, offset: 0, length: 5 },
                { id: 'm2', target: { case: 'propId', value: 'map' }, offset: 16, length: 4 },
              ],
            },
          ],
        },
      }),
    });

    const scene = await api.getScene('scene');

    expect(scene.elements[0]).toEqual({
      id: 'h',
      sceneId: 'scene',
      position: 0,
      type: 'Heading',
      text: null,
      locationId: null,
      timeOfDay: 'NIGHT',
      speakerId: null,
      parenthetical: null,
      transition: null,
      mentions: [],
    });
    expect(scene.elements[1].type).toBe('Action');
    expect(scene.elements[1].mentions).toEqual([
      { id: 'm1', characterId: 'bolt', locationId: null, propId: null, offset: 0, length: 5 },
      { id: 'm2', characterId: null, locationId: null, propId: 'map', offset: 16, length: 4 },
    ]);
  });

  it('sends a scene with positions in row order, enums, unset nulls and mention targets', async () => {
    let sent: SaveSceneRequest | undefined;
    const api = apiWith({
      saveScene: (request) => {
        sent = request;
        return {};
      },
    });
    const scene: Scene = {
      id: 'scene',
      containerId: 'act',
      position: 0,
      title: 'Bolt Wakes',
      elements: [
        {
          id: 'd',
          sceneId: 'scene',
          position: 7,
          type: 'Dialogue',
          text: 'Hi, @Mara.',
          locationId: null,
          timeOfDay: null,
          speakerId: 'bolt',
          parenthetical: 'beat',
          transition: null,
          mentions: [
            {
              id: 'm',
              characterId: null,
              locationId: 'docks',
              propId: null,
              offset: 4,
              length: 5,
            },
          ],
        },
        {
          id: 't',
          sceneId: 'scene',
          position: 3,
          type: 'Transition',
          text: null,
          locationId: null,
          timeOfDay: null,
          speakerId: null,
          parenthetical: null,
          transition: 'CUT TO',
          mentions: [],
        },
      ],
    };

    await api.saveScene(scene);

    const [dialogue, transition] = sent!.scene!.elements;
    expect(dialogue.position).toBe(0);
    expect(dialogue.type).toBe(SceneElementType.DIALOGUE);
    expect(dialogue.speakerId).toBe('bolt');
    expect(dialogue.locationId).toBeUndefined();
    expect(dialogue.mentions[0].target).toEqual({ case: 'locationId', value: 'docks' });
    expect(transition.position).toBe(1);
    expect(transition.type).toBe(SceneElementType.TRANSITION);
    expect(transition.text).toBeUndefined();
    expect(transition.transition).toBe('CUT TO');
  });

  it('sends library changes and vocabulary with proto enums', async () => {
    let location: UpdateLocationRequest | undefined;
    let vocabulary: SaveVocabularyRequest | undefined;
    const api = apiWith({
      updateLocation: (request) => {
        location = request;
        return {};
      },
      saveVocabulary: (request) => {
        vocabulary = request;
        return {};
      },
    });

    await api.updateLocation({
      id: 'docks',
      storyId: 'story',
      name: 'Harbor Docks',
      setting: 'Interior',
      hue: 45,
    });
    await api.saveVocabulary('story', {
      timesOfDay: ['GOLDEN HOUR'],
      transitions: [],
      characterKinds: [{ label: 'Ghost', avatarShape: 'Shield' }],
    });

    expect(location).toMatchObject({
      locationId: 'docks',
      name: 'Harbor Docks',
      setting: LocationSetting.INTERIOR,
      hue: 45,
    });
    expect(vocabulary?.storyId).toBe('story');
    expect(vocabulary?.vocabulary?.timesOfDay).toEqual(['GOLDEN HOUR']);
    expect(vocabulary?.vocabulary?.characterKinds[0]).toMatchObject({
      label: 'Ghost',
      avatarShape: AvatarShape.SHIELD,
    });
  });

  it('lists stories with their unix-second timestamps as dates', async () => {
    const api = apiWith({
      listStories: () => ({
        stories: [
          { id: 's', title: 'The Signal', createdUnixSeconds: 1n, lastModifiedUnixSeconds: 90n },
        ],
      }),
    });

    expect(await api.listStories()).toEqual([
      { id: 's', title: 'The Signal', createdAt: new Date(1000), lastModifiedAt: new Date(90000) },
    ]);
  });

  it('lets a name clash reach the writer verbatim', async () => {
    const api = apiWith({
      createCharacter: () => {
        throw new ConnectError('There’s already a character called “Mara”.', Code.AlreadyExists);
      },
    });

    const error = await api
      .createCharacter('story', { name: 'mara', kind: 'Human', hue: 1 })
      .catch((e: unknown) => e);

    expect(describeError(error, 'fallback')).toBe('There’s already a character called “Mara”.');
  });

  it('refuses a row type it does not know', async () => {
    const api = apiWith({
      getScene: () => ({
        scene: { id: 's', elements: [{ id: 'x', type: SceneElementType.UNSPECIFIED }] },
      }),
    });

    await expect(api.getScene('s')).rejects.toThrow('The API sent an unknown row type.');
  });
});

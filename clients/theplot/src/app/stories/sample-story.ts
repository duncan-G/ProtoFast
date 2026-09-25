import { linkTypedReferences } from './mentions';
import { Character } from './model/character';
import { Location } from './model/location';
import { LocationSetting } from './model/location-setting';
import { Prop } from './model/prop';
import { Referable } from './model/referable';
import { Scene } from './model/scene';
import { SceneElement } from './model/scene-element';
import { SceneElementType } from './model/scene-element-type';
import { Story } from './model/story';

export const SAMPLE_STORY_ID = '5e0d0000-0000-4000-8000-000000000001';

const ACT_ONE = '5e0d0000-0000-4000-8000-0000000000a1';
const ACT_TWO = '5e0d0000-0000-4000-8000-0000000000a2';
const HUES = [25, 255, 145, 85, 315, 200, 350, 110];

let counter = 0;
const seedId = (prefix: string) =>
  `5e0d0000-0000-4000-${prefix}-${(++counter).toString(16).padStart(12, '0')}`;

const characters: Character[] = (
  [
    ['Mara', 'Human', 25],
    ['Bolt', 'Robot', 255],
    ['Old Fen', 'Human', 145],
    ['Pigeon', 'Animal', 85],
    ...(
      [
        ['Juno', 'Human'],
        ['Tess', 'Human'],
        ['Unit K-9', 'Robot'],
        ['Marlow', 'Human'],
        ['Sprocket', 'Robot'],
        ['Aunt Ro', 'Human'],
        ['The Ferryman', 'Creature'],
        ['Ivo', 'Human'],
        ['Moth', 'Creature'],
        ['Captain Reyes', 'Human'],
        ['Dot', 'Robot'],
        ['Hollis', 'Human'],
        ['Radio Voice', 'Voice'],
        ['Nell', 'Human'],
        ['Grit', 'Animal'],
        ['Officer Park', 'Human'],
        ['Loom', 'Creature'],
        ['Sami', 'Human'],
        ['Beacon', 'Robot'],
        ['Widow Ash', 'Human'],
      ] as const
    ).map(([name, kind], j) => [name, kind, HUES[(j + 4) % 8]]),
  ] as [string, string, number][]
).map(([name, kind, hue]) => ({
  id: seedId('8c00'),
  storyId: SAMPLE_STORY_ID,
  name,
  kind,
  hue,
}));

const locations: Location[] = (
  [
    ['Exterior', 'Junkyard — Scrap Hill', 45],
    ['Interior', "Fen's Workshop", 200],
    ['Exterior', 'Water Tower Rooftop', 315],
    ...(
      [
        ['Exterior', 'Harbor Docks'],
        ['Interior', 'Mara’s Bedroom'],
        ['Interior', 'Night Market'],
        ['Exterior', 'Overpass'],
        ['Interior', 'Subway Car'],
        ['Exterior', 'Radio Mast Ridge'],
        ['Interior', 'Abandoned Cinema'],
        ['Exterior', 'Salt Flats'],
        ['Interior', 'Fen’s Kitchen'],
        ['Exterior', 'Scrapyard Gate'],
        ['Interior', 'Server Vault'],
        ['Exterior', 'Canal Bridge'],
        ['Interior', 'Noodle Stall'],
        ['Exterior', 'Crane Graveyard'],
        ['Interior', 'School Corridor'],
        ['Exterior', 'Lighthouse'],
        ['Interior', 'Train Depot'],
        ['Exterior', 'Pigeon Loft'],
        ['Interior', 'Repair Bay 7'],
        ['Exterior', 'Storm Drain'],
        ['Interior', 'Control Room'],
        ['Exterior', 'Old Highway'],
        ['Interior', 'Clock Tower Stairs'],
        ['Exterior', 'Beach at Low Tide'],
        ['Interior', 'Greenhouse'],
        ['Exterior', 'City Wall'],
        ['Interior', 'Elevator Shaft'],
      ] as const
    ).map(([setting, name], k) => [setting, name, HUES[k % 8]]),
  ] as [LocationSetting, string, number][]
).map(([setting, name, hue]) => ({
  id: seedId('8100'),
  storyId: SAMPLE_STORY_ID,
  name,
  setting,
  hue,
}));

const props: Prop[] = ['Radio', 'Brass Key', 'Map'].map((name) => ({
  id: seedId('8900'),
  storyId: SAMPLE_STORY_ID,
  name,
}));

const referables: Referable[] = [
  ...characters.map((c) => ({
    kind: 'character' as const,
    id: c.id,
    name: c.name,
  })),
  ...locations.map((l) => ({
    kind: 'location' as const,
    id: l.id,
    name: l.name,
  })),
  ...props.map((p) => ({ kind: 'prop' as const, id: p.id, name: p.name })),
];

const character = (name: string) => characters.find((c) => c.name === name)!.id;
const location = (name: string) => locations.find((l) => l.name === name)!.id;

type Row =
  | ['heading', string, string]
  | ['action' | 'description' | 'narration', string]
  | ['dialogue', string, string, string]
  | ['transition', string];

const TYPES: Record<Row[0], SceneElementType> = {
  heading: 'Heading',
  action: 'Action',
  description: 'Description',
  narration: 'Narration',
  dialogue: 'Dialogue',
  transition: 'Transition',
};

function element(sceneId: string, row: Row, position: number): SceneElement {
  const base: SceneElement = {
    id: seedId('8e00'),
    sceneId,
    position,
    type: TYPES[row[0]],
    text: null,
    locationId: null,
    timeOfDay: null,
    speakerId: null,
    parenthetical: null,
    transition: null,
    mentions: [],
  };
  switch (row[0]) {
    case 'heading':
      return { ...base, locationId: location(row[1]), timeOfDay: row[2] };
    case 'transition':
      return { ...base, transition: row[1] };
    case 'dialogue':
      return {
        ...withText(base, row[3]),
        speakerId: character(row[1]),
        parenthetical: row[2] || null,
      };
    default:
      return withText(base, row[1]);
  }
}

function withText(base: SceneElement, text: string): SceneElement {
  const linked = linkTypedReferences({ text, mentions: [] }, referables, 0, text.length);
  return { ...base, text, mentions: linked.mentions };
}

function scene(containerId: string, position: number, title: string, rows: Row[]): Scene {
  const id = seedId('8500');
  return {
    id,
    containerId,
    position,
    title,
    elements: rows.map((row, i) => element(id, row, i)),
  };
}

const scenes: Scene[] = [
  scene(ACT_ONE, 0, 'Three Winters', [
    ['heading', 'Mara’s Bedroom', 'NIGHT'],
    ['narration', 'Every night for three winters, @Mara listened to static.'],
    ['action', 'She turns the dial of the @Radio a hair to the left. Nothing.'],
  ]),
  scene(ACT_ONE, 1, 'The Rumor', [
    ['heading', 'Night Market', 'DAY'],
    [
      'dialogue',
      'Juno',
      '',
      'They say something big is buried on Scrap Hill. Something that talks back.',
    ],
  ]),
  scene(ACT_ONE, 2, 'Signal Lost', [
    ['heading', 'Junkyard — Scrap Hill', 'DUSK'],
    [
      'action',
      '@Mara climbs toward the summit as the light fails. Far off, the @Water Tower Rooftop blinks red.',
    ],
  ]),
  scene(ACT_ONE, 3, 'Bolt Wakes', [
    ['heading', 'Junkyard — Scrap Hill', 'NIGHT'],
    [
      'description',
      'Mountains of rusted cars under a sodium-orange sky. Rain ticks on sheet metal.',
    ],
    [
      'action',
      '@Mara climbs a heap of washing machines, the @Radio strapped to her back, crackling.',
    ],
    ['dialogue', 'Mara', 'whispering', 'Come on… one bar. Just one.'],
    [
      'action',
      'A hatch in the heap bursts open. @Bolt unfolds — seven feet of dented chrome, one eye flickering.',
    ],
    ['dialogue', 'Bolt', '', 'SIGNAL DETECTED. YOU ARE… VERY LOUD.'],
    ['dialogue', 'Pigeon', 'from a hubcap', 'He’s not wrong.'],
    [
      'narration',
      'She had spent three winters listening for that voice. She hadn’t expected it to be rude.',
    ],
    ['transition', 'CUT TO'],
    ['heading', "Fen's Workshop", 'CONTINUOUS'],
    ['action', '@Old Fen turns the @Brass Key over in his fingers, not looking up.'],
    ['dialogue', 'Old Fen', '', 'You brought it here? To my shop?'],
    ['dialogue', 'Bolt', 'beat', 'I DO NOT FIT THROUGH THE DOOR.'],
    ['transition', 'DISSOLVE TO'],
    ['heading', 'Water Tower Rooftop', 'DAWN'],
    [
      'description',
      'The @Map is pinned under a brick, corners lifting in the wind. The whole city hums below.',
    ],
    ['action', '@Mara flattens the @Map. @Bolt crouches beside her, servos whining.'],
    [
      'dialogue',
      'Radio Voice',
      'through the radio, crackling',
      '…anyone on this frequency… the Night Market closes at midnight…',
    ],
    ['dialogue', 'Mara', '', 'That’s it. That’s the signal.'],
    ['transition', 'SMASH CUT TO'],
    ['heading', 'Night Market', 'NIGHT'],
    [
      'description',
      'Lanterns strung between shipping containers. Steam, bartering, a hundred radios playing a hundred stations.',
    ],
    [
      'action',
      '@Juno and @Tess run a stall of salvaged circuit boards. @Sprocket, a waist-high robot, sorts screws by sound.',
    ],
    ['dialogue', 'Juno', '', 'Tess. Look who walked in with a seven-foot antique.'],
    ['dialogue', 'Tess', 'under her breath', 'Don’t stare. Robots that big don’t belong to kids.'],
    ['dialogue', 'Sprocket', '', 'SCREW. SCREW. WASHER. SCREW.'],
    [
      'action',
      '@Captain Reyes pushes through the crowd, flanked by @Unit K-9, its red visor sweeping the stalls.',
    ],
    ['dialogue', 'Captain Reyes', '', 'Salvage license, please. For the big one.'],
    [
      'dialogue',
      'Unit K-9',
      'scanning Bolt',
      'UNREGISTERED. ORIGIN: UNKNOWN. THREAT: …UNDETERMINED.',
    ],
    ['dialogue', 'Bolt', '', 'I AM MOSTLY HARMLESS.'],
    [
      'dialogue',
      'Aunt Ro',
      'from behind a noodle pot',
      'He’s with me, Captain. My nephew’s science project.',
    ],
    ['dialogue', 'Captain Reyes', '', 'Your nephew is a teenage girl, Ro.'],
    ['dialogue', 'Aunt Ro', '', 'She’s very gifted.'],
    [
      'narration',
      'Everyone in the market knew @Aunt Ro lied for a living. Nobody had ever caught her doing it.',
    ],
    [
      'action',
      '@Marlow, a boy with a stack of newspapers, presses a second @Brass Key into @Mara’s palm — same teeth, different color.',
    ],
    ['dialogue', 'Marlow', 'whispering', 'The Ferryman wants to see you. Tonight.'],
    ['dialogue', 'Mara', '', 'Who’s the Ferryman?'],
    [
      'action',
      '@Moth, a pale winged thing, drifts down from the lanterns and settles on @Bolt’s shoulder.',
    ],
    ['dialogue', 'Moth', '', 'Wrong question. Ask what he wants.'],
    ['dialogue', 'Dot', 'hovering', 'BEEP. That means “follow me.” I’m told it’s obvious.'],
    ['transition', 'DISSOLVE TO'],
    ['heading', 'Harbor Docks', 'NIGHT'],
    [
      'description',
      'Black water, a single lantern on a flat-bottomed boat. Fog rolls off the bay.',
    ],
    [
      'action',
      '@The Ferryman leans on a long pole, face hidden under a wide hood. Beside the boat stands @Widow Ash, holding an umbrella though it isn’t raining.',
    ],
    [
      'dialogue',
      'The Ferryman',
      '',
      'Two keys. One crossing. You’ll have to leave the metal one behind.',
    ],
    ['dialogue', 'Bolt', '', 'I OBJECT.'],
    ['dialogue', 'Widow Ash', 'dry', 'Everyone objects. Then they get in the boat.'],
    ['transition', 'FADE OUT'],
  ]),
  scene(ACT_ONE, 4, 'The Crossing', [
    ['heading', 'Harbor Docks', 'NIGHT'],
    ['description', 'The boat slides into fog. The shore disappears.'],
  ]),
  scene(ACT_TWO, 0, 'The Far Shore', [
    ['heading', 'Salt Flats', 'DAWN'],
    [
      'action',
      '@Mara steps off the boat onto white salt. Behind her, the fog closes over the @Harbor Docks like a door.',
    ],
  ]),
];

export function sampleStory(): Story {
  return structuredClone({
    id: SAMPLE_STORY_ID,
    title: 'The Signal in the Scrap',
    vocabulary: { timesOfDay: [], transitions: [], characterKinds: [] },
    containers: [
      {
        id: ACT_ONE,
        storyId: SAMPLE_STORY_ID,
        position: 0,
        label: 'Act I',
        scenes: [],
      },
      {
        id: ACT_TWO,
        storyId: SAMPLE_STORY_ID,
        position: 1,
        label: 'Act II',
        scenes: [],
      },
    ],
    characters,
    locations,
    props,
  });
}

export function sampleScenes(): Scene[] {
  return structuredClone(scenes);
}

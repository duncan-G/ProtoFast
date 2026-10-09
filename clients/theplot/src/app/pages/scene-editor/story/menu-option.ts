import { AvatarShape } from '../../../stories/model/avatar-shape';

export interface MenuOption {
  key: string;
  label: string;
  sub: string;
  look: 'glyph' | 'avatar' | 'location' | 'prop' | 'new';
  glyph: string;
  hue: number | null;
  shape: AvatarShape;
  run: () => void;
}

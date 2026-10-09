import { BlockFocus } from './block-focus';

export interface FocusRequest extends BlockFocus {
  caret: 'start' | 'end' | 'all' | number;
}

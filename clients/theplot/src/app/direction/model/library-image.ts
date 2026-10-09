import { LibraryTarget } from '../../stories/model/library-target';
import { MediaImage } from './media-image';

/** A character's, location's, prop's or extra's picture, which frames are kept consistent with. */
export interface LibraryImage {
  target: LibraryTarget;
  image: MediaImage;
}

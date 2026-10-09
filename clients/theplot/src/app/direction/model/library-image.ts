import { ReferenceTarget } from '../../stories/model/reference-target';
import { MediaImage } from './media-image';

/** A character's, location's or prop's picture, which frames are kept consistent with. */
export interface LibraryImage {
  target: ReferenceTarget;
  image: MediaImage;
}

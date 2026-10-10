import { MediaImage } from './media-image';
import { PictureTarget } from './picture-target';

/** A library entry's or location variation's picture, which frames are kept consistent with. */
export interface LibraryImage {
  target: PictureTarget;
  image: MediaImage;
}

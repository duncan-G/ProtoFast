import { LibraryTarget } from '../../stories/model/library-target';

/** Anything the library pictures: an entry, or a part of a location, drawn from the location's picture. */
export type PictureTarget = LibraryTarget | { kind: 'locationVariation'; id: string };

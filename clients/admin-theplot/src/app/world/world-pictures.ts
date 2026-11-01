import { inject, Injectable, type OnDestroy, signal, untracked } from '@angular/core';
import type {
  WorldView,
  WorldViewPicture,
} from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { errorMessage, TheplotAdminApi } from '../theplot-admin';
import { viewKey } from './world';
import { WorldSession } from './world-session';

export interface ViewState {
  picturing?: boolean;
  error?: string;
}

/** The pictures of the world's views: those made since the world was read, and each one's bytes as a blob URL. */
@Injectable()
export class WorldPictures implements OnDestroy {
  private readonly api = inject(TheplotAdminApi);
  private readonly session = inject(WorldSession);
  private readonly made = signal<ReadonlyMap<string, WorldViewPicture>>(new Map());
  private readonly states = signal<ReadonlyMap<string, ViewState>>(new Map());
  private readonly urls = signal<ReadonlyMap<string, string>>(new Map());
  private readonly loading = new Set<string>();

  pictureOf(view: WorldView): WorldViewPicture | undefined {
    return this.made().get(viewKey(view)) ?? view.picture;
  }

  stateOf(view: WorldView): ViewState {
    return this.states().get(viewKey(view)) ?? {};
  }

  urlOf(picture: WorldViewPicture): string | undefined {
    return this.urls().get(urlKey(picture));
  }

  /** Fetches the picture's bytes once; a failed fetch is tried again the next time it is asked for. */
  async load(picture: WorldViewPicture): Promise<void> {
    const key = urlKey(picture);
    const storyId = untracked(() => this.session.world()?.reply.storyId);
    if (!storyId || this.loading.has(key) || untracked(() => this.urls().has(key))) {
      return;
    }
    this.loading.add(key);
    try {
      const reply = await this.api.worldModels.getViewPicture({ storyId, pictureId: picture.id });
      this.keep(key, reply.image, reply.contentType);
    } catch {
      // The placeholder stays.
    } finally {
      this.loading.delete(key);
    }
  }

  async picture(view: WorldView): Promise<void> {
    const world = this.session.world();
    const scene = world?.reply.scenes[view.scene];
    if (!world || !scene) {
      return;
    }
    const key = viewKey(view);
    this.patch(key, { picturing: true, error: '' });
    try {
      const reply = await this.api.worldModels.pictureView({
        storyId: world.reply.storyId,
        sceneId: scene.id,
        camera: view.camera,
        toward: view.toward,
      });
      const picture = reply.picture;
      if (picture) {
        const before = this.pictureOf(view);
        this.keep(urlKey(picture), reply.image, picture.contentType);
        this.made.update((all) => new Map(all).set(key, picture));
        if (before && urlKey(before) !== urlKey(picture)) {
          this.forget(urlKey(before));
        }
      }
    } catch (err) {
      this.patch(key, { error: errorMessage(err) });
    } finally {
      this.patch(key, { picturing: false });
    }
  }

  ngOnDestroy(): void {
    for (const url of this.urls().values()) {
      URL.revokeObjectURL(url);
    }
  }

  private keep(key: string, bytes: Uint8Array, contentType: string): void {
    const url = URL.createObjectURL(
      new Blob([bytes as Uint8Array<ArrayBuffer>], { type: contentType }),
    );
    this.urls.update((all) => new Map(all).set(key, url));
  }

  private forget(key: string): void {
    const url = this.urls().get(key);
    if (url) {
      URL.revokeObjectURL(url);
      this.urls.update((all) => {
        const next = new Map(all);
        next.delete(key);
        return next;
      });
    }
  }

  private patch(key: string, change: Partial<ViewState>): void {
    this.states.update((all) => new Map(all).set(key, { ...all.get(key), ...change }));
  }
}

/** Picturing a view again keeps its id, so the time tells the bytes apart. */
function urlKey(picture: WorldViewPicture): string {
  return `${picture.id}@${picture.createdUnixMs}`;
}

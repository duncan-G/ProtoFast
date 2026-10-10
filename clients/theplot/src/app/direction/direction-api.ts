import { Injectable } from '@angular/core';
import { Character } from '../stories/model/character';
import { Location } from '../stories/model/location';
import { Extra } from '../stories/model/extra';
import { Prop } from '../stories/model/prop';
import { Scene } from '../stories/model/scene';
import { ConnectDirectionApi } from './connect-direction-api';
import { DescribeTarget } from './model/describe-target';
import { DirectResult } from './model/direct-result';
import { DirectTarget } from './model/direct-target';
import { LibraryImage } from './model/library-image';
import { PictureTarget } from './model/picture-target';
import { PictureRefinement } from './model/picture-refinement';
import { SceneDirection } from './model/scene-direction';
import { SpeechVoice } from './model/speech-voice';
import { VoiceQuery } from './model/voice-query';

/**
 * The director layer over a scene. Rows are known by their place in the scene (a cue by its line,
 * a frame by its first row, a look by its character, a state by its prop); the API assigns ids and
 * returns them with every save.
 */
@Injectable({ providedIn: 'root', useClass: ConnectDirectionApi })
export abstract class DirectionApi {
  abstract getSceneDirection(sceneId: string): Promise<SceneDirection>;

  /** Replaces the scene's layer; rows left out are deleted, changed rows are marked edited. */
  abstract saveSceneDirection(direction: SceneDirection): Promise<SceneDirection>;

  /** The scene and its layer in one write, so a changed line lands with its cue. */
  abstract saveDirectedScene(scene: Scene, direction: SceneDirection): Promise<SceneDirection>;

  /**
   * Has the model direct the target and saves the result. Edited rows are kept unless
   * `replaceEdited`. Extras the scene needs that the library lacks are added to it.
   */
  abstract direct(
    sceneId: string,
    target: DirectTarget,
    replaceEdited?: boolean,
  ): Promise<DirectResult>;

  /** Has the model write the entry's sheet and saves it. */
  abstract describe(target: DescribeTarget): Promise<Character | Location | Prop | Extra>;

  /** Speaks one line, or every line without current audio; `regenerate` renders again regardless. */
  abstract generateAudio(
    sceneId: string,
    elementId?: string | null,
    regenerate?: boolean,
  ): Promise<SceneDirection>;

  /** The provider's English voice library, most used first. */
  abstract searchVoices(query: VoiceQuery): Promise<SpeechVoice[]>;

  /** Casts the character a library voice that fits their sheet, never the one they have. */
  abstract castVoice(characterId: string): Promise<Character>;

  /**
   * Pictures a frame, unless its picture is current or `regenerate`. The pictures that already
   * exist around it are offered as references; nothing else is pictured to serve as one.
   */
  abstract generateFrameImage(
    sceneId: string,
    frameId: string,
    regenerate?: boolean,
  ): Promise<SceneDirection>;

  /**
   * Pictures a character in one of the scene's looks that differs from the library's: the opening
   * look, or the change on `elementId`. Frames in that look draw the character from it.
   */
  abstract generateLookImage(
    sceneId: string,
    characterId: string,
    elementId: string | null,
    regenerate?: boolean,
  ): Promise<SceneDirection>;

  /** Pictures a library entry from its sheet; null when the API has nothing to show for it. */
  abstract generateLibraryImage(
    target: PictureTarget,
    regenerate?: boolean,
  ): Promise<LibraryImage | null>;

  /**
   * Edits the frame's current picture by the refinement alone; it stays current, and pictures made
   * from it go stale. A locked frame is left as it is.
   */
  abstract refineFrameImage(
    sceneId: string,
    frameId: string,
    refinement: PictureRefinement,
  ): Promise<SceneDirection>;

  /** Edits a look's current picture by the refinement alone. */
  abstract refineLookImage(
    sceneId: string,
    characterId: string,
    elementId: string | null,
    refinement: PictureRefinement,
  ): Promise<SceneDirection>;

  /** Edits a library entry's current picture by the refinement alone. */
  abstract refineLibraryImage(
    target: PictureTarget,
    refinement: PictureRefinement,
  ): Promise<LibraryImage | null>;

  abstract listLibraryImages(storyId: string): Promise<LibraryImage[]>;
}

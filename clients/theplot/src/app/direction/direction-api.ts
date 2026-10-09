import { Injectable } from '@angular/core';
import { Character } from '../stories/model/character';
import { Location } from '../stories/model/location';
import { Prop } from '../stories/model/prop';
import { Scene } from '../stories/model/scene';
import { ConnectDirectionApi } from './connect-direction-api';
import { DescribeTarget } from './model/describe-target';
import { DirectTarget } from './model/direct-target';
import { SceneDirection } from './model/scene-direction';
import { SpeechVoice } from './model/speech-voice';

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

  /** Has the model direct the target and saves the result. Edited rows are kept unless `replaceEdited`. */
  abstract direct(
    sceneId: string,
    target: DirectTarget,
    replaceEdited?: boolean,
  ): Promise<SceneDirection>;

  /** Has the model write the entry's sheet and saves it. */
  abstract describe(target: DescribeTarget): Promise<Character | Location | Prop>;

  /** Speaks one line, or every line without current audio; `regenerate` renders again regardless. */
  abstract generateAudio(
    sceneId: string,
    elementId?: string | null,
    regenerate?: boolean,
  ): Promise<SceneDirection>;

  abstract listVoices(): Promise<{ provider: string; voices: SpeechVoice[] }>;
}

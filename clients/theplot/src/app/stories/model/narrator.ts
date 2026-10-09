import { VoiceProfile } from './voice-profile';

/** A voice that reads action and narration. */
export interface Narrator {
  /** Empty until the API has saved it. */
  id: string;
  name: string;
  voice: VoiceProfile;
}

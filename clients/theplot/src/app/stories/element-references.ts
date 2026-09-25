import { forgetReferences, renameReferences } from './mentions';
import { ReferenceTarget } from './model/reference-target';
import { SceneElement } from './model/scene-element';

export function forgetInElement(element: SceneElement, target: ReferenceTarget): SceneElement {
  const mentions = forgetReferences(element.mentions, target);
  const clearsLocation = target.kind === 'location' && element.locationId === target.id;
  const clearsSpeaker = target.kind === 'character' && element.speakerId === target.id;
  if (mentions.length === element.mentions.length && !clearsLocation && !clearsSpeaker) {
    return element;
  }
  return {
    ...element,
    mentions,
    locationId: clearsLocation ? null : element.locationId,
    speakerId: clearsSpeaker ? null : element.speakerId,
  };
}

export function renameInElement(
  element: SceneElement,
  target: ReferenceTarget,
  name: string,
): SceneElement {
  if (element.text === null || element.mentions.length === 0) {
    return element;
  }
  const renamed = renameReferences(
    { text: element.text, mentions: element.mentions },
    target,
    name,
  );
  return renamed.text === element.text
    ? element
    : { ...element, text: renamed.text, mentions: renamed.mentions };
}

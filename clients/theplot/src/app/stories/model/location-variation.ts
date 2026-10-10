/** A part of a location and the view from there; frames there take their background from its picture. */
export interface LocationVariation {
  /** Empty until the API has saved it. */
  id: string;
  name: string;
  /** Where the camera stands and what it sees from there. */
  description: string;
}

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { momentLabel } from './labels';
import { WorldCharacterChip } from './world-character-chip';
import { WorldPlaceLink } from './world-place-link';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-cast',
  imports: [WorldCharacterChip, WorldPlaceLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (session.world(); as world) {
      <div class="overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-gray-200 text-gray-500">
            <tr>
              <th class="px-4 py-3 font-medium">Character</th>
              <th class="px-4 py-3 font-medium">Where</th>
              <th class="px-4 py-3 font-medium">Since</th>
              <th class="px-4 py-3 font-medium">Last placed</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-100">
            @for (member of session.cast(); track member.character.id) {
              <tr [class.bg-indigo-50/40]="member.inScene">
                <td class="px-4 py-2">
                  <app-world-character
                    [id]="member.character.id"
                    [fact]="member.fact"
                    [changed]="member.fact?.start === session.moment()"
                    [carried]="!member.inScene"
                  />
                </td>
                <td class="px-4 py-2">
                  @if (member.fact) {
                    <app-world-place [place]="member.fact.place" />
                  } @else {
                    <span class="text-gray-400">not placed yet</span>
                  }
                </td>
                <td class="px-4 py-2 whitespace-nowrap">
                  @if (member.fact) {
                    <button
                      type="button"
                      class="text-gray-600 hover:underline"
                      (click)="session.go(member.fact.start)"
                    >
                      {{ momentLabel(world, member.fact.start) }}
                    </button>
                  }
                </td>
                <td class="px-4 py-2 whitespace-nowrap text-gray-600">
                  @if (member.inScene) {
                    <span class="text-indigo-700">in this scene</span>
                  } @else if (member.lastSeenScene >= 0) {
                    Scene {{ member.lastSeenScene + 1 }}
                  } @else {
                    —
                  }
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="4" class="px-4 py-8 text-center text-gray-500">
                  The story has no characters.
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class WorldCast {
  protected readonly session = inject(WorldSession);
  protected readonly momentLabel = momentLabel;
}

import { Component, input } from '@angular/core';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  template: `<span class="status" [class]="'status-' + value().toLowerCase()">{{ value() }}</span>`
})
export class StatusBadgeComponent {
  readonly value = input.required<string>();
}

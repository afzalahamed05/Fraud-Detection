import { Component, input } from '@angular/core';

@Component({
  selector: 'app-stat-card',
  standalone: true,
  template: `
    <div class="card">
      <span class="label">{{ label() }}</span>
      <span class="value">{{ value() }}</span>
    </div>
  `
})
export class StatCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number | null>();
}

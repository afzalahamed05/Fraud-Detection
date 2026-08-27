import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { AlertStatus, FraudAlert } from '../../core/models/transaction.model';

@Component({
  selector: 'app-alert-detail',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, RouterLink, StatusBadgeComponent],
  templateUrl: './alert-detail.component.html'
})
export class AlertDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthService);

  readonly alert = signal<FraudAlert | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly updating = signal(false);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) return;

    this.loading.set(true);
    this.api.getFraudAlert(id).subscribe({
      next: (a) => {
        this.alert.set(a);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Alert not found.');
        this.loading.set(false);
      }
    });
  }

  triggeredRulesList(): string[] {
    const raw = this.alert()?.triggeredRules;
    if (!raw) return [];
    try {
      return JSON.parse(raw);
    } catch {
      return [];
    }
  }

  setStatus(status: AlertStatus): void {
    const current = this.alert();
    if (!current) return;

    this.updating.set(true);
    this.api.updateAlertStatus(current.id, status).subscribe({
      next: (updated) => {
        this.alert.set(updated);
        this.updating.set(false);
      },
      error: () => {
        this.error.set('Failed to update alert status. Are you logged in?');
        this.updating.set(false);
      }
    });
  }
}

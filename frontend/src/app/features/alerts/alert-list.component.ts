import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { AlertSeverity, AlertStatus, FraudAlert } from '../../core/models/transaction.model';

@Component({
  selector: 'app-alert-list',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, FormsModule, StatusBadgeComponent],
  templateUrl: './alert-list.component.html'
})
export class AlertListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly alerts = signal<FraudAlert[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly pageSize = 20;
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  severity: AlertSeverity | '' = '';
  status: AlertStatus | '' = '';
  source = '';

  readonly severities: AlertSeverity[] = ['Low', 'Medium', 'High', 'Critical'];
  readonly statuses: AlertStatus[] = ['Open', 'Reviewed', 'Dismissed'];

  ngOnInit(): void {
    this.load();
  }

  onFilterChange(): void {
    this.page.set(1);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .getFraudAlerts(this.page(), this.pageSize, {
        severity: this.severity || undefined,
        status: this.status || undefined,
        source: this.source || undefined
      })
      .subscribe({
        next: (result) => {
          this.alerts.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
          this.error.set(null);
        },
        error: () => {
          this.error.set('Failed to load fraud alerts.');
          this.loading.set(false);
        }
      });
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / this.pageSize));
  }

  goToPage(delta: number): void {
    const next = this.page() + delta;
    if (next < 1 || next > this.totalPages()) return;
    this.page.set(next);
    this.load();
  }

  openDetail(a: FraudAlert): void {
    this.router.navigate(['/alerts', a.id]);
  }
}

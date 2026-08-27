import { Component, DestroyRef, OnInit, inject, signal, computed } from '@angular/core';
import { DatePipe, DecimalPipe, CurrencyPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval, startWith } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { StatCardComponent } from '../../shared/components/stat-card/stat-card.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import {
  DailyTrend,
  DashboardStats,
  FraudAlert,
  PipelineHealth,
  TopTrigger,
  Transaction
} from '../../core/models/transaction.model';

const POLL_INTERVAL_MS = 3000;

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DatePipe, DecimalPipe, CurrencyPipe, RouterLink, StatCardComponent, StatusBadgeComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);

  readonly stats = signal<DashboardStats | null>(null);
  readonly transactions = signal<Transaction[]>([]);
  readonly alerts = signal<FraudAlert[]>([]);
  readonly pipeline = signal<PipelineHealth | null>(null);
  readonly trends = signal<DailyTrend[]>([]);
  readonly topTriggers = signal<TopTrigger[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly simulating = signal(false);

  readonly severityBars = computed(() => {
    const bySeverity = this.stats()?.alertsBySeverity ?? {};
    const order = ['Low', 'Medium', 'High', 'Critical'];
    const max = Math.max(1, ...Object.values(bySeverity));
    return order.map((severity) => ({
      severity,
      count: bySeverity[severity] ?? 0,
      pct: Math.round(((bySeverity[severity] ?? 0) / max) * 100)
    }));
  });

  readonly trendBars = computed(() => {
    const data = this.trends();
    const max = Math.max(1, ...data.map((d) => d.transactionCount));
    return data.map((d) => ({
      ...d,
      pct: Math.round((d.transactionCount / max) * 100),
      flaggedPct: d.transactionCount > 0 ? Math.round((d.flaggedCount / d.transactionCount) * 100) : 0
    }));
  });

  readonly topTriggerBars = computed(() => {
    const data = this.topTriggers();
    const max = Math.max(1, ...data.map((t) => t.count));
    return data.map((t) => ({ ...t, pct: Math.round((t.count / max) * 100) }));
  });

  ngOnInit(): void {
    interval(POLL_INTERVAL_MS)
      .pipe(startWith(0), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refresh());
  }

  private refresh(): void {
    this.api.getDashboardStats().subscribe({
      next: (stats) => this.stats.set(stats),
      error: () => this.error.set('Failed to load dashboard stats. Is the API running?')
    });

    this.api.getTransactions(1, 10).subscribe({
      next: (result) => {
        this.transactions.set(result.items);
        this.loading.set(false);
        this.error.set(null);
      },
      error: () => {
        this.error.set('Failed to load transactions. Is the API running?');
        this.loading.set(false);
      }
    });

    this.api.getFraudAlerts(1, 10).subscribe({
      next: (result) => this.alerts.set(result.items)
    });

    this.api.getPipelineHealth().subscribe({
      next: (health) => this.pipeline.set(health)
    });

    this.api.getTrends(14).subscribe({
      next: (trends) => this.trends.set(trends)
    });

    this.api.getTopTriggers(5).subscribe({
      next: (triggers) => this.topTriggers.set(triggers)
    });
  }

  /** Fires one transaction at the API so the Pending -> Approved/Flagged transition through
   *  Kafka is visible live on the next poll tick, without waiting for a real client integration. */
  simulateTransaction(): void {
    if (!this.auth.isAuthenticated()) {
      this.router.navigateByUrl('/login');
      return;
    }

    this.simulating.set(true);
    const risky = Math.random() < 0.3;

    this.api
      .createTransaction({
        accountId: crypto.randomUUID(),
        merchantName: risky ? 'Sketchy Overseas Corp' : 'Corner Coffee',
        merchantCategory: risky ? 'Electronics' : 'Dining',
        amount: risky ? 8000 + Math.random() * 9000 : 5 + Math.random() * 80,
        currency: 'USD',
        countryCode: risky ? 'RU' : 'US'
      })
      .subscribe({
        next: () => {
          this.simulating.set(false);
          this.refresh();
        },
        error: () => {
          this.simulating.set(false);
          this.error.set('Failed to create transaction. Is the API running?');
        }
      });
  }
}

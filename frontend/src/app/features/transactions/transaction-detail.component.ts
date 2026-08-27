import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { FraudAlert, Transaction } from '../../core/models/transaction.model';

@Component({
  selector: 'app-transaction-detail',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, RouterLink, StatusBadgeComponent],
  templateUrl: './transaction-detail.component.html'
})
export class TransactionDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  readonly transaction = signal<Transaction | null>(null);
  readonly alerts = signal<FraudAlert[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) return;

    this.api.getTransaction(id).subscribe({
      next: (t) => {
        this.transaction.set(t);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Transaction not found.');
        this.loading.set(false);
      }
    });

    this.api.getFraudAlerts(1, 10, { transactionId: id }).subscribe({
      next: (result) => this.alerts.set(result.items)
    });
  }

  processingLatencyMs(): number | null {
    const t = this.transaction();
    if (!t?.publishedToKafkaUtc || !t?.processedAtUtc) return null;
    return new Date(t.processedAtUtc).getTime() - new Date(t.publishedToKafkaUtc).getTime();
  }
}

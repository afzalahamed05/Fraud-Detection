import { Component, OnInit, DestroyRef, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { Transaction, TransactionStatus } from '../../core/models/transaction.model';

@Component({
  selector: 'app-transaction-list',
  standalone: true,
  imports: [CurrencyPipe, DatePipe, FormsModule, StatusBadgeComponent],
  templateUrl: './transaction-list.component.html'
})
export class TransactionListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchChanged = new Subject<void>();

  readonly transactions = signal<Transaction[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly pageSize = 20;
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  search = '';
  status: TransactionStatus | '' = '';

  readonly statuses: TransactionStatus[] = ['Pending', 'Approved', 'Flagged', 'Declined'];

  ngOnInit(): void {
    this.searchChanged
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page.set(1);
        this.load();
      });
    this.load();
  }

  onSearchChange(): void {
    this.searchChanged.next();
  }

  onStatusChange(): void {
    this.page.set(1);
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api
      .getTransactions(this.page(), this.pageSize, {
        search: this.search || undefined,
        status: this.status || undefined
      })
      .subscribe({
        next: (result) => {
          this.transactions.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
          this.error.set(null);
        },
        error: () => {
          this.error.set('Failed to load transactions.');
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

  openDetail(t: Transaction): void {
    this.router.navigate(['/transactions', t.id]);
  }
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { CustomerRiskProfile } from '../../core/models/transaction.model';

@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [CurrencyPipe, DatePipe],
  templateUrl: './customer-list.component.html'
})
export class CustomerListComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly profiles = signal<CustomerRiskProfile[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly pageSize = 25;
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.getCustomerProfiles(this.page(), this.pageSize).subscribe({
      next: (result) => {
        this.profiles.set(result.items);
        this.totalCount.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load customer risk profiles. Has the PySpark analytics job run yet?');
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
}

import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { TransactionListComponent } from './transaction-list.component';
import { ApiService } from '../../core/services/api.service';
import { PagedResult, Transaction } from '../../core/models/transaction.model';

function emptyPage(): PagedResult<Transaction> {
  return { items: [], page: 1, pageSize: 20, totalCount: 0 };
}

describe('TransactionListComponent', () => {
  let fixture: ComponentFixture<TransactionListComponent>;
  let component: TransactionListComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiSpy = jasmine.createSpyObj('ApiService', ['getTransactions']);
    apiSpy.getTransactions.and.returnValue(of(emptyPage()));

    await TestBed.configureTestingModule({
      imports: [TransactionListComponent],
      providers: [{ provide: ApiService, useValue: apiSpy }, provideRouter([])]
    }).compileComponents();

    fixture = TestBed.createComponent(TransactionListComponent);
    component = fixture.componentInstance;
  });

  it('loads page 1 on init', () => {
    fixture.detectChanges();
    expect(apiSpy.getTransactions).toHaveBeenCalledWith(1, 20, { search: undefined, status: undefined });
  });

  it('debounces search input before reloading', fakeAsync(() => {
    fixture.detectChanges();
    apiSpy.getTransactions.calls.reset();

    component.search = 'coffee';
    component.onSearchChange();
    component.search = 'coffee shop';
    component.onSearchChange();

    tick(299);
    expect(apiSpy.getTransactions).not.toHaveBeenCalled();

    tick(1);
    expect(apiSpy.getTransactions).toHaveBeenCalledTimes(1);
    expect(apiSpy.getTransactions).toHaveBeenCalledWith(1, 20, { search: 'coffee shop', status: undefined });
  }));

  it('resets to page 1 when the status filter changes', () => {
    fixture.detectChanges();
    component.page.set(3);

    component.status = 'Flagged';
    component.onStatusChange();

    expect(component.page()).toBe(1);
    expect(apiSpy.getTransactions).toHaveBeenCalledWith(1, 20, { search: undefined, status: 'Flagged' });
  });

  it('totalPages rounds up and never goes below 1', () => {
    apiSpy.getTransactions.and.returnValue(of({ items: [], page: 1, pageSize: 20, totalCount: 45 }));
    fixture.detectChanges();

    expect(component.totalPages()).toBe(3);
  });

  it('goToPage ignores out-of-range navigation', () => {
    fixture.detectChanges();
    apiSpy.getTransactions.calls.reset();

    component.goToPage(-1); // already on page 1
    expect(apiSpy.getTransactions).not.toHaveBeenCalled();
  });
});

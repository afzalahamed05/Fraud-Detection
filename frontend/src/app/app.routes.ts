import { Routes } from '@angular/router';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { TransactionListComponent } from './features/transactions/transaction-list.component';
import { TransactionDetailComponent } from './features/transactions/transaction-detail.component';
import { AlertListComponent } from './features/alerts/alert-list.component';
import { AlertDetailComponent } from './features/alerts/alert-detail.component';
import { CustomerListComponent } from './features/customers/customer-list.component';
import { LoginComponent } from './features/login/login.component';

export const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'transactions', component: TransactionListComponent },
  { path: 'transactions/:id', component: TransactionDetailComponent },
  { path: 'alerts', component: AlertListComponent },
  { path: 'alerts/:id', component: AlertDetailComponent },
  { path: 'customers', component: CustomerListComponent },
  { path: 'login', component: LoginComponent },
  { path: '**', redirectTo: '' }
];

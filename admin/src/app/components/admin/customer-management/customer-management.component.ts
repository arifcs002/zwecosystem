import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { CustomerService, CustomerSummary, CustomerProfile } from '../../../services/customer/customer.service';

@Component({
  selector: 'app-customer-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './customer-management.component.html',
  styleUrl: './customer-management.component.css'
})
export class CustomerManagementComponent implements OnInit {
  private customerService = inject(CustomerService);

  customers: CustomerSummary[] = [];
  isLoading = false;
  errorMsg = '';

  search = '';
  private search$ = new Subject<string>();

  // Detail drawer
  selected: CustomerProfile | null = null;
  detailLoading = false;

  ngOnInit() {
    this.load();
    this.search$.pipe(debounceTime(300), distinctUntilChanged()).subscribe(() => this.load());
  }

  onSearchChange() {
    this.search$.next(this.search);
  }

  load() {
    this.isLoading = true;
    this.errorMsg = '';
    this.customerService.getCustomers(this.search.trim() || undefined).subscribe({
      next: (data) => { this.customers = data; this.isLoading = false; },
      error: () => { this.errorMsg = 'Failed to load customers.'; this.isLoading = false; }
    });
  }

  openDetail(c: CustomerSummary) {
    this.detailLoading = true;
    this.selected = { phone: c.phone, name: c.name, totalOrders: c.totalOrders, totalSpent: c.totalSpent, orders: [] };
    this.customerService.getCustomer(c.phone).subscribe({
      next: (p) => { this.selected = p; this.detailLoading = false; },
      error: () => { this.detailLoading = false; }
    });
  }

  closeDetail() { this.selected = null; }

  get totalCustomers(): number { return this.customers.length; }
  get totalRevenue(): number { return this.customers.reduce((s, c) => s + Number(c.totalSpent || 0), 0); }

  statusClass(status: string): string {
    const s = (status || '').toUpperCase();
    if (s === 'COMPLETED') return 'bg-emerald-500/15 text-emerald-400';
    if (s === 'PENDING') return 'bg-amber-500/15 text-amber-400';
    if (s === 'PROCESSING') return 'bg-sky-500/15 text-sky-400';
    if (s === 'CANCELLED') return 'bg-rose-500/15 text-rose-400';
    if (s === 'SHIPPED') return 'bg-violet-500/15 text-violet-400';
    return 'bg-gray-500/15 text-gray-400';
  }
}

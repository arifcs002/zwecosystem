import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../../services/order/order.service';
import { DashboardService } from '../../../services/dashboard/dashboard.service';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './reports.component.html',
  styleUrl: './reports.component.css'
})
export class ReportsComponent implements OnInit {
  private orderService = inject(OrderService);
  private dashboardService = inject(DashboardService);

  allOrders: any[] = [];
  filteredOrders: any[] = [];
  isLoading = false;
  errorMsg = '';

  filterDate = '';
  filterStatus = 'ALL';
  statuses = ['ALL', 'CANCELLED', 'COMPLETED', 'PACKED', 'PENDING', 'PROCESSING', 'SHIPPED'];

  stats = { todayRevenue: 0, todayOrders: 0, monthRevenue: 0, monthOrders: 0, totalRevenue: 0, totalOrders: 0 };

  ngOnInit() {
    this.filterDate = new Date().toISOString().split('T')[0];
    this.load();
  }

  load() {
    this.isLoading = true;
    this.orderService.getOrders().subscribe({
      next: (data) => {
        this.allOrders = data.sort((a: any, b: any) =>
          new Date(b.CreatedDate || b.createdDate || 0).getTime() - new Date(a.CreatedDate || a.createdDate || 0).getTime()
        );
        this.calcStats();
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => {
        this.errorMsg = 'Failed to load orders.';
        this.isLoading = false;
      }
    });
  }

  calcStats() {
    const today = new Date().toDateString();
    const thisMonth = new Date().getMonth();
    const thisYear = new Date().getFullYear();

    this.stats = { todayRevenue: 0, todayOrders: 0, monthRevenue: 0, monthOrders: 0, totalRevenue: 0, totalOrders: 0 };

    for (const o of this.allOrders) {
      if (o.Status === 'CANCELLED') continue;
      const d = new Date(o.CreatedDate || o.createdDate || 0);
      const total = Number(o.Total ?? o.total ?? 0);
      this.stats.totalRevenue += total;
      this.stats.totalOrders++;
      if (d.toDateString() === today) { this.stats.todayRevenue += total; this.stats.todayOrders++; }
      if (d.getMonth() === thisMonth && d.getFullYear() === thisYear) { this.stats.monthRevenue += total; this.stats.monthOrders++; }
    }
  }

  applyFilter() {
    let list = [...this.allOrders];
    if (this.filterDate) {
      list = list.filter(o => {
        const d = new Date(o.CreatedDate || o.createdDate || 0).toISOString().split('T')[0];
        return d === this.filterDate;
      });
    }
    if (this.filterStatus !== 'ALL') {
      list = list.filter(o => (o.Status || o.status) === this.filterStatus);
    }
    this.filteredOrders = list;
  }

  get filteredRevenue(): number {
    return this.filteredOrders
      .filter(o => (o.Status || o.status) !== 'CANCELLED')
      .reduce((s, o) => s + Number(o.Total ?? o.total ?? 0), 0);
  }

  statusClass(status: string): string {
    const s = (status || '').toUpperCase();
    if (s === 'COMPLETED') return 'bg-emerald-500/15 text-emerald-400';
    if (s === 'PENDING') return 'bg-amber-500/15 text-amber-400';
    if (s === 'PROCESSING') return 'bg-sky-500/15 text-sky-400';
    if (s === 'CANCELLED') return 'bg-rose-500/15 text-rose-400';
    if (s === 'SHIPPED') return 'bg-violet-500/15 text-violet-400';
    return 'bg-gray-500/15 text-gray-400';
  }

  clearFilter() {
    this.filterDate = '';
    this.filterStatus = 'ALL';
    this.applyFilter();
  }

  // Export whatever the current filter shows to a CSV the accountant can open
  // in Excel/Sheets. Client-side only — uses the already-loaded rows.
  exportCsv() {
    if (!this.filteredOrders.length) return;
    const headers = ['Order #', 'Customer', 'Phone', 'Date', 'Payment', 'Amount', 'Status'];
    const esc = (v: any) => {
      const s = (v ?? '').toString().replace(/"/g, '""');
      return /[",\n]/.test(s) ? `"${s}"` : s;
    };
    const rows = this.filteredOrders.map(o => [
      o.OrderNumber || o.orderNumber || '',
      o.CustomerName || o.customerName || '',
      o.CustomerPhone || o.customerPhone || '',
      new Date(o.CreatedDate || o.createdDate || 0).toISOString().split('T')[0],
      o.PaymentMethod || o.paymentMethod || '',
      Number(o.Total ?? o.total ?? 0),
      o.Status || o.status || ''
    ].map(esc).join(','));

    // Prepend a UTF-8 BOM so Excel renders the ৳ / Bangla text correctly.
    const csv = '﻿' + [headers.join(','), ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const label = this.filterDate || 'all';
    a.download = `orders-report-${label}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }
}

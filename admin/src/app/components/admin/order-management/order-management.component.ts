import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../../services/order/order.service';

@Component({
  selector: 'app-order-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './order-management.component.html',
  styleUrl: './order-management.component.css'
})
export class OrderManagementComponent implements OnInit {
  private orderService = inject(OrderService);

  orders: any[] = [];
  filteredOrders: any[] = [];
  searchQuery = '';
  filterStatus = 'ALL';
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  showDetailModal = false;
  selectedOrder: any = null;
  newStatus = '';
  statusNote = '';

  statuses = ['ALL', 'CANCELLED', 'COMPLETED', 'PENDING', 'PROCESSING'];
  orderStatuses = ['CANCELLED', 'COMPLETED', 'PACKED', 'PENDING', 'PROCESSING', 'SHIPPED'];

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.orderService.getOrders().subscribe({
      next: (data) => { this.orders = data; this.applyFilter(); this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Failed to load orders.'; }
    });
  }

  applyFilter() {
    let result = [...this.orders];
    if (this.filterStatus !== 'ALL') result = result.filter(o => o.status === this.filterStatus);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(o => o.orderNumber?.toLowerCase().includes(q) || o.customerName?.toLowerCase().includes(q) || o.customerPhone?.includes(q));
    }
    this.filteredOrders = result;
  }

  openDetail(order: any) {
    this.selectedOrder = order;
    this.newStatus = order.status;
    this.statusNote = '';
    this.showDetailModal = true;
    this.errorMsg = '';
  }

  closeModal() { this.showDetailModal = false; this.selectedOrder = null; }

  updateStatus() {
    if (!this.newStatus) return;
    this.orderService.updateOrderStatus(this.selectedOrder.id, this.newStatus, this.statusNote).subscribe({
      next: () => {
        this.successMsg = 'Order status updated!';
        this.closeModal();
        this.load();
        setTimeout(() => this.successMsg = '', 3000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to update status.'; }
    });
  }

  getStatusClass(status: string): string {
    const map: any = { PENDING: 'status-pending', PROCESSING: 'status-processing', PACKED: 'status-packed', SHIPPED: 'status-shipped', COMPLETED: 'status-completed', CANCELLED: 'status-cancelled' };
    return map[status] || 'status-pending';
  }

  getPaymentClass(status: string): string {
    return status === 'PAID' ? 'status-completed' : status === 'FAILED' ? 'status-cancelled' : 'status-pending';
  }
}

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../../services/order/order.service';
import { UserService } from '../../../services/user/user.service';

@Component({
  selector: 'app-delivery-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './delivery-management.component.html',
  styleUrl: './delivery-management.component.css'
})
export class DeliveryManagementComponent implements OnInit {
  private orderService = inject(OrderService);
  private userService = inject(UserService);

  orders: any[] = [];
  filteredOrders: any[] = [];
  deliveryAgents: any[] = [];
  filterStatus = 'PROCESSING';
  searchQuery = '';
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  showUpdateModal = false;
  selectedOrder: any = null;
  newStatus = '';
  statusNote = '';

  deliveryStatuses = ['CANCELLED', 'COMPLETED', 'PACKED', 'SHIPPED'];
  filterStatuses = ['ALL', 'CANCELLED', 'COMPLETED', 'PACKED', 'PROCESSING', 'SHIPPED'];

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.orderService.getOrders().subscribe({
      next: (data) => {
        this.orders = data.filter((o: any) => !['PENDING', 'CANCELLED'].includes(o.status) || o.status === 'CANCELLED');
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  applyFilter() {
    let result = [...this.orders];
    if (this.filterStatus !== 'ALL') result = result.filter(o => o.status === this.filterStatus);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(o => o.orderNumber?.toLowerCase().includes(q) || o.customerName?.toLowerCase().includes(q));
    }
    this.filteredOrders = result;
  }

  openUpdate(order: any) {
    this.selectedOrder = order;
    this.newStatus = order.status;
    this.statusNote = '';
    this.showUpdateModal = true;
    this.errorMsg = '';
  }

  closeModal() { this.showUpdateModal = false; this.selectedOrder = null; }

  updateDeliveryStatus() {
    this.orderService.updateOrderStatus(this.selectedOrder.id, this.newStatus, this.statusNote).subscribe({
      next: () => {
        this.successMsg = 'Delivery status updated!';
        this.closeModal();
        this.load();
        setTimeout(() => this.successMsg = '', 3000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to update.'; }
    });
  }

  setFilter(status: string) {
    this.filterStatus = status;
    this.applyFilter();
  }

  getCountForStatus(status: string): number {
    if (status === 'ALL') return this.orders.length;
    return this.orders.filter((o: any) => o.status === status).length;
  }

  getStatusClass(status: string): string {
    const map: any = { PROCESSING: 'status-processing', PACKED: 'status-packed', SHIPPED: 'status-shipped', COMPLETED: 'status-completed', CANCELLED: 'status-cancelled' };
    return map[status] || 'status-pending';
  }

  getStatusIcon(status: string): string {
    const map: any = { PROCESSING: '⚙️', PACKED: '📦', SHIPPED: '🚚', COMPLETED: '✅', CANCELLED: '❌' };
    return map[status] || '📋';
  }
}

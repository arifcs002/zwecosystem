import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OrderService } from '../../../services/order/order.service';

@Component({
  selector: 'app-payment-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './payment-management.component.html',
  styleUrl: './payment-management.component.css'
})
export class PaymentManagementComponent implements OnInit {
  private orderService = inject(OrderService);

  orders: any[] = [];
  filteredOrders: any[] = [];
  filterPayment = 'PENDING';
  searchQuery = '';
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  showVerifyModal = false;
  selectedOrder: any = null;
  verifyForm = { transactionId: '', provider: 'BKASH', amount: 0, senderNumber: '', referenceLog: '' };

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.orderService.getOrders().subscribe({
      next: (data) => {
        this.orders = data.filter((o: any) => ['BKASH', 'NAGAD', 'ROCKET'].includes(o.paymentMethod));
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  applyFilter() {
    let result = [...this.orders];
    if (this.filterPayment !== 'ALL') result = result.filter(o => o.paymentStatus === this.filterPayment);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(o => o.orderNumber?.toLowerCase().includes(q) || o.customerName?.toLowerCase().includes(q));
    }
    this.filteredOrders = result;
  }

  openVerify(order: any) {
    this.selectedOrder = order;
    this.verifyForm = { transactionId: '', provider: order.paymentMethod || 'BKASH', amount: order.total, senderNumber: '', referenceLog: '' };
    this.showVerifyModal = true;
    this.errorMsg = '';
  }

  closeModal() { this.showVerifyModal = false; this.selectedOrder = null; }

  verify() {
    if (!this.verifyForm.transactionId.trim()) { this.errorMsg = 'Transaction ID is required.'; return; }
    const payload = { orderId: this.selectedOrder.id, ...this.verifyForm };
    this.orderService.verifyPayment(payload).subscribe({
      next: () => {
        this.successMsg = 'Payment verified successfully!';
        this.closeModal();
        this.load();
        setTimeout(() => this.successMsg = '', 4000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || 'Verification failed.'; }
    });
  }

  getPaymentClass(status: string) {
    return status === 'PAID' ? 'pay-paid' : status === 'FAILED' ? 'pay-failed' : 'pay-pending';
  }
}

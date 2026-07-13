import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RefundService } from '../../../services/refund/refund.service';
import { PaymentService } from '../../../services/payment/payment.service';

@Component({
  selector: 'app-refund-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './refund-management.component.html',
  styleUrl: './refund-management.component.css'
})
export class RefundManagementComponent implements OnInit {
  private refundService = inject(RefundService);
  private paymentService = inject(PaymentService);

  refunds: any[] = [];
  filteredRefunds: any[] = [];
  filterStatus = 'ALL';
  searchQuery = '';
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  showCreateModal = false;
  eligiblePayments: any[] = [];
  createForm = { paymentId: 0, orderId: 0, amount: 0, reason: '' };

  showRejectModal = false;
  selectedRefund: any = null;
  rejectReason = '';

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.refundService.getAll().subscribe({
      next: (data) => {
        this.refunds = data;
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  applyFilter() {
    let result = [...this.refunds];
    if (this.filterStatus !== 'ALL') result = result.filter(r => r.status === this.filterStatus);
    if (this.searchQuery) {
      const q = this.searchQuery.toLowerCase();
      result = result.filter(r => r.orderNumber?.toLowerCase().includes(q));
    }
    this.filteredRefunds = result;
  }

  openCreate() {
    this.errorMsg = '';
    this.createForm = { paymentId: 0, orderId: 0, amount: 0, reason: '' };
    this.paymentService.getAll('SUCCESS').subscribe({
      next: (data) => { this.eligiblePayments = data; },
      error: () => { this.eligiblePayments = []; }
    });
    this.showCreateModal = true;
  }

  onPaymentSelected() {
    const payment = this.eligiblePayments.find(p => p.id === +this.createForm.paymentId);
    if (payment) {
      this.createForm.orderId = payment.orderId;
      this.createForm.amount = payment.amount;
    }
  }

  closeCreateModal() { this.showCreateModal = false; }

  submitCreate() {
    if (!this.createForm.paymentId) { this.errorMsg = 'Select a payment to refund.'; return; }
    if (!this.createForm.amount || this.createForm.amount <= 0) { this.errorMsg = 'Enter a valid amount.'; return; }

    this.refundService.create(this.createForm).subscribe({
      next: () => {
        this.successMsg = 'Refund requested successfully.';
        this.closeCreateModal();
        this.load();
        setTimeout(() => this.successMsg = '', 4000);
      },
      error: (e) => { this.errorMsg = e?.error?.message || e?.error || 'Failed to request refund.'; }
    });
  }

  approve(refund: any) {
    this.refundService.approve(refund.id).subscribe({
      next: () => { this.successMsg = `Refund #${refund.id} approved.`; this.load(); setTimeout(() => this.successMsg = '', 4000); },
      error: (e) => { this.errorMsg = e?.error || 'Failed to approve refund.'; }
    });
  }

  openReject(refund: any) {
    this.selectedRefund = refund;
    this.rejectReason = '';
    this.showRejectModal = true;
  }

  closeRejectModal() { this.showRejectModal = false; this.selectedRefund = null; }

  submitReject() {
    this.refundService.reject(this.selectedRefund.id, this.rejectReason).subscribe({
      next: () => {
        this.successMsg = `Refund #${this.selectedRefund.id} rejected.`;
        this.closeRejectModal();
        this.load();
        setTimeout(() => this.successMsg = '', 4000);
      },
      error: (e) => { this.errorMsg = e?.error || 'Failed to reject refund.'; }
    });
  }

  complete(refund: any) {
    if (!confirm(`Confirm that you have manually sent ৳${refund.amount} back to the customer for order ${refund.orderNumber}?`)) return;
    this.refundService.complete(refund.id).subscribe({
      next: () => { this.successMsg = `Refund #${refund.id} marked completed.`; this.load(); setTimeout(() => this.successMsg = '', 4000); },
      error: (e) => { this.errorMsg = e?.error || 'Failed to complete refund.'; }
    });
  }

  getStatusClass(status: string) {
    return status === 'COMPLETED' ? 'status-completed'
      : status === 'APPROVED' ? 'status-approved'
      : status === 'REJECTED' ? 'status-rejected'
      : 'status-requested';
  }
}

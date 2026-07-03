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

  statusHistory: { status: string; note?: string; createdDate: string }[] = [];
  courierName = '';
  trackingNumber = '';
  savingCourier = false;

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
    this.courierName = order.courierName || '';
    this.trackingNumber = order.trackingNumber || '';
    this.statusHistory = [];
    // The list rows may not carry line items — pull the full order so the
    // detail table (and the invoice print) always has them.
    this.orderService.getOrderById(order.id).subscribe({
      next: (full) => {
        if (this.selectedOrder?.id === full.id) {
          this.selectedOrder = { ...this.selectedOrder, ...full };
          this.courierName = full.courierName || this.courierName;
          this.trackingNumber = full.trackingNumber || this.trackingNumber;
        }
      },
      error: () => { /* keep the list row we already have */ }
    });
    this.orderService.getOrderHistory(order.id).subscribe({
      next: (h) => { this.statusHistory = h; },
      error: () => { this.statusHistory = []; }
    });
  }

  saveCourier() {
    if (!this.selectedOrder) return;
    this.savingCourier = true;
    this.orderService.updateCourier(this.selectedOrder.id, this.courierName.trim(), this.trackingNumber.trim()).subscribe({
      next: (res) => {
        this.savingCourier = false;
        this.successMsg = 'Courier info saved!';
        this.selectedOrder.courierName = res.courierName;
        this.selectedOrder.trackingNumber = res.trackingNumber;
        setTimeout(() => this.successMsg = '', 2500);
      },
      error: (e) => { this.savingCourier = false; this.errorMsg = e?.error?.message || 'Failed to save courier info.'; }
    });
  }

  // Open a clean, print-friendly invoice in a new window and trigger print.
  // Kept as a standalone document so the app's own dark theme/CSS doesn't
  // bleed into what comes out of the printer.
  printInvoice() {
    const o = this.selectedOrder;
    if (!o) return;
    const esc = (v: any) => (v ?? '').toString().replace(/[&<>]/g, (c: string) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] || c));
    const money = (v: any) => '৳' + Number(v ?? 0).toLocaleString('en-US', { maximumFractionDigits: 0 });
    const date = new Date(o.createdDate || o.CreatedDate || Date.now()).toLocaleString();

    const rows = (o.items || []).map((it: any) => `
      <tr>
        <td>${esc(it.product?.name || it.productName || it.productId)}</td>
        <td style="text-align:center">${esc(it.quantity)}</td>
        <td style="text-align:right">${money(it.price)}</td>
        <td style="text-align:right">${money(it.totalPrice ?? (it.price * it.quantity))}</td>
      </tr>`).join('');

    const html = `<!doctype html><html><head><meta charset="utf-8"><title>Invoice ${esc(o.orderNumber)}</title>
      <style>
        * { font-family: Arial, sans-serif; }
        body { padding: 32px; color: #111; max-width: 720px; margin: 0 auto; }
        h1 { margin: 0 0 4px; font-size: 22px; }
        .muted { color: #666; font-size: 13px; }
        .row { display: flex; justify-content: space-between; margin: 18px 0; gap: 24px; }
        table { width: 100%; border-collapse: collapse; margin-top: 12px; }
        th, td { padding: 8px 6px; border-bottom: 1px solid #ddd; font-size: 13px; }
        th { text-align: left; background: #f5f5f5; }
        .totals { margin-top: 14px; margin-left: auto; width: 260px; font-size: 14px; }
        .totals div { display: flex; justify-content: space-between; padding: 4px 0; }
        .totals .grand { border-top: 2px solid #111; font-weight: bold; font-size: 16px; margin-top: 6px; padding-top: 8px; }
        @media print { body { padding: 0; } }
      </style></head><body>
      <h1>INVOICE</h1>
      <div class="muted">Order ${esc(o.orderNumber)} · ${esc(date)}</div>
      <div class="row">
        <div>
          <strong>Bill To</strong><br>
          <span class="muted">${esc(o.customerName || 'Guest')}<br>
          ${esc(o.customerPhone || '')}<br>
          ${esc(o.shippingAddress || '')}${o.shippingDistrict ? ', ' + esc(o.shippingDistrict) : ''}</span>
        </div>
        <div style="text-align:right">
          <strong>Status</strong><br><span class="muted">${esc(o.status)}</span><br>
          <strong>Payment</strong><br><span class="muted">${esc(o.paymentMethod)} (${esc(o.paymentStatus)})</span>
          ${o.courierName ? `<br><strong>Courier</strong><br><span class="muted">${esc(o.courierName)}${o.trackingNumber ? ' · ' + esc(o.trackingNumber) : ''}</span>` : ''}
        </div>
      </div>
      <table>
        <thead><tr><th>Item</th><th style="text-align:center">Qty</th><th style="text-align:right">Price</th><th style="text-align:right">Amount</th></tr></thead>
        <tbody>${rows || '<tr><td colspan="4" class="muted">No line items recorded.</td></tr>'}</tbody>
      </table>
      <div class="totals">
        <div><span>Subtotal</span><span>${money(o.subtotal)}</span></div>
        <div><span>Discount</span><span>-${money(o.discount)}</span></div>
        <div><span>Shipping</span><span>${money(o.shippingFee)}</span></div>
        <div class="grand"><span>Total</span><span>${money(o.total)}</span></div>
      </div>
      <p class="muted" style="text-align:center;margin-top:32px">Thank you for your order!</p>
      <script>window.onload = function(){ window.print(); }<\/script>
      </body></html>`;

    const w = window.open('', '_blank', 'width=800,height=900');
    if (!w) { this.errorMsg = 'Please allow pop-ups to print the invoice.'; return; }
    w.document.write(html);
    w.document.close();
  }

  closeModal() { this.showDetailModal = false; this.selectedOrder = null; this.statusHistory = []; }

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

import { Component, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { PosService, PosProduct } from '../../../services/pos/pos.service';
import { OfflinePosService, OfflineSale } from '../../../services/pos/offline-pos.service';
import { BluetoothPrinterService } from '../../../services/pos/bluetooth-printer.service';
import { LedgerService } from '../../../services/ledger/ledger.service';
import { SettingsService, CompanySetting } from '../../../services/settings/settings.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { AuthService } from '../../../services/auth/auth.service';
import { CameraScannerComponent } from '../../shared/camera-scanner/camera-scanner.component';

type PosMode = 'FIXED' | 'BARGAIN';

interface CartLine {
  product: PosProduct;
  quantity: number;
  price: number; // editable copy of product.price (only actually applied in BARGAIN mode)
}

const RECEIPT_TEXT_KEY = 'pos_receipt_texts';

@Component({
    selector: 'app-pos-management',
    imports: [CommonModule, FormsModule, CameraScannerComponent],
    templateUrl: './pos-management.component.html',
    styleUrl: './pos-management.component.css'
})
export class PosManagementComponent implements OnInit {
  private posService = inject(PosService);
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);
  authService = inject(AuthService);
  offline = inject(OfflinePosService);
  printer = inject(BluetoothPrinterService);
  private ledger = inject(LedgerService);

  @ViewChild(CameraScannerComponent) scanner?: CameraScannerComponent;

  mode: PosMode = 'FIXED';
  barcodeInput = '';
  cart: CartLine[] = [];
  discount = 0;
  paymentMethod = 'CASH';
  amountPaid: number | null = null;   // credit sales: paid now
  owed: number | null = null;         // this customer's current balance (looked up by phone)
  creditLimit = 0;
  customerName = '';
  customerPhone = '';
  isLoading = false;
  isCheckingOut = false;
  lastReceipt: any = null;

  // Admin-only mode settings panel
  showSettings = false;
  savingSettings = false;

  ngOnInit() {
    this.loadMode();
    this.offline.start();
  }

  loadMode() {
    this.settingsService.getSettings().subscribe({
      next: (s) => { this.mode = (s.find(x => x.key === 'pos_mode')?.value === 'BARGAIN') ? 'BARGAIN' : 'FIXED'; },
      error: () => {}
    });
  }

  saveMode(newMode: PosMode) {
    this.savingSettings = true;
    const settings: CompanySetting[] = [{ key: 'pos_mode', value: newMode, groupName: 'POS' }];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.mode = newMode; this.savingSettings = false; this.notify.notify({ type: 'success', title: 'Saved', message: `POS mode set to ${newMode === 'BARGAIN' ? 'Manual/Bargaining' : 'Fixed Price'}.`, ttlMs: 3000 }); },
      error: () => { this.savingSettings = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save POS mode.', ttlMs: 4000 }); }
    });
  }

  get isBargain() { return this.mode === 'BARGAIN'; }
  get isCredit() { return this.paymentMethod === 'CREDIT'; }
  /** What the customer will still owe from THIS sale. */
  get dueNow(): number { return Math.max(this.total - (this.amountPaid || 0), 0); }

  // When the cashier picks credit and types the phone, show what the customer already owes.
  lookupOwed() {
    this.owed = null;
    const phone = this.customerPhone.trim();
    if (!this.isCredit || phone.length < 11 || !navigator.onLine) return;
    this.ledger.customer(phone).subscribe({
      next: d => { this.owed = d.balance; this.creditLimit = d.creditLimit; },
      error: () => { this.owed = 0; }       // 404/400: no account yet
    });
  }
  onPaymentChange() { this.amountPaid = null; this.lookupOwed(); }

  // ── Scan / lookup ────────────────────────────────────────────
  onScanSubmit() {
    const code = this.barcodeInput.trim();
    if (!code) return;
    this.lookupAndAdd(code);
    this.barcodeInput = '';
  }

  openCamera() { this.scanner?.show(); }
  onCameraScanned(code: string) { this.lookupAndAdd(code); }

  private lookupAndAdd(barcode: string) {
    // No connection: use the copy of the product list kept on this device.
    if (!navigator.onLine) { void this.lookupLocal(barcode); return; }
    this.isLoading = true;
    this.posService.lookup(barcode).subscribe({
      next: (p) => { this.addToCart(p); this.isLoading = false; },
      error: (err: HttpErrorResponse) => {
        this.isLoading = false;
        // The connection dropped mid-request: fall back to the local copy instead of failing the scan.
        if (err.status === 0) { void this.lookupLocal(barcode); return; }
        this.notify.notify({ type: 'warning', title: 'Not found', message: `No product matches "${barcode}".`, ttlMs: 3500 });
      }
    });
  }

  private async lookupLocal(barcode: string) {
    const p = await this.offline.findLocal(barcode);
    if (p) { this.addToCart(p); return; }
    this.notify.notify({ type: 'warning', title: 'Not found offline', message: `"${barcode}" is not in this device's product list. Connect once to refresh it.`, ttlMs: 4500 });
  }

  addToCart(p: PosProduct) {
    const existing = this.cart.find(l => l.product.id === p.id);
    if (existing) {
      if (existing.quantity < p.stockQuantity) existing.quantity++;
      else this.notify.notify({ type: 'warning', title: 'Stock limit', message: `Only ${p.stockQuantity} in stock.`, ttlMs: 3000 });
      return;
    }
    if (p.stockQuantity <= 0) {
      this.notify.notify({ type: 'warning', title: 'Out of stock', message: `${p.name} has no stock.`, ttlMs: 3000 });
      return;
    }
    this.cart.push({ product: p, quantity: 1, price: p.price });
  }

  changeQty(line: CartLine, delta: number) {
    const next = line.quantity + delta;
    if (next <= 0) { this.cart = this.cart.filter(l => l !== line); return; }
    if (next > line.product.stockQuantity) {
      this.notify.notify({ type: 'warning', title: 'Stock limit', message: `Only ${line.product.stockQuantity} in stock.`, ttlMs: 3000 });
      return;
    }
    line.quantity = next;
  }

  removeLine(line: CartLine) { this.cart = this.cart.filter(l => l !== line); }

  // ── Totals ───────────────────────────────────────────────────
  get subtotal(): number { return this.cart.reduce((s, l) => s + (this.isBargain ? l.price : l.product.price) * l.quantity, 0); }
  get total(): number { return Math.max(this.subtotal - (this.discount || 0), 0); }

  // ── Checkout ─────────────────────────────────────────────────
  checkout() {
    if (this.cart.length === 0) {
      this.notify.notify({ type: 'warning', title: 'Cart empty', message: 'Scan or add a product first.', ttlMs: 3000 });
      return;
    }
    this.isCheckingOut = true;
    const items = this.cart.map(l => ({
      productId: l.product.id,
      quantity: l.quantity,
      price: this.isBargain ? l.price : null
    }));
    if (this.isCredit) {
      if (!this.customerName.trim() || this.customerPhone.trim().length < 11) {
        this.isCheckingOut = false;
        this.notify.notify({ type: 'warning', title: 'Customer needed', message: 'A credit (baki) sale needs the customer\'s name and mobile number.', ttlMs: 4500 });
        return;
      }
      if ((this.amountPaid || 0) > this.total) {
        this.isCheckingOut = false;
        this.notify.notify({ type: 'warning', title: 'Check the amount', message: 'The amount paid now is more than the total.', ttlMs: 4000 });
        return;
      }
    }
    const request = {
      items, discount: this.discount || 0, paymentMethod: this.paymentMethod,
      customerName: this.customerName, customerPhone: this.customerPhone,
      ...(this.isCredit ? { amountPaid: this.amountPaid || 0 } : {})
    };
    // Every sale gets a unique reference. If the answer is lost (signal drops right after paying) the sale can be
    // sent again safely: the server recognises the reference and returns the original sale instead of making a second.
    const clientRef = OfflinePosService.newRef();
    const soldAtDate = new Date();
    const soldAt = soldAtDate.toISOString();
    const snapshot = {
      lines: this.cart.map(l => ({ productId: l.product.id, name: l.product.name, barcode: l.product.barcode, quantity: l.quantity, unitPrice: this.isBargain ? l.price : l.product.price })),
      subtotal: this.subtotal, discount: this.discount || 0, total: this.total
    };

    if (!navigator.onLine) { void this.saveOffline(clientRef, soldAt, request, snapshot); return; }

    this.posService.checkout({ ...request, clientRef }).subscribe({
      next: (res) => {
        this.isCheckingOut = false;
        this.lastReceipt = { ...res.receipt, lines: snapshot.lines, date: soldAtDate };
        this.rememberReceiptText(res.receipt);
        this.resetSale();
        this.notify.notify({ type: 'success', title: 'Sale complete', message: `Order ${res.orderNumber} — ৳${res.receipt.total}`, ttlMs: 4000 });
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 0) { void this.saveOffline(clientRef, soldAt, request, snapshot); return; }   // connection dropped: keep the sale
        this.isCheckingOut = false;
        this.notify.notify({ type: 'error', title: 'Checkout failed', message: err?.error?.message || 'Could not complete the sale.', ttlMs: 5000 });
      }
    });
  }

  private async saveOffline(clientRef: string, soldAt: string, request: any, snapshot: { lines: any[]; subtotal: number; discount: number; total: number }) {
    const saved = await this.offline.queueSale({ clientRef, soldAt, request, ...snapshot });
    this.isCheckingOut = false;
    if (!saved) {
      this.notify.notify({ type: 'error', title: 'Cannot save offline', message: 'This browser blocks local storage, so the sale was NOT saved. Reconnect and try again.', ttlMs: 8000 });
      return;
    }
    const texts = this.receiptText();
    this.lastReceipt = {
      header: texts.header, footer: texts.footer, orderNumber: saved.localNumber,
      subtotal: snapshot.subtotal, discount: snapshot.discount, total: snapshot.total,
      paymentMethod: request.paymentMethod, offline: true, lines: snapshot.lines, date: new Date(soldAt),
      ...(request.paymentMethod === 'CREDIT' ? { amountPaid: (request as any).amountPaid || 0, due: Math.max(snapshot.total - ((request as any).amountPaid || 0), 0), balance: null } : {})
    };
    this.resetSale();
    this.notify.notify({ type: 'warning', title: 'Saved offline', message: 'No connection. The sale is stored on this device and will be sent automatically.', ttlMs: 6000 });
  }

  private resetSale() {
    this.cart = [];
    this.discount = 0;
    this.amountPaid = null;
    this.owed = null;
    this.customerName = '';
    this.customerPhone = '';
  }

  private rememberReceiptText(r: any) {
    try { localStorage.setItem(RECEIPT_TEXT_KEY, JSON.stringify({ header: r?.header, footer: r?.footer })); } catch { /* ignore */ }
  }
  private receiptText(): { header: string; footer: string } {
    try {
      const t = JSON.parse(localStorage.getItem(RECEIPT_TEXT_KEY) || '{}');
      return { header: t.header || 'Thank you!', footer: t.footer || 'Please visit again.' };
    } catch { return { header: 'Thank you!', footer: 'Please visit again.' }; }
  }

  closeReceipt() { this.lastReceipt = null; this.printMsg = ''; }

  // ── Bluetooth receipt printing ───────────────────────────────
  printMsg = '';
  printOk = false;

  async printReceipt() {
    const r = this.lastReceipt;
    if (!r) return;
    this.printMsg = ''; this.printOk = false;
    try {
      await this.printer.print({
        header: r.header, footer: r.footer, orderNumber: r.orderNumber,
        lines: (r.lines || []).map((l: any) => ({ name: l.name, quantity: l.quantity, unitPrice: l.unitPrice })),
        subtotal: r.subtotal, discount: r.discount, total: r.total, paymentMethod: r.paymentMethod,
        amountPaid: r.amountPaid, due: r.due, balance: r.balance,
        cashierName: r.cashierName, date: r.date ? new Date(r.date) : new Date(), offline: r.offline
      });
      this.printOk = true; this.printMsg = 'Sent to the printer.';
    } catch {
      this.printMsg = this.printer.error$.value || '';       // a cancelled device chooser leaves this empty
    }
  }

  // ── Offline status bar ───────────────────────────────────────
  trackRef(_: number, s: OfflineSale) { return s.clientRef; }
}

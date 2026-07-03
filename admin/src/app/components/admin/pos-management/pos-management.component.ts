import { Component, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PosService, PosProduct } from '../../../services/pos/pos.service';
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

@Component({
  selector: 'app-pos-management',
  standalone: true,
  imports: [CommonModule, FormsModule, CameraScannerComponent],
  templateUrl: './pos-management.component.html',
  styleUrl: './pos-management.component.css'
})
export class PosManagementComponent implements OnInit {
  private posService = inject(PosService);
  private settingsService = inject(SettingsService);
  private notify = inject(GlobalNotificationService);
  authService = inject(AuthService);

  @ViewChild(CameraScannerComponent) scanner?: CameraScannerComponent;

  mode: PosMode = 'FIXED';
  barcodeInput = '';
  cart: CartLine[] = [];
  discount = 0;
  paymentMethod = 'CASH';
  customerName = '';
  customerPhone = '';
  isLoading = false;
  isCheckingOut = false;
  lastReceipt: any = null;

  // Admin-only mode settings panel
  showSettings = false;
  savingSettings = false;

  ngOnInit() { this.loadMode(); }

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
    this.isLoading = true;
    this.posService.lookup(barcode).subscribe({
      next: (p) => { this.addToCart(p); this.isLoading = false; },
      error: () => {
        this.isLoading = false;
        this.notify.notify({ type: 'warning', title: 'Not found', message: `No product matches "${barcode}".`, ttlMs: 3500 });
      }
    });
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
    this.posService.checkout({
      items, discount: this.discount || 0, paymentMethod: this.paymentMethod,
      customerName: this.customerName, customerPhone: this.customerPhone
    }).subscribe({
      next: (res) => {
        this.isCheckingOut = false;
        this.lastReceipt = res.receipt;
        this.cart = [];
        this.discount = 0;
        this.customerName = '';
        this.customerPhone = '';
        this.notify.notify({ type: 'success', title: 'Sale complete', message: `Order ${res.orderNumber} — ৳${res.receipt.total}`, ttlMs: 4000 });
      },
      error: (err) => {
        this.isCheckingOut = false;
        this.notify.notify({ type: 'error', title: 'Checkout failed', message: err?.error?.message || 'Could not complete the sale.', ttlMs: 5000 });
      }
    });
  }

  closeReceipt() { this.lastReceipt = null; }
}

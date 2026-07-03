import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InventoryService, InventoryItem, InventoryOverview, InventoryMovement } from '../../../services/inventory/inventory.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';

type Tab = 'overview' | 'purchase' | 'movements';

@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [CommonModule, FormsModule, ImgUrlPipe],
  templateUrl: './inventory.component.html',
  styleUrl: './inventory.component.css'
})
export class InventoryComponent implements OnInit {
  private inventory = inject(InventoryService);
  private notify = inject(GlobalNotificationService);
  private confirmSvc = inject(ConfirmDialogService);

  tab: Tab = 'overview';
  isLoading = false;

  // Overview
  overview: InventoryOverview | null = null;
  search = '';
  stockFilter: 'all' | 'low' | 'out' = 'all';

  // Adjust modal
  adjustItem: InventoryItem | null = null;
  adjustDelta = 0;
  adjustReason = '';
  saving = false;

  // Purchase form — category-filtered, searchable existing-product picker
  // (name + thumbnail) instead of one long flat dropdown.
  purchaseCategory = 'ALL';
  purchaseSearch = '';
  purchaseSelected: InventoryItem | null = null;
  purchaseQty = 0;
  purchaseCost: number | null = null;
  purchaseSupplier = '';

  // Cached picker results — recomputed only on explicit filter changes, not on
  // every change-detection cycle. A fresh array from a template-bound getter
  // makes *ngFor replace the DOM nodes on every CD tick, which can swallow a
  // click that lands between mousedown and mouseup.
  purchaseResults: InventoryItem[] = [];

  get purchaseCategories(): string[] {
    const cats = [...new Set((this.overview?.items || []).map(i => i.category).filter(Boolean))] as string[];
    return ['ALL', ...cats];
  }

  refreshPurchaseResults() {
    if (!this.overview) { this.purchaseResults = []; return; }
    let items = this.overview.items;
    if (this.purchaseCategory !== 'ALL') items = items.filter(i => i.category === this.purchaseCategory);
    const q = this.purchaseSearch.toLowerCase().trim();
    if (q) items = items.filter(i => i.name.toLowerCase().includes(q) || (i.sku || '').toLowerCase().includes(q));
    this.purchaseResults = items;
  }

  trackByItemId(_: number, i: InventoryItem): number { return i.id; }

  selectPurchaseItem(item: InventoryItem) {
    this.purchaseSelected = item;
    this.purchaseSearch = item.name;
  }
  clearPurchaseSelection() {
    this.purchaseSelected = null;
    this.purchaseSearch = '';
    this.refreshPurchaseResults();
  }

  // Movements
  movements: InventoryMovement[] = [];
  moveProductId: number | null = null;
  moveFrom = '';
  moveTo = '';

  ngOnInit() { this.loadOverview(); }

  setTab(t: Tab) {
    this.tab = t;
    if (t === 'movements' && this.movements.length === 0) this.loadMovements();
  }

  // ── Overview ───────────────────────────────────────────────
  loadOverview() {
    this.isLoading = true;
    this.inventory.getOverview().subscribe({
      next: (o) => { this.overview = o; this.isLoading = false; this.refreshPurchaseResults(); },
      error: () => { this.isLoading = false; }
    });
  }

  get threshold(): number { return this.overview?.lowStockThreshold ?? 5; }

  get filteredItems(): InventoryItem[] {
    if (!this.overview) return [];
    const q = this.search.toLowerCase().trim();
    return this.overview.items.filter(i => {
      if (this.stockFilter === 'low' && !(i.stockQuantity > 0 && i.stockQuantity <= this.threshold)) return false;
      if (this.stockFilter === 'out' && i.stockQuantity > 0) return false;
      if (q && !(i.name.toLowerCase().includes(q) || (i.sku || '').toLowerCase().includes(q))) return false;
      return true;
    });
  }

  stockClass(i: InventoryItem): string {
    if (i.stockQuantity <= 0) return 'out';
    if (i.stockQuantity <= this.threshold) return 'low';
    return 'ok';
  }

  // ── Adjust ─────────────────────────────────────────────────
  openAdjust(item: InventoryItem) {
    this.adjustItem = item;
    this.adjustDelta = 0;
    this.adjustReason = '';
  }
  closeAdjust() { this.adjustItem = null; }

  saveAdjust() {
    if (!this.adjustItem || !this.adjustDelta) {
      this.notify.notify({ type: 'warning', title: 'Enter quantity', message: 'Use + to add or − to remove stock.', ttlMs: 3500 });
      return;
    }
    this.saving = true;
    this.inventory.adjust(this.adjustItem.id, this.adjustDelta, this.adjustReason).subscribe({
      next: (res) => {
        if (this.overview) {
          const it = this.overview.items.find(x => x.id === res.productId);
          if (it) it.stockQuantity = res.stockQuantity;
        }
        this.saving = false;
        this.adjustItem = null;
        this.movements = []; // force reload on next visit
        this.notify.notify({ type: 'success', title: 'Stock updated', message: `New stock: ${res.stockQuantity}`, ttlMs: 3000 });
        this.recomputeSummary();
      },
      error: (err) => {
        this.saving = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: err?.error?.message || 'Could not adjust stock.', ttlMs: 5000 });
      }
    });
  }

  // ── Purchase ───────────────────────────────────────────────
  savePurchase() {
    if (!this.purchaseSelected || this.purchaseQty <= 0) {
      this.notify.notify({ type: 'warning', title: 'Missing details', message: 'Pick a product and enter quantity.', ttlMs: 3500 });
      return;
    }
    this.saving = true;
    this.inventory.purchase(this.purchaseSelected.id, this.purchaseQty, this.purchaseCost ?? undefined, this.purchaseSupplier).subscribe({
      next: (res) => {
        if (this.overview) {
          const it = this.overview.items.find(x => x.id === res.productId);
          if (it) it.stockQuantity = res.stockQuantity;
        }
        this.saving = false;
        this.notify.notify({ type: 'success', title: 'Stock received', message: `New stock: ${res.stockQuantity}`, ttlMs: 3000 });
        this.clearPurchaseSelection(); this.purchaseQty = 0; this.purchaseCost = null; this.purchaseSupplier = '';
        this.movements = [];
        this.recomputeSummary();
      },
      error: (err) => {
        this.saving = false;
        this.notify.notify({ type: 'error', title: 'Failed', message: err?.error?.message || 'Could not record purchase.', ttlMs: 5000 });
      }
    });
  }

  // ── Movements ──────────────────────────────────────────────
  loadMovements() {
    this.isLoading = true;
    this.inventory.getMovements(this.moveProductId ?? undefined, this.moveFrom || undefined, this.moveTo || undefined).subscribe({
      next: (m) => { this.movements = m; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  movementLabel(t: string): string {
    return { PURCHASE: 'Purchase', ADJUST_IN: 'Adjust +', ADJUST_OUT: 'Adjust −', SALE: 'Sale', RETURN: 'Return' }[t] || t;
  }

  private recomputeSummary() {
    if (!this.overview) return;
    const items = this.overview.items;
    this.overview.summary = {
      skuCount: items.length,
      totalUnits: items.reduce((s, i) => s + i.stockQuantity, 0),
      stockValue: items.reduce((s, i) => s + i.stockQuantity * i.wholesalePrice, 0),
      lowCount: items.filter(i => i.stockQuantity > 0 && i.stockQuantity <= this.threshold).length,
      outCount: items.filter(i => i.stockQuantity <= 0).length
    };
  }
}

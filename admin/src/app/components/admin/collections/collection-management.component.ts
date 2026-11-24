import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CollectionService, ShopCollection } from '../../../services/collection/collection.service';
import { ProductService } from '../../../services/product/product.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';

interface Pick { key: string; name: string; category: string; price: number; }

// Same key the storefront and API use: every size of a product shares "<base name>::<categoryId>".
const keyOf = (g: ProductGroup) => `${g.baseName}::${g.categoryId ?? ''}`;

@Component({
    selector: 'app-collection-management',
    imports: [CommonModule, FormsModule],
    template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🗂️ Collections</h1>
      <p class="page-subtitle">Hand-picked sets of products ("Eid Collection", "Back to school") with their own page on your store</p>
    </div>
    <button class="btn-primary" (click)="openAdd()">+ New Collection</button>
  </div>

  @if (successMsg) {
    <div class="alert-success">✅ {{ successMsg }}</div>
  }
  @if (errorMsg && !showModal) {
    <div class="alert-error">❌ {{ errorMsg }}</div>
  }

  <div class="table-card">
    @if (isLoading) {
      <div class="loading">Loading…</div>
    }
    @if (!isLoading) {
      <table class="data-table">
        <thead><tr><th>Collection</th><th>Products</th><th>Order</th><th>Status</th><th>Actions</th></tr></thead>
        <tbody>
          @for (c of rows; track c) {
            <tr>
              <td><b>{{ c.name }}</b><div style="font-size:12.5px;opacity:.65">/collection/{{ c.slug }}</div></td>
              <td>{{ c.productKeys.length }}</td>
              <td>{{ c.sortOrder }}</td>
              <td><span class="name-badge" [style.opacity]="c.isActive ? 1 : .6">{{ c.isActive ? 'Visible' : 'Hidden' }}</span></td>
              <td class="actions">
                <button class="btn-edit" (click)="openEdit(c)">✏️ Edit</button>
                <button class="btn-delete" (click)="remove(c)">🗑️ Delete</button>
              </td>
            </tr>
          }
          @if (rows.length === 0) {
            <tr><td colspan="5" class="empty-row">No collections yet.</td></tr>
          }
        </tbody>
      </table>
    }
  </div>

  @if (showModal) {
    <div class="modal-overlay" (click)="showModal = false">
      <div class="modal" style="max-width:640px" (click)="$event.stopPropagation()">
        <div class="modal-header"><h2>{{ form.id ? 'Edit Collection' : 'New Collection' }}</h2><button class="close-btn" (click)="showModal = false">✕</button></div>
        <div class="modal-body">
          @if (errorMsg) {
            <div class="alert-error">❌ {{ errorMsg }}</div>
          }
          <div class="form-group"><label>Name *</label><input class="form-input" [(ngModel)]="form.name" name="cname" maxlength="80" placeholder="Eid Collection" /></div>
          <div class="form-group"><label>Description</label><textarea class="form-input" rows="2" [(ngModel)]="form.description" name="cdesc" maxlength="500"></textarea></div>
          <div style="display:flex;gap:16px;align-items:end;flex-wrap:wrap">
            <div class="form-group" style="margin:0"><label>Display order</label><input class="form-input" type="number" [(ngModel)]="form.sortOrder" name="corder" style="width:110px" /></div>
            <label style="display:flex;gap:8px;align-items:center;margin-bottom:8px"><input type="checkbox" [(ngModel)]="form.isActive" name="cactive" /> Visible on the store</label>
          </div>
          <div class="form-group" style="margin-top:12px">
            <label>Products ({{ selected.size }} selected)</label>
            <input class="form-input" [(ngModel)]="pickSearch" name="psearch" placeholder="🔍 Filter products" />
            <div style="max-height:260px;overflow:auto;margin-top:8px;border:1px solid rgba(255,255,255,.1);border-radius:10px">
              @for (p of filteredPicks(); track p) {
                <label style="display:flex;gap:10px;align-items:center;padding:8px 12px;border-bottom:1px solid rgba(255,255,255,.06);cursor:pointer">
                  <input type="checkbox" [checked]="selected.has(p.key)" (change)="toggle(p.key)" />
                  <span style="flex:1">{{ p.name }}<small style="opacity:.6"> · {{ p.category }}</small></span>
                  <span style="opacity:.8">৳{{ p.price | number:'1.0-0' }}</span>
                </label>
              }
              @if (filteredPicks().length === 0) {
                <div style="padding:14px;opacity:.6;text-align:center">No products match.</div>
              }
            </div>
          </div>
        </div>
        <div class="modal-footer"><button class="btn-secondary" (click)="showModal = false">Cancel</button><button class="btn-primary" (click)="save()" [disabled]="saving">{{ saving ? 'Saving…' : 'Save' }}</button></div>
      </div>
    </div>
  }
</div>`
})
export class CollectionManagementComponent implements OnInit {
  private svc = inject(CollectionService);
  private products = inject(ProductService);
  private confirmSvc = inject(ConfirmDialogService);

  rows: ShopCollection[] = [];
  picks: Pick[] = [];
  selected = new Set<string>();
  pickSearch = '';
  isLoading = false; saving = false; showModal = false;
  successMsg = ''; errorMsg = '';
  form: { id: number | null; name: string; description: string; isActive: boolean; sortOrder: number } =
    { id: null, name: '', description: '', isActive: true, sortOrder: 0 };

  ngOnInit() {
    this.load();
    this.products.getProducts().subscribe({
      next: (p) => this.picks = groupProducts(p).map(g => ({ key: keyOf(g), name: g.baseName, category: g.categoryName, price: g.price }))
        .sort((a, b) => a.name.localeCompare(b.name)),
      error: () => this.errorMsg = 'Could not load your products.'
    });
  }

  load() {
    this.isLoading = true;
    this.svc.getAll().subscribe({
      next: (r) => { this.rows = r; this.isLoading = false; },
      error: () => { this.isLoading = false; this.errorMsg = 'Could not load collections.'; }
    });
  }

  filteredPicks() {
    const q = this.pickSearch.trim().toLowerCase();
    return q ? this.picks.filter(p => p.name.toLowerCase().includes(q) || p.category.toLowerCase().includes(q)) : this.picks;
  }

  toggle(key: string) { this.selected.has(key) ? this.selected.delete(key) : this.selected.add(key); }

  openAdd() {
    this.form = { id: null, name: '', description: '', isActive: true, sortOrder: this.rows.length + 1 };
    this.selected = new Set(); this.pickSearch = ''; this.errorMsg = ''; this.showModal = true;
  }

  openEdit(c: ShopCollection) {
    this.form = { id: c.id, name: c.name, description: c.description || '', isActive: c.isActive, sortOrder: c.sortOrder };
    this.selected = new Set(c.productKeys); this.pickSearch = ''; this.errorMsg = ''; this.showModal = true;
  }

  save() {
    this.errorMsg = '';
    if (this.form.name.trim().length < 2) { this.errorMsg = 'Enter a collection name.'; return; }
    // Keep earlier picks even for products that are no longer listed, and preserve the owner's pick order.
    const keys = [...this.selected];
    const dto = { name: this.form.name.trim(), description: this.form.description.trim() || null, isActive: this.form.isActive, sortOrder: this.form.sortOrder || 0, productKeys: keys };
    this.saving = true;
    (this.form.id ? this.svc.update(this.form.id, dto) : this.svc.create(dto)).subscribe({
      next: () => { this.saving = false; this.showModal = false; this.flash('Collection saved.'); this.load(); },
      error: (e) => { this.saving = false; this.errorMsg = e?.error?.message || 'Could not save the collection.'; }
    });
  }

  async remove(c: ShopCollection) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete collection', message: `Delete "${c.name}"? The products themselves stay.`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.svc.remove(c.id).subscribe({ next: () => { this.flash('Collection deleted.'); this.load(); }, error: () => this.errorMsg = 'Could not delete.' });
  }

  private flash(m: string) { this.successMsg = m; setTimeout(() => this.successMsg = '', 3000); }
}

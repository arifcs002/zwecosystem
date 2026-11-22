import { Injectable } from '@angular/core';

export interface WishlistEntry {
  key: string;          // stable group key: "<base name>::<categoryId>"
  productId: number;    // a representative variant id (used for the product link)
  name: string;
  price: number;
  image: string;
  categoryName: string;
  addedAt: number;
}

const STORAGE_KEY = 'shop_wishlist_v1';

// Per-browser wishlist (localStorage). It is scoped by tenant so two shops opened in
// the same browser never mix their lists. No backend needed.
@Injectable({ providedIn: 'root' })
export class WishlistService {
  private items: WishlistEntry[] = [];
  private tenantId = '';

  /** Load the list for a tenant (call when a storefront page resolves its company). */
  use(tenantId: string | number) {
    const id = String(tenantId);
    if (id === this.tenantId) return;
    this.tenantId = id;
    try {
      const raw = localStorage.getItem(`${STORAGE_KEY}:${id}`);
      this.items = raw ? JSON.parse(raw) : [];
      if (!Array.isArray(this.items)) this.items = [];
    } catch { this.items = []; }
  }

  private persist() {
    try { localStorage.setItem(`${STORAGE_KEY}:${this.tenantId}`, JSON.stringify(this.items)); } catch { /* storage full/blocked */ }
  }

  get all(): WishlistEntry[] { return this.items; }
  get count(): number { return this.items.length; }

  has(key: string): boolean { return this.items.some(i => i.key === key); }

  toggle(entry: Omit<WishlistEntry, 'addedAt'>): boolean {
    if (!this.tenantId) this.use(localStorage.getItem('tenant_company_id') || '0');
    const idx = this.items.findIndex(i => i.key === entry.key);
    if (idx >= 0) { this.items.splice(idx, 1); this.persist(); return false; }
    this.items.unshift({ ...entry, addedAt: Date.now() });
    this.persist();
    return true;
  }

  remove(key: string) {
    this.items = this.items.filter(i => i.key !== key);
    this.persist();
  }
}

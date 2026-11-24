import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { Product, ProductService } from '../../../services/product/product.service';

interface OfferRow {
  id: number; name: string; offerType: 'BUY_GET' | 'BUNDLE' | 'QTY_PERCENT'; productId: number | null; productName: string | null;
  buyQty: number; getQty: number; minQty: number; percent: number; bundlePrice: number; startsAt: string | null; endsAt: string | null; isActive: boolean;
  items: { productId: number; name: string; quantity: number }[];
}

// Automatic offers: buy X get Y free, a bundle at one price, or a percentage off when buying several.
@Component({
  selector: 'app-offers',
  imports: [CommonModule, FormsModule],
  template: `
<div class="page-container">
  <div class="page-header">
    <div>
      <h1 class="page-title">🎁 Offers</h1>
      <p class="page-subtitle">Discounts that apply by themselves at the till and in the online cart</p>
    </div>
    <button class="btn-primary" *ngIf="!editing" (click)="startNew()">+ New offer</button>
  </div>

  <div class="alert-error" *ngIf="errorMsg" role="alert">❌ {{ errorMsg }}</div>
  <div class="alert-success" *ngIf="okMsg" role="status">✅ {{ okMsg }}</div>

  <div class="table-card" style="padding:16px;margin-bottom:16px" *ngIf="editing">
    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px">
      <label style="font-size:12.5px">Offer name
        <input class="form-input" maxlength="80" [(ngModel)]="f.name" style="margin-top:4px" placeholder="e.g. Buy 2 get 1 free"></label>
      <label style="font-size:12.5px">Type
        <select class="form-input" [(ngModel)]="f.type" style="margin-top:4px">
          <option value="BUY_GET">Buy some, get some free</option>
          <option value="BUNDLE">Bundle at one price</option>
          <option value="QTY_PERCENT">% off when buying several</option>
        </select></label>
      <label style="font-size:12.5px" *ngIf="f.type !== 'BUNDLE'">Product {{ f.type === 'QTY_PERCENT' ? '(empty = every product)' : '' }}
        <select class="form-input" [(ngModel)]="f.productId" style="margin-top:4px">
          <option [ngValue]="null">{{ f.type === 'QTY_PERCENT' ? 'Every product' : 'Choose…' }}</option>
          <option *ngFor="let p of products" [ngValue]="p.id">{{ p.name }} (৳{{ p.price }})</option>
        </select></label>
      <ng-container *ngIf="f.type === 'BUY_GET'">
        <label style="font-size:12.5px">Customer buys <input class="form-input" type="number" min="1" [(ngModel)]="f.buyQty" style="margin-top:4px"></label>
        <label style="font-size:12.5px">…and gets free <input class="form-input" type="number" min="1" [(ngModel)]="f.getQty" style="margin-top:4px"></label>
      </ng-container>
      <ng-container *ngIf="f.type === 'QTY_PERCENT'">
        <label style="font-size:12.5px">When buying at least <input class="form-input" type="number" min="2" [(ngModel)]="f.minQty" style="margin-top:4px"></label>
        <label style="font-size:12.5px">Discount (%) <input class="form-input" type="number" min="1" max="90" [(ngModel)]="f.percent" style="margin-top:4px"></label>
      </ng-container>
      <label style="font-size:12.5px">Starts (optional) <input class="form-input" type="date" [(ngModel)]="f.startsAt" style="margin-top:4px"></label>
      <label style="font-size:12.5px">Ends (optional) <input class="form-input" type="date" [(ngModel)]="f.endsAt" style="margin-top:4px"></label>
    </div>

    <div *ngIf="f.type === 'BUNDLE'" style="margin-top:12px">
      <b style="font-size:13px">Products in the bundle</b>
      <div *ngFor="let it of f.items; let i = index" style="display:flex;gap:8px;margin-top:6px;flex-wrap:wrap">
        <select class="form-input" style="flex:1;min-width:200px" [(ngModel)]="it.productId">
          <option [ngValue]="null">Choose…</option>
          <option *ngFor="let p of products" [ngValue]="p.id">{{ p.name }} (৳{{ p.price }})</option>
        </select>
        <input class="form-input" type="number" min="1" style="width:90px" [(ngModel)]="it.quantity" aria-label="Quantity">
        <button class="btn-secondary" (click)="f.items.splice(i, 1)" [disabled]="f.items.length <= 2">Remove</button>
      </div>
      <div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap;align-items:end">
        <button class="btn-secondary" (click)="f.items.push({ productId: null, quantity: 1 })">+ Add a product</button>
        <label style="font-size:12.5px">Bundle price (৳) <input class="form-input" type="number" min="1" [(ngModel)]="f.bundlePrice" style="margin-top:4px;width:140px"></label>
        <span style="opacity:.7;font-size:13px">Regular price: ৳{{ regular() | number:'1.0-0' }}</span>
      </div>
    </div>

    <label style="display:flex;gap:8px;align-items:center;margin-top:12px;font-size:13px"><input type="checkbox" [(ngModel)]="f.isActive"> Offer is ON</label>
    <div style="display:flex;gap:8px;margin-top:12px">
      <button class="btn-primary" (click)="save()" [disabled]="busy">{{ busy ? 'Saving…' : 'Save offer' }}</button>
      <button class="btn-secondary" (click)="editing = false">Cancel</button>
    </div>
    <p style="opacity:.6;font-size:12.5px;margin:10px 0 0">One item can be used by only one offer. Online, the shopper gets the largest of: offer, coupon, member discount (they don't add up). At the till an offer adds to any discount you type.</p>
  </div>

  <div class="table-card" style="padding:16px" *ngIf="!editing">
    <p *ngIf="!rows.length" style="opacity:.65">No offers yet.</p>
    <table class="data-table" style="width:100%;min-width:0;font-size:13.5px" *ngIf="rows.length">
      <thead><tr><th>Offer</th><th>What it does</th><th>When</th><th>Status</th><th></th></tr></thead>
      <tbody><tr *ngFor="let o of rows">
        <td><b>{{ o.name }}</b></td>
        <td>{{ describe(o) }}</td>
        <td style="font-size:12.5px">{{ o.startsAt ? (o.startsAt | date:'d MMM') : 'now' }} → {{ o.endsAt ? (o.endsAt | date:'d MMM yyyy') : 'no end' }}</td>
        <td><span [style.color]="o.isActive ? '#34d399' : '#9ca3af'">{{ o.isActive ? 'ON' : 'OFF' }}</span></td>
        <td style="text-align:right;white-space:nowrap">
          <button class="btn-secondary" (click)="toggle(o)">{{ o.isActive ? 'Turn off' : 'Turn on' }}</button>
          <button class="btn-secondary" (click)="edit(o)">Edit</button>
          <button class="btn-secondary" (click)="remove(o)">Delete</button>
        </td>
      </tr></tbody>
    </table>
  </div>
</div>`
})
export class OffersComponent implements OnInit {
  private http = inject(HttpClient);
  private productApi = inject(ProductService);
  private api = `${environment.apiUrl}/offers`;
  rows: OfferRow[] = []; products: Product[] = [];
  editing = false; editId: number | null = null;
  f: any = this.blank();
  busy = false; errorMsg = ''; okMsg = '';

  ngOnInit() { this.load(); this.productApi.getProducts().subscribe({ next: p => this.products = p, error: () => { /* the list just stays empty */ } }); }
  load() { this.http.get<OfferRow[]>(this.api).subscribe({ next: r => this.rows = r, error: () => this.errorMsg = 'Could not load the offers.' }); }

  private blank() { return { name: '', type: 'BUY_GET', productId: null, buyQty: 2, getQty: 1, minQty: 3, percent: 10, bundlePrice: null, startsAt: '', endsAt: '', isActive: true, items: [{ productId: null, quantity: 1 }, { productId: null, quantity: 1 }] }; }
  startNew() { this.f = this.blank(); this.editId = null; this.editing = true; this.errorMsg = ''; this.okMsg = ''; }
  regular(): number { return (this.f.items as { productId: number | null; quantity: number }[]).reduce((s, i) => s + (this.products.find(p => p.id === i.productId)?.price || 0) * (i.quantity || 0), 0); }

  describe(o: OfferRow): string {
    if (o.offerType === 'BUY_GET') return `Buy ${o.buyQty}, get ${o.getQty} free — ${o.productName || ''}`;
    if (o.offerType === 'QTY_PERCENT') return `${o.percent}% off when buying ${o.minQty}+ of ${o.productName || 'any product'}`;
    return `Bundle: ${o.items.map(i => i.quantity + '× ' + i.name).join(' + ')} for ৳${o.bundlePrice}`;
  }

  edit(o: OfferRow) {
    this.f = { name: o.name, type: o.offerType, productId: o.productId, buyQty: o.buyQty || 2, getQty: o.getQty || 1, minQty: o.minQty || 3, percent: o.percent || 10, bundlePrice: o.bundlePrice || null,
      startsAt: o.startsAt ? o.startsAt.slice(0, 10) : '', endsAt: o.endsAt ? o.endsAt.slice(0, 10) : '', isActive: o.isActive,
      items: o.items.length ? o.items.map(i => ({ productId: i.productId, quantity: i.quantity })) : this.blank().items };
    this.editId = o.id; this.editing = true; this.errorMsg = ''; this.okMsg = '';
  }

  private body(f: any, isActive = f.isActive) {
    return {
      name: f.name, type: f.type, productId: f.type === 'BUNDLE' ? null : f.productId,
      buyQty: f.buyQty, getQty: f.getQty, minQty: f.minQty, percent: f.percent, bundlePrice: f.bundlePrice,
      startsAt: f.startsAt ? new Date(f.startsAt + 'T00:00:00+06:00').toISOString() : null,
      endsAt: f.endsAt ? new Date(f.endsAt + 'T23:59:59+06:00').toISOString() : null,
      isActive, items: f.type === 'BUNDLE' ? f.items.filter((i: any) => i.productId).map((i: any) => ({ productId: i.productId, quantity: i.quantity })) : []
    };
  }

  save() {
    this.busy = true; this.errorMsg = ''; this.okMsg = '';
    const req = this.editId ? this.http.put(`${this.api}/${this.editId}`, this.body(this.f)) : this.http.post(this.api, this.body(this.f));
    req.subscribe({
      next: () => { this.busy = false; this.editing = false; this.okMsg = 'Offer saved.'; this.load(); },
      error: (e: HttpErrorResponse) => { this.busy = false; this.errorMsg = e?.error?.message || 'Could not save the offer.'; }
    });
  }

  toggle(o: OfferRow) {
    const f = { name: o.name, type: o.offerType, productId: o.productId, buyQty: o.buyQty, getQty: o.getQty, minQty: o.minQty, percent: o.percent, bundlePrice: o.bundlePrice,
      startsAt: o.startsAt ? o.startsAt.slice(0, 10) : '', endsAt: o.endsAt ? o.endsAt.slice(0, 10) : '', items: o.items };
    this.http.put(`${this.api}/${o.id}`, this.body(f, !o.isActive)).subscribe({ next: () => this.load(), error: (e: HttpErrorResponse) => this.errorMsg = e?.error?.message || 'Could not change the offer.' });
  }

  remove(o: OfferRow) {
    if (!confirm(`Delete the offer "${o.name}"?`)) return;
    this.http.delete(`${this.api}/${o.id}`).subscribe({ next: () => { this.okMsg = 'Offer deleted.'; this.load(); }, error: () => this.errorMsg = 'Could not delete.' });
  }
}

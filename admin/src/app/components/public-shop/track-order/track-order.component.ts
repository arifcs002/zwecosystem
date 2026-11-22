import { Component, OnInit, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { OrderService, TrackedOrder } from '../../../services/order/order.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';

const STEPS = ['PENDING', 'PROCESSING', 'PACKED', 'SHIPPED', 'COMPLETED'];
const LABEL: Record<string, string> = {
  PENDING: 'Order placed', PROCESSING: 'Processing', PACKED: 'Packed', SHIPPED: 'Out for delivery',
  COMPLETED: 'Delivered', CANCELLED: 'Cancelled'
};

// Customer-facing order tracking: order number + phone → status steps, items, courier.
@Component({
  selector: 'app-track-order',
  standalone: true,
  imports: [TranslatePipe, CommonModule, FormsModule, RouterModule, ImgUrlPipe, ImgFallbackDirective, ShopHeaderComponent, ShopFooterComponent],
  template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="groups"></app-shop-header>
    <main class="max-w-2xl mx-auto w-full px-4 py-8 flex-1">
      <h1 class="text-2xl font-bold text-gray-900 mb-1">{{ 'Track your order' | t }}</h1>
      <p class="text-sm text-gray-500 mb-5">Enter the order number from your confirmation and the mobile number you ordered with.</p>

      <form (ngSubmit)="search()" class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5 space-y-3">
        <div>
          <label for="trk-no" class="block text-xs font-semibold text-gray-500 mb-1">{{ 'Order number' | t }}</label>
          <input id="trk-no" [(ngModel)]="orderNumber" name="orderNumber" placeholder="e.g. WEB-6393…" autocomplete="off"
                 class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
        </div>
        <div>
          <label for="trk-ph" class="block text-xs font-semibold text-gray-500 mb-1">{{ 'Mobile number' | t }}</label>
          <input id="trk-ph" [(ngModel)]="phone" name="phone" inputmode="tel" placeholder="01XXXXXXXXX" autocomplete="tel"
                 class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
        </div>
        <button type="submit" [disabled]="busy || !orderNumber.trim() || !phone.trim()"
                class="w-full py-3 bg-indigo-600 text-white font-bold rounded-xl hover:bg-indigo-700 transition disabled:opacity-50">
          {{ busy ? ('Looking up…' | t) : ('Track order' | t) }}
        </button>
        <p *ngIf="error" role="alert" class="text-sm text-rose-600 text-center">{{ error }}</p>
      </form>

      <section *ngIf="order as o" class="mt-6 bg-white rounded-2xl border border-gray-100 shadow-sm p-5">
        <div class="flex items-start justify-between gap-3 mb-4">
          <div>
            <p class="text-xs text-gray-400">{{ 'Order' | t }}</p>
            <p class="font-bold text-gray-900 break-all">{{ o.orderNumber }}</p>
            <p class="text-xs text-gray-400 mt-0.5">Placed {{ o.createdDate | date:'medium' }}</p>
          </div>
          <span class="text-xs font-bold px-3 py-1 rounded-full flex-shrink-0"
                [ngClass]="o.status === 'CANCELLED' ? 'bg-rose-100 text-rose-700' : o.status === 'COMPLETED' ? 'bg-emerald-100 text-emerald-700' : 'bg-indigo-100 text-indigo-700'">{{ label(o.status) }}</span>
        </div>

        <ol *ngIf="o.status !== 'CANCELLED'" class="flex items-center mb-5" [attr.aria-label]="'Order progress' | t">
          <li *ngFor="let s of steps; let i = index" class="flex-1 flex items-center last:flex-none">
            <div class="flex flex-col items-center gap-1">
              <span class="w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold"
                    [ngClass]="i <= stepIndex(o.status) ? 'bg-indigo-600 text-white' : 'bg-gray-100 text-gray-400'">{{ i <= stepIndex(o.status) ? '✓' : i + 1 }}</span>
              <span class="text-[10px] text-center w-14 leading-tight" [ngClass]="i <= stepIndex(o.status) ? 'text-gray-700 font-semibold' : 'text-gray-400'">{{ label(s) }}</span>
            </div>
            <span *ngIf="i < steps.length - 1" class="flex-1 h-0.5 mx-1 -mt-4" [ngClass]="i < stepIndex(o.status) ? 'bg-indigo-600' : 'bg-gray-200'"></span>
          </li>
        </ol>

        <div *ngIf="o.courierName || o.trackingNumber" class="mb-4 p-3 rounded-xl bg-indigo-50 text-sm text-indigo-900">
          <span class="font-semibold">{{ 'Courier:' | t }}</span> {{ o.courierName || '—' }}
          <span *ngIf="o.trackingNumber"> · <span class="font-semibold">{{ 'Tracking:' | t }}</span> {{ o.trackingNumber }}</span>
        </div>

        <div class="divide-y divide-gray-100 mb-4">
          <div *ngFor="let it of o.items" class="py-2.5 flex items-center gap-3">
            <img [src]="it.imageUrl | imgUrl" appImgFallback class="w-12 h-12 rounded-lg object-cover bg-gray-100" alt="">
            <div class="flex-1 min-w-0"><p class="text-sm font-medium text-gray-800 truncate">{{ it.productName }}</p><p class="text-xs text-gray-400">{{ it.quantity }} × ৳{{ it.unitPrice | number:'1.0-0' }}</p></div>
            <span class="text-sm font-bold text-gray-900">৳{{ it.totalPrice | number:'1.0-0' }}</span>
          </div>
        </div>

        <div class="space-y-1.5 text-sm">
          <div class="flex justify-between text-gray-600"><span>{{ 'Subtotal' | t }}</span><span>৳{{ o.subtotal | number:'1.0-0' }}</span></div>
          <div *ngIf="o.discount > 0" class="flex justify-between text-emerald-600"><span>{{ 'Discount' | t }}<span *ngIf="o.couponCode"> ({{ o.couponCode }})</span></span><span>−৳{{ o.discount | number:'1.0-0' }}</span></div>
          <div class="flex justify-between text-gray-600"><span>{{ 'Delivery' | t }}</span><span>{{ o.shippingFee === 0 ? 'FREE' : '৳' + (o.shippingFee | number:'1.0-0') }}</span></div>
          <div class="flex justify-between font-bold text-gray-900 text-base pt-2 border-t border-gray-100"><span>Total ({{ o.paymentMethod }})</span><span>৳{{ o.total | number:'1.0-0' }}</span></div>
        </div>

        <div *ngIf="o.timeline.length" class="mt-5 pt-4 border-t border-gray-100">
          <p class="text-xs font-bold text-gray-400 uppercase tracking-wide mb-2">{{ 'Updates' | t }}</p>
          <ul class="space-y-1.5">
            <li *ngFor="let t of o.timeline" class="text-sm text-gray-600 flex justify-between gap-3"><span>{{ label(t.status) }}</span><span class="text-gray-400 text-xs">{{ t.date | date:'MMM d, h:mm a' }}</span></li>
          </ul>
        </div>
      </section>
    </main>
    <app-shop-footer [store]="store" [slug]="slug"></app-shop-footer>
  </div>`
})
export class TrackOrderComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private tenant = inject(TenantService);
  private storefront = inject(StorefrontService);
  private orders = inject(OrderService);

  slug = '';
  store: StorefrontData | null = null;
  groups: ProductGroup[] = [];
  orderNumber = '';
  phone = '';
  busy = false;
  error = '';
  order: TrackedOrder | null = null;
  steps = STEPS;

  label(s: string) { return LABEL[s] || s; }
  stepIndex(status: string) { const i = STEPS.indexOf(status); return i < 0 ? 0 : i; }

  ngOnInit() {
    this.slug = this.tenant.getSlug(this.route);
    // Prefill from the link on the order-confirmation screen (?order=...) and the phone last used at checkout.
    const q = this.route.snapshot.queryParamMap;
    this.orderNumber = q.get('order') || '';
    this.phone = q.get('phone') || '';
    this.storefront.getStorefront(this.slug).subscribe({
      next: (data) => {
        this.store = data;
        localStorage.setItem('tenant_company_id', String(data.company.id)); // X-Tenant-ID for the track call
        this.groups = groupProducts(data.products);
        if (this.orderNumber && this.phone) this.search();
      },
      error: () => { this.error = 'This store could not be found.'; }
    });
  }

  search() {
    if (this.busy) return;
    this.error = ''; this.order = null; this.busy = true;
    this.orders.trackOrder(this.orderNumber.trim(), this.phone.trim()).subscribe({
      next: (o) => { this.order = o; this.busy = false; },
      error: (err) => {
        this.busy = false;
        this.error = err?.status === 429 ? 'Too many attempts. Please wait a minute and try again.'
          : (err?.error?.message || 'We couldn’t find an order with those details.');
      }
    });
  }
}

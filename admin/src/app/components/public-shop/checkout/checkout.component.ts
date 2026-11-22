import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CartService } from '../../../services/cart/cart.service';
import { OrderService } from '../../../services/order/order.service';
import { CouponService } from '../../../services/coupon/coupon.service';
import { AbandonedCartService } from '../../../services/abandoned-cart/abandoned-cart.service';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';

interface PayMethod { value: string; label: string; note?: string; }

// Bangladeshi mobile, same rule as the server (PhoneUtil.NormalizeBd).
const BD_MOBILE = /^(?:\+?88)?01[3-9]\d{8}$/;

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [TranslatePipe, CommonModule, FormsModule, RouterModule, ImgFallbackDirective, ShopHeaderComponent, ShopFooterComponent],
  templateUrl: './checkout.component.html'
})
export class CheckoutComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private storefront = inject(StorefrontService);
  private orderService = inject(OrderService);
  private couponService = inject(CouponService);
  private abandoned = inject(AbandonedCartService);
  private notify = inject(GlobalNotificationService);
  private tenant = inject(TenantService);
  cart = inject(CartService);

  companySlug = '';
  store: StorefrontData | null = null;
  groups: ProductGroup[] = [];
  get companyName() { return this.store?.company.name || ''; }

  payMethods: PayMethod[] = [];
  deliveryCharge = 0;
  freeDeliveryAbove = 0;

  // Coupon
  couponInput = '';
  appliedCoupon = '';
  couponDiscount = 0;
  couponMessage = '';
  couponError = false;
  couponBusy = false;

  placing = false;
  placedOrderNumber: string | null = null;
  placedTotal = 0;
  placedPhone = '';
  copied = false;

  private captureTimer: any = null;
  private lastCaptured = '';

  ngOnInit() {
    this.companySlug = this.tenant.getSlug(this.route);
    this.storefront.getStorefront(this.companySlug).subscribe({
      next: (data) => {
        this.store = data;
        this.groups = groupProducts(data.products);
        localStorage.setItem('tenant_company_id', data.company.id.toString());
        this.applySettings(data);
        this.scheduleCapture();
      },
      error: () => { /* header/footer just stay empty; the form still works */ }
    });
  }

  ngOnDestroy() { if (this.captureTimer) clearTimeout(this.captureTimer); }

  private applySettings(data: StorefrontData) {
    const val = (k: string) => data.settings.find(s => s.key === k)?.value;
    const on = (k: string, def = false) => { const v = val(k); return v == null ? def : v === 'true'; };

    this.deliveryCharge = +(val('delivery_charge') || 0) || 0;
    this.freeDeliveryAbove = +(val('free_delivery_above') || 0) || 0;

    const methods: PayMethod[] = [];
    if (on('payment_cod', true)) methods.push({ value: 'COD', label: 'Cash on Delivery' });
    if (on('payment_bkash')) methods.push({ value: 'BKASH', label: 'bKash', note: val('bkash_number') ? `Send to ${val('bkash_number')}` : undefined });
    if (on('payment_online')) methods.push({ value: 'ONLINE', label: 'Online Payment' });
    if (methods.length === 0) methods.push({ value: 'COD', label: 'Cash on Delivery' });
    this.payMethods = methods;

    if (!methods.some(m => m.value === this.cart.customerInfo.paymentMethod)) {
      this.cart.customerInfo.paymentMethod = methods[0].value;
    }
  }

  get subtotal() { return this.cart.cartSubtotal; }
  get shippingFee() {
    if (this.freeDeliveryAbove > 0 && this.subtotal >= this.freeDeliveryAbove) return 0;
    return this.deliveryCharge;
  }
  get total() { return Math.max(this.subtotal + this.shippingFee - this.couponDiscount, 0); }
  get phoneLooksValid() { return BD_MOBILE.test((this.cart.customerInfo.mobile || '').replace(/[\s\-()]/g, '')); }

  backToShop() { this.router.navigate(this.tenant.routeSegments(this.companySlug)); }
  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.companySlug, ...parts); }

  // ── Cart edits (also keep an applied coupon honest) ──
  changeQty(item: any, delta: number) { this.cart.updateQuantity(item, delta); this.afterCartChange(); }
  removeItem(item: any) { this.cart.removeFromCart(item); this.afterCartChange(); }
  private afterCartChange() {
    if (this.appliedCoupon) this.checkCoupon(this.appliedCoupon, true);
    this.scheduleCapture();
  }

  // ── Coupon ──
  applyCoupon() {
    const code = this.couponInput.trim();
    if (!code) { this.couponError = true; this.couponMessage = 'Enter a coupon code.'; return; }
    this.checkCoupon(code, false);
  }

  removeCoupon() {
    this.appliedCoupon = ''; this.couponDiscount = 0; this.couponMessage = ''; this.couponError = false; this.couponInput = '';
  }

  private checkCoupon(code: string, silent: boolean) {
    if (this.couponBusy) return;
    this.couponBusy = true;
    const items = this.cart.cart.map(i => ({ productId: i.product.id, quantity: i.quantity }));
    this.couponService.validate(code, items).subscribe({
      next: (r) => {
        this.couponBusy = false;
        if (r.valid) {
          this.appliedCoupon = r.code || code.toUpperCase(); this.couponDiscount = r.discount;
          this.couponError = false; this.couponMessage = `Coupon ${this.appliedCoupon} applied — you save ৳${Math.round(r.discount)}.`;
        } else {
          const wasApplied = !!this.appliedCoupon;
          this.appliedCoupon = ''; this.couponDiscount = 0; this.couponError = true;
          this.couponMessage = wasApplied && silent ? `Coupon removed: ${r.message}` : r.message;
        }
      },
      error: (err) => {
        this.couponBusy = false;
        this.couponError = true;
        this.couponMessage = err?.error?.message || 'Could not check the coupon. Please try again.';
      }
    });
  }

  // ── Abandoned cart capture: once there's a valid phone and items, remember the cart (debounced) ──
  scheduleCapture() {
    if (this.captureTimer) clearTimeout(this.captureTimer);
    this.captureTimer = setTimeout(() => this.capture(), 1500);
  }

  private capture() {
    if (this.placedOrderNumber || this.cart.cart.length === 0 || !this.phoneLooksValid) return;
    const info = this.cart.customerInfo;
    const items = this.cart.cart.map(i => ({ productId: i.product.id, quantity: i.quantity }));
    const fingerprint = JSON.stringify([info.mobile, info.name, info.address, items]);
    if (fingerprint === this.lastCaptured) return;
    this.lastCaptured = fingerprint;
    this.abandoned.capture({
      sessionKey: this.cart.sessionKey,
      customerName: info.name?.trim() || undefined,
      customerPhone: info.mobile.trim(),
      shippingAddress: info.address?.trim() || undefined,
      items
    }).subscribe({ error: () => { this.lastCaptured = ''; /* best-effort; never bother the shopper */ } });
  }

  // ── Place order ──
  placeOrder() {
    if (!this.cart.isCheckoutValid()) {
      this.notify.notify({ type: 'warning', title: 'Missing details', message: 'Please fill in name, phone and address.', ttlMs: 4000 });
      return;
    }
    if (this.placing) return;
    this.placing = true;
    const phone = this.cart.customerInfo.mobile.trim();
    this.orderService.placePublicOrder(this.cart.buildOrderRequest(this.appliedCoupon)).subscribe({
      next: (res) => {
        this.placedOrderNumber = res.orderNumber;
        this.placedTotal = res.total;
        this.placedPhone = phone;
        this.cart.clearAfterOrder(res.orderNumber);
        this.removeCoupon();
        this.placing = false;
        window.scrollTo({ top: 0, behavior: 'smooth' });
      },
      error: (err) => {
        this.placing = false;
        const msg = err?.error?.message || 'Could not place your order.';
        // A coupon that stopped being valid between "apply" and "place" must not block the order.
        if (this.appliedCoupon && /coupon/i.test(msg)) this.removeCoupon();
        this.notify.notify({ type: 'error', title: 'Order failed', message: msg, ttlMs: 6000 });
      }
    });
  }

  copyOrderNumber() {
    if (!this.placedOrderNumber) return;
    navigator.clipboard?.writeText(this.placedOrderNumber).then(() => {
      this.copied = true; setTimeout(() => this.copied = false, 2000);
    }).catch(() => { /* clipboard blocked — the number is visible on screen anyway */ });
  }
}

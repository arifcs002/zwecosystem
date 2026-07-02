import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CompanyService } from '../../../services/company/company.service';
import { SettingsService } from '../../../services/settings/settings.service';
import { CartService } from '../../../services/cart/cart.service';
import { OrderService } from '../../../services/order/order.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

interface PayMethod { value: string; label: string; note?: string; }

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './checkout.component.html'
})
export class CheckoutComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private settingsService = inject(SettingsService);
  private orderService = inject(OrderService);
  private notify = inject(GlobalNotificationService);
  cart = inject(CartService);

  companySlug = '';
  companyName = '';
  companyLogo = '';

  payMethods: PayMethod[] = [];
  deliveryCharge = 0;
  freeDeliveryAbove = 0;

  placing = false;
  placedOrderNumber: string | null = null;
  placedTotal = 0;

  ngOnInit() {
    this.companySlug = this.route.snapshot.paramMap.get('companySlug') || '';
    this.companyService.getPublicCompany(this.companySlug).subscribe({
      next: (comp) => {
        this.companyName = comp.name;
        this.companyLogo = comp.logoUrl || '';
        localStorage.setItem('tenant_company_id', comp.id.toString());
        this.loadSettings();
      },
      error: () => this.loadSettings()
    });
  }

  private loadSettings() {
    this.settingsService.getPublicSettings().subscribe({
      next: (settings) => {
        const val = (k: string) => settings.find(s => s.key === k)?.value;
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
    });
  }

  get subtotal() { return this.cart.cartSubtotal; }
  get shippingFee() {
    if (this.freeDeliveryAbove > 0 && this.subtotal >= this.freeDeliveryAbove) return 0;
    return this.deliveryCharge;
  }
  get total() { return this.subtotal + this.shippingFee; }

  backToShop() { this.router.navigate(['/', this.companySlug]); }

  placeOrder() {
    if (!this.cart.isCheckoutValid()) {
      this.notify.notify({ type: 'warning', title: 'Missing details', message: 'Please fill in name, phone and address.', ttlMs: 4000 });
      return;
    }
    if (this.placing) return;
    this.placing = true;
    this.orderService.placePublicOrder(this.cart.buildOrderRequest()).subscribe({
      next: (res) => {
        this.placedOrderNumber = res.orderNumber;
        this.placedTotal = res.total;
        this.cart.clearAfterOrder(res.orderNumber);
        this.placing = false;
      },
      error: (err) => {
        this.placing = false;
        this.notify.notify({ type: 'error', title: 'Order failed', message: err?.error?.message || 'Could not place your order.', ttlMs: 6000 });
      }
    });
  }
}

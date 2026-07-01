import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CartService } from '../../../services/cart/cart.service';
import { OrderService } from '../../../services/order/order.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-cart-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cart-widget.component.html'
})
export class CartWidgetComponent {
  cartService = inject(CartService);
  private orderService = inject(OrderService);
  private notify = inject(GlobalNotificationService);

  placeOrder() {
    if (!this.cartService.isCheckoutValid()) {
      this.notify.notify({ type: 'warning', title: 'Missing details', message: 'Please fill in name, phone and address.', ttlMs: 4000 });
      return;
    }
    if (this.cartService.placingOrder) return;

    this.cartService.placingOrder = true;
    this.orderService.placePublicOrder(this.cartService.buildOrderRequest()).subscribe({
      next: (res) => {
        this.cartService.clearAfterOrder(res.orderNumber);
        this.notify.notify({
          type: 'success',
          title: 'Order placed!',
          message: `Order ${res.orderNumber} confirmed. Total ৳${res.total}. We'll call you to confirm delivery.`,
          ttlMs: 8000
        });
      },
      error: (err) => {
        this.cartService.placingOrder = false;
        this.notify.notify({
          type: 'error',
          title: 'Order failed',
          message: err?.error?.message || 'Could not place your order. Please try again.',
          ttlMs: 6000
        });
      }
    });
  }
}

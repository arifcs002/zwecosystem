import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService } from '../../../services/cart/cart.service';

@Component({
  selector: 'app-cart-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cart-widget.component.html'
})
export class CartWidgetComponent {
  cartService = inject(CartService);
  private router = inject(Router);

  // Checkout is a full page now — close the drawer and route to it. The slug is
  // the first URL segment (/{slug}, /{slug}/category/…, /{slug}/product/…).
  goToCheckout() {
    if (this.cartService.cart.length === 0) return;
    const slug = this.router.url.split('/').filter(Boolean)[0];
    this.cartService.isCartOpen = false;
    this.router.navigate(['/', slug, 'checkout']);
  }
}

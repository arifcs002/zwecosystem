import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CartService } from '../../../services/cart/cart.service';
import { TenantService } from '../../../services/tenant/tenant.service';

@Component({
  selector: 'app-cart-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cart-widget.component.html'
})
export class CartWidgetComponent {
  cartService = inject(CartService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private tenant = inject(TenantService);

  // Checkout is a full page now — close the drawer and route to it.
  goToCheckout() {
    if (this.cartService.cart.length === 0) return;
    const slug = this.tenant.getSlug(this.route);
    this.cartService.isCartOpen = false;
    this.router.navigate(this.tenant.routeSegments(slug, 'checkout'));
  }
}

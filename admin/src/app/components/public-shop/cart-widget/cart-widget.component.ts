import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CartService } from '../../../services/cart/cart.service';

@Component({
  selector: 'app-cart-widget',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cart-widget.component.html'
})
export class CartWidgetComponent {
  cartService = inject(CartService);

  placeOrder() {
    const total = this.cartService.cartSubtotal;
    const payment = this.cartService.customerInfo.paymentMethod;
    if (!this.cartService.placeOrder()) {
      alert('Please fill out all required fields.');
      return;
    }
    alert(`Order placed!\nTotal: ৳${total}\nPayment: ${payment}`);
  }
}

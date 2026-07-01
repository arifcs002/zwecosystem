import { Injectable } from '@angular/core';

export interface ShopProduct {
  id: number;
  name: string;
  price: number;
  image: string;
  category: string;
  categoryId: number;
  stock: number;
  size?: string;
}

export interface CartItem { product: ShopProduct; quantity: number; }

const STORAGE_KEY = 'shop_cart_v1';

// Singleton so the cart survives navigation between the storefront homepage,
// category pages, and the product detail page (each is a separate routed
// component — without a shared service the cart would reset on every route
// change).
@Injectable({ providedIn: 'root' })
export class CartService {
  cart: CartItem[] = [];
  isCartOpen = false;
  isCheckoutOpen = false;
  customerInfo = { name: '', mobile: '', address: '', district: '', thana: '', notes: '', paymentMethod: 'COD' };

  // Checkout flow state (driven by the cart widget / checkout page).
  placingOrder = false;
  lastOrderNumber: string | null = null;

  constructor() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) this.cart = JSON.parse(raw);
    } catch { this.cart = []; }
  }

  private persist() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(this.cart));
  }

  toggleCart() { this.isCartOpen = !this.isCartOpen; if (this.isCartOpen) this.isCheckoutOpen = false; }

  addToCart(product: ShopProduct, quantity = 1) {
    if (product.stock <= 0) return;
    const item = this.cart.find(i => i.product.id === product.id);
    if (item) item.quantity = Math.min(item.quantity + quantity, product.stock);
    else this.cart.push({ product, quantity: Math.min(quantity, product.stock) });
    this.persist();
    this.isCartOpen = true;
  }

  updateQuantity(item: CartItem, delta: number) {
    item.quantity += delta;
    if (item.quantity <= 0) this.cart = this.cart.filter(c => c !== item);
    else if (item.quantity > item.product.stock) item.quantity = item.product.stock;
    this.persist();
  }

  removeFromCart(item: CartItem) {
    this.cart = this.cart.filter(c => c !== item);
    this.persist();
  }

  get cartTotalItems() { return this.cart.reduce((s, i) => s + i.quantity, 0); }
  get cartSubtotal() { return this.cart.reduce((s, i) => s + i.product.price * i.quantity, 0); }

  proceedToCheckout() { if (this.cart.length) { this.isCartOpen = false; this.isCheckoutOpen = true; } }
  closeCheckout() { this.isCheckoutOpen = false; }

  isCheckoutValid(): boolean {
    return !!(this.customerInfo.name && this.customerInfo.mobile && this.customerInfo.address);
  }

  // Maps the current cart + customer info into the shape the public checkout
  // API expects. Prices are intentionally omitted — the backend re-reads them.
  buildOrderRequest() {
    return {
      items: this.cart.map(i => ({ productId: i.product.id, quantity: i.quantity })),
      customerName: this.customerInfo.name.trim(),
      customerPhone: this.customerInfo.mobile.trim(),
      shippingAddress: this.customerInfo.address.trim(),
      shippingDistrict: this.customerInfo.district?.trim() || undefined,
      shippingThana: this.customerInfo.thana?.trim() || undefined,
      orderNotes: this.customerInfo.notes?.trim() || undefined,
      paymentMethod: this.customerInfo.paymentMethod
    };
  }

  // Called once the order has been accepted by the backend.
  clearAfterOrder(orderNumber: string) {
    this.cart = [];
    this.persist();
    this.isCheckoutOpen = false;
    this.placingOrder = false;
    this.lastOrderNumber = orderNumber;
    this.customerInfo = { name: '', mobile: '', address: '', district: '', thana: '', notes: '', paymentMethod: 'COD' };
  }
}

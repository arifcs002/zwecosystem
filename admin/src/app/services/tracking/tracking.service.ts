import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';

declare global { interface Window { fbq?: any; _fbq?: any; dataLayer?: any[]; gtag?: (...a: any[]) => void; } }

const PIXEL_ID = /^\d{5,20}$/;
const GA4_ID = /^G-[A-Z0-9]{6,14}$/;
// Admin / account-management screens are never tracked.
const UNTRACKED = ['/workspace', '/admin', '/login', '/company', '/account'];

export interface TrackItem { id: number | string; name?: string; price: number; quantity?: number; category?: string; }

/**
 * Per-shop Meta Pixel + Google Analytics 4. IDs come from the shop's public settings (and are validated here
 * before they are ever put into a script URL). Does nothing unless the shop configured an ID, and respects the
 * browser's Do-Not-Track. The purchase event carries the order number as the Meta event id, which the server-side
 * Conversions API event reuses so Meta counts one sale, not two.
 */
@Injectable({ providedIn: 'root' })
export class TrackingService {
  private router = inject(Router);
  private fbId = '';
  private gaId = '';
  private lastPageUrl = '';
  private navSubscribed = false;

  get active() { return !!(this.fbId || this.gaId); }

  /** Called each time a storefront payload arrives; safe to call repeatedly. */
  configure(settings: { key: string; value: string }[]) {
    if (this.doNotTrack()) return;
    const val = (k: string) => (settings.find(s => s.key === k)?.value || '').trim();
    const fb = val('meta_pixel_id'), ga = val('ga4_measurement_id');
    let started = false;
    if (PIXEL_ID.test(fb) && fb !== this.fbId) { this.fbId = fb; this.startPixel(fb); started = true; }
    if (GA4_ID.test(ga) && ga !== this.gaId) { this.gaId = ga; this.startGa(ga); started = true; }
    if (!this.navSubscribed) {
      this.navSubscribed = true;
      this.router.events.pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
        .subscribe(e => this.pageView(e.urlAfterRedirects));
    }
    if (started) this.pageView(this.router.url, true);
  }

  pageView(url: string, force = false) {
    if (!this.active || this.isUntracked(url)) return;
    const path = url.split('?')[0];
    if (!force && path === this.lastPageUrl) return;
    this.lastPageUrl = path;
    this.fb('track', 'PageView');
    this.ga('event', 'page_view', { page_path: path, page_location: window.location.href });
  }

  viewContent(i: TrackItem) {
    this.fb('track', 'ViewContent', { content_ids: [String(i.id)], content_type: 'product', content_name: i.name, value: i.price, currency: 'BDT' });
    this.ga('event', 'view_item', { currency: 'BDT', value: i.price, items: [this.gaItem(i)] });
  }

  addToCart(i: TrackItem) {
    const qty = i.quantity ?? 1;
    this.fb('track', 'AddToCart', { content_ids: [String(i.id)], content_type: 'product', content_name: i.name, value: i.price * qty, currency: 'BDT' });
    this.ga('event', 'add_to_cart', { currency: 'BDT', value: i.price * qty, items: [this.gaItem(i)] });
  }

  initiateCheckout(items: TrackItem[], value: number) {
    this.fb('track', 'InitiateCheckout', { content_ids: items.map(i => String(i.id)), content_type: 'product', num_items: items.reduce((s, i) => s + (i.quantity ?? 1), 0), value, currency: 'BDT' });
    this.ga('event', 'begin_checkout', { currency: 'BDT', value, items: items.map(i => this.gaItem(i)) });
  }

  purchase(orderNumber: string, items: TrackItem[], value: number) {
    // eventID = order number: the server's Conversions API event uses the same id, so Meta de-duplicates them.
    this.fb('track', 'Purchase', {
      content_ids: items.map(i => String(i.id)), content_type: 'product', value, currency: 'BDT',
      contents: items.map(i => ({ id: String(i.id), quantity: i.quantity ?? 1, item_price: i.price }))
    }, { eventID: orderNumber });
    this.ga('event', 'purchase', { transaction_id: orderNumber, currency: 'BDT', value, items: items.map(i => this.gaItem(i)) });
  }

  /** Browser identifiers the server forwards to Meta so it can match the purchase to the ad click. */
  signals(): { fbp?: string; fbc?: string; sourceUrl: string } {
    const cookie = (n: string) => { try { return document.cookie.split('; ').find(c => c.startsWith(n + '='))?.split('=')[1]; } catch { return undefined; } };
    let fbc = cookie('_fbc');
    if (!fbc) {
      // Meta's pixel normally sets _fbc from ?fbclid=…; rebuild it if the pixel hasn't yet.
      const click = new URLSearchParams(window.location.search).get('fbclid');
      if (click && /^[\w\-]{5,200}$/.test(click)) fbc = `fb.1.${Date.now()}.${click}`;
    }
    return { fbp: this.fbId ? cookie('_fbp') : undefined, fbc: this.fbId ? fbc : undefined, sourceUrl: window.location.href };
  }

  // ── internals ──
  private startPixel(id: string) {
    try {
      if (!window.fbq) {
        const n: any = window.fbq = function (...a: any[]) { n.callMethod ? n.callMethod.apply(n, a) : n.queue.push(a); };
        if (!window._fbq) window._fbq = n;
        n.push = n; n.loaded = true; n.version = '2.0'; n.queue = [];
        const s = document.createElement('script');
        s.async = true; s.src = 'https://connect.facebook.net/en_US/fbevents.js';
        document.head.appendChild(s);
      }
      window.fbq('init', id);
    } catch { /* blocked by an ad-blocker or CSP — tracking is best-effort */ }
  }

  private startGa(id: string) {
    try {
      window.dataLayer = window.dataLayer || [];
      window.gtag = function () { window.dataLayer!.push(arguments); };
      const s = document.createElement('script');
      s.async = true; s.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`;
      document.head.appendChild(s);
      window.gtag('js', new Date());
      window.gtag('config', id, { send_page_view: false });
    } catch { /* best effort */ }
  }

  private fb(...args: any[]) { try { if (this.fbId && window.fbq) window.fbq(...args); } catch { /* ignore */ } }
  private ga(...args: any[]) { try { if (this.gaId && window.gtag) window.gtag(...args); } catch { /* ignore */ } }
  private gaItem(i: TrackItem) { return { item_id: String(i.id), item_name: i.name, price: i.price, quantity: i.quantity ?? 1, item_category: i.category }; }
  private isUntracked(url: string) { return UNTRACKED.some(p => url === p || url.startsWith(p + '/') || url.startsWith(p + '?')); }
  private doNotTrack() { try { return navigator.doNotTrack === '1' || (window as any).doNotTrack === '1'; } catch { return false; } }
}

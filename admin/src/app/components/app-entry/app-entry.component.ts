import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { Capacitor } from '@capacitor/core';
import { environment } from '../../../environments/environment';
import { PublicPlan, planPerks } from '../company-register/company-register.component';

const STORAGE_KEY = 'app_login_context'; // 'admin' | '<companySlug>'

// Root route ("/"): the public home page. Explains the product to a shop owner, shows the live
// price list, and sends them to sign up or log in. Inside the packaged mobile app (no address bar)
// the picked login is remembered so returning users skip straight to their login screen — see
// login.component's "Switch account" link. On the web this page is shown every time the base URL is hit.
//
// NOTE: the wording below is a first draft — marketing copy is the owner's call; edit freely.
@Component({
  selector: 'app-entry',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="home" *ngIf="showPicker">
      <div class="glow glow-a"></div>
      <div class="glow glow-b"></div>

      <header class="nav">
        <a class="brand" routerLink="/"><img src="assets/auleco-mark-light.png" alt="" width="32" height="32"><span>Auleco</span></a>
        <nav class="nav-actions">
          <button type="button" class="link" (click)="goCompany()">Log in</button>
          <a routerLink="/company/register" class="btn btn-small">Start free</a>
        </nav>
      </header>

      <section class="hero">
        <div class="eyebrow">For small shops in Bangladesh</div>
        <h1>Your shop, online and at the counter &mdash; in one place.</h1>
        <p class="lead">Get your own web address, take Cash on Delivery orders from Facebook customers, sell in-store with a POS, and keep your stock right &mdash; all from your phone.</p>
        <div class="cta">
          <a routerLink="/company/register" class="btn btn-primary">{{ trialDays > 0 ? 'Start your ' + trialDays + '-day free trial' : 'Create your shop' }} &rarr;</a>
          <a href="#plans" class="btn btn-ghost" (click)="scrollTo($event, 'plans')">See plans</a>
        </div>
        <ul class="trust">
          <li>&#10003; Cash on Delivery ready</li>
          <li>&#10003; Works great on mobile</li>
          <li>&#10003; Bangla &amp; English storefront</li>
        </ul>
        <div class="address-pill" aria-hidden="true"><span class="dot"></span> yourshop.auleco.com</div>
      </section>

      <section class="features" aria-label="What you get">
        <article *ngFor="let f of features" class="feature">
          <div class="feature-icon" aria-hidden="true">{{ f.icon }}</div>
          <h3>{{ f.title }}</h3>
          <p>{{ f.text }}</p>
        </article>
      </section>

      <section class="steps" aria-label="How it works">
        <h2>Up and selling in three steps</h2>
        <ol>
          <li><b>1</b><div><h3>Create your shop</h3><p>Pick your web address and plan. Takes a couple of minutes.</p></div></li>
          <li><b>2</b><div><h3>Add your products</h3><p>Photos, prices, sizes and stock &mdash; print barcodes and price tags when you need them.</p></div></li>
          <li><b>3</b><div><h3>Share the link</h3><p>Post it on Facebook or WhatsApp. Orders arrive; you send them with your courier.</p></div></li>
        </ol>
      </section>

      <section id="plans" class="plans" *ngIf="plans.length" aria-label="Plans">
        <h2>Simple plans</h2>
        <p class="plans-sub" *ngIf="trialDays > 0">Every plan starts with a {{ trialDays }}-day free trial.</p>
        <div class="plan-grid">
          <article *ngFor="let p of plans" class="plan">
            <h3>{{ p.name }}</h3>
            <div class="price">&#2547;{{ p.price | number:'1.0-0' }}<span> / {{ p.billingCycle }}</span></div>
            <ul><li *ngFor="let perk of p.perks">&#10003; {{ perk }}</li></ul>
            <a routerLink="/company/register" [queryParams]="{ plan: p.id }" class="btn btn-small btn-block">Choose {{ p.name }}</a>
          </article>
        </div>
      </section>

      <section class="final">
        <h2>Ready to open your online shop?</h2>
        <a routerLink="/company/register" class="btn btn-primary">Create your shop &rarr;</a>
      </section>

      <footer class="foot">
        <span>&copy; Auleco</span>
        <span class="foot-links">
          <button type="button" class="link" (click)="goCompany()">Shop login</button>
          <button type="button" class="link" (click)="goAdmin()">Platform admin</button>
        </span>
      </footer>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .home { position: relative; overflow: hidden; min-height: 100vh; color: #e5e7eb; font-family: Inter, system-ui, sans-serif;
      background: radial-gradient(circle at 15% 0%, rgba(139,92,246,.16), transparent 50%), radial-gradient(circle at 90% 30%, rgba(244,63,94,.12), transparent 45%), #08090f; }
    .glow { position: absolute; border-radius: 999px; filter: blur(120px); pointer-events: none; }
    .glow-a { top: -140px; left: -120px; width: 420px; height: 420px; background: rgba(139,92,246,.2); }
    .glow-b { top: 520px; right: -160px; width: 420px; height: 420px; background: rgba(244,63,94,.14); }
    section, .nav, .foot { position: relative; z-index: 2; width: 100%; max-width: 1040px; margin: 0 auto; padding-left: 20px; padding-right: 20px; box-sizing: border-box; }
    .nav { display: flex; align-items: center; justify-content: space-between; padding-top: 18px; padding-bottom: 6px; z-index: 3; }
    .brand { display: flex; align-items: center; gap: 10px; color: #fff; font-weight: 800; font-size: 18px; text-decoration: none; }
    .nav-actions { display: flex; align-items: center; gap: 14px; }
    .link { background: none; border: 0; color: #cbd5e1; font: inherit; font-size: 14px; font-weight: 600; cursor: pointer; padding: 8px 4px; }
    .link:hover { color: #fff; }
    .btn { display: inline-flex; align-items: center; justify-content: center; padding: 14px 26px; border-radius: 12px; font-weight: 700; font-size: 15px; text-decoration: none; cursor: pointer; border: 0; font-family: inherit; }
    .btn-primary { color: #fff; background: linear-gradient(135deg,#e11d48,#f43f5e); box-shadow: 0 12px 28px rgba(225,29,72,.32); }
    .btn-ghost { color: #e5e7eb; border: 1px solid rgba(255,255,255,.18); background: rgba(255,255,255,.03); }
    .btn-small { padding: 9px 16px; font-size: 13.5px; color: #fff; background: linear-gradient(135deg,#7c3aed,#8b5cf6); }
    .btn-block { width: 100%; margin-top: auto; }
    .hero { text-align: center; padding-top: 56px; padding-bottom: 64px; display: flex; flex-direction: column; align-items: center; }
    .eyebrow { font-size: 12px; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #a78bfa; margin-bottom: 18px; }
    h1 { margin: 0; font-size: clamp(32px, 6vw, 56px); line-height: 1.08; font-weight: 800; color: #fff; max-width: 820px; letter-spacing: -.02em; }
    .lead { margin: 20px 0 0; font-size: clamp(16px, 2.4vw, 19px); line-height: 1.65; color: #9ca3af; max-width: 640px; }
    .cta { display: flex; gap: 12px; flex-wrap: wrap; justify-content: center; margin-top: 32px; }
    .trust { list-style: none; padding: 0; margin: 26px 0 0; display: flex; gap: 8px 22px; flex-wrap: wrap; justify-content: center; font-size: 13.5px; color: #a7f3d0; }
    .address-pill { margin-top: 34px; padding: 10px 18px; border-radius: 999px; border: 1px solid rgba(255,255,255,.12); background: rgba(255,255,255,.04); font-family: ui-monospace, Menlo, monospace; font-size: 14px; color: #c4b5fd; display: inline-flex; align-items: center; gap: 10px; }
    .address-pill .dot { width: 8px; height: 8px; border-radius: 50%; background: #34d399; box-shadow: 0 0 10px #34d399; }
    .features { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; padding-bottom: 72px; }
    .feature { padding: 22px; border-radius: 18px; border: 1px solid rgba(255,255,255,.08); background: rgba(255,255,255,.03); }
    .feature-icon { font-size: 24px; margin-bottom: 12px; }
    .feature h3 { margin: 0 0 6px; font-size: 16px; color: #fff; }
    .feature p { margin: 0; font-size: 13.5px; line-height: 1.55; color: #9ca3af; }
    h2 { margin: 0 0 24px; text-align: center; font-size: clamp(22px, 4vw, 32px); color: #fff; letter-spacing: -.01em; }
    .steps { padding-bottom: 72px; }
    .steps ol { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    .steps li { display: flex; gap: 14px; padding: 20px; border-radius: 18px; background: rgba(139,92,246,.07); border: 1px solid rgba(139,92,246,.2); }
    .steps li b { flex: none; width: 34px; height: 34px; border-radius: 50%; background: #7c3aed; color: #fff; display: grid; place-items: center; font-size: 15px; }
    .steps h3 { margin: 0 0 4px; font-size: 15px; color: #fff; }
    .steps p { margin: 0; font-size: 13px; line-height: 1.5; color: #9ca3af; }
    .plans { padding-bottom: 72px; scroll-margin-top: 16px; }
    .plans-sub { text-align: center; margin: -12px 0 24px; color: #9ca3af; font-size: 14px; }
    .plan-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 18px; max-width: 760px; margin: 0 auto; }
    .plan { display: flex; flex-direction: column; padding: 26px; border-radius: 20px; border: 1px solid rgba(244,63,94,.25); background: linear-gradient(180deg, rgba(244,63,94,.07), rgba(18,19,28,.9)); }
    .plan h3 { margin: 0; font-size: 18px; color: #fff; }
    .price { margin: 8px 0 14px; font-size: 30px; font-weight: 800; color: #fff; }
    .price span { font-size: 13px; font-weight: 500; color: #9ca3af; }
    .plan ul { list-style: none; margin: 0 0 22px; padding: 0; font-size: 13.5px; color: #cbd5e1; display: grid; gap: 8px; }
    .final { text-align: center; padding-bottom: 64px; }
    .final h2 { margin-bottom: 20px; }
    .foot { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px; padding-top: 22px; padding-bottom: 30px; border-top: 1px solid rgba(255,255,255,.07); font-size: 13px; color: #6b7280; }
    .foot-links { display: flex; gap: 18px; }
    @media (max-width: 820px) { .features, .steps ol { grid-template-columns: 1fr; } .hero { padding-top: 36px; } }
    @media (prefers-reduced-motion: no-preference) { .btn { transition: transform .15s ease, box-shadow .15s ease; } .btn:hover { transform: translateY(-1px); } }
  `]
})
export class AppEntryComponent implements OnInit {
  private router = inject(Router);
  private http = inject(HttpClient);

  showPicker = false;
  plans: PublicPlan[] = [];
  trialDays = 0;

  readonly features = [
    { icon: '🛍️', title: 'Your own online store', text: 'A web address with your shop name. Customers browse, add to cart and order with Cash on Delivery.' },
    { icon: '🧾', title: 'POS for the counter', text: 'Scan barcodes, print receipts and price tags, and keep walk-in and online sales in one place.' },
    { icon: '📦', title: 'Stock that adds up', text: 'Sizes, quantities and low-stock alerts, so you never sell what you do not have.' },
    { icon: '🚚', title: 'Courier booking', text: 'Send parcels to Steadfast or Pathao from the order screen and track their status.' },
    { icon: '📣', title: 'Made for Facebook selling', text: 'Coupons, abandoned-cart reminders, and Meta Pixel tracking to see which ads bring orders.' },
    { icon: '📱', title: 'Runs on your phone', text: 'Manage everything from a mobile browser or the app. Your storefront shows in Bangla or English.' }
  ];

  ngOnInit() {
    // Native app: remember the last choice so returning users skip the picker.
    if (Capacitor.isNativePlatform()) {
      const remembered = localStorage.getItem(STORAGE_KEY);
      if (!remembered) { this.showPicker = true; this.loadPlans(); return; }
      if (remembered === 'admin') this.router.navigate(['/admin/login']);
      else this.router.navigate(['/', remembered, 'login']);
      return;
    }

    // Web: always show the home page.
    this.showPicker = true;
    this.loadPlans();
  }

  // The price list is live from the server so the page never shows stale prices.
  // If it can't load, that section is simply hidden and the rest of the page still works.
  private loadPlans() {
    this.http.get<any[]>(`${environment.apiUrl}/subscriptionplans/public`).subscribe({
      next: (rows) => {
        this.plans = rows.map(r => ({ id: r.id, name: r.name, price: r.price, billingCycle: r.billingCycle, perks: planPerks(r.features) }));
        this.trialDays = rows[0]?.trialDays ?? 0;
      },
      error: () => { /* no price list */ }
    });
  }

  scrollTo(e: Event, id: string) {
    e.preventDefault();
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  goAdmin() {
    localStorage.setItem(STORAGE_KEY, 'admin');
    this.router.navigate(['/admin/login']);
  }

  goCompany() {
    this.router.navigate(['/company/login']);
  }
}

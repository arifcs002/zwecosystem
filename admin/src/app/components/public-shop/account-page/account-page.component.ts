import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { AccountService, ShopperOrder } from '../../../services/account/account.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';

// Customer account: log in / sign up, then profile, saved delivery details, order history, password.
@Component({
  selector: 'app-account-page',
  standalone: true,
  imports: [TranslatePipe, CommonModule, FormsModule, RouterModule, ShopHeaderComponent, ShopFooterComponent],
  template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="groups"></app-shop-header>
    <main class="max-w-2xl mx-auto w-full px-4 py-8 flex-1">

      <div *ngIf="!account.ready" class="text-center py-24 text-gray-400 text-sm">{{ 'Loading…' | t }}</div>

      <!-- ══ Logged out ══ -->
      <section *ngIf="account.ready && !account.loggedIn" class="bg-white rounded-2xl border border-gray-100 shadow-sm p-6">
        <div class="flex rounded-xl bg-gray-100 p-1 mb-5" role="tablist">
          <button role="tab" [attr.aria-selected]="mode === 'login'" (click)="setMode('login')" class="flex-1 py-2 text-sm font-semibold rounded-lg transition" [ngClass]="mode === 'login' ? 'bg-white shadow text-gray-900' : 'text-gray-500'">{{ 'Log in' | t }}</button>
          <button role="tab" [attr.aria-selected]="mode === 'register'" (click)="setMode('register')" class="flex-1 py-2 text-sm font-semibold rounded-lg transition" [ngClass]="mode === 'register' ? 'bg-white shadow text-gray-900' : 'text-gray-500'">{{ 'Create account' | t }}</button>
        </div>

        <form (ngSubmit)="submitAuth()" class="space-y-3">
          <input *ngIf="mode === 'register'" [(ngModel)]="form.name" name="name" autocomplete="name" [placeholder]="'Full Name *' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
          <input [(ngModel)]="form.phone" name="phone" inputmode="tel" autocomplete="tel" [placeholder]="'Mobile number (01XXXXXXXXX)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
          <input *ngIf="mode === 'register'" [(ngModel)]="form.email" name="email" type="email" autocomplete="email" [placeholder]="'Email (optional)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
          <input [(ngModel)]="form.password" name="password" type="password" [autocomplete]="mode === 'login' ? 'current-password' : 'new-password'" [placeholder]="(mode === 'register' ? 'Password (at least 8 characters)' : 'Password') | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
          <p *ngIf="error" role="alert" class="text-sm text-rose-600">{{ error }}</p>
          <button type="submit" [disabled]="busy" class="w-full py-3 bg-indigo-600 text-white font-bold rounded-xl hover:bg-indigo-700 transition disabled:opacity-60">{{ busy ? ('Please wait…' | t) : (mode === 'login' ? ('Log in' | t) : ('Create account' | t)) }}</button>
        </form>
        <p class="text-xs text-gray-400 mt-4 text-center">{{ 'You can still order without an account.' | t }}</p>
      </section>

      <!-- ══ Logged in ══ -->
      <ng-container *ngIf="account.ready && account.profile as p">
        <div class="flex items-start justify-between gap-3 mb-5">
          <div>
            <h1 class="text-2xl font-bold text-gray-900">{{ p.name }}</h1>
            <p class="text-sm text-gray-500">{{ p.phone }}</p>
            <p *ngIf="p.group" class="mt-1 inline-block text-xs font-bold text-emerald-700 bg-emerald-50 px-2.5 py-1 rounded-full">★ {{ p.group.name }} — {{ p.group.discountPercent }}% {{ 'off every order' | t }}</p>
          </div>
          <button (click)="logout()" class="text-sm font-semibold text-gray-500 hover:text-rose-600 border border-gray-200 rounded-lg px-3 py-1.5">{{ 'Log out' | t }}</button>
        </div>

        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5 mb-5">
          <h2 class="font-bold text-gray-900 mb-3">{{ 'My orders' | t }}</h2>
          <p *ngIf="orders === null" class="text-sm text-gray-400">{{ 'Loading…' | t }}</p>
          <p *ngIf="orders && orders.length === 0" class="text-sm text-gray-400">{{ 'No orders yet. Orders you place while logged in appear here.' | t }}</p>
          <div class="divide-y divide-gray-100">
            <a *ngFor="let o of orders" [routerLink]="link('track-order')" [queryParams]="{ order: o.orderNumber, phone: p.phone }" class="flex items-center justify-between gap-3 py-3 hover:bg-gray-50 -mx-2 px-2 rounded-lg">
              <div class="min-w-0">
                <p class="text-sm font-semibold text-gray-800 truncate">{{ o.orderNumber }}</p>
                <p class="text-xs text-gray-400">{{ o.date | date:'mediumDate' }}<span *ngIf="o.discountLabel"> · {{ o.discountLabel }}</span></p>
              </div>
              <div class="text-right flex-shrink-0">
                <p class="text-sm font-bold text-gray-900">৳{{ o.total | number:'1.0-0' }}</p>
                <p class="text-[11px] font-semibold text-indigo-600">{{ o.status }}</p>
              </div>
            </a>
          </div>
        </section>

        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5 mb-5">
          <h2 class="font-bold text-gray-900 mb-1">{{ 'Delivery details' | t }}</h2>
          <p class="text-xs text-gray-400 mb-3">{{ 'We fill these in for you at checkout.' | t }}</p>
          <form (ngSubmit)="saveProfile()" class="space-y-3">
            <input [(ngModel)]="edit.name" name="ename" [placeholder]="'Full Name *' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            <input [(ngModel)]="edit.email" name="eemail" type="email" [placeholder]="'Email (optional)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            <textarea [(ngModel)]="edit.address" name="eaddr" rows="2" [placeholder]="'Full Address *' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400 resize-none"></textarea>
            <div class="grid sm:grid-cols-2 gap-3">
              <input [(ngModel)]="edit.district" name="edist" [placeholder]="'District' | t" class="border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              <input [(ngModel)]="edit.thana" name="ethana" [placeholder]="'Thana / Area' | t" class="border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            </div>
            <p *ngIf="profileMsg" role="status" class="text-sm" [ngClass]="profileOk ? 'text-emerald-600' : 'text-rose-600'">{{ profileMsg }}</p>
            <button type="submit" [disabled]="busy" class="px-5 py-2 bg-indigo-600 text-white text-sm font-semibold rounded-xl hover:bg-indigo-700 disabled:opacity-60">{{ 'Save' | t }}</button>
          </form>
        </section>

        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5">
          <h2 class="font-bold text-gray-900 mb-3">{{ 'Change password' | t }}</h2>
          <form (ngSubmit)="changePassword()" class="space-y-3">
            <input [(ngModel)]="pw.current" name="pcur" type="password" autocomplete="current-password" [placeholder]="'Current password' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            <input [(ngModel)]="pw.next" name="pnew" type="password" autocomplete="new-password" [placeholder]="'New password (at least 8 characters)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            <p *ngIf="pwMsg" role="status" class="text-sm" [ngClass]="pwOk ? 'text-emerald-600' : 'text-rose-600'">{{ pwMsg }}</p>
            <button type="submit" [disabled]="busy" class="px-5 py-2 border border-gray-200 text-gray-700 text-sm font-semibold rounded-xl hover:bg-gray-50 disabled:opacity-60">{{ 'Change password' | t }}</button>
          </form>
        </section>
      </ng-container>
    </main>
    <app-shop-footer [store]="store" [slug]="slug"></app-shop-footer>
  </div>`
})
export class AccountPageComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private tenant = inject(TenantService);
  private storefront = inject(StorefrontService);
  account = inject(AccountService);

  slug = '';
  store: StorefrontData | null = null;
  groups: ProductGroup[] = [];
  mode: 'login' | 'register' = 'login';
  form = { name: '', phone: '', email: '', password: '' };
  edit = { name: '', email: '', address: '', district: '', thana: '' };
  pw = { current: '', next: '' };
  orders: ShopperOrder[] | null = null;
  busy = false;
  error = ''; profileMsg = ''; profileOk = false; pwMsg = ''; pwOk = false;

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }
  setMode(m: 'login' | 'register') { this.mode = m; this.error = ''; }

  ngOnInit() {
    this.slug = this.tenant.getSlug(this.route);
    this.storefront.getStorefront(this.slug).subscribe({
      next: (data) => {
        this.store = data;
        this.groups = groupProducts(data.products);
        localStorage.setItem('tenant_company_id', String(data.company.id));
        this.account.use(data.company.id);
        this.afterLoginState();
        // The profile arrives asynchronously after use(); poll briefly until the account service is ready.
        const t = setInterval(() => { if (this.account.ready) { clearInterval(t); this.afterLoginState(); } }, 100);
        setTimeout(() => clearInterval(t), 5000);
      },
      error: () => { this.error = 'This store could not be found.'; }
    });
  }

  private afterLoginState() {
    const p = this.account.profile;
    if (!p) return;
    this.edit = { name: p.name, email: p.email || '', address: p.address || '', district: p.district || '', thana: p.thana || '' };
    this.orders = null;
    this.account.orders().subscribe({ next: (o) => this.orders = o, error: () => this.orders = [] });
  }

  submitAuth() {
    if (this.busy) return;
    this.error = '';
    const f = this.form;
    if (!f.phone.trim() || !f.password) { this.error = 'Enter your mobile number and password.'; return; }
    if (this.mode === 'register' && f.name.trim().length < 2) { this.error = 'Please enter your name.'; return; }
    this.busy = true;
    const req = this.mode === 'login'
      ? this.account.login(f.phone.trim(), f.password)
      : this.account.register({ name: f.name.trim(), phone: f.phone.trim(), password: f.password, email: f.email.trim() || undefined });
    req.subscribe({
      next: () => { this.busy = false; this.form = { name: '', phone: '', email: '', password: '' }; this.afterLoginState(); },
      error: (e) => { this.busy = false; this.error = e?.status === 429 ? 'Too many attempts. Please wait a few minutes.' : (e?.error?.message || 'Something went wrong. Please try again.'); }
    });
  }

  saveProfile() {
    this.busy = true; this.profileMsg = '';
    this.account.update({ name: this.edit.name, email: this.edit.email || undefined, address: this.edit.address || undefined, district: this.edit.district || undefined, thana: this.edit.thana || undefined }).subscribe({
      next: () => { this.busy = false; this.profileOk = true; this.profileMsg = 'Saved.'; },
      error: (e) => { this.busy = false; this.profileOk = false; this.profileMsg = e?.error?.message || 'Could not save.'; }
    });
  }

  changePassword() {
    this.busy = true; this.pwMsg = '';
    this.account.changePassword(this.pw.current, this.pw.next).subscribe({
      next: () => { this.busy = false; this.pwOk = true; this.pwMsg = 'Password changed. Other devices were signed out.'; this.pw = { current: '', next: '' }; },
      error: (e) => { this.busy = false; this.pwOk = false; this.pwMsg = e?.error?.message || 'Could not change the password.'; }
    });
  }

  logout() { this.account.logout().subscribe({ next: () => { this.orders = null; }, error: () => { this.orders = null; } }); }
}

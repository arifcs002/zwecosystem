import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { StorefrontData, StorefrontService } from '../../../services/storefront/storefront.service';
import { AccountService, ShopperLoyalty, ShopperOrder, ShopperOrderDetail } from '../../../services/account/account.service';
import { TenantService } from '../../../services/tenant/tenant.service';
import { ProductGroup, groupProducts } from '../../../utils/product-group.util';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { I18nService } from '../../../i18n/i18n.service';
import { ShopHeaderComponent } from '../shared/shop-header.component';
import { ShopFooterComponent } from '../shared/shop-footer.component';

// Customer account: log in / sign up, then profile, saved delivery details, order history, password.
@Component({
    selector: 'app-account-page',
    imports: [TranslatePipe, CommonModule, FormsModule, RouterModule, ShopHeaderComponent, ShopFooterComponent],
    template: `
  <div class="shop-root min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
    <app-shop-header [slug]="slug" [storeName]="store?.company?.name || ''" [logo]="store?.company?.logoUrl || ''" [groups]="groups"></app-shop-header>
    <main class="max-w-2xl mx-auto w-full px-4 py-8 flex-1">
  
      @if (!account.ready) {
        <div class="text-center py-24 text-gray-400 text-sm">{{ 'Loading…' | t }}</div>
      }
  
      <!-- ══ Logged out ══ -->
      @if (account.ready && !account.loggedIn) {
        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-6">
          <div class="flex rounded-xl bg-gray-100 p-1 mb-5" role="tablist">
            <button role="tab" [attr.aria-selected]="mode === 'login'" (click)="setMode('login')" class="flex-1 py-2 text-sm font-semibold rounded-lg transition" [ngClass]="mode === 'login' ? 'bg-white shadow text-gray-900' : 'text-gray-500'">{{ 'Log in' | t }}</button>
            <button role="tab" [attr.aria-selected]="mode === 'register'" (click)="setMode('register')" class="flex-1 py-2 text-sm font-semibold rounded-lg transition" [ngClass]="mode === 'register' ? 'bg-white shadow text-gray-900' : 'text-gray-500'">{{ 'Create account' | t }}</button>
          </div>
          @if (notice && !forgot) {
            <p role="status" class="mb-3 text-sm text-emerald-700 bg-emerald-50 rounded-lg px-3 py-2">{{ notice | t }}</p>
          }
          @if (forgot) {
            <form (ngSubmit)="forgotStep === 1 ? sendCode() : resetNow()" class="space-y-3">
              <h2 class="font-bold text-gray-900">{{ 'Reset your password' | t }}</h2>
              <input [(ngModel)]="form.phone" name="fphone" inputmode="tel" autocomplete="tel" [readOnly]="forgotStep === 2" [placeholder]="'Mobile number (01XXXXXXXXX)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              @if (forgotStep === 2) {
                <input [(ngModel)]="fcode" name="fcode" inputmode="numeric" maxlength="6" autocomplete="one-time-code" [placeholder]="'6-digit code from SMS' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm tracking-widest focus:outline-none focus:border-indigo-400">
                <input [(ngModel)]="fnew" name="fnew" type="password" autocomplete="new-password" [placeholder]="'New password (at least 8 characters)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              }
              @if (forgotInfo) {
                <p class="text-sm text-emerald-700">{{ forgotInfo | t }}</p>
              }
              @if (error) {
                <p role="alert" class="text-sm text-rose-600">{{ error }}</p>
              }
              <button type="submit" [disabled]="busy" class="w-full py-3 bg-indigo-600 text-white font-bold rounded-xl hover:bg-indigo-700 transition disabled:opacity-60">{{ busy ? ('Please wait…' | t) : (forgotStep === 1 ? ('Send code' | t) : ('Reset password' | t)) }}</button>
              <button type="button" (click)="closeForgot()" class="w-full text-sm text-gray-500 hover:underline">{{ 'Back to log in' | t }}</button>
            </form>
          }
          @if (!forgot) {
            <form (ngSubmit)="submitAuth()" class="space-y-3">
              @if (mode === 'register') {
                <input [(ngModel)]="form.name" name="name" autocomplete="name" [placeholder]="'Full Name *' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              }
              <input [(ngModel)]="form.phone" name="phone" inputmode="tel" autocomplete="tel" [placeholder]="'Mobile number (01XXXXXXXXX)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              @if (mode === 'register') {
                <input [(ngModel)]="form.email" name="email" type="email" autocomplete="email" [placeholder]="'Email (optional)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              }
              <input [(ngModel)]="form.password" name="password" type="password" [autocomplete]="mode === 'login' ? 'current-password' : 'new-password'" [placeholder]="(mode === 'register' ? 'Password (at least 8 characters)' : 'Password') | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
              @if (error) {
                <p role="alert" class="text-sm text-rose-600">{{ error }}</p>
              }
              @if (mode === 'login') {
                <button type="button" (click)="openForgot()" class="text-sm text-indigo-600 hover:underline">{{ 'Forgot password?' | t }}</button>
              }
              <button type="submit" [disabled]="busy" class="w-full py-3 bg-indigo-600 text-white font-bold rounded-xl hover:bg-indigo-700 transition disabled:opacity-60">{{ busy ? ('Please wait…' | t) : (mode === 'login' ? ('Log in' | t) : ('Create account' | t)) }}</button>
            </form>
          }
          <p class="text-xs text-gray-400 mt-4 text-center">{{ 'You can still order without an account.' | t }}</p>
        </section>
      }
  
      <!-- ══ Logged in ══ -->
      @if (account.ready && account.profile; as p) {
        <div class="flex items-start justify-between gap-3 mb-5">
          <div>
            <h1 class="text-2xl font-bold text-gray-900">{{ p.name }}</h1>
            <p class="text-sm text-gray-500">{{ p.phone }}
              @if (p.phoneVerified) {
                <span class="ml-1 text-xs font-semibold text-emerald-600">&#10003; {{ 'Verified' | t }}</span>
              }
              @if (!p.phoneVerified && !vOpen) {
                <button type="button" (click)="startVerify()" [disabled]="busy" class="ml-2 text-xs font-semibold text-indigo-600 hover:underline">{{ 'Verify number' | t }}</button>
              }
            </p>
            @if (!p.phoneVerified && vOpen) {
              <form (ngSubmit)="confirmVerify()" class="mt-2 flex gap-2 items-center flex-wrap">
                <input [(ngModel)]="vCode" name="vcode" inputmode="numeric" maxlength="6" autocomplete="one-time-code" [placeholder]="'6-digit code from SMS' | t" class="border border-gray-200 rounded-lg px-3 py-2 text-sm tracking-widest w-44 focus:outline-none focus:border-indigo-400">
                <button type="submit" [disabled]="busy" class="px-4 py-2 bg-indigo-600 text-white text-sm font-semibold rounded-lg disabled:opacity-60">{{ 'Verify' | t }}</button>
              </form>
            }
            @if (vMsg) {
              <p role="status" class="text-xs mt-1" [ngClass]="vOk ? 'text-emerald-600' : 'text-rose-600'">{{ vMsg | t }}</p>
            }
            @if (p.group) {
              <p class="mt-1 inline-block text-xs font-bold text-emerald-700 bg-emerald-50 px-2.5 py-1 rounded-full">★ {{ p.group.name }} — {{ p.group.discountPercent }}% {{ 'off every order' | t }}</p>
            }
          </div>
          <button (click)="logout()" class="text-sm font-semibold text-gray-500 hover:text-rose-600 border border-gray-200 rounded-lg px-3 py-1.5">{{ 'Log out' | t }}</button>
        </div>
        @if (loyalty && loyalty.enabled) {
          <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5 mb-5">
            <h2 class="font-bold text-gray-900 mb-1">⭐ {{ 'My points' | t }}</h2>
            @if (!loyalty.verified) {
              <p class="text-sm text-gray-500">{{ 'Verify your phone number below to see and use your points.' | t }}</p>
            } @else {
              <p class="text-2xl font-extrabold text-gray-900">{{ loyalty.balance | number }} <span class="text-sm font-semibold text-gray-400">{{ 'points' | t }} · ≈ ৳{{ (loyalty.worth || 0) | number:'1.0-0' }}</span></p>
              <p class="text-xs text-gray-400 mt-1">{{ 'Show your phone number at the shop counter to use your points on your next bill.' | t }}@if (loyalty.minRedeem) { {{ 'Minimum' | t }} {{ loyalty.minRedeem }} {{ 'points at a time.' | t }} }</p>
              @if (loyalty.entries.length) {
                <div class="divide-y divide-gray-100 mt-3">
                  @for (e of loyalty.entries; track e) {
                    <div class="flex justify-between py-1.5 text-sm"><span class="text-gray-600">{{ pointLabel(e.type) | t }} <span class="text-xs text-gray-400">{{ e.date | date:'d MMM' }}</span></span>
                      <span class="font-semibold" [class.text-rose-600]="e.points < 0" [class.text-emerald-600]="e.points > 0">{{ e.points > 0 ? '+' + e.points : e.points }}</span></div>
                  }
                </div>
              }
            }
          </section>
        }
        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5 mb-5">
          <h2 class="font-bold text-gray-900 mb-3">{{ 'My orders' | t }}</h2>
          @if (orders === null) {
            <p class="text-sm text-gray-400">{{ 'Loading…' | t }}</p>
          }
          @if (orders && orders.length === 0) {
            <p class="text-sm text-gray-400">{{ 'No orders yet. Orders you place while logged in appear here.' | t }}</p>
          }
          <div class="divide-y divide-gray-100">
            @for (o of orders; track o) {
            <div>
              <a [routerLink]="link('track-order')" [queryParams]="{ order: o.orderNumber, phone: p.phone }" class="flex items-center justify-between gap-3 py-3 hover:bg-gray-50 -mx-2 px-2 rounded-lg">
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-gray-800 truncate">{{ o.orderNumber }}</p>
                  <p class="text-xs text-gray-400">{{ o.date | date:'mediumDate' }}@if (o.discountLabel) {
                    <span> · {{ o.discountLabel }}</span>
                  }</p>
                </div>
                <div class="text-right flex-shrink-0">
                  <p class="text-sm font-bold text-gray-900">৳{{ o.total | number:'1.0-0' }}</p>
                  <p class="text-[11px] font-semibold text-indigo-600">{{ o.status }}</p>
                </div>
              </a>
              <button type="button" (click)="toggleOrder(o.orderNumber)" class="text-xs font-semibold text-indigo-600 hover:underline mb-2">{{ openOrder === o.orderNumber ? ('Hide items' | t) : ('View items' | t) }}</button>
              @if (openOrder === o.orderNumber) {
                <div class="bg-gray-50 rounded-lg p-3 mb-3 text-xs text-gray-600">
                  @if (!details[o.orderNumber]) { <p>{{ 'Loading…' | t }}</p> }
                  @if (details[o.orderNumber]; as d) {
                    @for (it of d.items; track it) {
                      <div class="flex justify-between gap-2 py-0.5"><span>{{ it.quantity }} × {{ it.name }}</span><span>৳{{ it.lineTotal | number:'1.0-0' }}</span></div>
                    }
                    @if (d.discount > 0) { <div class="flex justify-between py-0.5 text-emerald-700"><span>{{ 'Discount' | t }}@if (d.discountLabel) { ({{ d.discountLabel }}) }</span><span>−৳{{ d.discount | number:'1.0-0' }}</span></div> }
                    @if (d.shippingFee > 0) { <div class="flex justify-between py-0.5"><span>{{ 'Delivery' | t }}</span><span>৳{{ d.shippingFee | number:'1.0-0' }}</span></div> }
                    <div class="flex justify-between py-0.5 font-bold text-gray-900"><span>{{ 'Total' | t }}</span><span>৳{{ d.total | number:'1.0-0' }}</span></div>
                    @if (d.timeline.length) {
                      <p class="mt-2 text-gray-400">@for (t of d.timeline; track t; let last = $last) { {{ t.status }} ({{ t.date | date:'d MMM' }}){{ last ? '' : ' → ' }} }</p>
                    }
                  }
                </div>
              }
            </div>
            }
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
            @if (profileMsg) {
              <p role="status" class="text-sm" [ngClass]="profileOk ? 'text-emerald-600' : 'text-rose-600'">{{ profileMsg }}</p>
            }
            <button type="submit" [disabled]="busy" class="px-5 py-2 bg-indigo-600 text-white text-sm font-semibold rounded-xl hover:bg-indigo-700 disabled:opacity-60">{{ 'Save' | t }}</button>
          </form>
        </section>
        <section class="bg-white rounded-2xl border border-gray-100 shadow-sm p-5">
          <h2 class="font-bold text-gray-900 mb-3">{{ 'Change password' | t }}</h2>
          <form (ngSubmit)="changePassword()" class="space-y-3">
            <input [(ngModel)]="pw.current" name="pcur" type="password" autocomplete="current-password" [placeholder]="'Current password' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            <input [(ngModel)]="pw.next" name="pnew" type="password" autocomplete="new-password" [placeholder]="'New password (at least 8 characters)' | t" class="w-full border border-gray-200 rounded-lg px-3 py-2.5 text-sm focus:outline-none focus:border-indigo-400">
            @if (pwMsg) {
              <p role="status" class="text-sm" [ngClass]="pwOk ? 'text-emerald-600' : 'text-rose-600'">{{ pwMsg }}</p>
            }
            <button type="submit" [disabled]="busy" class="px-5 py-2 border border-gray-200 text-gray-700 text-sm font-semibold rounded-xl hover:bg-gray-50 disabled:opacity-60">{{ 'Change password' | t }}</button>
          </form>
        </section>
      }
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
  private i18n = inject(I18nService);
  private tr(s: string) { return this.i18n.t(s); }

  slug = '';
  store: StorefrontData | null = null;
  groups: ProductGroup[] = [];
  mode: 'login' | 'register' = 'login';
  form = { name: '', phone: '', email: '', password: '' };
  edit = { name: '', email: '', address: '', district: '', thana: '' };
  pw = { current: '', next: '' };
  orders: ShopperOrder[] | null = null;
  loyalty: ShopperLoyalty | null = null;
  openOrder: string | null = null;
  details: Record<string, ShopperOrderDetail> = {};
  toggleOrder(no: string) {
    if (this.openOrder === no) { this.openOrder = null; return; }
    this.openOrder = no;
    if (!this.details[no]) this.account.orderDetail(no).subscribe({ next: d => this.details[no] = d, error: () => { /* the row just stays empty */ } });
  }
  pointLabel(t: string) { return ({ EARN: 'Earned', REDEEM: 'Used', RETURN_REVERSE: 'Returned goods', CANCEL_EARN: 'Order cancelled', CANCEL_REDEEM: 'Order cancelled (points back)', ADJUST: 'Shop bonus / correction' } as Record<string, string>)[t] || t; }
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
    this.loyalty = null; this.account.loyalty().subscribe({ next: l => this.loyalty = l, error: () => this.loyalty = null });
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

  vOpen = false; vCode = ''; vMsg = ''; vOk = false;

  startVerify() {
    const phone = this.account.profile?.phone;
    if (!phone || this.busy) return;
    this.busy = true; this.vMsg = '';
    this.account.sendOtp(phone, 'verify').subscribe({
      next: () => { this.busy = false; this.vOpen = true; this.vOk = true; this.vMsg = 'A code is on its way.'; },
      error: (e) => { this.busy = false; this.vOk = false; this.vMsg = e?.status === 429 ? 'Too many codes requested. Please wait a few minutes.' : e?.status === 503 ? 'SMS codes are not available for this shop right now.' : (e?.error?.message || 'Could not send the code.'); }
    });
  }

  confirmVerify() {
    if (this.vCode.trim().length !== 6 || this.busy) { this.vOk = false; this.vMsg = 'Enter the 6-digit code.'; return; }
    this.busy = true; this.vMsg = '';
    this.account.verifyPhone(this.vCode.trim()).subscribe({
      next: () => { this.busy = false; this.vOpen = false; this.vCode = ''; this.vOk = true; this.vMsg = 'Number verified.'; },
      error: (e) => { this.busy = false; this.vOk = false; this.vMsg = e?.error?.message || 'That code is wrong or has expired.'; }
    });
  }

  notice = '';
  forgot = false; forgotStep: 1 | 2 = 1; fcode = ''; fnew = ''; forgotInfo = '';
  openForgot() { this.forgot = true; this.forgotStep = 1; this.error = ''; this.forgotInfo = ''; this.fcode = ''; this.fnew = ''; }
  closeForgot() { this.forgot = false; this.error = ''; this.forgotInfo = ''; }

  sendCode() {
    this.error = '';
    if (!this.form.phone.trim()) { this.error = 'Enter your mobile number.'; return; }
    this.busy = true;
    this.account.sendOtp(this.form.phone.trim(), 'reset').subscribe({
      next: () => { this.busy = false; this.forgotStep = 2; this.forgotInfo = 'If this number has an account, a code is on its way.'; },
      error: (e) => { this.busy = false; this.error = e?.status === 429 ? 'Too many codes requested. Please wait a few minutes.' : (e?.error?.message || 'Could not send the code.'); }
    });
  }

  resetNow() {
    this.error = '';
    if (this.fcode.trim().length !== 6 || this.fnew.length < 8) { this.error = 'Enter the 6-digit code and a new password of at least 8 characters.'; return; }
    this.busy = true;
    this.account.resetPassword(this.form.phone.trim(), this.fcode.trim(), this.fnew).subscribe({
      next: () => { this.busy = false; this.forgot = false; this.form.password = ''; this.error = ''; this.mode = 'login'; this.forgotInfo = ''; this.notice = 'Password changed. Please log in.'; },
      error: (e) => { this.busy = false; this.forgotInfo = ''; this.error = e?.error?.message || 'Could not reset the password.'; }
    });
  }

  logout() { this.account.logout().subscribe({ next: () => { this.orders = null; }, error: () => { this.orders = null; } }); }
}

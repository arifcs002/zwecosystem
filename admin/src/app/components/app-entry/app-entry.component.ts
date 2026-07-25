import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Capacitor } from '@capacitor/core';

const STORAGE_KEY = 'app_login_context'; // 'admin' | '<companySlug>'

// Root route ("/"). Always shows a home page with two choices: Admin Panel
// Login and Company Login (store-code lookup lives at /company/login), plus
// a Company Creation (register) link. Inside the packaged mobile app (no
// address bar), the picked choice is remembered so returning users skip
// straight to their login screen — see login.component's "Switch account"
// link for changing that later. On the web the home page is shown every
// time the base URL is hit.
@Component({
  selector: 'app-entry',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="entry-wrap" *ngIf="showPicker">
      <div class="entry-glow entry-glow-a"></div>
      <div class="entry-glow entry-glow-b"></div>

      <div class="entry-hero">
        <div class="entry-eyebrow">Multi-Store Retail Platform</div>
        <img src="assets/auleco-logo-full-light.png" alt="Auleco — Ecosystem for the Future" class="entry-logo-img">
        <p class="entry-sub">One system to run every store — inventory, POS, orders, and your storefront, all in a single connected workspace built for shoe retailers.</p>
      </div>

      <div class="entry-cards">
        <div class="entry-card entry-card-admin">
          <div class="entry-card-icon entry-card-icon-admin">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#a78bfa" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
          </div>
          <h3>Admin Panel Login</h3>
          <p>Super admin access — manage companies, subscriptions, platform-wide settings, and every store on the network.</p>
          <button class="entry-btn entry-btn-admin" (click)="goAdmin()">Continue to Admin Login &rarr;</button>
        </div>

        <div class="entry-card entry-card-company">
          <div class="entry-card-icon entry-card-icon-company">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#fb7185" stroke-width="2"><path d="M3 9.5 12 3l9 6.5V21a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z"/></svg>
          </div>
          <h3>Company Login</h3>
          <p>Sign in to your store's workspace — inventory, orders, POS, and reports for your team.</p>
          <button class="entry-btn entry-btn-company" (click)="goCompany()">Continue to Company Login &rarr;</button>
        </div>
      </div>

      <div class="entry-footer">
        <span>New to Auleco?</span>
        <a routerLink="/company/register">Create a company &rarr;</a>
      </div>
    </div>
  `,
  styles: [`
    .entry-wrap { min-height: 100vh; display: flex; flex-direction: column; align-items: center; padding: 100px 20px 60px;
      background: radial-gradient(circle at 20% 0%, rgba(139,92,246,.14), transparent 55%), radial-gradient(circle at 85% 100%, rgba(244,63,94,.12), transparent 50%), #08090f;
      position: relative; overflow: hidden; font-family: Inter, system-ui, sans-serif; }
    .entry-glow { position: absolute; border-radius: 999px; filter: blur(110px); }
    .entry-glow-a { top: -120px; left: -120px; width: 420px; height: 420px; background: rgba(139,92,246,.18); }
    .entry-glow-b { bottom: -140px; right: -120px; width: 460px; height: 460px; background: rgba(244,63,94,.15); }
    .entry-hero { position: relative; z-index: 2; display: flex; flex-direction: column; align-items: center; text-align: center; max-width: 640px; }
    .entry-eyebrow { font-size: 12px; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #a78bfa; margin-bottom: 16px; }
    .entry-logo-img { height: 300px; width: auto; margin-top: -50px; margin-bottom: -50px; }
    .entry-sub { margin: 0; font-size: 16px; line-height: 1.6; color: #9ca3af; max-width: 520px; }
    .entry-cards { position: relative; z-index: 2; display: grid; grid-template-columns: 1fr 1fr; gap: 20px; width: 100%; max-width: 880px; margin-top: 52px; }
    @media (max-width: 720px) { .entry-cards { grid-template-columns: 1fr; } .entry-logo-img { height: 180px; margin-top: -30px; margin-bottom: -30px; } }
    .entry-card { border-radius: 22px; padding: 32px 28px; display: flex; flex-direction: column; align-items: flex-start; box-shadow: 0 20px 50px rgba(0,0,0,.35); }
    .entry-card-admin { background: linear-gradient(180deg, rgba(139,92,246,.08), rgba(18,19,28,.9)); border: 1px solid rgba(139,92,246,.25); }
    .entry-card-company { background: linear-gradient(180deg, rgba(244,63,94,.08), rgba(18,19,28,.9)); border: 1px solid rgba(244,63,94,.25); }
    .entry-card-icon { width: 44px; height: 44px; border-radius: 12px; display: flex; align-items: center; justify-content: center; margin-bottom: 18px; }
    .entry-card-icon-admin { background: rgba(139,92,246,.15); }
    .entry-card-icon-company { background: rgba(244,63,94,.15); }
    .entry-card h3 { margin: 0 0 8px; font-size: 19px; font-weight: 800; color: #fff; }
    .entry-card p { margin: 0 0 22px; font-size: 13.5px; color: #9ca3af; line-height: 1.55; }
    .entry-btn { margin-top: auto; width: 100%; color: #fff; border: 0; padding: 13px; border-radius: 12px; font-weight: 700; font-size: 14px; cursor: pointer; font-family: Inter, sans-serif; }
    .entry-btn-admin { background: linear-gradient(135deg,#7c3aed,#8b5cf6); box-shadow: 0 8px 20px rgba(124,58,237,.3); }
    .entry-btn-company { background: linear-gradient(135deg,#e11d48,#f43f5e); box-shadow: 0 8px 20px rgba(225,29,72,.3); }
    .entry-footer { position: relative; z-index: 2; margin-top: 36px; display: flex; align-items: center; gap: 8px; font-size: 13px; color: #6b7280; }
    .entry-footer a { color: #a7f3d0; font-weight: 700; text-decoration: underline; text-underline-offset: 3px; }
  `]
})
export class AppEntryComponent implements OnInit {
  private router = inject(Router);

  showPicker = false;

  ngOnInit() {
    // Native app: remember the last choice so returning users skip the picker.
    if (Capacitor.isNativePlatform()) {
      const remembered = localStorage.getItem(STORAGE_KEY);
      if (!remembered) { this.showPicker = true; return; }
      if (remembered === 'admin') this.router.navigate(['/admin/login']);
      else this.router.navigate(['/', remembered, 'login']);
      return;
    }

    // Web: always show the home page.
    this.showPicker = true;
  }

  goAdmin() {
    localStorage.setItem(STORAGE_KEY, 'admin');
    this.router.navigate(['/admin/login']);
  }

  goCompany() {
    this.router.navigate(['/company/login']);
  }
}

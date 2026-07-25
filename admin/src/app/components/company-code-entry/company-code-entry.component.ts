import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CompanyService } from '../../services/company/company.service';
import { TenantService } from '../../services/tenant/tenant.service';

// Route: /company/login. Rose-themed store-code lookup step (used to live
// inline on the home page). On success, forwards to the existing
// :companySlug/login route (which login.component already themes for company context).
@Component({
  selector: 'app-company-code-entry',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="wrap">
      <div class="glow-a"></div>
      <div class="glow-b"></div>
      <div class="card">
        <a routerLink="/" class="back">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10 19l-7-7m0 0l7-7m-7 7h18"/></svg>
          Back to Home
        </a>

        <div class="head">
          <div class="icon">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2"><path d="M3 9.5 12 3l9 6.5V21a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z"/></svg>
          </div>
          <div class="eyebrow">Company Login</div>
          <h2>Find your store</h2>
          <p class="sub">Enter your store code to continue</p>
        </div>

        <p *ngIf="error" class="error">{{ error }}</p>

        <label class="label">Store Code</label>
        <input class="input" [(ngModel)]="code" placeholder="e.g. A1B2C3" (keyup.enter)="submit()" [disabled]="checking" style="text-transform:uppercase; letter-spacing:.05em;">
        <button class="btn" (click)="submit()" [disabled]="checking || !code.trim()">{{ checking ? 'Checking…' : 'Continue' }}</button>

        <p class="alt"><a routerLink="/admin/login">Are you an admin? Go to Admin Login</a></p>
      </div>
    </div>
  `,
  styles: [`
    .wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 90px 20px 40px; position: relative;
      background: radial-gradient(circle at 50% 0%, rgba(244,63,94,.15), transparent 55%), #0a0808; font-family: Inter, system-ui, sans-serif; overflow: hidden; }
    .glow-a { position: absolute; top: 20%; left: 8%; width: 340px; height: 340px; border-radius: 999px; background: rgba(244,63,94,.12); filter: blur(100px); }
    .glow-b { position: absolute; bottom: 10%; right: 8%; width: 380px; height: 380px; border-radius: 999px; background: rgba(251,113,133,.1); filter: blur(110px); }
    .card { position: relative; z-index: 2; width: 100%; max-width: 400px; background: #161213; border: 1px solid rgba(244,63,94,.22);
      border-radius: 24px; padding: 40px 34px; box-shadow: 0 30px 70px rgba(0,0,0,.5); }
    .back { display: inline-flex; align-items: center; gap: 6px; color: #6b7280; font-size: 12px; text-decoration: none; margin-bottom: 28px; }
    .head { display: flex; flex-direction: column; align-items: center; text-align: center; margin-bottom: 26px; }
    .icon { width: 60px; height: 60px; border-radius: 16px; background: linear-gradient(135deg,#e11d48,#f43f5e); display: flex; align-items: center; justify-content: center;
      margin-bottom: 16px; box-shadow: 0 10px 26px rgba(225,29,72,.35); }
    .eyebrow { font-size: 11px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; color: #fda4af; margin-bottom: 6px; }
    .head h2 { margin: 0; font-size: 20px; font-weight: 800; color: #fff; }
    .sub { margin: 8px 0 0; font-size: 13px; color: #9ca3af; }
    .error { background: rgba(239,68,68,.1); border: 1px solid rgba(239,68,68,.25); color: #fca5a5; font-size: 12.5px; padding: 10px 14px; border-radius: 10px; margin-bottom: 16px; }
    .label { display: block; font-size: 12.5px; font-weight: 600; color: #d1d5db; margin-bottom: 7px; }
    .input { width: 100%; box-sizing: border-box; background: #f5f6fa; border: 1px solid transparent; color: #111827; border-radius: 11px; padding: 12px 14px; font-size: 14px; font-family: Inter, sans-serif; }
    .btn { margin-top: 14px; width: 100%; background: linear-gradient(135deg,#e11d48,#f43f5e); color: #fff; border: 0; padding: 13px; border-radius: 11px;
      font-weight: 700; font-size: 14px; cursor: pointer; font-family: Inter, sans-serif; box-shadow: 0 10px 24px rgba(225,29,72,.3); }
    .btn:disabled { opacity: .5; cursor: default; }
    .alt { text-align: center; margin: 22px 0 0; }
    .alt a { color: #6b7280; font-size: 12px; text-decoration: underline; text-underline-offset: 2px; }
  `]
})
export class CompanyCodeEntryComponent {
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private tenant = inject(TenantService);

  code = '';
  checking = false;
  error = '';

  submit() {
    const code = this.code.trim().toUpperCase();
    if (!code) return;
    this.checking = true;
    this.error = '';
    this.companyService.getPublicCompanyByCode(code).subscribe({
      next: (company) => {
        localStorage.setItem('app_login_context', company.subdomain);
        if (this.tenant.canUseSubdomains()) {
          // Hard-navigate the browser to the company's own subdomain —
          // Angular routing can't cross hostnames within the SPA.
          window.location.href = this.tenant.externalTenantUrl(company.subdomain, 'login');
        } else {
          this.router.navigate(['/', company.subdomain, 'login']);
        }
      },
      error: () => {
        this.checking = false;
        this.error = `No store found for code "${code}". Check the code and try again.`;
      }
    });
  }
}

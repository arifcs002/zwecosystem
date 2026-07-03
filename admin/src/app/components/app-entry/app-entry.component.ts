import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Capacitor } from '@capacitor/core';
import { CompanyService } from '../../services/company/company.service';

const STORAGE_KEY = 'app_login_context'; // 'admin' | '<companySlug>'

// Root route. On the web (browser), the URL bar already carries context
// (/admin/... vs /{slug}/...), so this just forwards to super-admin login —
// unchanged behavior. Inside the packaged mobile app there's no address bar,
// so a company admin has no way to reach their own store's login screen;
// this screen lets them pick "Platform Owner" or type their store code once,
// remembers the choice, and both super admin and every company admin can use
// the exact same APK from then on. See login.component's "Switch account"
// link for changing the remembered choice later.
@Component({
  selector: 'app-entry',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="entry-wrap" *ngIf="showPicker">
      <div class="entry-card">
        <div class="entry-logo">⚡</div>
        <h1>ZW Ecosystem</h1>
        <p class="entry-sub">Sign in as the platform owner, or enter your store's code.</p>

        <button class="entry-admin-btn" (click)="goAdmin()">🛡️ I'm the Platform Owner (Super Admin)</button>

        <div class="entry-divider"><span>or</span></div>

        <label class="entry-label">Store Code</label>
        <input class="entry-input" [(ngModel)]="slug" placeholder="e.g. A1B2C3" (keyup.enter)="goStore()" [disabled]="checking">
        <p *ngIf="error" class="entry-error">{{ error }}</p>
        <button class="entry-store-btn" (click)="goStore()" [disabled]="checking || !slug.trim()">
          {{ checking ? 'Checking…' : 'Continue to My Store' }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .entry-wrap { min-height: 100vh; display: flex; align-items: center; justify-content: center; background: #0d0d1a; padding: 20px; }
    .entry-card { width: 100%; max-width: 380px; background: #12112a; border: 1px solid #2a2850; border-radius: 20px; padding: 32px 26px; text-align: center; }
    .entry-logo { font-size: 40px; margin-bottom: 6px; }
    .entry-card h1 { color: #fff; font-size: 22px; margin: 0 0 6px; }
    .entry-sub { color: #9ca3af; font-size: 13px; margin: 0 0 22px; }
    .entry-admin-btn { width: 100%; background: linear-gradient(135deg,#7c3aed,#6366f1); color: #fff; border: 0;
      padding: 13px; border-radius: 12px; font-weight: 700; font-size: 14px; cursor: pointer; }
    .entry-divider { display: flex; align-items: center; gap: 10px; color: #6b7280; font-size: 12px; margin: 20px 0; }
    .entry-divider::before, .entry-divider::after { content: ''; flex: 1; height: 1px; background: #2a2850; }
    .entry-label { display: block; text-align: left; font-size: 11px; font-weight: 700; text-transform: uppercase;
      letter-spacing: .05em; color: #8b8aa7; margin-bottom: 6px; }
    .entry-input { width: 100%; box-sizing: border-box; background: #1c1b3a; border: 1px solid #2f2d55; color: #fff;
      border-radius: 10px; padding: 12px 14px; font-size: 14px; margin-bottom: 8px; }
    .entry-input:focus { outline: none; border-color: #7c3aed; }
    .entry-error { color: #f87171; font-size: 12px; text-align: left; margin: 0 0 8px; }
    .entry-store-btn { width: 100%; background: #1c1b3a; border: 1px solid #4c1d95; color: #c4b5fd;
      padding: 12px; border-radius: 12px; font-weight: 700; font-size: 14px; cursor: pointer; }
    .entry-store-btn:disabled { opacity: .5; cursor: default; }
  `]
})
export class AppEntryComponent implements OnInit {
  private router = inject(Router);
  private companyService = inject(CompanyService);

  showPicker = false;
  slug = '';
  checking = false;
  error = '';

  ngOnInit() {
    // Web: URL bar already carries context — keep the original direct behavior.
    if (!Capacitor.isNativePlatform()) {
      this.router.navigate(['/admin/login']);
      return;
    }

    const remembered = localStorage.getItem(STORAGE_KEY);
    if (!remembered) { this.showPicker = true; return; }
    if (remembered === 'admin') this.router.navigate(['/admin/login']);
    else this.router.navigate(['/', remembered, 'login']);
  }

  goAdmin() {
    localStorage.setItem(STORAGE_KEY, 'admin');
    this.router.navigate(['/admin/login']);
  }

  goStore() {
    const code = this.slug.trim().toUpperCase();
    if (!code) return;
    this.checking = true;
    this.error = '';
    this.companyService.getPublicCompanyByCode(code).subscribe({
      next: (company) => {
        localStorage.setItem(STORAGE_KEY, company.subdomain);
        this.router.navigate(['/', company.subdomain, 'login']);
      },
      error: () => {
        this.checking = false;
        this.error = `No store found for code "${code}". Check the code and try again.`;
      }
    });
  }
}

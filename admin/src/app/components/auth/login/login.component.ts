import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { Capacitor } from '@capacitor/core';
import { AuthService } from '../../../services/auth/auth.service';
import { ThemeService } from '../../../services/theme/theme.service';
import { CompanyService } from '../../../services/company/company.service';
import { RequiredErrorComponent } from '../../../../app/shared/required-error/required-error.component';
import { TenantService } from '../../../services/tenant/tenant.service';

const APP_ENTRY_STORAGE_KEY = 'app_login_context';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RequiredErrorComponent, RouterModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent implements OnInit {
  email = '';
  password = '';
  error = '';
  isLoading = false;
  showPassword = false;

  companyName = 'AULECO';
  logoUrl = ''; // Empty shows the default shield icon; otherwise shows the image at this URL
  companyLogoUrl: string = '';
  isCompanySubdomain: boolean = false;
  loginContext: string = 'admin'; // 'admin' or companySlug

  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private themeService = inject(ThemeService);
  private companyService = inject(CompanyService);
  private tenant = inject(TenantService);

  // Only relevant inside the packaged app (no address bar) — lets a user
  // return to the Super Admin / Store Code picker to switch accounts.
  get showSwitchAccount(): boolean { return Capacitor.isNativePlatform(); }

  get isAdmin(): boolean { return this.loginContext === 'admin'; }
  get accentGrad(): string {
    return this.isAdmin ? 'linear-gradient(135deg,#7c3aed,#8b5cf6)' : 'linear-gradient(135deg,#e11d48,#f43f5e)';
  }

  switchAccount() {
    localStorage.removeItem(APP_ENTRY_STORAGE_KEY);
    this.router.navigate(['/']);
  }

  ngOnInit() {
    this.detectContext();
  }

  detectContext() {
    // Check URL path
    const url = this.router.url;
    if (url.includes('/admin/login')) {
      this.loginContext = 'admin';
      this.isCompanySubdomain = false;
      this.companyName = 'AULECO';
      this.logoUrl = 'assets/auleco-mark-light.png';
      this.themeService.applyTheme('deep-royal-amethyst');
      return;
    }

    // Company subdomain (zairasworld.auleco.com/login) — slug comes from the
    // hostname, no route param. Falls back to the legacy /:companySlug/login
    // route param for IP access / local dev without subdomain DNS.
    const hostSlug = this.tenant.getSubdomainFromHost();
    if (hostSlug) {
      this.setCompanyContext(hostSlug);
      return;
    }

    this.route.paramMap.subscribe(params => {
      const slug = params.get('companySlug');
      if (slug) this.setCompanyContext(slug);
    });
  }

  private setCompanyContext(slug: string) {
    this.loginContext = slug;
    this.companyService.getPublicCompany(slug).subscribe({
      next: (company) => {
        this.companyName = company.name;
        localStorage.setItem('tenant_company_id', company.id.toString());
        if (company.logoUrl) {
          this.logoUrl = company.logoUrl;
          this.companyLogoUrl = company.logoUrl;
        } else {
          this.logoUrl = '';
        }
      },
      error: () => {
        this.companyName = 'Store Not Found';
        this.loginContext = 'invalid';
      }
    });
    this.themeService.applyTheme('cyberpunk-teal');
  }

  onSubmit() {
    if (!this.email || !this.password) {
      this.error = 'Please enter both username/email and password';
      return;
    }

    this.isLoading = true;
    this.error = '';
    // Mobile keyboards can auto-capitalize/pad the first field even with
    // autocapitalize="off" set on some devices — trim defensively since the
    // email lookup on the backend is an exact, case-sensitive match.
    const email = this.email.trim();

    this.authService.login(email, this.password, this.loginContext).subscribe({
      next: (response: any) => {
        if (this.loginContext === 'admin') {
          this.router.navigate(['/admin/dashboard']);
        } else {
          this.router.navigate(this.tenant.routeSegments(this.loginContext, 'workspace', 'dashboard'));
        }
      },
      error: (err: any) => {
        this.error = err.error?.message || 'Invalid credentials';
        this.isLoading = false;
      }
    });
  }
}

import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../../services/auth/auth.service';
import { ThemeService } from '../../../services/theme/theme.service';
import { CompanyService } from '../../../services/company/company.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent implements OnInit {
  email = '';
  password = '';
  error = '';
  isLoading = false;

  companyName = 'ZW ECOSYSTEM';
  logoUrl = ''; // If 'ZW', show custom ZW logo, if empty show default, else show image
  companyLogoUrl: string = '';
  isCompanySubdomain: boolean = false;
  loginContext: string = 'admin'; // 'admin' or companySlug

  private authService = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private themeService = inject(ThemeService);
  private companyService = inject(CompanyService);

  ngOnInit() {
    this.detectContext();
  }

  detectContext() {
    // Check URL path
    const url = this.router.url;
    if (url.includes('/admin/login')) {
      this.loginContext = 'admin';
      this.isCompanySubdomain = false;
      this.companyName = 'ZW ECOSYSTEM';
      this.logoUrl = 'ZW';
      this.themeService.applyTheme('deep-royal-amethyst');
    } else {
      // Must be /:companySlug/login
      this.route.paramMap.subscribe(params => {
        const slug = params.get('companySlug');
        if (slug) {
          this.loginContext = slug;
          if (this.loginContext === 'admin') {
            this.companyName = 'ZW ECOSYSTEM';
            this.logoUrl = 'ZW';
          } else {
            this.companyService.getCompanies().subscribe({
              next: (companies: any[]) => {
                const company = companies.find((c: any) => c.subdomain === this.loginContext);
                if (company) {
                  this.companyName = company.name;
                  localStorage.setItem('tenant_company_id', company.id);
                  if (company.logoUrl) {
                    this.logoUrl = company.logoUrl;
                    this.companyLogoUrl = company.logoUrl;
                  } else {
                    this.logoUrl = '';
                  }
                } else {
                  this.companyName = 'Store Not Found';
                  this.loginContext = 'invalid';
                }
              },
              error: (err: any) => console.error('Error fetching companies:', err)
            });
          }
          this.themeService.applyTheme('cyberpunk-teal'); // Fallback or dynamic
        }
      });
    }
  }

  onSubmit() {
    if (!this.email || !this.password) {
      this.error = 'Please enter both username/email and password';
      return;
    }

    this.isLoading = true;
    this.error = '';

    this.authService.login(this.email, this.password, this.loginContext).subscribe({
      next: (response: any) => {
        if (this.loginContext === 'admin') {
          this.router.navigate(['/admin/dashboard']);
        } else {
          this.router.navigate(['/', this.loginContext, 'workspace', 'dashboard']);
        }
      },
      error: (err: any) => {
        this.error = err.error?.message || 'Invalid credentials';
        this.isLoading = false;
      }
    });
  }
}

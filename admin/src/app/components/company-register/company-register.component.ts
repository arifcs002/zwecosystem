import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { environment } from '../../../environments/environment';
import { RequiredErrorComponent } from '../../shared/required-error/required-error.component';

export interface PublicPlan { id: number; name: string; price: number; billingCycle: string; perks: string[]; }

// Turns a plan's feature JSON into short, readable bullets. Only things the plan actually includes are listed.
export function planPerks(features: string | null | undefined): string[] {
  let f: Record<string, any> = {};
  try { f = features ? JSON.parse(features) : {}; } catch { f = {}; }
  const out: string[] = [];
  if (typeof f['max_products'] === 'number') out.push(`Up to ${f['max_products']} products`);
  if (f['ecommerce_enabled'] !== false) out.push('Online store with your own web address');
  if (f['pos_enabled'] !== false) out.push('Point of Sale (POS) for the counter');
  out.push(f['multi_staff'] === true ? 'Multiple staff logins' : 'Owner login');
  return out;
}

const VALID_PHONE_PREFIXES = ['019', '015', '014', '013', '018', '017', '016'];

@Component({
  selector: 'app-company-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, RequiredErrorComponent],
  templateUrl: './company-register.component.html'
})
export class CompanyRegisterComponent implements OnInit {
  private http = inject(HttpClient);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  formData = {
    companyName: '',
    subdomain: '',
    ownerFirstName: '',
    ownerLastName: '',
    ownerEmail: '',
    ownerPhone: '',
    password: '',
    address: '',
    division: '',
    district: '',
    thana: '',
    planId: null as number | null
  };

  plans: PublicPlan[] = [];
  trialDays = 0;

  ngOnInit() {
    this.http.get<any[]>(`${environment.apiUrl}/subscriptionplans/public`).subscribe({
      next: (rows) => {
        this.plans = rows.map(r => ({ id: r.id, name: r.name, price: r.price, billingCycle: r.billingCycle, perks: planPerks(r.features) }));
        this.trialDays = rows[0]?.trialDays ?? 0;
        // Default to the cheapest plan so the form works even if the price list can't load.
        // A plan picked on the home page (?plan=ID) wins; otherwise default to the cheapest.
        const wanted = Number(this.route.snapshot.queryParamMap.get('plan'));
        if (this.plans.length && this.formData.planId == null) this.formData.planId = this.plans.find(p => p.id === wanted)?.id ?? this.plans[0].id;
      },
      error: () => { /* signup still works: the server falls back to the Basic plan */ }
    });
  }

  loading = false;
  successMessage = '';
  errorMessage = '';
  showPassword = false;

  subdomainError = '';
  phoneError = '';

  // Populated from the register-company response for the success screen.
  registeredStoreCode = '';
  registeredStoreUrl = '';

  get rootDomain() { return environment.rootDomain; }

  // Lowercase, letters/numbers/underscore only, max 30 chars — sanitized live
  // as the user types (not just validated after the fact).
  sanitizeSubdomain(event: Event) {
    const input = event.target as HTMLInputElement;
    const val = input.value.toLowerCase().replace(/[^a-z0-9_]/g, '').slice(0, 30);
    this.formData.subdomain = val;
    input.value = val;
    this.subdomainError = val.length === 30 ? 'Maximum 30 characters.' : '';
  }

  // Bangladeshi mobile format: 11 digits, starting with one of the valid
  // prefixes. If the user types without the leading 0 (e.g. "1712345678"),
  // it's prepended automatically.
  sanitizePhone(event: Event) {
    const input = event.target as HTMLInputElement;
    let val = input.value.replace(/[^0-9]/g, '');
    if (val.length > 0 && val[0] === '1') val = '0' + val;
    val = val.slice(0, 11);
    this.formData.ownerPhone = val;
    input.value = val;

    if (!val) { this.phoneError = ''; return; }
    const prefix = val.slice(0, 3);
    if (!VALID_PHONE_PREFIXES.includes(prefix)) {
      this.phoneError = 'Must start with 013, 014, 015, 016, 017, 018, or 019.';
    } else if (val.length < 11) {
      this.phoneError = 'Phone number must be 11 digits.';
    } else {
      this.phoneError = '';
    }
  }

  get hasBlockingErrors(): boolean {
    return !!this.subdomainError || !!this.phoneError;
  }

  onSubmit() {
    if (this.hasBlockingErrors) return;
    this.loading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.http.post(`${environment.apiUrl}/auth/register-company`, this.formData).subscribe({
      next: (res: any) => {
        this.loading = false;
        this.successMessage = res.message || 'Registration successful. Awaiting Super Admin approval.';
        this.registeredStoreCode = res.appCode || '';
        const subdomain = res.subdomain || this.formData.subdomain;
        this.registeredStoreUrl = `${subdomain}.${this.rootDomain}`;
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Registration failed. Please try again.';
      }
    });
  }

  get whatsappShareUrl(): string {
    const text = `My Auleco store is registered!\n\nStore URL: http://${this.registeredStoreUrl}\nStore Code: ${this.registeredStoreCode}`;
    return `https://wa.me/?text=${encodeURIComponent(text)}`;
  }

  goBack() {
    this.router.navigate(['/']);
  }
}

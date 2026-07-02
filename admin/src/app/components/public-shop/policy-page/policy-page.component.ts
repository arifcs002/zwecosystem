import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { CompanyService } from '../../../services/company/company.service';
import { SettingsService } from '../../../services/settings/settings.service';

const PAGE_TITLES: Record<string, string> = {
  about: 'About Us',
  privacy: 'Privacy Policy',
  refund: 'Refund Policy',
  terms: 'Terms & Conditions',
  contact: 'Contact Us'
};

@Component({
  selector: 'app-policy-page',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-h-screen bg-gray-50 flex flex-col" style="font-family: 'Outfit', sans-serif;">
      <nav class="bg-white shadow-sm border-b border-gray-200">
        <div class="max-w-screen-lg mx-auto px-4 h-16 flex items-center justify-between">
          <div class="flex items-center gap-3 cursor-pointer" (click)="backToShop()">
            <img *ngIf="companyLogo" [src]="companyLogo" class="h-9 w-9 rounded-xl object-contain border border-gray-100" alt="">
            <span class="font-bold text-gray-900 text-lg">{{ companyName }}</span>
          </div>
          <button (click)="backToShop()" class="text-sm text-gray-500 hover:text-indigo-600 font-medium">← Back to shop</button>
        </div>
      </nav>

      <div class="max-w-screen-md mx-auto w-full px-4 py-10 flex-1">
        <h1 class="text-3xl font-extrabold text-gray-900 mb-6">{{ title }}</h1>

        <div *ngIf="pageKey === 'contact'" class="bg-white rounded-2xl border border-gray-100 shadow-sm p-6 space-y-3 text-gray-700">
          <p *ngIf="contactAddress"><span class="font-semibold">📍 Address:</span> {{ contactAddress }}</p>
          <p *ngIf="contactPhone"><span class="font-semibold">📞 Phone:</span> {{ contactPhone }}</p>
          <p *ngIf="contactEmail"><span class="font-semibold">✉️ Email:</span> {{ contactEmail }}</p>
          <p *ngIf="!contactAddress && !contactPhone && !contactEmail" class="text-gray-400">No contact information available.</p>
        </div>

        <div *ngIf="pageKey !== 'contact'" class="bg-white rounded-2xl border border-gray-100 shadow-sm p-6">
          <p *ngIf="content" class="text-gray-700 whitespace-pre-line leading-relaxed">{{ content }}</p>
          <p *ngIf="!content" class="text-gray-400">This page has not been set up yet.</p>
        </div>
      </div>
    </div>
  `
})
export class PolicyPageComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private settingsService = inject(SettingsService);

  companySlug = '';
  companyName = '';
  companyLogo = '';
  pageKey = '';
  title = '';
  content = '';
  contactAddress = ''; contactPhone = ''; contactEmail = '';

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      this.pageKey = (params.get('key') || 'about').toLowerCase();
      this.title = PAGE_TITLES[this.pageKey] || 'Information';

      this.companyService.getPublicCompany(this.companySlug).subscribe({
        next: (comp) => {
          this.companyName = comp.name;
          this.companyLogo = comp.logoUrl || '';
          localStorage.setItem('tenant_company_id', comp.id.toString());
          this.loadContent();
        },
        error: () => this.loadContent()
      });
    });
  }

  private loadContent() {
    this.settingsService.getPublicSettings().subscribe({
      next: (settings) => {
        const v = (k: string) => settings.find(s => s.key === k)?.value || '';
        this.content = v(`page_${this.pageKey}`);
        this.contactAddress = v('contact_address');
        this.contactPhone = v('contact_phone');
        this.contactEmail = v('contact_email');
      }
    });
  }

  backToShop() { this.router.navigate(['/', this.companySlug]); }
}

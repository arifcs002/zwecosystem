import { Component, Input, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { ImgUrlPipe } from '../../../pipes/img-url.pipe';
import { ImgFallbackDirective } from '../../../directives/img-fallback.directive';
import { StorefrontData } from '../../../services/storefront/storefront.service';
import { TenantService } from '../../../services/tenant/tenant.service';

const SOCIAL: { key: string; label: string }[] = [
  { key: 'facebook_link', label: 'Facebook' }, { key: 'instagram_link', label: 'Instagram' },
  { key: 'twitter_link', label: 'X' }, { key: 'youtube_link', label: 'YouTube' },
  { key: 'whatsapp_link', label: 'WhatsApp' }, { key: 'tiktok_link', label: 'TikTok' },
  { key: 'linkedin_link', label: 'LinkedIn' }
];

// Footer shared by all inner storefront pages (the home page keeps its own copy).
@Component({
  selector: 'app-shop-footer',
  standalone: true,
  imports: [TranslatePipe, CommonModule, RouterModule, ImgUrlPipe, ImgFallbackDirective],
  template: `
  <footer *ngIf="store" class="bg-gray-900 text-gray-400 mt-auto">
    <div class="max-w-screen-xl mx-auto px-6 py-10">
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-8">
        <div>
          <div class="flex items-center gap-2.5 mb-3">
            <img *ngIf="store.company.logoUrl" [src]="store.company.logoUrl | imgUrl" appImgFallback class="h-8 w-8 rounded-lg object-contain" alt="">
            <span class="font-bold text-white text-base">{{ store.company.name }}</span>
          </div>
          <p *ngIf="val('footer_about_text')" class="text-xs text-gray-500 leading-relaxed mb-4">{{ val('footer_about_text') }}</p>
          <div *ngIf="socials.length" class="flex items-center gap-2.5">
            <a *ngFor="let s of socials" [href]="s.url" target="_blank" rel="noopener noreferrer" [title]="s.label"
               class="w-8 h-8 rounded-full bg-white/10 hover:bg-indigo-600 flex items-center justify-center text-xs font-bold text-gray-300 hover:text-white transition">{{ s.label.charAt(0) }}</a>
          </div>
        </div>
        <div>
          <h4 class="text-white font-semibold text-sm mb-3">{{ 'Customer Care' | t }}</h4>
          <ul class="space-y-2 text-xs">
            <li><a [routerLink]="link('track-order')" class="hover:text-white transition">{{ 'Track Your Order' | t }}</a></li>
            <li><a [routerLink]="link('shop')" class="hover:text-white transition">{{ 'All Products' | t }}</a></li>
            <li><a [routerLink]="link('wishlist')" class="hover:text-white transition">{{ 'My Wishlist' | t }}</a></li>
            <li><a [routerLink]="link('page', 'refund')" class="hover:text-white transition">{{ 'Refund Policy' | t }}</a></li>
            <li><a [routerLink]="link('page', 'privacy')" class="hover:text-white transition">{{ 'Privacy Policy' | t }}</a></li>
            <li><a [routerLink]="link('page', 'terms')" class="hover:text-white transition">{{ 'Terms &amp; Conditions' | t }}</a></li>
          </ul>
        </div>
        <div *ngIf="store.categories.length">
          <h4 class="text-white font-semibold text-sm mb-3">{{ 'Shop By' | t }}</h4>
          <ul class="space-y-2 text-xs">
            <li *ngFor="let c of topCategories"><a (click)="goCategory(c.id)" class="hover:text-white transition cursor-pointer">{{ c.name }}</a></li>
          </ul>
        </div>
        <div *ngIf="val('contact_address') || val('contact_phone') || val('contact_email')">
          <h4 class="text-white font-semibold text-sm mb-3">{{ 'Get in Touch' | t }}</h4>
          <ul class="space-y-2 text-xs">
            <li *ngIf="val('contact_address')" class="flex items-start gap-2"><span>📍</span><span>{{ val('contact_address') }}</span></li>
            <li *ngIf="val('contact_phone')" class="flex items-center gap-2"><span>📞</span><a [href]="'tel:' + val('contact_phone')" class="hover:text-white">{{ val('contact_phone') }}</a></li>
            <li *ngIf="val('contact_email')" class="flex items-center gap-2"><span>✉️</span><span>{{ val('contact_email') }}</span></li>
          </ul>
        </div>
      </div>
      <div class="border-t border-white/10 mt-8 pt-5 text-xs text-gray-600">© {{ year }} {{ store.company.name }}. All rights reserved.</div>
    </div>
  </footer>`
})
export class ShopFooterComponent {
  private tenant = inject(TenantService);
  private router = inject(Router);
  @Input() store: StorefrontData | null = null;
  @Input() slug = '';
  year = new Date().getFullYear();

  link(...parts: string[]): any[] { return this.tenant.routeSegments(this.slug, ...parts); }
  val(key: string): string { return this.store?.settings.find(s => s.key === key)?.value || ''; }
  get socials() { return SOCIAL.map(s => ({ ...s, url: this.val(s.key) })).filter(s => !!s.url); }
  get topCategories() { return (this.store?.categories || []).slice(0, 8); }
  goCategory(id: number | string | undefined) { this.router.navigate(this.link('category', String(id))); }
}

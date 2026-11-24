import { Component, Input, inject } from '@angular/core';
import { TranslatePipe } from '../../../i18n/translate.pipe';

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
    imports: [TranslatePipe, RouterModule, ImgUrlPipe, ImgFallbackDirective],
    template: `
  @if (store) {
    <footer class="bg-gray-900 text-gray-400 mt-auto">
      <div class="max-w-screen-xl mx-auto px-6 py-10">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-8">
          <div>
            <div class="flex items-center gap-2.5 mb-3">
              @if (store.company.logoUrl) {
                <img [src]="store.company.logoUrl | imgUrl" appImgFallback class="h-8 w-8 rounded-lg object-contain" alt="">
              }
              <span class="font-bold text-white text-base">{{ store.company.name }}</span>
            </div>
            @if (val('footer_about_text')) {
              <p class="text-xs text-gray-500 leading-relaxed mb-4">{{ val('footer_about_text') }}</p>
            }
            @if (socials.length) {
              <div class="flex items-center gap-2.5">
                @for (s of socials; track s) {
                  <a [href]="s.url" target="_blank" rel="noopener noreferrer" [title]="s.label"
                  class="w-8 h-8 rounded-full bg-white/10 hover:bg-indigo-600 flex items-center justify-center text-xs font-bold text-gray-300 hover:text-white transition">{{ s.label.charAt(0) }}</a>
                }
              </div>
            }
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
          @if (store.categories.length) {
            <div>
              <h4 class="text-white font-semibold text-sm mb-3">{{ 'Shop By' | t }}</h4>
              <ul class="space-y-2 text-xs">
                @for (c of topCategories; track c) {
                  <li><a (click)="goCategory(c.id)" class="hover:text-white transition cursor-pointer">{{ c.name }}</a></li>
                }
              </ul>
            </div>
          }
          @if (val('contact_address') || val('contact_phone') || val('contact_email')) {
            <div>
              <h4 class="text-white font-semibold text-sm mb-3">{{ 'Get in Touch' | t }}</h4>
              <ul class="space-y-2 text-xs">
                @if (val('contact_address')) {
                  <li class="flex items-start gap-2"><span>📍</span><span>{{ val('contact_address') }}</span></li>
                }
                @if (val('contact_phone')) {
                  <li class="flex items-center gap-2"><span>📞</span><a [href]="'tel:' + val('contact_phone')" class="hover:text-white">{{ val('contact_phone') }}</a></li>
                }
                @if (val('contact_email')) {
                  <li class="flex items-center gap-2"><span>✉️</span><span>{{ val('contact_email') }}</span></li>
                }
              </ul>
            </div>
          }
        </div>
        <div class="border-t border-white/10 mt-8 pt-5 text-xs text-gray-600">© {{ year }} {{ store.company.name }}. All rights reserved.</div>
      </div>
    </footer>
  }`
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

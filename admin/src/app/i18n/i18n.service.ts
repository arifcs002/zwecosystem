import { Injectable } from '@angular/core';
import { BN } from './storefront-bn';

export type Lang = 'en' | 'bn';
const KEY = 'shop_lang';

// Storefront language (English / বাংলা). The choice is remembered per browser and also drives
// <html lang> and the Bangla web font. Admin screens are intentionally not translated here.
@Injectable({ providedIn: 'root' })
export class I18nService {
  lang: Lang = 'en';

  constructor() {
    try {
      const saved = localStorage.getItem(KEY);
      this.lang = saved === 'bn' || saved === 'en' ? saved
        : (navigator.language || '').toLowerCase().startsWith('bn') ? 'bn' : 'en';
    } catch { this.lang = 'en'; }
    this.apply();
  }

  // t('Only {n} left', { n: 3 }) — {name} placeholders are filled in after translating.
  t(text: string, params?: Record<string, string | number>): string {
    let out = this.lang === 'bn' && text ? (BN[text] ?? text) : text;
    if (params && out) for (const k of Object.keys(params)) out = out.split('{' + k + '}').join(String(params[k]));
    return out;
  }

  set(lang: Lang) {
    this.lang = lang;
    try { localStorage.setItem(KEY, lang); } catch { /* private mode */ }
    this.apply();
  }

  toggle() { this.set(this.lang === 'en' ? 'bn' : 'en'); }

  private apply() {
    const el = document.documentElement;
    el.lang = this.lang;
    el.classList.toggle('lang-bn', this.lang === 'bn');
  }
}

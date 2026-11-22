import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { resolveImageUrl } from '../../utils/image-url.util';

export interface PageSeo { title: string; description?: string | null; image?: string | null; type?: 'website' | 'product'; }

/**
 * Keeps the browser tab title and the <meta> description / Open Graph tags in step with the current storefront
 * page. (Facebook & co. don't run JavaScript, so link previews come from the server-rendered crawler page; this
 * covers what a person sees and the search engines that do render JS, such as Google.)
 */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private title = inject(Title);
  private meta = inject(Meta);

  set(p: PageSeo) {
    this.title.setTitle(p.title);
    const desc = (p.description || '').replace(/\s+/g, ' ').trim().slice(0, 300);
    this.tag('name', 'description', desc);
    this.tag('property', 'og:title', p.title);
    this.tag('property', 'og:description', desc);
    this.tag('property', 'og:type', p.type || 'website');
    this.tag('property', 'og:url', window.location.href.split('#')[0]);
    this.tag('property', 'og:image', p.image ? resolveImageUrl(p.image) : '');
  }

  private tag(attr: 'name' | 'property', key: string, content: string) {
    if (!content) { this.meta.removeTag(`${attr}='${key}'`); return; }
    this.meta.updateTag({ [attr]: key, content });
  }
}

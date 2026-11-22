import { Directive, ElementRef, HostListener, inject } from '@angular/core';

// A neutral inline placeholder (no network request) — used when a product image is
// missing, was deleted, or its URL is broken, so shoppers never see a broken-image
// icon with alt text.
export const IMG_PLACEHOLDER =
  'data:image/svg+xml;utf8,' + encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120">
       <rect width="120" height="120" fill="#f3f4f6"/>
       <g fill="none" stroke="#c4c8d0" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
         <rect x="30" y="38" width="60" height="44" rx="6"/>
         <circle cx="48" cy="54" r="5"/>
         <path d="M34 78l18-16 12 10 10-8 12 14"/>
       </g>
     </svg>`);

/** Usage: <img [src]="..." appImgFallback>. Swaps to the placeholder once, on load error. */
@Directive({ selector: 'img[appImgFallback]', standalone: true })
export class ImgFallbackDirective {
  private el = inject<ElementRef<HTMLImageElement>>(ElementRef);

  @HostListener('error')
  onError() {
    const img = this.el.nativeElement;
    if (img.src === IMG_PLACEHOLDER || img.dataset['fallbackApplied']) return; // never loop
    img.dataset['fallbackApplied'] = '1';
    img.src = IMG_PLACEHOLDER;
    img.classList.add('img-fallback');
  }
}

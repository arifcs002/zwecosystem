import { Component, Input, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ImgUrlPipe } from '../../../../pipes/img-url.pipe';
import { HeroSlide } from '../../../../models/storefront-layout.model';

@Component({
  selector: 'app-hero-slider',
  standalone: true,
  imports: [CommonModule, RouterModule, ImgUrlPipe],
  templateUrl: './hero-slider.component.html',
  styleUrl: './hero-slider.component.css'
})
export class HeroSliderComponent implements OnInit, OnDestroy {
  @Input() slides: HeroSlide[] = [];
  @Input() companySlug = '';

  current = 0;
  private timer: any;

  ngOnInit() {
    if (this.slides.length > 1) {
      this.timer = setInterval(() => this.next(), 6000);
    }
  }
  ngOnDestroy() { if (this.timer) clearInterval(this.timer); }

  go(i: number) { this.current = i; }
  next() { this.current = (this.current + 1) % this.slides.length; }

  // Stored links are store-relative ("/category/1"); prefix the tenant slug so
  // they resolve under /{slug}/…. A blank link renders no button.
  linkFor(link?: string): any[] | null {
    if (!link) return null;
    const clean = link.replace(/^\/+/, '');
    return ['/', this.companySlug, ...clean.split('/').filter(Boolean)];
  }
}

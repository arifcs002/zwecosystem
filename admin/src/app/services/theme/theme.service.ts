import { Injectable, Inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export const THEMES: { [key: string]: { primary: string; primaryHover: string; gradientStart: string; gradientEnd: string } } = {
  'cyberpunk-teal': {
    primary: '#0ea5e9',
    primaryHover: '#0284c7',
    gradientStart: '#06b6d4',
    gradientEnd: '#3b82f6'
  },
  'sunset-amber': {
    primary: '#f59e0b',
    primaryHover: '#d97706',
    gradientStart: '#fb923c',
    gradientEnd: '#ef4444'
  },
  'obsidian-emerald': {
    primary: '#10b981',
    primaryHover: '#059669',
    gradientStart: '#34d399', 
    gradientEnd: '#059669'
  },
  'deep-royal-amethyst': {
    primary: '#8b5cf6',
    primaryHover: '#7c3aed',
    gradientStart: '#a855f7', 
    gradientEnd: '#6366f1'
  },
  'ocean-abyssal': {
    primary: '#3b82f6',
    primaryHover: '#2563eb',
    gradientStart: '#3b82f6', 
    gradientEnd: '#1e3a8a'
  }
};

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  constructor(@Inject(PLATFORM_ID) private platformId: Object) {}

  applyTheme(themeId: string) {
    if (!isPlatformBrowser(this.platformId)) return;

    const theme = THEMES[themeId] || THEMES['cyberpunk-teal'];
    
    document.documentElement.style.setProperty('--theme-primary', theme.primary);
    document.documentElement.style.setProperty('--theme-primary-hover', theme.primaryHover);
    document.documentElement.style.setProperty('--theme-grad-start', theme.gradientStart);
    document.documentElement.style.setProperty('--theme-grad-end', theme.gradientEnd);
  }
}

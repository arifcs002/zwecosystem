import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { SettingsService, CompanySetting } from '../../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../../services/category/category.service';
import { GlobalNotificationService } from '../../../../services/global-notification/global-notification.service';

@Component({
  selector: 'app-ecommerce-navigation',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './ecommerce-navigation.component.html',
  styleUrl: '../ecommerce-config.css'
})
export class EcommerceNavigationComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);
  private notify = inject(GlobalNotificationService);

  categories: Category[] = [];
  navCategoryIds: number[] = []; // ordered

  announcementEnabled = false;
  announcementText = '';
  announcementLink = '';

  isLoading = false;
  saving = false;

  ngOnInit() {
    this.isLoading = true;
    forkJoin({
      cats: this.categoryService.getCategories(),
      settings: this.settingsService.getSettings()
    }).subscribe({
      next: ({ cats, settings }) => {
        this.categories = cats.sort((a, b) => a.name.localeCompare(b.name));
        settings.forEach(s => {
          if (s.key === 'announcement_enabled') this.announcementEnabled = s.value === 'true';
          if (s.key === 'announcement_text') this.announcementText = s.value;
          if (s.key === 'announcement_link') this.announcementLink = s.value;
          if (s.key === 'nav_categories') this.navCategoryIds = s.value ? s.value.split(',').filter(Boolean).map(Number) : [];
        });
        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  isNavSelected(id: number) { return this.navCategoryIds.includes(Number(id)); }
  toggleNav(id: number) {
    const cid = Number(id);
    const i = this.navCategoryIds.indexOf(cid);
    if (i >= 0) this.navCategoryIds.splice(i, 1);
    else this.navCategoryIds.push(cid);
  }
  moveNav(index: number, delta: number) {
    const to = index + delta;
    if (to < 0 || to >= this.navCategoryIds.length) return;
    [this.navCategoryIds[index], this.navCategoryIds[to]] = [this.navCategoryIds[to], this.navCategoryIds[index]];
  }
  catName(id: number) { return this.categories.find(c => Number(c.id) === Number(id))?.name || '—'; }

  save() {
    this.saving = true;
    const settings: CompanySetting[] = [
      { key: 'announcement_enabled', value: this.announcementEnabled ? 'true' : 'false', groupName: 'STOREFRONT' },
      { key: 'announcement_text', value: this.announcementText, groupName: 'STOREFRONT' },
      { key: 'announcement_link', value: this.announcementLink, groupName: 'STOREFRONT' },
      { key: 'nav_categories', value: this.navCategoryIds.join(','), groupName: 'STOREFRONT' }
    ];
    this.settingsService.updateSettings(settings).subscribe({
      next: () => { this.saving = false; this.notify.notify({ type: 'success', title: 'Saved', message: 'Navigation settings saved.', ttlMs: 3000 }); },
      error: () => { this.saving = false; this.notify.notify({ type: 'error', title: 'Failed', message: 'Could not save.', ttlMs: 5000 }); }
    });
  }
}

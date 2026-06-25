import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsService, CompanySetting } from '../../../services/settings/settings.service';
import { CategoryService, Category } from '../../../services/category/category.service';

@Component({
  selector: 'app-dashboard-config',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard-config.component.html',
  styleUrl: './dashboard-config.component.css'
})
export class DashboardConfigComponent implements OnInit {
  private settingsService = inject(SettingsService);
  private categoryService = inject(CategoryService);

  categories: Category[] = [];
  selectedCategoryIds: { [id: string]: boolean } = {};

  storeName = '';
  storePhone = '';
  primaryColor = '#7c3aed';
  logoUrl = '';
  facebookLink = '';
  instagramLink = '';

  successMsg = '';
  errorMsg = '';
  isLoading = false;

  ngOnInit() {
    this.loadData();
  }

  loadData() {
    this.isLoading = true;
    this.categoryService.getCategories().subscribe({
      next: (cats: Category[]) => {
        this.categories = cats;
        this.loadSettings();
      },
      error: (err: any) => {
        this.errorMsg = 'Failed to load categories.';
        this.isLoading = false;
      }
    });
  }

  loadSettings() {
    this.settingsService.getSettings().subscribe({
      next: (settings: CompanySetting[]) => {
        settings.forEach((s: CompanySetting) => {
          if (s.key === 'store_name') this.storeName = s.value;
          if (s.key === 'store_phone') this.storePhone = s.value;
          if (s.key === 'primary_color') this.primaryColor = s.value;
          if (s.key === 'logo_url') this.logoUrl = s.value;
          if (s.key === 'facebook_link') this.facebookLink = s.value;
          if (s.key === 'instagram_link') this.instagramLink = s.value;
          if (s.key === 'visible_dashboard_categories') {
            const ids = s.value ? s.value.split(',') : [];
            this.selectedCategoryIds = {};
            ids.forEach((id: string) => {
              if (id) this.selectedCategoryIds[id] = true;
            });
          }
        });
        this.isLoading = false;
      },
      error: (err: any) => {
        this.errorMsg = 'Failed to load settings.';
        this.isLoading = false;
      }
    });
  }

  saveSettings() {
    this.isLoading = true;
    this.successMsg = '';
    this.errorMsg = '';

    const selectedIds = Object.keys(this.selectedCategoryIds).filter(id => this.selectedCategoryIds[id]).join(',');

    const settings: CompanySetting[] = [
      { key: 'store_name', value: this.storeName, groupName: 'GENERAL' },
      { key: 'store_phone', value: this.storePhone, groupName: 'GENERAL' },
      { key: 'primary_color', value: this.primaryColor, groupName: 'THEME' },
      { key: 'logo_url', value: this.logoUrl, groupName: 'THEME' },
      { key: 'facebook_link', value: this.facebookLink, groupName: 'SOCIAL' },
      { key: 'instagram_link', value: this.instagramLink, groupName: 'SOCIAL' },
      { key: 'visible_dashboard_categories', value: selectedIds, groupName: 'DASHBOARD' }
    ];

    this.settingsService.updateSettings(settings).subscribe({
      next: () => {
        this.successMsg = 'Configuration saved successfully!';
        this.isLoading = false;
        setTimeout(() => this.successMsg = '', 3000);
      },
      error: (err: any) => {
        this.errorMsg = 'Failed to save configuration.';
        this.isLoading = false;
      }
    });
  }
}

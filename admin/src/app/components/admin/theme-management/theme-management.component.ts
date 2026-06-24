import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

interface CompanyThemeConfig {
  companyId: string;
  companyName: string;
  subdomain: string;
  themeId: string;
}

@Component({
  selector: 'app-theme-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './theme-management.component.html',
  styleUrl: './theme-management.component.css'
})
export class ThemeManagementComponent implements OnInit {
  
  companies: CompanyThemeConfig[] = [
    { companyId: '1', companyName: 'Demo Fashion Shop', subdomain: 'fashion', themeId: 'cyberpunk-teal' },
    { companyId: '2', companyName: 'Zaira Retail Ltd', subdomain: 'zaira', themeId: 'sunset-amber' },
  ];

  themes = [
    { id: 'cyberpunk-teal', name: 'Cyberpunk Teal (Default)' },
    { id: 'sunset-amber', name: 'Sunset Amber' },
    { id: 'obsidian-emerald', name: 'Obsidian Emerald' },
    { id: 'deep-royal-amethyst', name: 'Deep Royal Amethyst' },
    { id: 'ocean-abyssal', name: 'Ocean Abyssal' }
  ];

  selectedCompanyId: string = '';
  selectedThemeId: string = '';
  
  showSuccessPopup = false;
  successMessage = '';

  ngOnInit() {
    if (this.companies.length > 0) {
      this.selectedCompanyId = this.companies[0].companyId;
      this.selectedThemeId = this.companies[0].themeId;
    }
  }

  onCompanyChange() {
    const comp = this.companies.find(c => c.companyId === this.selectedCompanyId);
    if (comp) {
      this.selectedThemeId = comp.themeId;
    }
  }

  saveTheme() {
    const comp = this.companies.find(c => c.companyId === this.selectedCompanyId);
    if (comp) {
      comp.themeId = this.selectedThemeId;
      this.successMessage = `Theme updated for ${comp.companyName} successfully!`;
      this.showSuccessPopup = true;
    }
  }

  closePopup() {
    this.showSuccessPopup = false;
  }

  getSubdomain(): string {
    const comp = this.companies.find(c => c.companyId === this.selectedCompanyId);
    return comp ? comp.subdomain : '';
  }
}

import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BarcodeManagementComponent } from '../barcode-management/barcode-management.component';
import { PriceTagComponent } from '../price-tag/price-tag.component';

// Merges the two previously-separate "Barcode Management" and "Price Tag"
// pages into one, tab-switched screen — both did overlapping label/barcode
// printing, just with a different UI (grouped-by-variant vs flat + copies
// count). Kept as separate child components (not merged logic) to avoid
// risking either one's working print flow.
@Component({
  selector: 'app-product-labels',
  standalone: true,
  imports: [CommonModule, BarcodeManagementComponent, PriceTagComponent],
  templateUrl: './product-labels.component.html',
  styleUrl: './product-labels.component.css'
})
export class ProductLabelsComponent {
  activeTab: 'barcodes' | 'tags' = 'barcodes';
}

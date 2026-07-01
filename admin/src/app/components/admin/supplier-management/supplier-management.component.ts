import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SupplierService, Supplier } from '../../../services/supplier/supplier.service';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';
import { RequiredErrorComponent } from '../../../shared/required-error/required-error.component';

@Component({
  selector: 'app-supplier-management',
  standalone: true,
  imports: [CommonModule, FormsModule, RequiredErrorComponent],
  templateUrl: './supplier-management.component.html',
  styleUrl: './supplier-management.component.css'
})
export class SupplierManagementComponent implements OnInit {
  private supplierService = inject(SupplierService);
  private confirmSvc = inject(ConfirmDialogService);

  suppliers: Supplier[] = [];
  filteredSuppliers: Supplier[] = [];
  searchQuery = '';
  showModal = false;
  isEditMode = false;
  isLoading = false;
  successMsg = '';
  errorMsg = '';

  currentSupplier: Partial<Supplier> = this.getEmpty();

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.supplierService.getSuppliers().subscribe({
      next: (data) => { this.suppliers = data; this.filteredSuppliers = data; this.isLoading = false; },
      error: () => { this.isLoading = false; }
    });
  }

  filter() {
    const q = this.searchQuery.toLowerCase();
    this.filteredSuppliers = this.suppliers.filter(s =>
      s.name?.toLowerCase().includes(q) ||
      s.phoneNumber?.toLowerCase().includes(q) ||
      s.address?.toLowerCase().includes(q)
    );
  }

  openAdd() { this.currentSupplier = this.getEmpty(); this.isEditMode = false; this.showModal = true; this.errorMsg = ''; }

  openEdit(s: Supplier) { this.currentSupplier = { ...s }; this.isEditMode = true; this.showModal = true; this.errorMsg = ''; }

  closeModal() { this.showModal = false; }

  save() {
    this.errorMsg = '';
    if (!this.currentSupplier.name?.trim()) { this.errorMsg = 'Supplier name is required.'; return; }
    const obs = this.isEditMode
      ? this.supplierService.updateSupplier(this.currentSupplier.id!, this.currentSupplier)
      : this.supplierService.addSupplier(this.currentSupplier);
    obs.subscribe({
      next: () => { this.successMsg = this.isEditMode ? 'Supplier updated!' : 'Supplier added!'; this.closeModal(); this.load(); setTimeout(() => this.successMsg = '', 3000); },
      error: (e) => { this.errorMsg = e?.error?.message || 'Failed to save supplier.'; }
    });
  }

  async delete(id: number) {
    const ok = await this.confirmSvc.confirm({ title: 'Delete Supplier', message: 'Remove this supplier? This cannot be undone.', confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.supplierService.deleteSupplier(id).subscribe({
      next: () => { this.successMsg = 'Supplier deleted!'; this.load(); setTimeout(() => this.successMsg = '', 3000); },
      error: () => { this.errorMsg = 'Failed to delete supplier.'; }
    });
  }

  getEmpty(): Partial<Supplier> { return { name: '', phoneNumber: '', address: '' }; }
}

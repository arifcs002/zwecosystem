import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CategoryService, Category } from '../../../services/category/category.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';
import { buildIndentedList, FlatCategory, categoryPath } from '../../../utils/category-tree.util';

@Component({
  selector: 'app-category-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './category-management.component.html',
  styleUrl: './category-management.component.css'
})
export class CategoryManagementComponent implements OnInit {
  private categoryService = inject(CategoryService);
  private notify = inject(GlobalNotificationService);

  categories: Category[] = [];
  filteredCategories: Category[] = [];
  isLoading = false;
  searchQuery = '';

  showModal = false;
  isEditMode = false;
  saving = false;
  submitted = false;
  errorMsg = '';

  currentCategory: Category = this.getEmpty();

  categoryToDelete: Category | null = null;
  showDeleteModal = false;
  deleting = false;

  ngOnInit() { this.load(); }

  load() {
    this.isLoading = true;
    this.categoryService.getCategories().subscribe({
      next: (data) => {
        this.categories = data.sort((a, b) => a.name.localeCompare(b.name));
        this.applyFilter();
        this.isLoading = false;
      },
      error: () => {
        this.isLoading = false;
        this.notify.notify({ type: 'error', title: 'Load failed', message: 'Could not load categories.', ttlMs: 5000 });
      }
    });
  }

  // Tree-ordered, indented list for display. When searching, fall back to a flat
  // filtered list (depth 0) so matches aren't hidden by collapsed ancestors.
  treeRows: FlatCategory[] = [];

  applyFilter() {
    const q = this.searchQuery.toLowerCase().trim();
    if (q) {
      this.filteredCategories = this.categories.filter(c =>
        c.name.toLowerCase().includes(q) || (c.description ?? '').toLowerCase().includes(q));
      this.treeRows = this.filteredCategories.map(cat => ({ cat, depth: 0 }));
    } else {
      this.filteredCategories = [...this.categories];
      this.treeRows = buildIndentedList(this.categories);
    }
  }

  // Parent options: every category except the one being edited and its descendants
  // (prevents cycles), shown indented so any nesting depth is pickable.
  get parentOptions(): FlatCategory[] {
    return buildIndentedList(this.categories, this.isEditMode ? this.currentCategory.id : null);
  }

  fullPath(id: number | null | undefined): string { return categoryPath(this.categories, id); }

  openNew() {
    this.currentCategory = this.getEmpty();
    this.submitted = false;
    this.errorMsg = '';
    this.isEditMode = false;
    this.showModal = true;
  }

  openEdit(cat: Category) {
    this.currentCategory = { ...cat };
    this.submitted = false;
    this.errorMsg = '';
    this.isEditMode = true;
    this.showModal = true;
  }

  closeModal() { this.showModal = false; }

  save() {
    this.submitted = true;
    this.errorMsg = '';
    if (!this.currentCategory.name?.trim()) {
      this.errorMsg = 'Category name is required.';
      return;
    }
    this.saving = true;
    const action$ = this.isEditMode
      ? this.categoryService.updateCategory(this.currentCategory.id!, this.currentCategory)
      : this.categoryService.createCategory(this.currentCategory);

    action$.subscribe({
      next: () => {
        this.notify.notify({ type: 'success', title: this.isEditMode ? 'Updated' : 'Created', message: `Category "${this.currentCategory.name}" saved.`, ttlMs: 3000 });
        this.saving = false;
        this.showModal = false;
        this.load();
      },
      error: (err) => {
        this.saving = false;
        this.errorMsg = err.error?.message || 'Failed to save category.';
      }
    });
  }

  confirmDelete(cat: Category) {
    this.categoryToDelete = cat;
    this.showDeleteModal = true;
  }

  cancelDelete() { this.showDeleteModal = false; this.categoryToDelete = null; }

  doDelete() {
    if (!this.categoryToDelete?.id) return;
    this.deleting = true;
    this.categoryService.deleteCategory(this.categoryToDelete.id).subscribe({
      next: () => {
        this.notify.notify({ type: 'success', title: 'Deleted', message: `Category "${this.categoryToDelete!.name}" deleted.`, ttlMs: 3000 });
        this.categories = this.categories.filter(c => c.id !== this.categoryToDelete!.id);
        this.applyFilter();
        this.deleting = false;
        this.showDeleteModal = false;
        this.categoryToDelete = null;
      },
      error: () => {
        this.deleting = false;
        this.notify.notify({ type: 'error', title: 'Delete failed', message: 'Could not delete category.', ttlMs: 5000 });
      }
    });
  }

  getEmpty(): Category {
    return { name: '', description: '', sizes: '', parentId: null };
  }

  // Root categories only (for parent dropdown — a category can't be its own parent)
  get rootCategories(): Category[] {
    return this.categories.filter(c => !c.parentId && c.id !== this.currentCategory.id);
  }

  parentName(cat: Category): string {
    const p = this.categories.find(c => c.id === cat.parentId);
    return p ? p.name : '';
  }

  parseSizes(sizes: string | undefined): string[] {
    if (!sizes) return [];
    return sizes.split(',').map(s => s.trim()).filter(Boolean);
  }
}

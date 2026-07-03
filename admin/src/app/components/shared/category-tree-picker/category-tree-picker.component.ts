import { Component, ElementRef, HostListener, Input, OnChanges, Output, EventEmitter, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Category } from '../../../services/category/category.service';
import { buildTree, TreeNode, categoryPath } from '../../../utils/category-tree.util';

// Dropdown that renders categories as a real expand/collapse tree (parent →
// child → sub-child) instead of a flat indented <select>. One reusable
// picker for every "choose a category" spot (add-product, category admin's
// parent field, bulk restock, etc).
@Component({
  selector: 'app-category-tree-picker',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="ctp-wrap">
      <button type="button" class="ctp-trigger" (click)="toggle()" [class.open]="open">
        <span class="ctp-value" [class.placeholder]="!value">{{ value ? path : (placeholder || 'Select category') }}</span>
        <span class="ctp-chevron">{{ open ? '▲' : '▼' }}</span>
      </button>

      <div class="ctp-panel" *ngIf="open">
        <button *ngIf="allowNone" type="button" class="ctp-none" (click)="select(null)">
          — None (Root Category) —
        </button>
        <div class="ctp-tree">
          <ng-container *ngTemplateOutlet="nodeList; context: { nodes: tree, depth: 0 }"></ng-container>
        </div>
      </div>
    </div>

    <ng-template #nodeList let-nodes="nodes" let-depth="depth">
      <div class="ctp-node" *ngFor="let n of nodes">
        <div class="ctp-row" [style.padding-left.px]="depth * 18" [class.selected]="value === n.cat.id">
          <button type="button" class="ctp-expand" *ngIf="n.children.length" (click)="toggleExpand(n, $event)">
            {{ isExpanded(n) ? '▾' : '▸' }}
          </button>
          <span class="ctp-spacer" *ngIf="!n.children.length"></span>
          <button type="button" class="ctp-label" (click)="select(n.cat.id ?? null)">{{ n.cat.name }}</button>
        </div>
        <ng-container *ngIf="n.children.length && isExpanded(n)">
          <ng-container *ngTemplateOutlet="nodeList; context: { nodes: n.children, depth: depth + 1 }"></ng-container>
        </ng-container>
      </div>
    </ng-template>
  `,
  styles: [`
    .ctp-wrap { position: relative; }
    .ctp-trigger { width: 100%; display: flex; align-items: center; justify-content: space-between; gap: 8px;
      background: #1c1b3a; border: 1px solid #2f2d55; color: #e5e7eb; border-radius: 8px; padding: 9px 12px;
      font-size: 13px; cursor: pointer; text-align: left; }
    .ctp-trigger.open, .ctp-trigger:hover { border-color: #7c3aed; }
    .ctp-value { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ctp-value.placeholder { color: #6b7280; }
    .ctp-chevron { font-size: 9px; color: #8b8aa7; flex-shrink: 0; }

    .ctp-panel { position: absolute; z-index: 40; top: calc(100% + 4px); left: 0; right: 0; max-height: 320px;
      overflow-y: auto; background: #161532; border: 1px solid #2f2d55; border-radius: 10px; padding: 6px;
      box-shadow: 0 12px 32px rgba(0,0,0,.4); }
    .ctp-none { width: 100%; text-align: left; background: none; border: 0; color: #9ca3af; padding: 8px 10px;
      border-radius: 6px; cursor: pointer; font-size: 12px; margin-bottom: 4px; }
    .ctp-none:hover { background: #22203f; }

    .ctp-row { display: flex; align-items: center; gap: 4px; border-radius: 6px; }
    .ctp-row:hover { background: #1e1c40; }
    .ctp-row.selected { background: #2e1f5e; }
    .ctp-expand { background: none; border: 0; color: #8b8aa7; width: 18px; flex-shrink: 0; cursor: pointer; font-size: 10px; padding: 8px 0; }
    .ctp-spacer { width: 18px; flex-shrink: 0; }
    .ctp-label { flex: 1; text-align: left; background: none; border: 0; color: #e5e7eb; padding: 8px 4px; cursor: pointer; font-size: 13px; }
    .ctp-row.selected .ctp-label { color: #c4b5fd; font-weight: 600; }
  `]
})
export class CategoryTreePickerComponent implements OnChanges {
  private el = inject(ElementRef);

  @Input() categories: Category[] = [];
  @Input() value: number | null | undefined = null;
  @Input() excludeSubtreeOf: number | null = null;
  @Input() allowNone = true;
  @Input() placeholder = '';
  @Output() valueChange = new EventEmitter<number | null>();

  open = false;
  tree: TreeNode[] = [];
  private expanded = new Set<number>();

  ngOnChanges() {
    this.tree = buildTree(this.categories, this.excludeSubtreeOf);
    // Auto-expand the ancestor chain of the current value so it's visible.
    if (this.value != null) {
      const byId = new Map(this.categories.map(c => [c.id, c]));
      let cur = byId.get(this.value);
      while (cur?.parentId != null) { this.expanded.add(cur.parentId); cur = byId.get(cur.parentId); }
    }
  }

  get path(): string { return categoryPath(this.categories, this.value ?? null); }

  toggle() { this.open = !this.open; }
  isExpanded(n: TreeNode): boolean { return n.cat.id != null && this.expanded.has(n.cat.id); }
  toggleExpand(n: TreeNode, ev: Event) {
    ev.stopPropagation();
    if (n.cat.id == null) return;
    if (this.expanded.has(n.cat.id)) this.expanded.delete(n.cat.id); else this.expanded.add(n.cat.id);
  }

  select(id: number | null) {
    this.value = id;
    this.valueChange.emit(id);
    this.open = false;
  }

  @HostListener('document:click', ['$event'])
  onDocClick(ev: MouseEvent) {
    if (this.open && !this.el.nativeElement.contains(ev.target)) this.open = false;
  }
}

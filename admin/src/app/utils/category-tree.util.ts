import { Category } from '../services/category/category.service';

export interface FlatCategory { cat: Category; depth: number; }

// Depth-first ordered list with indentation depth, so an arbitrarily nested
// category tree renders (and picks) as an indented flat list. Roots = no parent
// (or a parent that isn't in the set). Children sorted by name at each level.
export function buildIndentedList(categories: Category[], excludeSubtreeOf?: number | null): FlatCategory[] {
  const excluded = excludeSubtreeOf != null ? descendantIds(categories, excludeSubtreeOf) : new Set<number>();
  const byParent = new Map<number | null, Category[]>();
  const ids = new Set(categories.map(c => c.id));
  for (const c of categories) {
    const pid = c.parentId != null && ids.has(c.parentId) ? c.parentId : null;
    if (!byParent.has(pid)) byParent.set(pid, []);
    byParent.get(pid)!.push(c);
  }
  byParent.forEach(list => list.sort((a, b) => a.name.localeCompare(b.name)));

  const out: FlatCategory[] = [];
  const walk = (pid: number | null, depth: number) => {
    for (const c of byParent.get(pid) || []) {
      if (c.id != null && excluded.has(c.id)) continue; // skip self + descendants
      out.push({ cat: c, depth });
      walk(c.id ?? null, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

// A category id plus every id nested beneath it (used to prevent a category from
// being reparented under one of its own descendants, which would form a cycle).
export function descendantIds(categories: Category[], id: number): Set<number> {
  const out = new Set<number>([id]);
  const children = (parent: number) => categories.filter(c => c.parentId === parent);
  const stack = [id];
  while (stack.length) {
    const cur = stack.pop()!;
    for (const child of children(cur)) {
      if (child.id != null && !out.has(child.id)) { out.add(child.id); stack.push(child.id); }
    }
  }
  return out;
}

// Breadcrumb path "Men › Shoes › Sneakers" for a category id.
export function categoryPath(categories: Category[], id: number | null | undefined): string {
  if (id == null) return '';
  const byId = new Map(categories.map(c => [c.id, c]));
  const parts: string[] = [];
  let cur = byId.get(id);
  let guard = 0;
  while (cur && guard++ < 50) {
    parts.unshift(cur.name);
    cur = cur.parentId != null ? byId.get(cur.parentId) : undefined;
  }
  return parts.join(' › ');
}

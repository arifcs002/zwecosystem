import { Product } from '../services/product/product.service';
import { ShopProduct } from '../services/cart/cart.service';
import { resolveImageUrl } from './image-url.util';

export interface ProductGroup {
  baseName: string;
  imageUrl: string;
  categoryId: number | null;
  categoryName: string;
  price: number;
  compareAtPrice: number | null; // "was" price for discount display (from representative variant)
  totalStock: number;
  createdDate: string | null;    // latest variant's created date (for "newest" sort / New badge)
  variants: Product[];           // one per size, sorted
}

// Whole-taka discount percent, or 0 when there's no valid "was" price.
export function discountPercent(g: ProductGroup): number {
  if (!g.compareAtPrice || g.compareAtPrice <= g.price) return 0;
  return Math.round(((g.compareAtPrice - g.price) / g.compareAtPrice) * 100);
}

// Size variants are stored as separate Product rows named "{base} (Size X)".
// This strips that suffix to recover the shared base name — the one place this
// regex is defined; every listing surface (admin table, public shop, pickers)
// should group through this function instead of re-implementing it.
export function baseProductName(name: string): string {
  return name.replace(/\s*\(Size [^)]+\)\s*$/i, '').trim();
}

export function groupProducts(products: Product[]): ProductGroup[] {
  const map = new Map<string, Product[]>();
  products.forEach(p => {
    const key = `${baseProductName(p.name)}::${p.categoryId ?? ''}`;
    if (!map.has(key)) map.set(key, []);
    map.get(key)!.push(p);
  });

  return Array.from(map.values()).map(variants => {
    const sorted = [...variants].sort((a, b) =>
      (a.size || '').localeCompare(b.size || '', undefined, { numeric: true }));
    const dates = sorted.map(v => v.createdDate || v.createdAt).filter(Boolean) as string[];
    return {
      baseName: baseProductName(sorted[0].name),
      imageUrl: sorted.find(v => v.imageUrl)?.imageUrl || '',
      categoryId: sorted[0].categoryId ?? null,
      categoryName: sorted[0].category?.name || 'Uncategorized',
      price: sorted[0].price,
      compareAtPrice: sorted[0].compareAtPrice ?? null,
      totalStock: sorted.reduce((s, v) => s + v.stockQuantity, 0),
      createdDate: dates.length ? dates.sort().slice(-1)[0] : null,
      variants: sorted
    };
  });
}

// Re-derive a product's full group from a flat product list, given any one of
// its variant ids — used wherever only a representative id is stored (e.g. a
// curated "category_order_{id}" pick) but the full grouped card is needed.
export function groupForProductId(products: Product[], productId: number): ProductGroup | null {
  const anchor = products.find(p => p.id === productId);
  if (!anchor) return null;
  const key = `${baseProductName(anchor.name)}::${anchor.categoryId ?? ''}`;
  const variants = products.filter(p => `${baseProductName(p.name)}::${p.categoryId ?? ''}` === key);
  return groupProducts(variants)[0] ?? null;
}

// Convert one specific size variant into the shape the cart/checkout need —
// used at "Add to Cart" time, once a size has been resolved (either the
// product's only variant, or the one the customer picked on the detail page).
export function toShopProduct(variant: Product): ShopProduct {
  return {
    id: variant.id,
    name: baseProductName(variant.name),
    price: variant.price,
    image: resolveImageUrl(variant.imageUrl),
    category: variant.category?.name || 'Uncategorized',
    categoryId: Number(variant.categoryId) || 0,
    stock: variant.stockQuantity,
    size: variant.size || undefined
  };
}

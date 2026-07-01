import { Product } from '../services/product/product.service';
import { Category } from '../services/category/category.service';
import { ProductGroup, groupProducts, groupForProductId, discountPercent } from './product-group.util';
import { StorefrontBlock } from '../models/storefront-layout.model';

// Does productCatId belong to catId, directly or via any sub-category?
function belongsToCategory(productCatId: number, catId: number, cats: Category[]): boolean {
  if (productCatId === catId) return true;
  return cats
    .filter(c => Number(c.parentId) === catId)
    .some(sub => belongsToCategory(productCatId, Number(sub.id), cats));
}

// Resolve the product groups a product_carousel / product_grid block should show,
// based on its `source`. All grouping/discount logic lives here so the block
// components stay presentational.
export function resolveBlockProducts(
  block: StorefrontBlock,
  allProducts: Product[],
  categories: Category[]
): ProductGroup[] {
  const limit = block.limit && block.limit > 0 ? block.limit : 10;

  switch (block.source) {
    case 'curated': {
      // Expand each hand-picked representative id into its full group, de-duping.
      const seen = new Set<string>();
      const out: ProductGroup[] = [];
      for (const id of block.productIds || []) {
        const g = groupForProductId(allProducts, id);
        if (!g) continue;
        const key = `${g.baseName}::${g.categoryId}`;
        if (seen.has(key)) continue;
        seen.add(key);
        out.push(g);
      }
      return out.slice(0, limit);
    }

    case 'category': {
      if (block.categoryId == null) return [];
      const inCat = allProducts.filter(p =>
        p.categoryId != null && belongsToCategory(Number(p.categoryId), Number(block.categoryId), categories));
      return groupProducts(inCat)
        .sort(byNewest)
        .slice(0, limit);
    }

    case 'discounted': {
      return groupProducts(allProducts)
        .filter(g => discountPercent(g) > 0)
        .sort((a, b) => discountPercent(b) - discountPercent(a))
        .slice(0, limit);
    }

    case 'newest':
    default: {
      return groupProducts(allProducts)
        .sort(byNewest)
        .slice(0, limit);
    }
  }
}

function byNewest(a: ProductGroup, b: ProductGroup): number {
  return (b.createdDate || '').localeCompare(a.createdDate || '');
}

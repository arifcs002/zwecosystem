// ─────────────────────────────────────────────────────────────────────────
// Storefront layout schema.
//
// A company's homepage is an ordered list of "blocks". The whole thing is
// persisted as ONE company setting (`storefront_layout`, a JSON string) via the
// existing sp_upsert_setting — no schema change. The admin Storefront Builder
// edits this array; the public shop renders it block-by-block.
//
// Every block carries a stable `id` (for drag/track), a `type`, and an
// `enabled` flag. The remaining fields are type-specific and optional.
// ─────────────────────────────────────────────────────────────────────────

export type BlockType =
  | 'hero_slider'
  | 'category_icons'
  | 'product_carousel'
  | 'product_grid'
  | 'promo_banner'
  | 'brand_carousel'
  | 'testimonials';

// Where a product-based block pulls its products from.
export type ProductSource = 'category' | 'curated' | 'newest' | 'discounted';

export interface HeroSlide {
  image: string;
  title: string;
  subtitle?: string;
  ctaText?: string;
  ctaLink?: string;
}

export interface Testimonial {
  name: string;
  text: string;
  rating?: number; // 1..5
}

export interface StorefrontBlock {
  id: string;
  type: BlockType;
  enabled: boolean;

  // Section heading (product_carousel / product_grid / category_icons / brand_carousel / testimonials)
  title?: string;

  // hero_slider
  slides?: HeroSlide[];

  // category_icons
  categoryIds?: number[];
  maxVisible?: number;

  // product_carousel / product_grid
  source?: ProductSource;
  categoryId?: number | null;   // when source === 'category'
  productIds?: number[];        // when source === 'curated' (one representative variant id per group)
  limit?: number;
  columns?: number;             // product_grid only
  viewAllLink?: string;
  highlight?: boolean;          // colored/emphasised section background

  // promo_banner
  image?: string;
  link?: string;

  // brand_carousel
  brandIds?: number[];

  // testimonials
  items?: Testimonial[];
}

export interface StorefrontLayout {
  version: number;
  blocks: StorefrontBlock[];
}

export const STOREFRONT_LAYOUT_KEY = 'storefront_layout';
export const CURRENT_LAYOUT_VERSION = 1;

// Short random id — enough to disambiguate blocks within one layout for
// drag/track; not a security token.
export function newBlockId(): string {
  return 'blk_' + Math.random().toString(36).slice(2, 10);
}

// Human labels + emoji for each block type (used by the Builder palette).
export const BLOCK_META: Record<BlockType, { label: string; icon: string; hint: string }> = {
  hero_slider:      { label: 'Hero Slider',      icon: '🖼️', hint: 'Full-width banner slides with a call-to-action' },
  category_icons:   { label: 'Category Icons',   icon: '🧭', hint: 'Row of featured category shortcuts' },
  product_carousel: { label: 'Product Carousel', icon: '🛍️', hint: 'Horizontal-scrolling row of products' },
  product_grid:     { label: 'Product Grid',     icon: '▦',  hint: 'Multi-column grid of products' },
  promo_banner:     { label: 'Promo Banner',     icon: '📢', hint: 'Single full-width clickable image' },
  brand_carousel:   { label: 'Brand Carousel',   icon: '🏷️', hint: 'Row of brand logos' },
  testimonials:     { label: 'Testimonials',     icon: '💬', hint: 'Customer review cards' }
};

// A fresh, sensible default so a brand-new store isn't blank before the admin
// touches the Builder. Product carousels default to "newest" so they populate
// with whatever the store already has.
export function defaultLayout(): StorefrontLayout {
  return {
    version: CURRENT_LAYOUT_VERSION,
    blocks: [
      {
        id: newBlockId(), type: 'hero_slider', enabled: true,
        slides: [{
          image: '',
          title: 'Discover the latest trends',
          subtitle: 'Shop our curated collection — quality guaranteed, delivered to your door.',
          ctaText: 'Browse Categories', ctaLink: ''
        }]
      },
      { id: newBlockId(), type: 'category_icons', enabled: true, title: 'Shop by Category', categoryIds: [], maxVisible: 8 },
      { id: newBlockId(), type: 'product_carousel', enabled: true, title: 'New Arrivals', source: 'newest', limit: 10, viewAllLink: '' },
      { id: newBlockId(), type: 'product_carousel', enabled: true, title: 'On Sale', source: 'discounted', limit: 10, highlight: true, viewAllLink: '' }
    ]
  };
}

// Parse a stored layout string; fall back to the default on empty/corrupt data
// so the storefront and builder never crash on bad JSON.
export function parseLayout(raw: string | undefined | null): StorefrontLayout {
  if (!raw) return defaultLayout();
  try {
    const parsed = JSON.parse(raw) as StorefrontLayout;
    if (!parsed || !Array.isArray(parsed.blocks)) return defaultLayout();
    // Ensure every block has an id (older saves / hand-edits may omit it).
    parsed.blocks.forEach(b => { if (!b.id) b.id = newBlockId(); });
    return parsed;
  } catch {
    return defaultLayout();
  }
}

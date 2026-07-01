import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { forkJoin } from 'rxjs';
import { CompanyService } from '../../services/company/company.service';
import { CategoryService, Category } from '../../services/category/category.service';
import { ProductService } from '../../services/product/product.service';
import { SettingsService } from '../../services/settings/settings.service';
import { CartService } from '../../services/cart/cart.service';
import { ImgUrlPipe } from '../../pipes/img-url.pipe';
import { CartWidgetComponent } from './cart-widget/cart-widget.component';
import { ProductGroup, groupProducts, groupForProductId, toShopProduct } from '../../utils/product-group.util';
import { StorefrontBlock, parseLayout } from '../../models/storefront-layout.model';
import { resolveBlockProducts } from '../../utils/storefront-source.util';
import { HeroSliderComponent } from './blocks/hero-slider/hero-slider.component';
import { CategoryIconsComponent, IconCategory } from './blocks/category-icons/category-icons.component';
import { ProductCarouselComponent } from './blocks/product-carousel/product-carousel.component';
import { BrandCarouselComponent } from './blocks/brand-carousel/brand-carousel.component';
import { TestimonialsComponent } from './blocks/testimonials/testimonials.component';
import { BrandService, Brand } from '../../services/brand/brand.service';

export interface CategorySection {
  id: number;
  name: string;
  groups: ProductGroup[];
}

// A layout block plus the data resolved for it, ready to render.
export interface RenderBlock {
  block: StorefrontBlock;
  groups?: ProductGroup[];
  iconCategories?: IconCategory[];
  brands?: Brand[];
}

export interface CategoryTree {
  id: number;
  name: string;
  expanded: boolean;
  children: CategoryTree[];
}

@Component({
  selector: 'app-public-shop',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, ImgUrlPipe, CartWidgetComponent,
    HeroSliderComponent, CategoryIconsComponent, ProductCarouselComponent,
    BrandCarouselComponent, TestimonialsComponent
  ],
  templateUrl: './public-shop.component.html',
  styleUrl: './public-shop.component.css'
})
export class PublicShopComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private companyService = inject(CompanyService);
  private categoryService = inject(CategoryService);
  private productService = inject(ProductService);
  private settingsService = inject(SettingsService);
  private brandService = inject(BrandService);
  cartService = inject(CartService);

  companySlug = '';
  companyName = 'Loading...';
  companyLogo = '';
  isValidStore: boolean | null = null;
  isLoading = false;

  // Category tree for left sidebar
  categoryTree: CategoryTree[] = [];
  selectedCategoryId: number | null = null;   // null = home (all sections)

  // Category sections for homepage — one per category/sub-category the admin
  // enabled in Store Config, each showing its curated order or, lacking one,
  // its latest products. Each entry is a grouped product (all sizes folded
  // into one card) — see utils/product-group.util.ts.
  categorySections: CategorySection[] = [];

  // Category detail page (when See More clicked)
  detailCategory: CategorySection | null = null;

  // Homepage block layout (Storefront Builder) resolved to renderable data.
  renderBlocks: RenderBlock[] = [];
  lowStockThreshold = 5;
  // Kept raw for block source resolution + category detail.
  private allProductsRaw: any[] = [];
  private allCategoriesRaw: Category[] = [];
  private allBrandsRaw: Brand[] = [];

  // Sidebar toggle
  sidebarOpen = false;

  currentYear = new Date().getFullYear();

  // Footer / social links (Settings page)
  footerAboutText = '';
  socialLinks: { key: string; url: string; label: string }[] = [];

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.companySlug = params.get('companySlug') || '';
      const catId = params.get('categoryId');
      this.selectedCategoryId = catId ? Number(catId) : null;

      this.companyService.getPublicCompany(this.companySlug).subscribe({
        next: (comp) => {
          this.companyName = comp.name;
          this.companyLogo = comp.logoUrl || '';
          this.isValidStore = true;
          localStorage.setItem('tenant_company_id', comp.id.toString());
          this.loadStorefrontData();
        },
        error: () => {
          this.companyName = 'Store Not Found';
          this.isValidStore = false;
        }
      });
    });
  }

  loadStorefrontData() {
    this.isLoading = true;
    // Settings, categories, and products are independent of each other — fetch them
    // in parallel instead of waiting on each one in sequence.
    forkJoin({
      settings: this.settingsService.getPublicSettings(),
      allCats: this.categoryService.getCategories(),
      allProds: this.productService.getPublicProducts(),
      allBrands: this.brandService.getPublicBrands()
    }).subscribe({
      next: ({ settings, allCats, allProds, allBrands }) => {
        this.allBrandsRaw = allBrands || [];
        const catSetting = settings.find(s => s.key === 'visible_dashboard_categories');
        const visibleCategoryIds = catSetting?.value ? catSetting.value.split(',').filter(Boolean) : [];

        this.footerAboutText = settings.find(s => s.key === 'footer_about_text')?.value || '';
        const socialKeys: { key: string; label: string }[] = [
          { key: 'facebook_link', label: 'Facebook' },
          { key: 'instagram_link', label: 'Instagram' },
          { key: 'twitter_link', label: 'X' },
          { key: 'youtube_link', label: 'YouTube' },
          { key: 'whatsapp_link', label: 'WhatsApp' },
          { key: 'tiktok_link', label: 'TikTok' },
          { key: 'linkedin_link', label: 'LinkedIn' }
        ];
        this.socialLinks = socialKeys
          .map(sk => ({ ...sk, url: settings.find(s => s.key === sk.key)?.value || '' }))
          .filter(sk => !!sk.url);

        const displayCats = visibleCategoryIds.length > 0
          ? allCats.filter(c => visibleCategoryIds.includes((c.id || '').toString()))
          : allCats;

        // Keep raw data for storefront-block resolution + category detail.
        this.allProductsRaw = allProds;
        this.allCategoriesRaw = allCats;
        const lowStock = settings.find(s => s.key === 'low_stock_threshold')?.value;
        if (lowStock && !isNaN(+lowStock)) this.lowStockThreshold = +lowStock;

        // Build the homepage from the Storefront Builder layout.
        const layout = parseLayout(settings.find(s => s.key === 'storefront_layout')?.value);
        this.renderBlocks = layout.blocks
          .filter(b => b.enabled)
          .map(b => this.toRenderBlock(b))
          .filter(rb => this.hasContent(rb));

        // Build category tree (parent/child)
        this.buildCategoryTree(displayCats);

        // Per-category curated + ordered picks from Store Config
        // ("category_order_{id}", one representative product id per group). A
        // category with no curated order falls back to its latest products.
        const orderPrefix = 'category_order_';
        const categoryOrders = new Map<number, number[]>();
        settings.forEach(s => {
          if (s.key.startsWith(orderPrefix) && s.value) {
            categoryOrders.set(Number(s.key.slice(orderPrefix.length)), s.value.split(',').filter(Boolean).map(Number));
          }
        });

        // One homepage section per category the admin enabled in Store Config
        // (can be a top-level category or a specific sub-category). If nothing
        // has been configured yet, default to showing every top-level category.
        const homeCats = visibleCategoryIds.length > 0 ? displayCats : displayCats.filter(c => !c.parentId);
        this.categorySections = homeCats
          .map(cat => {
            const catId = Number(cat.id);
            const order = categoryOrders.get(catId);
            const groups = order && order.length > 0
              ? this.resolveCuratedGroups(allProds, order)
              : groupProducts(allProds.filter(p => this.productBelongsToCategory(Number(p.categoryId) || 0, catId, displayCats))).slice(0, 10);
            return { id: catId, name: cat.name, groups };
          })
          .filter(s => s.groups.length > 0);

        // If on category detail route, set detail (full listing — no curation/slicing)
        if (this.selectedCategoryId) {
          const cat = displayCats.find(c => Number(c.id) === this.selectedCategoryId);
          if (cat) {
            const catId = Number(cat.id);
            this.detailCategory = {
              id: catId,
              name: cat.name,
              groups: groupProducts(allProds.filter(p => this.productBelongsToCategory(Number(p.categoryId) || 0, catId, displayCats)))
            };
          }
        }

        this.isLoading = false;
      },
      error: () => { this.isLoading = false; }
    });
  }

  // ── Storefront block rendering ─────────────────────────────────────────
  private toRenderBlock(block: StorefrontBlock): RenderBlock {
    if (block.type === 'product_carousel' || block.type === 'product_grid') {
      return { block, groups: resolveBlockProducts(block, this.allProductsRaw, this.allCategoriesRaw) };
    }
    if (block.type === 'category_icons') {
      const ids = block.categoryIds || [];
      const ordered = ids.length
        ? ids.map(id => this.allCategoriesRaw.find(c => Number(c.id) === Number(id))).filter(Boolean) as Category[]
        : this.allCategoriesRaw.filter(c => !c.parentId);
      const list = (block.maxVisible && block.maxVisible > 0 ? ordered.slice(0, block.maxVisible) : ordered);
      return { block, iconCategories: list.map(c => ({ id: Number(c.id), name: c.name })) };
    }
    if (block.type === 'brand_carousel') {
      return { block, brands: this.allBrandsRaw };
    }
    return { block };
  }

  // Hide blocks that resolved to nothing (e.g. an empty carousel) so the page
  // never shows a stray heading with no content. Non-product blocks always show.
  private hasContent(rb: RenderBlock): boolean {
    switch (rb.block.type) {
      case 'product_carousel':
      case 'product_grid':   return !!rb.groups && rb.groups.length > 0;
      case 'category_icons': return !!rb.iconCategories && rb.iconCategories.length > 0;
      case 'hero_slider':    return !!rb.block.slides && rb.block.slides.length > 0;
      case 'promo_banner':   return !!rb.block.image;
      case 'brand_carousel': return !!rb.brands && rb.brands.length > 0;
      case 'testimonials':   return !!rb.block.items && rb.block.items.length > 0;
      default:               return true;
    }
  }

  blockViewAll(block: StorefrontBlock) {
    if (block.viewAllLink) {
      const parts = block.viewAllLink.replace(/^\/+/, '').split('/').filter(Boolean);
      this.router.navigate(['/', this.companySlug, ...parts]);
    } else if (block.source === 'category' && block.categoryId != null) {
      this.navigateToCategory(Number(block.categoryId));
    }
  }

  bannerLink(block: StorefrontBlock) {
    if (!block.link) return;
    const parts = block.link.replace(/^\/+/, '').split('/').filter(Boolean);
    this.router.navigate(['/', this.companySlug, ...parts]);
  }

  // Curated order stores one representative variant id per group — expand each
  // into its full group, de-duping in case two curated ids land in the same group.
  private resolveCuratedGroups(allProds: any[], order: number[]): ProductGroup[] {
    const seen = new Set<string>();
    const groups: ProductGroup[] = [];
    for (const id of order) {
      const g = groupForProductId(allProds, id);
      if (!g) continue;
      const key = `${g.baseName}::${g.categoryId}`;
      if (seen.has(key)) continue;
      seen.add(key);
      groups.push(g);
    }
    return groups;
  }

  buildCategoryTree(cats: Category[]) {
    const roots: CategoryTree[] = [];
    const map: { [id: number]: CategoryTree } = {};
    cats.forEach(c => { map[Number(c.id)] = { id: Number(c.id), name: c.name, expanded: false, children: [] }; });
    cats.forEach(c => {
      if (c.parentId && map[Number(c.parentId)]) {
        map[Number(c.parentId)].children.push(map[Number(c.id)]);
      } else {
        roots.push(map[Number(c.id)]);
      }
    });
    this.categoryTree = roots;
  }

  // Check if product belongs to category (including via sub-categories)
  productBelongsToCategory(productCatId: number, catId: number, allCats: Category[]): boolean {
    if (productCatId === catId) return true;
    const subIds = allCats.filter(c => Number(c.parentId) === catId).map(c => Number(c.id));
    return subIds.some(sid => this.productBelongsToCategory(productCatId, sid, allCats));
  }

  toggleCategoryExpand(node: CategoryTree) {
    node.expanded = !node.expanded;
  }

  navigateToCategory(catId: number) {
    this.selectedCategoryId = catId;
    this.router.navigate(['/', this.companySlug, 'category', catId]);
  }

  seeMore(catId: number) {
    this.navigateToCategory(catId);
  }

  goHome() {
    this.selectedCategoryId = null;
    this.detailCategory = null;
    this.router.navigate(['/', this.companySlug]);
  }

  goHomeAndClose() { this.sidebarOpen = false; this.goHome(); }
  navAndClose(catId: number) { this.sidebarOpen = false; this.navigateToCategory(catId); }

  openProduct(group: ProductGroup) {
    this.router.navigate(['/', this.companySlug, 'product', group.variants[0].id]);
  }

  stockLabel(stock: number): string {
    if (stock <= 0) return 'Out of Stock';
    if (stock <= 5) return `Only ${stock} left`;
    return '';
  }

  // Single-variant products (no sizes) can be added straight from the card.
  // Multi-variant products route to the detail page so a size must be chosen.
  quickAddToCart(group: ProductGroup) {
    if (group.variants.length !== 1) { this.openProduct(group); return; }
    this.cartService.addToCart(toShopProduct(group.variants[0]));
  }
}

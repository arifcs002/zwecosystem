-- ============================================================
-- ZW Ecosystem - Stored Procedures
-- Run once after DB creation. Safe to re-run (CREATE OR REPLACE).
-- ============================================================

-- Drop all existing SPs to allow signature/return-type changes
DROP FUNCTION IF EXISTS sp_get_companies();
-- sp_update_company is a PROCEDURE (see CREATE OR REPLACE PROCEDURE below, which
-- handles redefinition on its own) — DROP FUNCTION/PROCEDURE IF EXISTS here used
-- to abort this entire script with "is not a function/procedure" whenever the
-- live object's kind didn't match the DROP statement's kind, silently preventing
-- every statement after it (including new SPs) from ever being (re)created.
DROP PROCEDURE IF EXISTS sp_delete_company(INT);
DROP FUNCTION IF EXISTS sp_toggle_company_status(INT);
DROP FUNCTION IF EXISTS sp_get_users(INT,BOOLEAN);
DROP FUNCTION IF EXISTS sp_get_roles();
DROP FUNCTION IF EXISTS sp_get_suppliers(INT);
DROP FUNCTION IF EXISTS sp_get_categories(INT);
DROP FUNCTION IF EXISTS sp_get_orders(INT);
DROP FUNCTION IF EXISTS sp_get_order(INT);
DROP FUNCTION IF EXISTS sp_get_settings(INT);
DROP PROCEDURE IF EXISTS sp_upsert_setting(INT,TEXT,TEXT,TEXT);
DROP PROCEDURE IF EXISTS sp_update_company_profile(INT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,DECIMAL);
DROP FUNCTION IF EXISTS sp_get_dashboard_stats(INT);
DROP FUNCTION IF EXISTS sp_get_sales_chart(INT,INT);
DROP FUNCTION IF EXISTS sp_get_top_products(INT,INT);
DROP FUNCTION IF EXISTS sp_get_recent_orders(INT,INT);
DROP FUNCTION IF EXISTS sp_lookup_product(TEXT,INT);
DROP PROCEDURE IF EXISTS sp_generate_otp(TEXT,TEXT,TIMESTAMPTZ);
DROP FUNCTION IF EXISTS sp_register_company(TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,INT,INT);
DROP FUNCTION IF EXISTS sp_create_user(INT,TEXT,TEXT,TEXT,TEXT,TEXT,TEXT,BOOLEAN,INT,INT);
DROP PROCEDURE IF EXISTS sp_update_user(INT,TEXT,TEXT,TEXT,TEXT,BOOLEAN,TEXT,TEXT,INT,INT,INT);
DROP PROCEDURE IF EXISTS sp_delete_user(INT,INT);
DROP FUNCTION IF EXISTS sp_create_supplier(INT,TEXT,TEXT,TEXT,INT);
DROP PROCEDURE IF EXISTS sp_update_supplier(INT,TEXT,TEXT,TEXT,INT);
DROP PROCEDURE IF EXISTS sp_delete_supplier(INT,INT);
DROP FUNCTION IF EXISTS sp_create_category(INT,TEXT,TEXT,TEXT,TEXT,INT);
DROP FUNCTION IF EXISTS sp_create_category(INT,TEXT,TEXT,TEXT,TEXT,INT,INT);
DROP PROCEDURE IF EXISTS sp_update_category(INT,TEXT,TEXT,TEXT,TEXT,INT);
DROP PROCEDURE IF EXISTS sp_update_category(INT,TEXT,TEXT,TEXT,TEXT,INT,INT);
DROP PROCEDURE IF EXISTS sp_delete_category(INT,INT);
DROP FUNCTION IF EXISTS sp_create_product(INT,TEXT,TEXT,TEXT,TEXT,TEXT,DECIMAL,DECIMAL,INT,TEXT,INT,INT,TEXT,TEXT,INT,INT);
-- 17-param version (added pricing_tag_id) — dropped so the new compare_at_price
-- overload doesn't collide/ambiguate with the shorter one on positional calls.
DROP FUNCTION IF EXISTS sp_create_product(INT,TEXT,TEXT,TEXT,TEXT,TEXT,DECIMAL,DECIMAL,INT,TEXT,INT,INT,TEXT,TEXT,INT,INT,INT);
DROP PROCEDURE IF EXISTS sp_update_product(INT,TEXT,TEXT,DECIMAL,DECIMAL,INT,TEXT,TEXT,INT,INT,TEXT,INT,INT);
DROP PROCEDURE IF EXISTS sp_update_product(INT,TEXT,TEXT,DECIMAL,DECIMAL,INT,TEXT,TEXT,INT,INT,TEXT,INT,INT,INT);
DROP PROCEDURE IF EXISTS sp_delete_product(INT);
DROP PROCEDURE IF EXISTS sp_adjust_stock(INT,INT,INT);
DROP PROCEDURE IF EXISTS sp_update_order_status(INT,TEXT,TEXT);
DROP PROCEDURE IF EXISTS sp_cancel_order(INT);
DROP FUNCTION IF EXISTS sp_checkout_order(INT,TEXT,TEXT,INT,TEXT,TEXT,TEXT,DECIMAL,DECIMAL,DECIMAL,TEXT,TEXT,INT[],INT[],DECIMAL[],INT);
DROP FUNCTION IF EXISTS sp_verify_payment(INT,INT,TEXT,TEXT,DECIMAL,TEXT,TEXT,TEXT,TEXT);

-- ── Companies ────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_companies()
RETURNS TABLE(
    id INT, name TEXT, subdomain TEXT, "logoUrl" TEXT,
    "contactEmail" TEXT, "contactPhone" TEXT, "companyMobile" TEXT,
    "ownerName" TEXT, "ownerMobile" TEXT,
    division TEXT, district TEXT, thana TEXT, address TEXT,
    "facebookLink" TEXT, "instagramLink" TEXT,
    "bkashNumber" TEXT, "nagadNumber" TEXT,
    "bankName" TEXT, "bankAccountName" TEXT,
    "deliveryCharge" DECIMAL, "isActive" BOOLEAN,
    "approvalStatus" TEXT, "createdAt" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT
        c.id, c.name, c.subdomain, c.logo_url,
        c.contact_email, c.contact_phone, c.company_mobile,
        c.owner_name, c.owner_mobile,
        c.division, c.district, c.thana, c.address,
        c.facebook_link, c.instagram_link,
        c.bkash_number, c.nagad_number,
        c.bank_name, c.bank_account_name,
        c.delivery_charge, c.is_active,
        c.approval_status, c.created_date
    FROM companies c
    WHERE c.is_deleted = 0
    ORDER BY c.created_date DESC;
$$;

CREATE OR REPLACE PROCEDURE sp_update_company(
    p_id INT, p_name TEXT, p_subdomain TEXT,
    p_contact_email TEXT, p_contact_phone TEXT,
    p_owner_name TEXT, p_owner_mobile TEXT, p_company_mobile TEXT,
    p_division TEXT, p_district TEXT, p_thana TEXT, p_address TEXT,
    p_facebook_link TEXT, p_instagram_link TEXT,
    p_bkash_number TEXT, p_nagad_number TEXT,
    p_bank_name TEXT, p_bank_account_name TEXT,
    p_logo_url TEXT, p_banner_url TEXT,
    p_is_active BOOLEAN, p_approval_status TEXT
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE companies SET
        name             = p_name,
        subdomain        = lower(trim(p_subdomain)),
        contact_email    = NULLIF(trim(p_contact_email), ''),
        contact_phone    = NULLIF(trim(p_contact_phone), ''),
        owner_name       = NULLIF(trim(p_owner_name), ''),
        owner_mobile     = NULLIF(trim(p_owner_mobile), ''),
        company_mobile   = NULLIF(trim(p_company_mobile), ''),
        division         = NULLIF(trim(p_division), ''),
        district         = NULLIF(trim(p_district), ''),
        thana            = NULLIF(trim(p_thana), ''),
        address          = NULLIF(trim(p_address), ''),
        facebook_link    = NULLIF(trim(p_facebook_link), ''),
        instagram_link   = NULLIF(trim(p_instagram_link), ''),
        bkash_number     = NULLIF(trim(p_bkash_number), ''),
        nagad_number     = NULLIF(trim(p_nagad_number), ''),
        bank_name        = NULLIF(trim(p_bank_name), ''),
        bank_account_name = NULLIF(trim(p_bank_account_name), ''),
        logo_url         = NULLIF(trim(p_logo_url), ''),
        banner_url       = NULLIF(trim(p_banner_url), ''),
        is_active        = p_is_active,
        approval_status  = COALESCE(NULLIF(trim(p_approval_status), ''), 'Pending'),
        updated_date     = NOW()
    WHERE id = p_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_company(p_id INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE companies SET is_deleted = 1, deleted_date = NOW() WHERE id = p_id;
END;
$$;

CREATE OR REPLACE FUNCTION sp_toggle_company_status(p_id INT)
RETURNS TABLE("isActive" BOOLEAN, message TEXT) LANGUAGE plpgsql AS $$
DECLARE v_name TEXT; v_active BOOLEAN;
BEGIN
    UPDATE companies SET is_active = NOT is_active, updated_date = NOW()
    WHERE id = p_id
    RETURNING name, is_active INTO v_name, v_active;

    RETURN QUERY SELECT v_active,
        v_name || ' is now ' || CASE WHEN v_active THEN 'active' ELSE 'inactive' END;
END;
$$;

-- ── Users ────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_users(p_company_id INT, p_is_super_admin BOOLEAN)
RETURNS TABLE(
    "Id" INT, "Email" TEXT, "FirstName" TEXT, "LastName" TEXT,
    "PhoneNumber" TEXT, "IsActive" BOOLEAN, "CompanyId" INT, "Roles" TEXT
) LANGUAGE sql STABLE AS $$
    SELECT
        u.id, u.email, u.first_name, u.last_name,
        u.phone_number, u.is_active, u.company_id,
        COALESCE(string_agg(r.name, ',' ORDER BY r.name), '')
    FROM users u
    LEFT JOIN user_roles ur ON ur.user_id = u.id AND ur.is_deleted = 0
    LEFT JOIN roles r ON r.id = ur.role_id AND r.is_deleted = 0
    WHERE u.is_deleted = 0
      AND (p_is_super_admin OR u.company_id = p_company_id)
    GROUP BY u.id, u.email, u.first_name, u.last_name,
             u.phone_number, u.is_active, u.company_id
    ORDER BY u.created_date DESC;
$$;

CREATE OR REPLACE FUNCTION sp_get_roles()
RETURNS TABLE("Id" INT, "Name" TEXT, "Value" TEXT) LANGUAGE sql STABLE AS $$
    SELECT id, name, value FROM roles WHERE is_deleted = 0 ORDER BY id;
$$;

-- ── Suppliers ────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_suppliers(p_company_id INT)
RETURNS TABLE(
    id INT, name TEXT, address TEXT, "phoneNumber" TEXT, "createdAt" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT s.id, s.name, s.address, s.phone_number, s.created_date
    FROM suppliers s
    WHERE s.is_deleted = 0 AND s.company_id = p_company_id
    ORDER BY s.created_date DESC;
$$;

-- ── Categories ───────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_categories(p_company_id INT)
RETURNS TABLE(
    id INT, name TEXT, slug TEXT, description TEXT,
    "parentId" INT, sizes TEXT, "createdAt" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT c.id, c.name, c.slug, c.description,
           c.parent_id, c.sizes, c.created_date
    FROM categories c
    WHERE c.is_deleted = 0 AND c.company_id = p_company_id
    ORDER BY c.name;
$$;

-- ── Pricing Tags ─────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_pricing_tags(p_company_id INT)
RETURNS TABLE(
    id INT, name TEXT, "profitPercent" DECIMAL, "discountPercent" DECIMAL,
    "promoStartDate" TIMESTAMPTZ, "promoEndDate" TIMESTAMPTZ,
    "isActive" BOOLEAN, "createdAt" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT pt.id, pt.name, pt.profit_percent, pt.discount_percent,
           pt.promo_start_date, pt.promo_end_date, pt.is_active, pt.created_date
    FROM pricing_tags pt
    WHERE pt.is_deleted = 0 AND pt.company_id = p_company_id
    ORDER BY pt.name;
$$;

-- ── Orders ───────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_orders(p_company_id INT)
RETURNS TABLE(
    "Id" INT, "OrderNumber" TEXT, "CustomerName" TEXT, "CustomerPhone" TEXT,
    "Total" DECIMAL, "Status" TEXT, "PaymentMethod" TEXT, "PaymentStatus" TEXT,
    "SaleType" TEXT, "CreatedDate" TIMESTAMPTZ, "ItemsJson" TEXT
) LANGUAGE sql STABLE AS $$
    SELECT
        o.id, o.order_number, o.customer_name, o.customer_phone,
        o.total, o.status, o.payment_method, o.payment_status,
        o.sale_type, o.created_date,
        (
            SELECT COALESCE(json_agg(json_build_object(
                'productId', oi.product_id,
                'productName', p.name,
                'quantity', oi.quantity,
                'unitPrice', oi.price,
                'totalPrice', oi.total_price,
                'imageUrl', p.image_url
            ) ORDER BY oi.id), '[]')::TEXT
            FROM order_items oi
            LEFT JOIN products p ON p.id = oi.product_id
            WHERE oi.order_id = o.id
        ) AS items_json
    FROM orders o
    WHERE o.company_id = p_company_id
    ORDER BY o.created_date DESC;
$$;

CREATE OR REPLACE FUNCTION sp_get_order(p_id INT)
RETURNS TABLE(
    "Id" INT, "OrderNumber" TEXT, "CustomerName" TEXT, "CustomerPhone" TEXT,
    "Total" DECIMAL, "Status" TEXT, "PaymentMethod" TEXT, "PaymentStatus" TEXT,
    "SaleType" TEXT, "CreatedDate" TIMESTAMPTZ, "ItemsJson" TEXT
) LANGUAGE sql STABLE AS $$
    SELECT
        o.id, o.order_number, o.customer_name, o.customer_phone,
        o.total, o.status, o.payment_method, o.payment_status,
        o.sale_type, o.created_date,
        (
            SELECT COALESCE(json_agg(json_build_object(
                'productId', oi.product_id,
                'productName', p.name,
                'quantity', oi.quantity,
                'unitPrice', oi.price,
                'totalPrice', oi.total_price,
                'imageUrl', p.image_url
            ) ORDER BY oi.id), '[]')::TEXT
            FROM order_items oi
            LEFT JOIN products p ON p.id = oi.product_id
            WHERE oi.order_id = o.id
        )
    FROM orders o WHERE o.id = p_id;
$$;

-- ── Settings ─────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_settings(p_company_id INT)
RETURNS TABLE(
    "companyId" INT, "key" TEXT, "value" TEXT, "groupName" TEXT
) LANGUAGE sql STABLE AS $$
    SELECT company_id, key, value, group_name
    FROM company_settings
    WHERE company_id = p_company_id AND is_deleted = 0
    ORDER BY group_name, key;
$$;

CREATE OR REPLACE PROCEDURE sp_upsert_setting(
    p_company_id INT, p_key TEXT, p_value TEXT, p_group TEXT
) LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO company_settings(company_id, key, value, group_name, created_date, updated_date)
    VALUES (p_company_id, p_key, p_value, p_group, NOW(), NOW())
    ON CONFLICT (company_id, key)
    DO UPDATE SET value = p_value, group_name = p_group, updated_date = NOW();
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_company_profile(
    p_company_id INT, p_name TEXT, p_logo_url TEXT, p_banner_url TEXT,
    p_contact_email TEXT, p_contact_phone TEXT, p_address TEXT, p_delivery_charge DECIMAL
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE companies SET
        name            = p_name,
        logo_url        = NULLIF(trim(p_logo_url), ''),
        banner_url      = NULLIF(trim(p_banner_url), ''),
        contact_email   = NULLIF(trim(p_contact_email), ''),
        contact_phone   = NULLIF(trim(p_contact_phone), ''),
        address         = NULLIF(trim(p_address), ''),
        delivery_charge = p_delivery_charge,
        updated_date    = NOW()
    WHERE id = p_company_id;
END;
$$;

-- ── Dashboard ────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_get_dashboard_stats(p_company_id INT)
RETURNS TABLE(
    "totalRevenue" DECIMAL, "totalRevenueGrowth" DECIMAL,
    "totalOrders" INT, "totalOrdersGrowth" DECIMAL,
    "totalProducts" INT, "lowStockProducts" INT, "outOfStockProducts" INT,
    "totalCustomers" INT,
    "pendingOrders" INT, "processingOrders" INT,
    "completedOrders" INT, "cancelledOrders" INT,
    "posOrders" INT, "ecomOrders" INT
) LANGUAGE plpgsql STABLE AS $$
DECLARE
    v_now             TIMESTAMPTZ := NOW();
    v_start_month     TIMESTAMPTZ := date_trunc('month', NOW());
    v_start_last      TIMESTAMPTZ := date_trunc('month', NOW()) - INTERVAL '1 month';
    v_this_rev        DECIMAL;
    v_last_rev        DECIMAL;
    v_this_cnt        INT;
    v_last_cnt        INT;
BEGIN
    SELECT COALESCE(SUM(o.total), 0), COUNT(*) INTO v_this_rev, v_this_cnt
    FROM orders o WHERE o.company_id = p_company_id
      AND o.created_date >= v_start_month AND o.status <> 'CANCELLED';

    SELECT COALESCE(SUM(o.total), 0), COUNT(*) INTO v_last_rev, v_last_cnt
    FROM orders o WHERE o.company_id = p_company_id
      AND o.created_date >= v_start_last AND o.created_date < v_start_month
      AND o.status <> 'CANCELLED';

    RETURN QUERY
    SELECT
        ROUND(v_this_rev, 2),
        ROUND(CASE WHEN v_last_rev = 0 THEN 100
                   ELSE ((v_this_rev - v_last_rev) / v_last_rev) * 100 END, 1),
        v_this_cnt,
        ROUND(CASE WHEN v_last_cnt = 0 THEN 100
                   ELSE ((v_this_cnt - v_last_cnt)::DECIMAL / v_last_cnt) * 100 END, 1),
        (SELECT COUNT(*)::INT FROM products WHERE company_id = p_company_id AND is_deleted = 0),
        (SELECT COUNT(*)::INT FROM products WHERE company_id = p_company_id AND is_deleted = 0 AND stock_quantity > 0 AND stock_quantity <= 10),
        (SELECT COUNT(*)::INT FROM products WHERE company_id = p_company_id AND is_deleted = 0 AND stock_quantity = 0),
        (SELECT COUNT(*)::INT FROM users WHERE company_id = p_company_id AND is_deleted = 0),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND status = 'PENDING'),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND status = 'PROCESSING'),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND status = 'COMPLETED'),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND status = 'CANCELLED'),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND sale_type = 'POS'),
        (SELECT COUNT(*)::INT FROM orders WHERE company_id = p_company_id AND sale_type = 'ECOMMERCE');
END;
$$;

CREATE OR REPLACE FUNCTION sp_get_sales_chart(p_company_id INT, p_days INT DEFAULT 7)
RETURNS TABLE(
    date TEXT, label TEXT, revenue DECIMAL, orders INT
) LANGUAGE sql STABLE AS $$
    SELECT
        TO_CHAR(day, 'MM/DD'),
        TO_CHAR(day, 'Dy'),
        ROUND(COALESCE(SUM(o.total), 0), 2),
        COUNT(o.id)::INT
    FROM generate_series(
        (CURRENT_DATE - (p_days - 1) * INTERVAL '1 day')::TIMESTAMPTZ,
        CURRENT_DATE::TIMESTAMPTZ,
        INTERVAL '1 day'
    ) AS day
    LEFT JOIN orders o
        ON o.company_id = p_company_id
       AND o.created_date::DATE = day::DATE
       AND o.status <> 'CANCELLED'
    GROUP BY day ORDER BY day;
$$;

CREATE OR REPLACE FUNCTION sp_get_top_products(p_company_id INT, p_limit INT DEFAULT 5)
RETURNS TABLE(
    "productId" INT, "productName" TEXT, "imageUrl" TEXT,
    price DECIMAL, "totalSold" INT, "totalRevenue" DECIMAL
) LANGUAGE sql STABLE AS $$
    SELECT
        oi.product_id, p.name, p.image_url, p.price,
        SUM(oi.quantity)::INT,
        ROUND(SUM(oi.total_price), 2)
    FROM order_items oi
    JOIN orders o ON o.id = oi.order_id AND o.company_id = p_company_id AND o.status <> 'CANCELLED'
    JOIN products p ON p.id = oi.product_id
    GROUP BY oi.product_id, p.name, p.image_url, p.price
    ORDER BY SUM(oi.quantity) DESC
    LIMIT p_limit;
$$;

CREATE OR REPLACE FUNCTION sp_get_recent_orders(p_company_id INT, p_limit INT DEFAULT 10)
RETURNS TABLE(
    "Id" INT, "OrderNumber" TEXT, "CustomerName" TEXT, "CustomerPhone" TEXT,
    "Total" DECIMAL, "Status" TEXT, "PaymentMethod" TEXT, "PaymentStatus" TEXT,
    "SaleType" TEXT, "CreatedDate" TIMESTAMPTZ, "createdAt" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT
        o.id, o.order_number, o.customer_name, o.customer_phone,
        o.total, o.status, o.payment_method, o.payment_status,
        o.sale_type, o.created_date, o.created_date
    FROM orders o
    WHERE o.company_id = p_company_id
    ORDER BY o.created_date DESC
    LIMIT p_limit;
$$;

-- ── POS ──────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_lookup_product(p_barcode TEXT, p_company_id INT)
RETURNS TABLE(
    id INT, name TEXT, sku TEXT, barcode TEXT,
    price DECIMAL, "wholesalePrice" DECIMAL, "stockQuantity" INT,
    description TEXT, "imageUrl" TEXT,
    "categoryId" INT, "categoryName" TEXT,
    "brandId" INT, "brandName" TEXT
) LANGUAGE sql STABLE AS $$
    SELECT
        p.id, p.name, p.sku, p.barcode,
        p.price, p.wholesale_price, p.stock_quantity,
        p.description, p.image_url,
        p.category_id, cat.name,
        p.brand_id, b.name
    FROM products p
    LEFT JOIN categories cat ON cat.id = p.category_id
    LEFT JOIN brands b ON b.id = p.brand_id
    WHERE p.barcode = p_barcode
      AND (p_company_id IS NULL OR p.company_id = p_company_id)
      AND p.is_deleted = 0
    LIMIT 1;
$$;

-- ── Auth ─────────────────────────────────────────────────────

CREATE OR REPLACE PROCEDURE sp_generate_otp(
    p_email TEXT, p_otp TEXT, p_expires_at TIMESTAMPTZ
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE users SET otp = p_otp, otp_expires_at = p_expires_at WHERE email = p_email;
END;
$$;

CREATE OR REPLACE FUNCTION sp_register_company(
    p_company_name TEXT, p_subdomain TEXT, p_address TEXT,
    p_division TEXT, p_district TEXT, p_thana TEXT,
    p_owner_first_name TEXT, p_owner_last_name TEXT,
    p_owner_email TEXT, p_owner_phone TEXT, p_password_hash TEXT,
    p_plan_id INT, p_role_id INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE
    v_company_id INT;
    v_user_id INT;
BEGIN
    INSERT INTO companies (name, subdomain, address, division, district, thana,
        owner_name, owner_mobile, contact_email, contact_phone,
        is_active, approval_status, delivery_charge,
        created_date, updated_date, is_deleted)
    VALUES (p_company_name, lower(trim(p_subdomain)), p_address,
        p_division, p_district, p_thana,
        p_owner_first_name || ' ' || p_owner_last_name, p_owner_phone,
        p_owner_email, p_owner_phone,
        false, 'Pending', 0,
        NOW(), NOW(), 0)
    RETURNING id INTO v_company_id;

    INSERT INTO users (company_id, email, password_hash, first_name, last_name,
        phone_number, user_type, is_active, created_date, updated_date, is_deleted)
    VALUES (v_company_id, p_owner_email, p_password_hash,
        p_owner_first_name, p_owner_last_name, p_owner_phone,
        'companyadmin', true, NOW(), NOW(), 0)
    RETURNING id INTO v_user_id;

    IF p_role_id > 0 THEN
        INSERT INTO user_roles (user_id, role_id, created_date, updated_date, is_deleted)
        VALUES (v_user_id, p_role_id, NOW(), NOW(), 0)
        ON CONFLICT DO NOTHING;
    END IF;

    RETURN v_company_id;
END;
$$;

CREATE OR REPLACE FUNCTION sp_create_user(
    p_company_id INT, p_email TEXT, p_password_hash TEXT,
    p_first_name TEXT, p_last_name TEXT, p_phone TEXT,
    p_user_type TEXT, p_is_active BOOLEAN, p_role_id INT, p_created_by INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_user_id INT;
BEGIN
    INSERT INTO users (company_id, email, password_hash, first_name, last_name,
        phone_number, user_type, is_active, created_by, created_date, updated_date, is_deleted)
    VALUES (p_company_id, p_email, p_password_hash, p_first_name, p_last_name,
        p_phone, p_user_type, p_is_active, p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_user_id;

    IF p_role_id IS NOT NULL THEN
        INSERT INTO user_roles (user_id, role_id, created_date, updated_date, is_deleted)
        VALUES (v_user_id, p_role_id, NOW(), NOW(), 0)
        ON CONFLICT DO NOTHING;
    END IF;

    RETURN v_user_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_user(
    p_id INT, p_email TEXT, p_first_name TEXT, p_last_name TEXT,
    p_phone TEXT, p_is_active BOOLEAN, p_user_type TEXT,
    p_password_hash TEXT, p_company_id INT, p_role_id INT, p_updated_by INT
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE users SET
        email         = p_email,
        first_name    = p_first_name,
        last_name     = p_last_name,
        phone_number  = NULLIF(trim(p_phone), ''),
        is_active     = p_is_active,
        user_type     = p_user_type,
        password_hash = COALESCE(p_password_hash, password_hash),
        company_id    = p_company_id,
        updated_by    = p_updated_by,
        updated_date  = NOW()
    WHERE id = p_id;

    IF p_role_id IS NOT NULL THEN
        UPDATE user_roles SET is_deleted = 1 WHERE user_id = p_id;
        INSERT INTO user_roles (user_id, role_id, created_date, updated_date, is_deleted)
        VALUES (p_id, p_role_id, NOW(), NOW(), 0)
        ON CONFLICT DO NOTHING;
    END IF;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_user(p_id INT, p_deleted_by INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE users SET is_deleted = 1, deleted_date = NOW(), deleted_by = p_deleted_by WHERE id = p_id;
    UPDATE user_roles SET is_deleted = 1 WHERE user_id = p_id;
END;
$$;

-- ── Suppliers CRUD ───────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_create_supplier(
    p_company_id INT, p_name TEXT, p_phone TEXT, p_address TEXT, p_created_by INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_id INT;
BEGIN
    INSERT INTO suppliers (company_id, name, phone_number, address, created_by, created_date, updated_date, is_deleted)
    VALUES (p_company_id, p_name, NULLIF(trim(p_phone),''), NULLIF(trim(p_address),''), p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_supplier(
    p_id INT, p_name TEXT, p_phone TEXT, p_address TEXT, p_updated_by INT
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE suppliers SET
        name         = p_name,
        phone_number = NULLIF(trim(p_phone), ''),
        address      = NULLIF(trim(p_address), ''),
        updated_by   = p_updated_by,
        updated_date = NOW()
    WHERE id = p_id AND is_deleted = 0;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_supplier(p_id INT, p_deleted_by INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE suppliers SET is_deleted = 1, deleted_date = NOW(), deleted_by = p_deleted_by WHERE id = p_id;
END;
$$;

-- ── Categories CRUD ──────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_create_category(
    p_company_id INT, p_name TEXT, p_slug TEXT,
    p_description TEXT, p_sizes TEXT, p_created_by INT, p_parent_id INT DEFAULT NULL
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_id INT;
BEGIN
    INSERT INTO categories (company_id, name, slug, description, sizes, parent_id, created_by, created_date, updated_date, is_deleted)
    VALUES (p_company_id, p_name, p_slug, NULLIF(trim(p_description),''), NULLIF(trim(p_sizes),''), p_parent_id, p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_category(
    p_id INT, p_name TEXT, p_slug TEXT, p_description TEXT, p_sizes TEXT, p_updated_by INT, p_parent_id INT DEFAULT NULL
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE categories
    SET name = p_name, slug = p_slug,
        description = NULLIF(trim(p_description), ''),
        sizes = NULLIF(trim(p_sizes), ''),
        parent_id = p_parent_id,
        updated_date = NOW()
    WHERE id = p_id AND is_deleted = 0;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_category(p_id INT, p_deleted_by INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE categories SET is_deleted = 1, deleted_date = NOW(), deleted_by = p_deleted_by WHERE id = p_id;
END;
$$;

-- ── Pricing Tags CRUD ────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_create_pricing_tag(
    p_company_id INT, p_name TEXT, p_profit_percent DECIMAL,
    p_discount_percent DECIMAL, p_promo_start TIMESTAMPTZ, p_promo_end TIMESTAMPTZ,
    p_is_active BOOLEAN, p_created_by INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_id INT;
BEGIN
    INSERT INTO pricing_tags (
        company_id, name, profit_percent, discount_percent, promo_start_date, promo_end_date,
        is_active, created_by, created_date, updated_date, is_deleted)
    VALUES (
        p_company_id, p_name, p_profit_percent, p_discount_percent, p_promo_start, p_promo_end,
        p_is_active, p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_pricing_tag(
    p_id INT, p_name TEXT, p_profit_percent DECIMAL,
    p_discount_percent DECIMAL, p_promo_start TIMESTAMPTZ, p_promo_end TIMESTAMPTZ,
    p_is_active BOOLEAN, p_updated_by INT
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE pricing_tags SET
        name             = p_name,
        profit_percent   = p_profit_percent,
        discount_percent = p_discount_percent,
        promo_start_date = p_promo_start,
        promo_end_date   = p_promo_end,
        is_active        = p_is_active,
        updated_by       = p_updated_by,
        updated_date     = NOW()
    WHERE id = p_id AND is_deleted = 0;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_pricing_tag(p_id INT, p_deleted_by INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE pricing_tags SET is_deleted = 1, deleted_date = NOW(), deleted_by = p_deleted_by WHERE id = p_id;
END;
$$;

-- ── Products CRUD ────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_create_product(
    p_company_id INT, p_name TEXT, p_slug TEXT, p_sku TEXT, p_barcode TEXT,
    p_description TEXT, p_price DECIMAL, p_wholesale_price DECIMAL,
    p_stock_quantity INT, p_image_url TEXT,
    p_category_id INT, p_brand_id INT, p_status TEXT,
    p_size TEXT, p_supplier_id INT, p_created_by INT, p_pricing_tag_id INT DEFAULT NULL,
    p_compare_at_price DECIMAL DEFAULT NULL
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_id INT;
BEGIN
    INSERT INTO products (
        company_id, name, slug, sku, barcode, description,
        price, compare_at_price, wholesale_price, stock_quantity, image_url,
        category_id, brand_id, supplier_id, status, size, pricing_tag_id,
        created_by, created_date, updated_date, is_deleted)
    VALUES (
        p_company_id, p_name, p_slug, p_sku, p_barcode, NULLIF(trim(p_description),''),
        p_price, NULLIF(p_compare_at_price, 0), p_wholesale_price, p_stock_quantity, NULLIF(trim(p_image_url),''),
        NULLIF(p_category_id, 0), NULLIF(p_brand_id, 0), NULLIF(p_supplier_id, 0),
        p_status, NULLIF(trim(p_size),''), p_pricing_tag_id,
        p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_update_product(
    p_id INT, p_name TEXT, p_sku TEXT, p_price DECIMAL, p_wholesale_price DECIMAL,
    p_stock_quantity INT, p_description TEXT, p_image_url TEXT,
    p_category_id INT, p_brand_id INT, p_size TEXT, p_supplier_id INT, p_updated_by INT,
    p_pricing_tag_id INT DEFAULT NULL, p_compare_at_price DECIMAL DEFAULT NULL
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE products SET
        name            = p_name,
        sku             = p_sku,
        price           = p_price,
        compare_at_price = NULLIF(p_compare_at_price, 0),
        wholesale_price = p_wholesale_price,
        stock_quantity  = p_stock_quantity,
        description     = NULLIF(trim(p_description), ''),
        image_url       = NULLIF(trim(p_image_url), ''),
        category_id     = NULLIF(p_category_id, 0),
        brand_id        = NULLIF(p_brand_id, 0),
        supplier_id     = NULLIF(p_supplier_id, 0),
        size            = NULLIF(trim(p_size), ''),
        pricing_tag_id  = p_pricing_tag_id,
        updated_by      = p_updated_by,
        updated_date    = NOW()
    WHERE id = p_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_delete_product(p_id INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE products SET is_deleted = 1, deleted_date = NOW() WHERE id = p_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_adjust_stock(p_id INT, p_delta INT, p_updated_by INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE products
    SET stock_quantity = GREATEST(0, stock_quantity + p_delta),
        updated_date = NOW()
    WHERE id = p_id AND is_deleted = 0;
END;
$$;

-- Atomically bump and return a company's product running-number (for {SEQ}).
CREATE OR REPLACE FUNCTION sp_next_product_seq(p_company_id INT)
RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_seq INT;
BEGIN
    UPDATE companies SET product_seq = product_seq + 1 WHERE id = p_company_id
    RETURNING product_seq INTO v_seq;
    RETURN COALESCE(v_seq, 0);
END;
$$;

-- ── Inventory ────────────────────────────────────────────────
-- Apply a signed stock change AND record it in the movement ledger in one shot.
-- p_delta > 0 = stock in (purchase/adjust-in/return), < 0 = stock out.
CREATE OR REPLACE FUNCTION sp_inventory_move(
    p_company_id INT, p_product_id INT, p_delta INT, p_type TEXT,
    p_reason TEXT, p_unit_cost DECIMAL, p_reference TEXT, p_created_by INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_stock INT;
BEGIN
    UPDATE products
    SET stock_quantity = GREATEST(0, stock_quantity + p_delta), updated_date = NOW()
    WHERE id = p_product_id AND company_id = p_company_id AND is_deleted = 0
    RETURNING stock_quantity INTO v_stock;

    IF v_stock IS NULL THEN
        RAISE EXCEPTION 'Product % not found for company %', p_product_id, p_company_id;
    END IF;

    INSERT INTO inventory_movements(
        company_id, product_id, movement_type, quantity, reason, unit_cost, reference, stock_after, created_by, created_date)
    VALUES (p_company_id, p_product_id, p_type, p_delta, NULLIF(trim(p_reason),''), NULLIF(p_unit_cost,0),
            NULLIF(trim(p_reference),''), v_stock, p_created_by, NOW());

    RETURN v_stock;
END;
$$;

-- Movement report — optional product + date-range filters (0/NULL = ignore).
CREATE OR REPLACE FUNCTION sp_get_inventory_movements(
    p_company_id INT, p_product_id INT, p_from TIMESTAMPTZ, p_to TIMESTAMPTZ
) RETURNS TABLE(
    id INT, product_id INT, product_name TEXT, movement_type TEXT, quantity INT,
    reason TEXT, unit_cost DECIMAL, reference TEXT, stock_after INT, created_date TIMESTAMPTZ
) LANGUAGE plpgsql AS $$
BEGIN
    RETURN QUERY
    SELECT m.id, m.product_id, p.name::text, m.movement_type::text, m.quantity,
           m.reason::text, m.unit_cost, m.reference::text, m.stock_after, m.created_date
    FROM inventory_movements m
    LEFT JOIN products p ON p.id = m.product_id
    WHERE m.company_id = p_company_id
      AND (p_product_id IS NULL OR p_product_id = 0 OR m.product_id = p_product_id)
      AND (p_from IS NULL OR m.created_date >= p_from)
      AND (p_to   IS NULL OR m.created_date <  p_to)
    ORDER BY m.created_date DESC
    LIMIT 500;
END;
$$;

-- ── Orders CRUD ──────────────────────────────────────────────

CREATE OR REPLACE PROCEDURE sp_update_order_status(
    p_id INT, p_status TEXT, p_notes TEXT
) LANGUAGE plpgsql AS $$
BEGIN
    UPDATE orders SET status = p_status, updated_date = NOW() WHERE id = p_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_cancel_order(p_id INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE orders SET status = 'CANCELLED', updated_date = NOW() WHERE id = p_id;
    -- Restore stock
    UPDATE products p
    SET stock_quantity = p.stock_quantity + oi.quantity
    FROM order_items oi
    WHERE oi.order_id = p_id AND oi.product_id = p.id;
    -- Log the restock as RETURN movements (stock_after = restored level)
    INSERT INTO inventory_movements(company_id, product_id, movement_type, quantity, reference, stock_after, created_date)
    SELECT p.company_id, oi.product_id, 'RETURN', oi.quantity, o.order_number, p.stock_quantity, NOW()
    FROM order_items oi
    JOIN products p ON p.id = oi.product_id
    JOIN orders o  ON o.id = oi.order_id
    WHERE oi.order_id = p_id;
END;
$$;

CREATE OR REPLACE FUNCTION sp_checkout_order(
    p_company_id INT, p_order_number TEXT, p_sale_type TEXT, p_cashier_id INT,
    p_customer_name TEXT, p_customer_phone TEXT,
    p_status TEXT, p_subtotal DECIMAL, p_discount DECIMAL, p_total DECIMAL,
    p_payment_method TEXT, p_payment_status TEXT,
    p_product_ids INT[], p_quantities INT[], p_prices DECIMAL[],
    p_created_by INT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE
    v_order_id INT;
    i INT;
    v_stock INT;
BEGIN
    INSERT INTO orders (
        company_id, order_number, sale_type, sales_staff_id,
        customer_name, customer_phone, status,
        subtotal, discount, tax, shipping_fee, total,
        payment_method, payment_status,
        created_by, created_date, updated_date, is_deleted)
    VALUES (
        p_company_id, p_order_number, p_sale_type, p_cashier_id,
        p_customer_name, p_customer_phone, p_status,
        p_subtotal, p_discount, 0, 0, p_total,
        p_payment_method, p_payment_status,
        p_created_by, NOW(), NOW(), 0)
    RETURNING id INTO v_order_id;

    FOR i IN 1..array_length(p_product_ids, 1) LOOP
        INSERT INTO order_items (order_id, product_id, quantity, price, total_price, created_date, updated_date, is_deleted)
        VALUES (v_order_id, p_product_ids[i], p_quantities[i], p_prices[i],
                p_prices[i] * p_quantities[i], NOW(), NOW(), 0);

        UPDATE products SET
            stock_quantity = stock_quantity - p_quantities[i],
            updated_date   = NOW()
        WHERE id = p_product_ids[i]
        RETURNING stock_quantity INTO v_stock;

        INSERT INTO inventory_movements(company_id, product_id, movement_type, quantity, reference, stock_after, created_date)
        VALUES (p_company_id, p_product_ids[i], 'SALE', -p_quantities[i], p_order_number, v_stock, NOW());
    END LOOP;

    RETURN v_order_id;
END;
$$;

-- Storefront (guest) checkout. Mirrors sp_checkout_order but records the
-- delivery address/district/thana/note and a shipping fee — fields POS
-- walk-in orders don't have. Kept separate so the POS signature stays stable.
CREATE OR REPLACE FUNCTION sp_create_online_order(
    p_company_id INT, p_order_number TEXT,
    p_customer_name TEXT, p_customer_phone TEXT,
    p_shipping_address TEXT, p_shipping_district TEXT, p_shipping_thana TEXT,
    p_order_notes TEXT,
    p_subtotal DECIMAL, p_shipping_fee DECIMAL, p_total DECIMAL,
    p_payment_method TEXT,
    p_product_ids INT[], p_quantities INT[], p_prices DECIMAL[]
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE
    v_order_id INT;
    i INT;
    v_stock INT;
BEGIN
    INSERT INTO orders (
        company_id, order_number, sale_type, sales_staff_id,
        customer_name, customer_phone, status,
        subtotal, discount, tax, shipping_fee, total,
        payment_method, payment_status,
        shipping_address, shipping_district, shipping_thana, order_notes,
        created_date, updated_date, is_deleted)
    VALUES (
        p_company_id, p_order_number, 'ECOMMERCE', NULL,
        p_customer_name, p_customer_phone, 'PENDING',
        p_subtotal, 0, 0, p_shipping_fee, p_total,
        p_payment_method, 'PENDING',
        p_shipping_address, p_shipping_district, p_shipping_thana, p_order_notes,
        NOW(), NOW(), 0)
    RETURNING id INTO v_order_id;

    FOR i IN 1..array_length(p_product_ids, 1) LOOP
        INSERT INTO order_items (order_id, product_id, quantity, price, total_price, created_date, updated_date, is_deleted)
        VALUES (v_order_id, p_product_ids[i], p_quantities[i], p_prices[i],
                p_prices[i] * p_quantities[i], NOW(), NOW(), 0);

        UPDATE products SET
            stock_quantity = stock_quantity - p_quantities[i],
            updated_date   = NOW()
        WHERE id = p_product_ids[i]
        RETURNING stock_quantity INTO v_stock;

        INSERT INTO inventory_movements(company_id, product_id, movement_type, quantity, reference, stock_after, created_date)
        VALUES (p_company_id, p_product_ids[i], 'SALE', -p_quantities[i], p_order_number, v_stock, NOW());
    END LOOP;

    RETURN v_order_id;
END;
$$;

CREATE OR REPLACE FUNCTION sp_verify_payment(
    p_company_id INT, p_order_id INT, p_transaction_id TEXT,
    p_provider TEXT, p_amount DECIMAL, p_status TEXT,
    p_verification_type TEXT, p_sender_number TEXT, p_reference_log TEXT
) RETURNS INT LANGUAGE plpgsql AS $$
DECLARE v_id INT;
BEGIN
    UPDATE orders SET payment_status = p_status, updated_date = NOW() WHERE id = p_order_id;

    -- Insert into payment_logs if table exists, else just return 1
    BEGIN
        INSERT INTO payment_logs (
            company_id, order_id, transaction_id, provider, amount,
            status, verification_type, sender_number, reference_log,
            created_date, updated_date, is_deleted)
        VALUES (
            p_company_id, p_order_id, p_transaction_id, p_provider, p_amount,
            p_status, p_verification_type, p_sender_number, p_reference_log,
            NOW(), NOW(), 0)
        RETURNING id INTO v_id;
    EXCEPTION WHEN undefined_table THEN
        v_id := 1;
    END;

    RETURN COALESCE(v_id, 1);
END;
$$;

-- ── Unique constraint for settings upsert ────────────────────
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_company_settings_company_key'
    ) THEN
        ALTER TABLE company_settings
        ADD CONSTRAINT uq_company_settings_company_key UNIQUE (company_id, key);
    END IF;
END;
$$;

-- ============================================================
-- ZW Ecosystem - Stored Procedures
-- Run once after DB creation. Safe to re-run (CREATE OR REPLACE).
-- ============================================================

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

CREATE OR REPLACE FUNCTION sp_update_company(
    p_id INT, p_name TEXT, p_subdomain TEXT,
    p_contact_email TEXT, p_contact_phone TEXT,
    p_owner_name TEXT, p_owner_mobile TEXT, p_company_mobile TEXT,
    p_division TEXT, p_district TEXT, p_thana TEXT, p_address TEXT,
    p_facebook_link TEXT, p_instagram_link TEXT,
    p_bkash_number TEXT, p_nagad_number TEXT,
    p_bank_name TEXT, p_bank_account_name TEXT,
    p_logo_url TEXT, p_banner_url TEXT,
    p_is_active BOOLEAN, p_approval_status TEXT
) RETURNS VOID LANGUAGE plpgsql AS $$
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
    "CompanyId" INT, "Key" TEXT, "Value" TEXT, "GroupName" TEXT
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
    "SaleType" TEXT, "CreatedDate" TIMESTAMPTZ
) LANGUAGE sql STABLE AS $$
    SELECT
        o.id, o.order_number, o.customer_name, o.customer_phone,
        o.total, o.status, o.payment_method, o.payment_status,
        o.sale_type, o.created_date
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

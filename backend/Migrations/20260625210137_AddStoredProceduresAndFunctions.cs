using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredProceduresAndFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 25, 21, 1, 37, 111, DateTimeKind.Utc).AddTicks(3996));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                column: "updated_at",
                value: new DateTime(2026, 6, 25, 21, 1, 37, 111, DateTimeKind.Utc).AddTicks(3001));

            // 1. Create Stored Procedures
            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_create_product(
                    p_id UUID,
                    p_company_id UUID,
                    p_name VARCHAR,
                    p_slug VARCHAR,
                    p_sku VARCHAR,
                    p_barcode VARCHAR,
                    p_description VARCHAR,
                    p_price NUMERIC,
                    p_wholesale_price NUMERIC,
                    p_stock_quantity INTEGER,
                    p_image_url VARCHAR,
                    p_category_id UUID,
                    p_brand_id UUID,
                    p_status VARCHAR,
                    p_size VARCHAR,
                    p_supplier_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    INSERT INTO products (id, company_id, name, slug, sku, barcode, description, price, wholesale_price, stock_quantity, image_url, category_id, brand_id, status, size, supplier_id, created_at, updated_at)
                    VALUES (p_id, p_company_id, p_name, p_slug, p_sku, p_barcode, p_description, p_price, p_wholesale_price, p_stock_quantity, p_image_url, p_category_id, p_brand_id, p_status, p_size, p_supplier_id, NOW(), NOW());
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_update_product(
                    p_id UUID,
                    p_name VARCHAR,
                    p_sku VARCHAR,
                    p_price NUMERIC,
                    p_wholesale_price NUMERIC,
                    p_stock_quantity INTEGER,
                    p_description VARCHAR,
                    p_image_url VARCHAR,
                    p_category_id UUID,
                    p_brand_id UUID,
                    p_size VARCHAR,
                    p_supplier_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE products
                    SET name = p_name,
                        sku = p_sku,
                        price = p_price,
                        wholesale_price = p_wholesale_price,
                        stock_quantity = p_stock_quantity,
                        description = p_description,
                        image_url = p_image_url,
                        category_id = p_category_id,
                        brand_id = p_brand_id,
                        size = p_size,
                        supplier_id = p_supplier_id,
                        updated_at = NOW()
                    WHERE id = p_id;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_delete_product(
                    p_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM products WHERE id = p_id;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_create_supplier(
                    p_id UUID,
                    p_company_id UUID,
                    p_name VARCHAR,
                    p_phone_number VARCHAR,
                    p_address VARCHAR
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    INSERT INTO suppliers (id, company_id, name, phone_number, address, created_at, updated_at)
                    VALUES (p_id, p_company_id, p_name, p_phone_number, p_address, NOW(), NOW());
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_delete_supplier(
                    p_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM suppliers WHERE id = p_id;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_create_category(
                    p_id UUID,
                    p_company_id UUID,
                    p_name VARCHAR,
                    p_slug VARCHAR,
                    p_description VARCHAR,
                    p_sizes VARCHAR
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    INSERT INTO categories (id, company_id, name, slug, description, sizes, created_at, updated_at)
                    VALUES (p_id, p_company_id, p_name, p_slug, p_description, p_sizes, NOW(), NOW());
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_delete_category(
                    p_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM categories WHERE id = p_id;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_delete_user(
                    p_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    DELETE FROM user_roles WHERE user_id = p_id;
                    DELETE FROM users WHERE id = p_id;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_checkout_order(
                    p_order_id UUID,
                    p_company_id UUID,
                    p_order_number VARCHAR,
                    p_sale_type VARCHAR,
                    p_sales_staff_id UUID,
                    p_customer_name VARCHAR,
                    p_customer_phone VARCHAR,
                    p_status VARCHAR,
                    p_subtotal NUMERIC,
                    p_discount NUMERIC,
                    p_total NUMERIC,
                    p_payment_method VARCHAR,
                    p_payment_status VARCHAR,
                    p_product_ids UUID[],
                    p_quantities INTEGER[],
                    p_prices NUMERIC[]
                )
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    i INTEGER;
                    v_item_id UUID;
                BEGIN
                    -- 1. Insert Order
                    INSERT INTO orders (id, company_id, order_number, sale_type, sales_staff_id, customer_name, customer_phone, status, subtotal, discount, total, payment_method, payment_status, created_at, updated_at)
                    VALUES (p_order_id, p_company_id, p_order_number, p_sale_type, p_sales_staff_id, p_customer_name, p_customer_phone, p_status, p_subtotal, p_discount, p_total, p_payment_method, p_payment_status, NOW(), NOW());

                    -- 2. Loop through arrays and insert items + update stock
                    FOR i IN 1 .. array_length(p_product_ids, 1) LOOP
                        v_item_id := gen_random_uuid();
                        
                        -- Insert OrderItem
                        INSERT INTO order_items (id, order_id, product_id, quantity, price, total_price, created_at)
                        VALUES (v_item_id, p_order_id, p_product_ids[i], p_quantities[i], p_prices[i], p_prices[i] * p_quantities[i], NOW());

                        -- Update Stock
                        UPDATE products
                        SET stock_quantity = stock_quantity - p_quantities[i],
                            updated_at = NOW()
                        WHERE id = p_product_ids[i];
                    END LOOP;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_verify_payment(
                    p_payment_id UUID,
                    p_company_id UUID,
                    p_order_id UUID,
                    p_transaction_id VARCHAR,
                    p_provider VARCHAR,
                    p_amount NUMERIC,
                    p_status VARCHAR,
                    p_payment_type VARCHAR,
                    p_sender_number VARCHAR,
                    p_reference_log VARCHAR
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    -- 1. Insert Payment log
                    INSERT INTO payments (id, company_id, order_id, transaction_id, provider, amount, status, payment_type, sender_number, reference_log, created_at, updated_at)
                    VALUES (p_payment_id, p_company_id, p_order_id, p_transaction_id, p_provider, p_amount, p_status, p_payment_type, p_sender_number, p_reference_log, NOW(), NOW());

                    -- 2. Update Order Status
                    UPDATE orders
                    SET payment_status = 'PAID',
                        status = 'PROCESSING',
                        updated_at = NOW()
                    WHERE id = p_order_id;
                END;
                $$;
            ");

            // 2. Create Stored Functions (Queries returning Tables)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_products(
                    p_company_id UUID,
                    p_search VARCHAR
                )
                RETURNS TABLE (
                    id UUID,
                    company_id UUID,
                    name VARCHAR,
                    slug VARCHAR,
                    sku VARCHAR,
                    barcode VARCHAR,
                    description VARCHAR,
                    price NUMERIC,
                    wholesale_price NUMERIC,
                    stock_quantity INTEGER,
                    status VARCHAR,
                    category_id UUID,
                    brand_id UUID,
                    supplier_id UUID,
                    size VARCHAR,
                    image_url VARCHAR,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT p.id, p.company_id, p.name, p.slug, p.sku, p.barcode, p.description, p.price, p.wholesale_price, p.stock_quantity, p.status, p.category_id, p.brand_id, p.supplier_id, p.size, p.image_url, p.created_at, p.updated_at
                    FROM products p
                    WHERE (p_company_id IS NULL OR p.company_id = p_company_id)
                      AND (p_search IS NULL OR p_search = '' OR p.name ILIKE '%' || p_search || '%' OR p.sku ILIKE '%' || p_search || '%' OR p.barcode ILIKE '%' || p_search || '%')
                    ORDER BY p.created_at DESC;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_suppliers(
                    p_company_id UUID
                )
                RETURNS TABLE (
                    id UUID,
                    company_id UUID,
                    name VARCHAR,
                    phone_number VARCHAR,
                    address VARCHAR,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT s.id, s.company_id, s.name, s.phone_number, s.address, s.created_at, s.updated_at
                    FROM suppliers s
                    WHERE (p_company_id IS NULL OR s.company_id = p_company_id)
                    ORDER BY s.name;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_categories(
                    p_company_id UUID
                )
                RETURNS TABLE (
                    id UUID,
                    company_id UUID,
                    name VARCHAR,
                    slug VARCHAR,
                    description VARCHAR,
                    sizes VARCHAR,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT c.id, c.company_id, c.name, c.slug, c.description, c.sizes, c.created_at, c.updated_at
                    FROM categories c
                    WHERE (p_company_id IS NULL OR c.company_id = p_company_id)
                    ORDER BY c.name;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_users(
                    p_company_id UUID
                )
                RETURNS TABLE (
                    id UUID,
                    company_id UUID,
                    email VARCHAR,
                    password_hash VARCHAR,
                    first_name VARCHAR,
                    last_name VARCHAR,
                    phone_number VARCHAR,
                    address VARCHAR,
                    user_type VARCHAR,
                    is_active BOOLEAN,
                    otp VARCHAR,
                    otp_expires_at TIMESTAMP,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT u.id, u.company_id, u.email, u.password_hash, u.first_name, u.last_name, u.phone_number, u.address, u.user_type, u.is_active, u.otp, u.otp_expires_at, u.created_at, u.updated_at
                    FROM users u
                    WHERE (p_company_id IS NULL OR u.company_id = p_company_id)
                    ORDER BY u.created_at DESC;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_companies()
                RETURNS TABLE (
                    id UUID,
                    name VARCHAR,
                    subdomain VARCHAR,
                    logo_url VARCHAR,
                    banner_url VARCHAR,
                    contact_email VARCHAR,
                    contact_phone VARCHAR,
                    address VARCHAR,
                    delivery_charge NUMERIC,
                    is_active BOOLEAN,
                    subscription_plan_id UUID,
                    subscription_expires_at TIMESTAMP,
                    approval_status VARCHAR,
                    owner_name VARCHAR,
                    owner_mobile VARCHAR,
                    company_mobile VARCHAR,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP,
                    division VARCHAR,
                    district VARCHAR,
                    thana VARCHAR,
                    facebook_link VARCHAR,
                    instagram_link VARCHAR,
                    bkash_number VARCHAR,
                    nagad_number VARCHAR,
                    bank_name VARCHAR,
                    bank_account_name VARCHAR
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT c.id, c.name, c.subdomain, c.logo_url, c.banner_url, c.contact_email, c.contact_phone, c.address, c.delivery_charge, c.is_active, c.subscription_plan_id, c.subscription_expires_at, c.approval_status, c.owner_name, c.owner_mobile, c.company_mobile, c.created_at, c.updated_at, c.division, c.district, c.thana, c.facebook_link, c.instagram_link, c.bkash_number, c.nagad_number, c.bank_name, c.bank_account_name
                    FROM companies c
                    ORDER BY c.name;
                END;
                $$;
            ");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION fn_get_orders(
                    p_company_id UUID
                )
                RETURNS TABLE (
                    id UUID,
                    company_id UUID,
                    order_number VARCHAR,
                    sale_type VARCHAR,
                    sales_staff_id UUID,
                    customer_name VARCHAR,
                    customer_phone VARCHAR,
                    shipping_address VARCHAR,
                    subtotal NUMERIC,
                    discount NUMERIC,
                    shipping_fee NUMERIC,
                    total NUMERIC,
                    status VARCHAR,
                    payment_method VARCHAR,
                    payment_status VARCHAR,
                    notes VARCHAR,
                    created_at TIMESTAMP,
                    updated_at TIMESTAMP
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RETURN QUERY
                    SELECT o.id, o.company_id, o.order_number, o.sale_type, o.sales_staff_id, o.customer_name, o.customer_phone, o.shipping_address, o.subtotal, o.discount, o.shipping_fee, o.total, o.status, o.payment_method, o.payment_status, o.notes, o.created_at, o.updated_at
                    FROM orders o
                    WHERE (p_company_id IS NULL OR o.company_id = p_company_id)
                    ORDER BY o.created_at DESC;
                END;
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop procedures
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_create_product;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_update_product;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_delete_product;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_create_supplier;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_delete_supplier;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_create_category;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_delete_category;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_delete_user;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_checkout_order;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_verify_payment;");

            // Drop functions
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_products;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_suppliers;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_categories;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_companies;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fn_get_orders;");

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 25, 20, 56, 51, 212, DateTimeKind.Utc).AddTicks(3004));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                column: "updated_at",
                value: new DateTime(2026, 6, 25, 20, 56, 51, 212, DateTimeKind.Utc).AddTicks(1999));
        }
    }
}

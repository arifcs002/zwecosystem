using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateStoredProceduresAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed update for Super Admin password & email compatibility
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
                columns: new[] { "email", "password_hash", "updated_at" },
                values: new object[] { "arifsuperadmin", "$2b$11$f1FDqBdveY.KIotMM1fKM.OoZUEGh1tnXTAlWX6aGj3zZHsW2KCrK", new DateTime(2026, 6, 25, 20, 56, 51, 212, DateTimeKind.Utc).AddTicks(1999) });

            // ----------------------------------------------------
            // 1. PERFORMANCE INDEXES
            // ----------------------------------------------------

            // Index for users table - queries by email and tenant
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_users_email_company ON users (email, company_id);");
            
            // Index for products table - queries by name (search) and category/supplier
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_products_search ON products (company_id, category_id, supplier_id);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_products_name_trgm ON products USING gin (to_tsvector('english', name));");

            // Index for orders table - queries by status and date
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_orders_company_status_date ON orders (company_id, status, created_at DESC);");

            // ----------------------------------------------------
            // 2. STORED PROCEDURES (Insert / Get / Update operations)
            // ----------------------------------------------------

            // SP: Register new company (Company + owner admin user + default settings + role mapping)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_register_company(
                    p_company_id UUID,
                    p_company_name VARCHAR,
                    p_subdomain VARCHAR,
                    p_address VARCHAR,
                    p_division VARCHAR,
                    p_district VARCHAR,
                    p_thana VARCHAR,
                    p_owner_first_name VARCHAR,
                    p_owner_last_name VARCHAR,
                    p_owner_email VARCHAR,
                    p_owner_phone VARCHAR,
                    p_password_hash VARCHAR,
                    p_basic_plan_id UUID,
                    p_company_admin_role_id UUID,
                    p_owner_user_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    -- 1. Insert Company
                    INSERT INTO companies (id, name, subdomain, logo_url, banner_url, contact_email, contact_phone, address, delivery_charge, is_active, subscription_plan_id, subscription_expires_at, approval_status, owner_name, owner_mobile, company_mobile, created_at, updated_at, division, district, thana)
                    VALUES (p_company_id, p_company_name, p_subdomain, '/uploads/zw-logo.png', NULL, p_owner_email, p_owner_phone, p_address, 60.00, FALSE, p_basic_plan_id, NOW() + INTERVAL '1 month', 'Pending', p_owner_first_name || ' ' || p_owner_last_name, p_owner_phone, p_owner_phone, NOW(), NOW(), p_division, p_district, p_thana);

                    -- 2. Insert Owner Admin User
                    INSERT INTO users (id, company_id, email, password_hash, first_name, last_name, phone_number, address, user_type, is_active, created_at, updated_at)
                    VALUES (p_owner_user_id, p_company_id, p_owner_email, p_password_hash, p_owner_first_name, p_owner_last_name, p_owner_phone, p_address, 'CompanyAdmin', TRUE, NOW(), NOW());

                    -- 3. Map COMPANY_ADMIN role
                    INSERT INTO user_roles (user_id, role_id)
                    VALUES (p_owner_user_id, p_company_admin_role_id);

                    -- 4. Set Default Company Settings
                    INSERT INTO company_settings (company_id, key, value, group_name)
                    VALUES (p_company_id, 'shop_currency', 'BDT', 'GENERAL');

                    INSERT INTO company_settings (company_id, key, value, group_name)
                    VALUES (p_company_id, 'receipt_header', 'Thank you for shopping at ' || p_company_name || '!', 'POS');

                    INSERT INTO company_settings (company_id, key, value, group_name)
                    VALUES (p_company_id, 'receipt_footer', 'Eat pure, stay healthy.', 'POS');
                END;
                $$;
            ");

            // SP: Create User
            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_create_user(
                    p_user_id UUID,
                    p_company_id UUID,
                    p_email VARCHAR,
                    p_password_hash VARCHAR,
                    p_first_name VARCHAR,
                    p_last_name VARCHAR,
                    p_phone_number VARCHAR,
                    p_user_type VARCHAR,
                    p_is_active BOOLEAN,
                    p_role_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    INSERT INTO users (id, company_id, email, password_hash, first_name, last_name, phone_number, user_type, is_active, created_at, updated_at)
                    VALUES (p_user_id, p_company_id, p_email, p_password_hash, p_first_name, p_last_name, p_phone_number, p_user_type, p_is_active, NOW(), NOW());

                    IF p_role_id IS NOT NULL THEN
                        INSERT INTO user_roles (user_id, role_id)
                        VALUES (p_user_id, p_role_id);
                    END IF;
                END;
                $$;
            ");

            // SP: Update User Details
            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_update_user(
                    p_user_id UUID,
                    p_email VARCHAR,
                    p_first_name VARCHAR,
                    p_last_name VARCHAR,
                    p_phone_number VARCHAR,
                    p_is_active BOOLEAN,
                    p_user_type VARCHAR,
                    p_password_hash VARCHAR,
                    p_company_id UUID,
                    p_role_id UUID
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE users
                    SET email = p_email,
                        first_name = p_first_name,
                        last_name = p_last_name,
                        phone_number = p_phone_number,
                        is_active = p_is_active,
                        user_type = p_user_type,
                        password_hash = CASE WHEN p_password_hash IS NOT NULL AND p_password_hash <> '' THEN p_password_hash ELSE password_hash END,
                        company_id = CASE WHEN p_company_id IS NOT NULL THEN p_company_id ELSE company_id END,
                        updated_at = NOW()
                    WHERE id = p_user_id;

                    IF p_role_id IS NOT NULL THEN
                        DELETE FROM user_roles WHERE user_id = p_user_id;
                        INSERT INTO user_roles (user_id, role_id) VALUES (p_user_id, p_role_id);
                    END IF;
                END;
                $$;
            ");

            // SP: Update Company Admin Panel Details
            migrationBuilder.Sql(@"
                CREATE OR REPLACE PROCEDURE sp_update_company(
                    p_company_id UUID,
                    p_name VARCHAR,
                    p_subdomain VARCHAR,
                    p_contact_email VARCHAR,
                    p_company_mobile VARCHAR,
                    p_owner_name VARCHAR,
                    p_owner_mobile VARCHAR,
                    p_division VARCHAR,
                    p_district VARCHAR,
                    p_thana VARCHAR,
                    p_address VARCHAR,
                    p_facebook_link VARCHAR,
                    p_instagram_link VARCHAR,
                    p_bkash_number VARCHAR,
                    p_nagad_number VARCHAR,
                    p_bank_name VARCHAR,
                    p_bank_account_name VARCHAR,
                    p_is_active BOOLEAN,
                    p_approval_status VARCHAR,
                    p_logo_url VARCHAR
                )
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    UPDATE companies
                    SET name = p_name,
                        subdomain = p_subdomain,
                        contact_email = p_contact_email,
                        company_mobile = p_company_mobile,
                        owner_name = p_owner_name,
                        owner_mobile = p_owner_mobile,
                        division = p_division,
                        district = p_district,
                        thana = p_thana,
                        address = p_address,
                        facebook_link = p_facebook_link,
                        instagram_link = p_instagram_link,
                        bkash_number = p_bkash_number,
                        nagad_number = p_nagad_number,
                        bank_name = p_bank_name,
                        bank_account_name = p_bank_account_name,
                        is_active = p_is_active,
                        approval_status = p_approval_status,
                        logo_url = p_logo_url,
                        updated_at = NOW()
                    WHERE id = p_company_id;

                    -- Sync owner status based on approval/activation
                    IF p_approval_status = 'Approved' AND p_is_active = TRUE THEN
                        UPDATE users SET is_active = TRUE WHERE company_id = p_company_id AND user_type = 'CompanyAdmin';
                    ELSIF p_approval_status = 'Rejected' OR p_is_active = FALSE THEN
                        UPDATE users SET is_active = FALSE WHERE company_id = p_company_id AND user_type = 'CompanyAdmin';
                    END IF;
                END;
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop procedures
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_register_company;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_create_user;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_update_user;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sp_update_company;");

            // Drop indexes
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_users_email_company;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_products_search;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_products_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_orders_company_status_date;");

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 9, 3, 50, 257, DateTimeKind.Utc).AddTicks(1671));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                columns: new[] { "email", "password_hash", "updated_at" },
                values: new object[] { "arifowneradmin.bd", "$2a$11$T8X9d8k1pLz6eH1U8y.BWeT1W1T5.6K4Q8oO8uQ1H7zV1I3pP5O5a", new DateTime(2026, 6, 24, 9, 3, 50, 257, DateTimeKind.Utc).AddTicks(661) });
        }
    }
}

-- Batch 1 storefront features: discount coupons + abandoned-cart capture.
-- Safe to run on a fresh DB (EnsureCreated already built the tables) and on an existing one.

CREATE TABLE IF NOT EXISTS coupons (
    id                   SERIAL PRIMARY KEY,
    company_id           INTEGER       NOT NULL,
    code                 TEXT          NOT NULL,
    description          TEXT,
    discount_type        TEXT          NOT NULL DEFAULT 'PERCENT',   -- PERCENT | FIXED
    discount_value       NUMERIC(18,2) NOT NULL DEFAULT 0,
    min_order_amount     NUMERIC(18,2) NOT NULL DEFAULT 0,
    max_discount_amount  NUMERIC(18,2),
    usage_limit          INTEGER,
    used_count           INTEGER       NOT NULL DEFAULT 0,
    starts_at            TIMESTAMPTZ,
    expires_at           TIMESTAMPTZ,
    is_active            BOOLEAN       NOT NULL DEFAULT TRUE,
    created_by           INTEGER,
    created_date         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by           INTEGER,
    updated_date         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    deleted_by           INTEGER,
    deleted_date         TIMESTAMPTZ,
    is_deleted           INTEGER       NOT NULL DEFAULT 0
);

-- A code is unique per company among live (non-deleted) coupons, so a deleted code can be reused.
CREATE UNIQUE INDEX IF NOT EXISTS ux_coupons_company_code ON coupons (company_id, code) WHERE is_deleted = 0;

CREATE TABLE IF NOT EXISTS abandoned_carts (
    id                  SERIAL PRIMARY KEY,
    company_id          INTEGER       NOT NULL,
    session_key         TEXT          NOT NULL,
    customer_name       TEXT,
    customer_phone      TEXT          NOT NULL,
    shipping_address    TEXT,
    cart_json           TEXT          NOT NULL DEFAULT '[]',
    item_count          INTEGER       NOT NULL DEFAULT 0,
    subtotal            NUMERIC(18,2) NOT NULL DEFAULT 0,
    status              TEXT          NOT NULL DEFAULT 'OPEN',        -- OPEN | CONTACTED | RECOVERED | DISMISSED
    last_activity       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    recovered_order_id  INTEGER,
    created_by          INTEGER,
    created_date        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by          INTEGER,
    updated_date        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    deleted_by          INTEGER,
    deleted_date        TIMESTAMPTZ,
    is_deleted          INTEGER       NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_abandoned_carts_session ON abandoned_carts (company_id, session_key);
CREATE INDEX IF NOT EXISTS ix_abandoned_carts_company_status ON abandoned_carts (company_id, status, last_activity DESC);

ALTER TABLE orders ADD COLUMN IF NOT EXISTS coupon_code TEXT;

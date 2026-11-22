-- Batch 3: storefront customer accounts, customer groups (member discounts), collections.

CREATE TABLE IF NOT EXISTS customer_groups (
    id                SERIAL PRIMARY KEY,
    company_id        INTEGER       NOT NULL,
    name              TEXT          NOT NULL,
    description       TEXT,
    discount_percent  NUMERIC(18,2) NOT NULL DEFAULT 0,
    created_by        INTEGER,
    created_date      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by        INTEGER,
    updated_date      TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    deleted_by        INTEGER,
    deleted_date      TIMESTAMPTZ,
    is_deleted        INTEGER       NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_groups_company_name ON customer_groups (company_id, lower(name)) WHERE is_deleted = 0;

CREATE TABLE IF NOT EXISTS customer_accounts (
    id                SERIAL PRIMARY KEY,
    company_id        INTEGER     NOT NULL,
    name              TEXT        NOT NULL,
    phone             TEXT        NOT NULL,
    email             TEXT,
    password_hash     TEXT        NOT NULL,
    address           TEXT,
    district          TEXT,
    thana             TEXT,
    group_id          INTEGER,
    is_active         BOOLEAN     NOT NULL DEFAULT TRUE,
    last_login        TIMESTAMPTZ,
    created_by        INTEGER,
    created_date     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by        INTEGER,
    updated_date      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    deleted_by        INTEGER,
    deleted_date      TIMESTAMPTZ,
    is_deleted        INTEGER      NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_accounts_company_phone ON customer_accounts (company_id, phone) WHERE is_deleted = 0;

-- Shopper sessions live in their own table and are only honoured by api/account/*; a shopper
-- token is never a staff Bearer token, so it can't reach any admin endpoint.
CREATE TABLE IF NOT EXISTS customer_sessions (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER     NOT NULL,
    account_id    INTEGER     NOT NULL,
    token_hash    TEXT        NOT NULL,      -- SHA-256 of the token; the raw token is never stored
    expires_at    TIMESTAMPTZ NOT NULL,
    created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ip_address    TEXT,
    user_agent    TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_sessions_token ON customer_sessions (token_hash);
CREATE INDEX IF NOT EXISTS ix_customer_sessions_account ON customer_sessions (account_id);

CREATE TABLE IF NOT EXISTS collections (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER     NOT NULL,
    name          TEXT        NOT NULL,
    slug          TEXT        NOT NULL,
    description   TEXT,
    is_active     BOOLEAN     NOT NULL DEFAULT TRUE,
    sort_order    INTEGER     NOT NULL DEFAULT 0,
    created_by    INTEGER,
    created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by    INTEGER,
    updated_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_by    INTEGER,
    deleted_date  TIMESTAMPTZ,
    is_deleted    INTEGER     NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_collections_company_slug ON collections (company_id, slug) WHERE is_deleted = 0;

-- Items are keyed by the product group ("<base name>::<categoryId>") so every size of a shoe joins together.
CREATE TABLE IF NOT EXISTS collection_items (
    id             SERIAL PRIMARY KEY,
    company_id     INTEGER     NOT NULL,
    collection_id  INTEGER     NOT NULL,
    product_key    TEXT        NOT NULL,
    sort_order     INTEGER     NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_collection_items_unique ON collection_items (collection_id, product_key);

ALTER TABLE orders ADD COLUMN IF NOT EXISTS customer_account_id INTEGER;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS discount_label TEXT;

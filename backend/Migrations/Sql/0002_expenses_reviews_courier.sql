-- Batch 2: expense tracking (profit & loss), product reviews, courier booking columns.

CREATE TABLE IF NOT EXISTS expenses (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER       NOT NULL,
    category      TEXT          NOT NULL,
    amount        NUMERIC(18,2) NOT NULL,
    expense_date  DATE          NOT NULL,
    payee         TEXT,
    note          TEXT,
    created_by    INTEGER,
    created_date  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_by    INTEGER,
    updated_date  TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    deleted_by    INTEGER,
    deleted_date  TIMESTAMPTZ,
    is_deleted    INTEGER       NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_expenses_company_date ON expenses (company_id, expense_date DESC);

CREATE TABLE IF NOT EXISTS reviews (
    id             SERIAL PRIMARY KEY,
    company_id     INTEGER     NOT NULL,
    product_id     INTEGER     NOT NULL,
    product_key    TEXT        NOT NULL,   -- "<base name>::<categoryId>" — all size variants share it
    reviewer_name  TEXT        NOT NULL,
    rating         INTEGER     NOT NULL,
    comment        TEXT,
    status         TEXT        NOT NULL DEFAULT 'PENDING',   -- PENDING | APPROVED | REJECTED
    is_verified    BOOLEAN     NOT NULL DEFAULT FALSE,       -- order number + phone matched a real purchase
    created_by     INTEGER,
    created_date   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by     INTEGER,
    updated_date   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    deleted_by     INTEGER,
    deleted_date   TIMESTAMPTZ,
    is_deleted     INTEGER     NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_reviews_company_key_status ON reviews (company_id, product_key, status);

ALTER TABLE orders ADD COLUMN IF NOT EXISTS courier_consignment_id TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS courier_status TEXT;

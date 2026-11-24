-- Batch 14: automatic offers, product batches with expiry dates, daily owner summary.

-- Offer rules. offer_type: BUY_GET (buy X get Y free of one product), BUNDLE (set of products at one price),
-- QTY_PERCENT (percent off a product - or every product - when at least min_qty are bought).
CREATE TABLE IF NOT EXISTS offers (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER       NOT NULL,
    name          TEXT          NOT NULL,
    offer_type    TEXT          NOT NULL,
    product_id    INTEGER,
    buy_qty       INTEGER       NOT NULL DEFAULT 0,
    get_qty       INTEGER       NOT NULL DEFAULT 0,
    min_qty       INTEGER       NOT NULL DEFAULT 0,
    percent       NUMERIC(5,2)  NOT NULL DEFAULT 0,
    bundle_price  NUMERIC(18,2) NOT NULL DEFAULT 0,
    starts_at     TIMESTAMPTZ,
    ends_at       TIMESTAMPTZ,
    is_active     BOOLEAN       NOT NULL DEFAULT TRUE,
    created_date  TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_offers_company ON offers (company_id, is_active);

CREATE TABLE IF NOT EXISTS offer_items (
    offer_id   INTEGER NOT NULL REFERENCES offers(id) ON DELETE CASCADE,
    product_id INTEGER NOT NULL,
    quantity   INTEGER NOT NULL,
    PRIMARY KEY (offer_id, product_id)
);

-- A lot of one product with its own expiry date. `quantity` is how many of that lot are still on the shelf.
-- Stock itself stays in products.stock_quantity; batches only say which part of it expires when.
CREATE TABLE IF NOT EXISTS product_batches (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER     NOT NULL,
    product_id    INTEGER     NOT NULL,
    batch_no      TEXT,
    expiry_date   DATE        NOT NULL,
    quantity      INTEGER     NOT NULL,
    received_qty  INTEGER     NOT NULL,
    status        TEXT        NOT NULL DEFAULT 'ACTIVE',
    note          TEXT,
    created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_batches_product ON product_batches (company_id, product_id, expiry_date);
CREATE INDEX IF NOT EXISTS ix_batches_expiry ON product_batches (company_id, expiry_date) WHERE quantity > 0;

-- One row per shop per day once the evening summary was sent, so a restart can never send it twice.
CREATE TABLE IF NOT EXISTS daily_summaries (
    company_id   INTEGER     NOT NULL,
    summary_date DATE        NOT NULL,
    created_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (company_id, summary_date)
);

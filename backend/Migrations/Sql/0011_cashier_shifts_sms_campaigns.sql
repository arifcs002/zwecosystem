-- Batch 13: cashier shifts and customer SMS campaigns.

-- One row per shift. A cashier can have only one open shift at a time (partial unique index).
CREATE TABLE IF NOT EXISTS cashier_shifts (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER       NOT NULL,
    user_id       INTEGER       NOT NULL,
    opened_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    opening_cash  NUMERIC(18,2) NOT NULL DEFAULT 0,
    closed_at     TIMESTAMPTZ,
    expected_cash NUMERIC(18,2),
    counted_cash  NUMERIC(18,2),
    note          TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cashier_shift_open ON cashier_shifts (company_id, user_id) WHERE closed_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_cashier_shift_company ON cashier_shifts (company_id, opened_at);

-- Phones that asked not to get marketing SMS. Skipped by every campaign.
CREATE TABLE IF NOT EXISTS sms_optouts (
    company_id   INTEGER     NOT NULL,
    phone        TEXT        NOT NULL,
    created_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (company_id, phone)
);

CREATE TABLE IF NOT EXISTS sms_campaigns (
    id           SERIAL PRIMARY KEY,
    company_id   INTEGER     NOT NULL,
    name         TEXT        NOT NULL,
    audience     TEXT        NOT NULL,
    message      TEXT        NOT NULL,
    recipients   INTEGER     NOT NULL DEFAULT 0,
    sent         INTEGER     NOT NULL DEFAULT 0,
    failed       INTEGER     NOT NULL DEFAULT 0,
    created_by   INTEGER,
    created_date TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_sms_campaigns_company ON sms_campaigns (company_id, created_date);

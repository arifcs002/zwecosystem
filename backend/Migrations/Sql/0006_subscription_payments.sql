-- Batch 8: manual subscription payments (cash / bKash / Nagad / bank), recorded by the platform owner.
CREATE TABLE IF NOT EXISTS subscription_payments (
    id              SERIAL PRIMARY KEY,
    company_id      INTEGER       NOT NULL,
    plan_id         INTEGER,
    amount          NUMERIC(18,2) NOT NULL,
    method          TEXT          NOT NULL,
    reference       TEXT,
    months          INTEGER       NOT NULL DEFAULT 1,
    period_start    TIMESTAMPTZ   NOT NULL,
    period_end      TIMESTAMPTZ   NOT NULL,
    recorded_by     INTEGER,
    note            TEXT,
    created_date    TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_subscription_payments_company ON subscription_payments (company_id, created_date DESC);

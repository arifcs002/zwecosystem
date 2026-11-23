-- Batch 6: SMS one-time codes (password reset, phone verification).
CREATE TABLE IF NOT EXISTS phone_otps (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER     NOT NULL,
    phone         TEXT        NOT NULL,
    purpose       TEXT        NOT NULL,
    code_hash     TEXT        NOT NULL,
    attempts      INTEGER     NOT NULL DEFAULT 0,
    expires_at    TIMESTAMPTZ NOT NULL,
    used_at       TIMESTAMPTZ,
    created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_phone_otps_lookup ON phone_otps (company_id, phone, purpose, created_date DESC);
ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS phone_verified_at TIMESTAMPTZ;

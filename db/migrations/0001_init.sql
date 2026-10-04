-- =====================================================================
--  Sewa Everything — 0001_init.sql
--  Skema awal: seluruh tabel + jaring pengaman correctness di level DB.
--
--  Prinsip file ini: setiap aturan di Bagian 4 spesifikasi yang BISA
--  ditegakkan database, ditegakkan database — bukan cuma di kode C#.
--  Kode aplikasi yang salah, migrasi manual, atau psql iseng tetap
--  tidak boleh bisa merusak data.
-- =====================================================================

BEGIN;

-- Wajib untuk exclusion constraint: gist perlu opclass btree agar
-- kolom uuid (item_id) bisa dipakai dengan operator "=" di index gist.
CREATE EXTENSION IF NOT EXISTS btree_gist;


-- ---------------------------------------------------------------------
-- Helper: updated_at otomatis
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END $$;


-- =====================================================================
--  USERS
-- =====================================================================
CREATE TABLE users (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    role          text        NOT NULL CHECK (role IN ('owner','admin','seller','renter')),
    name          text        NOT NULL CHECK (btrim(name) <> ''),
    email         text        NOT NULL CHECK (email LIKE '%_@_%.%'),
    phone         text,
    password_hash text        NOT NULL,

    -- Verifikasi dokumen seller oleh admin (Bagian 3). Renter selalu false.
    is_verified   boolean     NOT NULL DEFAULT false,
    verified_at   timestamptz,
    verified_by   uuid        REFERENCES users(id),

    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_users_verified_consistent
        CHECK (is_verified = (verified_at IS NOT NULL))
);

-- Email unik case-insensitive tanpa perlu extension citext.
CREATE UNIQUE INDEX ux_users_email_lower ON users (lower(email));
CREATE INDEX ix_users_role ON users (role);

-- Antrean "verifikasi seller" (GET /admin/sellers/pending) — index parsial
-- supaya tetap ramping walau tabel users besar.
CREATE INDEX ix_users_sellers_pending ON users (created_at)
    WHERE role = 'seller' AND is_verified = false;

CREATE TRIGGER users_set_updated_at
    BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- =====================================================================
--  PLATFORM_SETTINGS — konfigurasi Owner (komisi, durasi soft-hold)
--  Pola singleton: PK boolean yang di-CHECK true, jadi baris kedua
--  mustahil dibuat.
-- =====================================================================
CREATE TABLE platform_settings (
    id               boolean     PRIMARY KEY DEFAULT true CHECK (id),

    commission_rate  numeric(6,4) NOT NULL DEFAULT 0.0500
                     CHECK (commission_rate >= 0 AND commission_rate <= 1),
    commission_mode  text         NOT NULL DEFAULT 'deduct'
                     CHECK (commission_mode IN ('deduct','on_top')),

    -- Berapa lama booking pending menahan slot sebelum auto-cancel.
    hold_minutes     integer     NOT NULL DEFAULT 15 CHECK (hold_minutes BETWEEN 1 AND 1440),

    updated_by       uuid        REFERENCES users(id),
    updated_at       timestamptz NOT NULL DEFAULT now()
);

INSERT INTO platform_settings (id) VALUES (true);

CREATE TRIGGER platform_settings_set_updated_at
    BEFORE UPDATE ON platform_settings
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- =====================================================================
--  PAYOUT_ACCOUNTS — tujuan uang keluar dari platform.
--  Dipakai DUA arah:
--    - seller  : menerima pencairan dana sewa
--    - renter  : menerima refund deposit kalau bayarnya lewat VA / QRIS /
--                tunai di minimarket (channel yang tidak bisa di-reversal
--                ke sumber).
-- =====================================================================
CREATE TABLE payout_accounts (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    kind           text NOT NULL CHECK (kind IN ('bank','ewallet')),
    provider_code  text NOT NULL CHECK (btrim(provider_code) <> ''),  -- 'BCA','BNI','gopay','ovo', ...
    account_number text NOT NULL CHECK (btrim(account_number) <> ''),
    account_holder text NOT NULL CHECK (btrim(account_holder) <> ''),

    is_default     boolean     NOT NULL DEFAULT false,
    verified_at    timestamptz,                                        -- hasil name-check gateway
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_payout_accounts_user ON payout_accounts (user_id);

-- Satu rekening default per user.
CREATE UNIQUE INDEX ux_payout_accounts_default ON payout_accounts (user_id)
    WHERE is_default;

-- Rekening yang sama tidak didaftarkan dua kali oleh user yang sama.
CREATE UNIQUE INDEX ux_payout_accounts_unique
    ON payout_accounts (user_id, provider_code, account_number);

CREATE TRIGGER payout_accounts_set_updated_at
    BEFORE UPDATE ON payout_accounts
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- =====================================================================
--  ITEMS
-- =====================================================================
CREATE TABLE items (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    seller_id      uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,

    title          text NOT NULL CHECK (btrim(title) <> '' AND length(title) <= 200),
    category       text NOT NULL CHECK (btrim(category) <> ''),
    description    text,

    price          numeric(14,2) NOT NULL CHECK (price > 0),
    price_unit     text          NOT NULL CHECK (price_unit IN ('hour','day','week','month')),
    deposit_amount numeric(14,2) NOT NULL DEFAULT 0 CHECK (deposit_amount >= 0),

    status         text NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),

    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_items_seller ON items (seller_id);
CREATE INDEX ix_items_browse ON items (category, price) WHERE status = 'active';

CREATE TRIGGER items_set_updated_at
    BEFORE UPDATE ON items
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Barang hanya boleh dimiliki akun ber-role seller.
CREATE OR REPLACE FUNCTION trg_items_seller_role() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE v_role text;
BEGIN
    SELECT role INTO v_role FROM users WHERE id = NEW.seller_id;
    IF v_role <> 'seller' THEN
        RAISE EXCEPTION 'items.seller_id harus user ber-role seller (dapat: %)', v_role
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER items_seller_role
    BEFORE INSERT OR UPDATE OF seller_id ON items
    FOR EACH ROW EXECUTE FUNCTION trg_items_seller_role();


-- =====================================================================
--  ITEM_PHOTOS
-- =====================================================================
CREATE TABLE item_photos (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id    uuid NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    url        text NOT NULL CHECK (btrim(url) <> ''),
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_item_photos_item ON item_photos (item_id, sort_order);


-- =====================================================================
--  ITEM_BLACKOUTS — tanggal yang diblok seller (servis, dipakai sendiri, dll)
--
--  `during` selalu half-open '[)'. Ini penting: sewa yang berakhir
--  10:00 dan sewa berikutnya yang mulai 10:00 TIDAK dianggap bentrok.
--  Kalau bound '[]' lolos masuk, back-to-back rental jadi mustahil.
--  timestamptz itu tipe kontinu, jadi PostgreSQL TIDAK menormalkan
--  bound-nya sendiri — CHECK di bawah yang memaksakan.
-- =====================================================================
CREATE TABLE item_blackouts (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id    uuid NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    during     tstzrange NOT NULL,
    reason     text,
    created_at timestamptz NOT NULL DEFAULT now(),

    -- kolom baca-saja untuk query & tampilan kalender
    starts_at  timestamptz GENERATED ALWAYS AS (lower(during)) STORED,
    ends_at    timestamptz GENERATED ALWAYS AS (upper(during)) STORED,

    CONSTRAINT ck_blackouts_range_canonical CHECK (
        lower(during) IS NOT NULL AND upper(during) IS NOT NULL
        AND lower_inc(during) AND NOT upper_inc(during)
        AND lower(during) < upper(during)
    ),

    -- Blackout sesama blackout pun tidak boleh tumpang tindih.
    CONSTRAINT no_blackout_overlap
        EXCLUDE USING gist (item_id WITH =, during WITH &&)
);


-- =====================================================================
--  BOOKINGS
--
--  Semua kolom uang adalah SNAPSHOT hasil hitungan server saat booking
--  dibuat. Harga listing boleh berubah besok — booking lama tidak ikut
--  berubah. Angka dari client tidak pernah dipakai (Aturan 4.1); CHECK
--  di bawah membuat database sendiri yang membuktikan aritmetikanya.
-- =====================================================================
CREATE TABLE bookings (
    id        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id   uuid NOT NULL REFERENCES items(id)  ON DELETE RESTRICT,
    renter_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,

    during    tstzrange NOT NULL,
    starts_at timestamptz GENERATED ALWAYS AS (lower(during)) STORED,
    ends_at   timestamptz GENERATED ALWAYS AS (upper(during)) STORED,

    status    text NOT NULL DEFAULT 'pending'
              CHECK (status IN ('pending','confirmed','active','completed','cancelled','disputed')),

    -- ---- snapshot harga (dikunci saat pembuatan) --------------------
    price_snapshot      numeric(14,2) NOT NULL CHECK (price_snapshot > 0),
    price_unit_snapshot text          NOT NULL CHECK (price_unit_snapshot IN ('hour','day','week','month')),
    duration_units      integer       NOT NULL CHECK (duration_units >= 1),

    total_rent          numeric(14,2) NOT NULL CHECK (total_rent > 0),
    deposit_amount      numeric(14,2) NOT NULL DEFAULT 0 CHECK (deposit_amount >= 0),

    -- ---- snapshot komisi (rate & mode saat booking dibuat) ----------
    platform_fee_rate   numeric(6,4)  NOT NULL DEFAULT 0
                        CHECK (platform_fee_rate >= 0 AND platform_fee_rate <= 1),
    platform_fee_mode   text          NOT NULL DEFAULT 'deduct'
                        CHECK (platform_fee_mode IN ('deduct','on_top')),
    platform_fee_amount numeric(14,2) NOT NULL DEFAULT 0 CHECK (platform_fee_amount >= 0),

    -- ---- turunan: dihitung DATABASE, tidak bisa dipalsukan client ---
    -- Yang harus dibayar renter di /pay:
    renter_total numeric(14,2) GENERATED ALWAYS AS (
        total_rent + deposit_amount
        + CASE WHEN platform_fee_mode = 'on_top' THEN platform_fee_amount ELSE 0 END
    ) STORED,

    -- Hak seller sebelum penyesuaian deposit (forfeit ditambahkan di ledger):
    seller_gross numeric(14,2) GENERATED ALWAYS AS (
        total_rent
        - CASE WHEN platform_fee_mode = 'deduct' THEN platform_fee_amount ELSE 0 END
    ) STORED,

    -- ---- soft-hold ---------------------------------------------------
    hold_expires_at  timestamptz,
    cancelled_reason text,

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_bookings_range_canonical CHECK (
        lower(during) IS NOT NULL AND upper(during) IS NOT NULL
        AND lower_inc(during) AND NOT upper_inc(during)
        AND lower(during) < upper(during)
    ),

    -- Aturan 4.1 ditegakkan database: total = harga x durasi, titik.
    CONSTRAINT ck_bookings_total_is_derived
        CHECK (total_rent = price_snapshot * duration_units),

    CONSTRAINT ck_bookings_fee_is_derived
        CHECK (platform_fee_amount = round(total_rent * platform_fee_rate, 2)),

    -- Booking pending WAJIB punya batas waktu hold.
    CONSTRAINT ck_bookings_pending_has_expiry
        CHECK (status <> 'pending' OR hold_expires_at IS NOT NULL)
);

-- =====================================================================
--  ATURAN 4.2 — ANTI DOUBLE-BOOKING DI LEVEL DATABASE
--
--  'pending' ikut dikunci: soft-hold yang tidak menahan slot bukan hold.
--  Begitu hold kedaluwarsa dan job mengubahnya jadi 'cancelled', baris
--  itu keluar dari index parsial dan slotnya bebas lagi dengan
--  sendirinya.
-- =====================================================================
ALTER TABLE bookings ADD CONSTRAINT no_overlap
    EXCLUDE USING gist (item_id WITH =, during WITH &&)
    WHERE (status IN ('pending','confirmed','active'));

CREATE INDEX ix_bookings_renter    ON bookings (renter_id, created_at DESC);
CREATE INDEX ix_bookings_item      ON bookings (item_id, starts_at);

-- Untuk job pelepas hold kedaluwarsa.
CREATE INDEX ix_bookings_hold_expiry ON bookings (hold_expires_at)
    WHERE status = 'pending';

CREATE TRIGGER bookings_set_updated_at
    BEFORE UPDATE ON bookings
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- ---------------------------------------------------------------------
--  ATURAN 4.3 — STATE MACHINE DIKUNCI DI DATABASE
--  Transisi di luar daftar ini ditolak, dari mana pun perintahnya datang.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_bookings_status_transition() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.status = OLD.status THEN
        RETURN NEW;
    END IF;

    IF NOT (
           (OLD.status = 'pending'   AND NEW.status IN ('confirmed','cancelled'))
        OR (OLD.status = 'confirmed' AND NEW.status IN ('active','cancelled'))
        OR (OLD.status = 'active'    AND NEW.status IN ('completed','disputed'))
        OR (OLD.status = 'disputed'  AND NEW.status =  'completed')
    ) THEN
        RAISE EXCEPTION 'transisi status booking terlarang: % -> %', OLD.status, NEW.status
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER bookings_status_transition
    BEFORE UPDATE OF status ON bookings
    FOR EACH ROW EXECUTE FUNCTION trg_bookings_status_transition();


-- ---------------------------------------------------------------------
--  Kesepakatan tidak boleh berubah setelah dibuat.
--  Yang boleh berubah cuma: status, hold_expires_at, cancelled_reason,
--  updated_at. Perpanjangan sewa = booking baru, bukan edit yang lama.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_bookings_immutable_terms() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF  NEW.item_id             IS DISTINCT FROM OLD.item_id
     OR NEW.renter_id           IS DISTINCT FROM OLD.renter_id
     OR NEW.during              IS DISTINCT FROM OLD.during
     OR NEW.price_snapshot      IS DISTINCT FROM OLD.price_snapshot
     OR NEW.price_unit_snapshot IS DISTINCT FROM OLD.price_unit_snapshot
     OR NEW.duration_units      IS DISTINCT FROM OLD.duration_units
     OR NEW.total_rent          IS DISTINCT FROM OLD.total_rent
     OR NEW.deposit_amount      IS DISTINCT FROM OLD.deposit_amount
     OR NEW.platform_fee_rate   IS DISTINCT FROM OLD.platform_fee_rate
     OR NEW.platform_fee_mode   IS DISTINCT FROM OLD.platform_fee_mode
     OR NEW.platform_fee_amount IS DISTINCT FROM OLD.platform_fee_amount
     OR NEW.created_at          IS DISTINCT FROM OLD.created_at
    THEN
        RAISE EXCEPTION 'syarat booking bersifat final dan tidak dapat diubah setelah dibuat'
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER bookings_immutable_terms
    BEFORE UPDATE ON bookings
    FOR EACH ROW EXECUTE FUNCTION trg_bookings_immutable_terms();


-- ---------------------------------------------------------------------
--  Booking tidak boleh menabrak blackout milik seller.
--  Exclusion constraint tidak bisa lintas tabel, jadi dijaga trigger di
--  KEDUA arah + advisory lock per item supaya dua transaksi bersamaan
--  (booking masuk vs blackout dibuat) tidak saling selip.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_booking_vs_blackout() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    v_item_id uuid;
    v_during  tstzrange;
    v_hit     uuid;
BEGIN
    IF TG_TABLE_NAME = 'bookings' THEN
        v_item_id := NEW.item_id;
        v_during  := NEW.during;

        -- Serialisasi per item: transaksi lain untuk item yang sama menunggu.
        PERFORM pg_advisory_xact_lock(hashtextextended(v_item_id::text, 0));

        SELECT b.id INTO v_hit
        FROM item_blackouts b
        WHERE b.item_id = v_item_id AND b.during && v_during
        LIMIT 1;

        IF v_hit IS NOT NULL THEN
            RAISE EXCEPTION 'rentang waktu bertabrakan dengan blackout seller (%)', v_hit
                USING ERRCODE = 'exclusion_violation';
        END IF;
    ELSE
        v_item_id := NEW.item_id;
        v_during  := NEW.during;

        PERFORM pg_advisory_xact_lock(hashtextextended(v_item_id::text, 0));

        SELECT bk.id INTO v_hit
        FROM bookings bk
        WHERE bk.item_id = v_item_id
          AND bk.during && v_during
          AND bk.status IN ('pending','confirmed','active')
        LIMIT 1;

        IF v_hit IS NOT NULL THEN
            RAISE EXCEPTION 'tidak bisa memblok tanggal: sudah ada booking aktif (%)', v_hit
                USING ERRCODE = 'exclusion_violation';
        END IF;
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER bookings_vs_blackout
    BEFORE INSERT ON bookings
    FOR EACH ROW EXECUTE FUNCTION trg_booking_vs_blackout();

CREATE TRIGGER blackouts_vs_bookings
    BEFORE INSERT ON item_blackouts
    FOR EACH ROW EXECUTE FUNCTION trg_booking_vs_blackout();


-- =====================================================================
--  BOOKING_STATUS_HISTORY — jejak audit, diisi otomatis oleh trigger.
--  Tidak ada jalur kode yang bisa mengubah status tanpa meninggalkan
--  baris di sini.
-- =====================================================================
CREATE TABLE booking_status_history (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    booking_id  uuid NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    from_status text,
    to_status   text NOT NULL,
    changed_by  uuid REFERENCES users(id),
    reason      text,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_booking_status_history_booking
    ON booking_status_history (booking_id, created_at);

CREATE OR REPLACE FUNCTION trg_bookings_log_status() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO booking_status_history (booking_id, from_status, to_status)
        VALUES (NEW.id, NULL, NEW.status);
    ELSIF NEW.status IS DISTINCT FROM OLD.status THEN
        INSERT INTO booking_status_history (booking_id, from_status, to_status, reason)
        VALUES (NEW.id, OLD.status, NEW.status, NEW.cancelled_reason);
    END IF;
    RETURN NULL;
END $$;

CREATE TRIGGER bookings_log_status
    AFTER INSERT OR UPDATE OF status ON bookings
    FOR EACH ROW EXECUTE FUNCTION trg_bookings_log_status();


-- =====================================================================
--  BOOKING_PHOTOS — bukti kondisi barang saat serah-terima & pengembalian
-- =====================================================================
CREATE TABLE booking_photos (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id  uuid NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    phase       text NOT NULL CHECK (phase IN ('handover','return')),
    url         text NOT NULL CHECK (btrim(url) <> ''),
    uploaded_by uuid NOT NULL REFERENCES users(id),
    note        text,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_booking_photos_booking ON booking_photos (booking_id, phase);


-- =====================================================================
--  PAYMENTS — buku besar append-only.
--  Satu baris = satu pergerakan dana. Baris tidak pernah dihapus dan
--  nominalnya tidak pernah diubah; yang boleh berubah hanya status
--  (pending -> paid/failed/expired) beserta referensi gateway-nya.
--
--  Posisi keuangan sebuah booking = agregasi baris-baris ini, bukan
--  kolom status yang di-mutate.
-- =====================================================================
CREATE TABLE payments (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,

    kind text NOT NULL CHECK (kind IN (
        'rent_charge',      -- renter  -> platform
        'deposit_charge',   -- renter  -> platform
        'platform_fee',     -- alokasi internal: pendapatan platform
        'deposit_forfeit',  -- alokasi internal: bagian deposit jatuh ke seller
        'rent_refund',      -- platform -> renter (pembatalan)
        'deposit_refund',   -- platform -> renter (barang kembali)
        'seller_payout'     -- platform -> seller (pencairan)
    )),

    direction text NOT NULL CHECK (direction IN ('in','out','internal')),

    -- Selalu positif. Arah uang dibaca dari `direction`, bukan tanda minus.
    amount   numeric(14,2) NOT NULL CHECK (amount > 0),
    currency char(3)       NOT NULL DEFAULT 'IDR',

    status text NOT NULL DEFAULT 'pending'
           CHECK (status IN ('pending','paid','failed','expired')),

    -- Bagaimana uangnya bergerak.
    --   gateway_charge   : tagihan ke renter
    --   gateway_reversal : refund balik ke sumber (kartu / e-wallet)
    --   disbursement     : transfer ke rekening terdaftar (asal bayar VA /
    --                      QRIS / tunai minimarket — tidak bisa di-reversal)
    --   internal         : alokasi pembukuan, uang tidak keluar platform
    method text NOT NULL CHECK (method IN
        ('gateway_charge','gateway_reversal','disbursement','internal')),

    channel        text,    -- 'va_bca','qris','gopay','cstore_alfamart','credit_card', ...
    counterparty_id uuid    REFERENCES users(id),   -- yang membayar / menerima
    payout_account_id uuid  REFERENCES payout_accounts(id),

    gateway_ref  text,      -- id transaksi di sisi Midtrans/Xendit
    parent_id    uuid REFERENCES payments(id),      -- refund menunjuk charge asalnya

    -- ATURAN 4.4: kunci idempotensi operasi keluar. UNIQUE, jadi operasi
    -- yang sama mustahil dieksekusi dua kali.
    idempotency_key text NOT NULL UNIQUE,

    failure_reason text,
    settled_at     timestamptz,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),

    -- kind menentukan direction — tidak bisa dikombinasi seenaknya
    CONSTRAINT ck_payments_kind_direction CHECK (
           (kind IN ('rent_charge','deposit_charge')                   AND direction = 'in')
        OR (kind IN ('rent_refund','deposit_refund','seller_payout')   AND direction = 'out')
        OR (kind IN ('platform_fee','deposit_forfeit')                 AND direction = 'internal')
    ),

    CONSTRAINT ck_payments_direction_method CHECK (
           (direction = 'in'       AND method = 'gateway_charge')
        OR (direction = 'out'      AND method IN ('gateway_reversal','disbursement'))
        OR (direction = 'internal' AND method = 'internal')
    ),

    -- Disbursement wajib punya rekening tujuan; reversal justru tidak boleh.
    CONSTRAINT ck_payments_disbursement_target CHECK (
        (method = 'disbursement') = (payout_account_id IS NOT NULL)
    ),

    CONSTRAINT ck_payments_settled CHECK ((status = 'paid') = (settled_at IS NOT NULL))
);

CREATE INDEX ix_payments_booking ON payments (booking_id, created_at);
CREATE INDEX ix_payments_status  ON payments (status) WHERE status = 'pending';
CREATE INDEX ix_payments_gateway_ref ON payments (gateway_ref) WHERE gateway_ref IS NOT NULL;

CREATE TRIGGER payments_set_updated_at
    BEFORE UPDATE ON payments
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- Append-only ditegakkan database: nominal & identitas baris beku,
-- DELETE dilarang total.
CREATE OR REPLACE FUNCTION trg_payments_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'baris payments tidak boleh dihapus (buku besar append-only)'
            USING ERRCODE = 'check_violation';
    END IF;

    IF  NEW.booking_id      IS DISTINCT FROM OLD.booking_id
     OR NEW.kind            IS DISTINCT FROM OLD.kind
     OR NEW.direction       IS DISTINCT FROM OLD.direction
     OR NEW.amount          IS DISTINCT FROM OLD.amount
     OR NEW.currency        IS DISTINCT FROM OLD.currency
     OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
     OR NEW.created_at      IS DISTINCT FROM OLD.created_at
    THEN
        RAISE EXCEPTION 'baris payments bersifat immutable; catat koreksi sebagai baris baru'
            USING ERRCODE = 'check_violation';
    END IF;

    -- Status hanya boleh maju dari pending.
    IF OLD.status <> NEW.status AND OLD.status <> 'pending' THEN
        RAISE EXCEPTION 'status pembayaran final (%) tidak dapat diubah', OLD.status
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER payments_append_only
    BEFORE UPDATE OR DELETE ON payments
    FOR EACH ROW EXECUTE FUNCTION trg_payments_append_only();


-- =====================================================================
--  WEBHOOK_EVENTS — idempotensi sisi MASUK (Aturan 4.4).
--  payments.idempotency_key menjaga operasi yang KITA kirim; tabel ini
--  menjaga notifikasi yang gateway kirim ke kita. Gateway boleh mengirim
--  event yang sama berkali-kali; UNIQUE (provider, event_id) membuat
--  percobaan kedua gagal insert, jadi efeknya cuma terjadi sekali.
-- =====================================================================
CREATE TABLE webhook_events (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider     text NOT NULL,               -- 'midtrans' | 'xendit'
    event_id     text NOT NULL,               -- id notifikasi milik gateway
    signature    text,
    payload      jsonb NOT NULL,
    received_at  timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz,
    process_error text,

    CONSTRAINT ux_webhook_events UNIQUE (provider, event_id)
);

CREATE INDEX ix_webhook_events_unprocessed ON webhook_events (received_at)
    WHERE processed_at IS NULL;


-- =====================================================================
--  REVIEWS — satu review per booking
-- =====================================================================
CREATE TABLE reviews (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id uuid NOT NULL UNIQUE REFERENCES bookings(id) ON DELETE RESTRICT,
    rating     smallint NOT NULL CHECK (rating BETWEEN 1 AND 5),
    comment    text,
    created_at timestamptz NOT NULL DEFAULT now()
);


-- =====================================================================
--  DISPUTES — satu sengketa terbuka per booking
-- =====================================================================
CREATE TABLE disputes (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id  uuid NOT NULL UNIQUE REFERENCES bookings(id) ON DELETE RESTRICT,
    raised_by   uuid NOT NULL REFERENCES users(id),
    reason      text NOT NULL CHECK (btrim(reason) <> ''),

    status      text NOT NULL DEFAULT 'open' CHECK (status IN ('open','resolved')),
    resolution  text,
    resolved_by uuid REFERENCES users(id),
    resolved_at timestamptz,

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_disputes_resolved_consistent CHECK (
        (status = 'resolved') =
        (resolution IS NOT NULL AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL)
    )
);

CREATE INDEX ix_disputes_open ON disputes (created_at) WHERE status = 'open';

CREATE TRIGGER disputes_set_updated_at
    BEFORE UPDATE ON disputes
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


COMMIT;

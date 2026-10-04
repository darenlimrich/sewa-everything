-- =====================================================================
--  Sewa Everything — 0003_booking_holds.sql
--  Milestone 4: dua jendela hold, bukan satu.
--
--  Alur yang diputuskan pemilik produk: renter memesan -> SELLER MENYETUJUI
--  -> renter membayar. Artinya ada dua tunggu berbeda, dan dua-duanya
--  menahan slot:
--
--    pending    menunggu seller menyetujui
--    confirmed  menunggu renter membayar
--
--  Skema 0001 cuma mengenal satu jendela (`hold_minutes`) karena rancangan
--  awalnya pembayaran yang langsung mengonfirmasi. Dengan alur baru,
--  bahaya yang ditulis di docs/schema-decisions.md poin 5 — "booking yang
--  tidak dibayar akan mengunci slot selamanya" — tidak hilang, ia cuma
--  pindah satu status ke kanan. Kalau cuma `pending` yang punya batas
--  waktu, booking yang sudah disetujui tapi tidak pernah dibayar akan
--  mengunci slot itu selamanya.
-- =====================================================================

BEGIN;


-- ---------------------------------------------------------------------
--  platform_settings: satu jendela jadi dua
--
--  `hold_minutes` diganti nama, bukan dipakai ulang begitu saja. Namanya
--  tidak lagi menggambarkan isinya sejak ada dua jendela, dan setelan yang
--  namanya menyesatkan adalah cara Owner salah mengatur batas waktu tanpa
--  sadar.
-- ---------------------------------------------------------------------
ALTER TABLE platform_settings RENAME COLUMN hold_minutes TO approval_minutes;

ALTER TABLE platform_settings
    RENAME CONSTRAINT platform_settings_hold_minutes_check
    TO platform_settings_approval_minutes_check;

-- 15 menit masuk akal sebagai jendela bayar; sebagai jendela balas seller
-- ia praktis menjamin setiap booking kedaluwarsa sebelum sempat dilihat.
ALTER TABLE platform_settings ALTER COLUMN approval_minutes SET DEFAULT 1440;
UPDATE platform_settings SET approval_minutes = 1440 WHERE approval_minutes = 15;

ALTER TABLE platform_settings
    ADD COLUMN payment_minutes integer NOT NULL DEFAULT 60
    CHECK (payment_minutes BETWEEN 1 AND 1440);

COMMENT ON COLUMN platform_settings.approval_minutes IS
    'Lama booking pending menahan slot sambil menunggu seller menyetujui.';
COMMENT ON COLUMN platform_settings.payment_minutes IS
    'Lama booking confirmed menahan slot sambil menunggu pembayaran renter.';


-- ---------------------------------------------------------------------
--  Index untuk job pelepas hold
--
--  Versi 0001 parsial `WHERE status = 'pending'`, jadi ia tidak melayani
--  booking `confirmed` yang juga punya tenggat. Diganti supaya job cukup
--  satu query untuk kedua fase.
--
--  `hold_expires_at IS NOT NULL` ikut masuk ke predikat karena booking
--  yang sudah dibayar (milestone 5) akan mengosongkan kolom ini — itulah
--  yang mengeluarkannya dari daftar kandidat kedaluwarsa. Booking lunas
--  tidak boleh ikut tersapu.
-- ---------------------------------------------------------------------
DROP INDEX ix_bookings_hold_expiry;

CREATE INDEX ix_bookings_hold_expiry ON bookings (hold_expires_at)
    WHERE status IN ('pending', 'confirmed') AND hold_expires_at IS NOT NULL;


COMMIT;

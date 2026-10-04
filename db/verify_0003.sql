-- =====================================================================
--  verify_0003.sql — membuktikan dua jendela hold dari 0003_booking_holds.sql
--  benar-benar terpasang, dan job pelepas hold bisa menemukan keduanya.
--
--  Dijalankan dalam satu transaksi lalu ROLLBACK.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0003.sql
-- =====================================================================

\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE t_results (
    seq    serial,
    bagian text,
    nama   text,
    lulus  boolean,
    detail text
) ON COMMIT DROP;

CREATE OR REPLACE FUNCTION t_check(
    p_bagian text, p_nama text, p_lulus boolean, p_detail text DEFAULT NULL
) RETURNS void LANGUAGE plpgsql AS $fn$
BEGIN
    INSERT INTO t_results (bagian, nama, lulus, detail)
    VALUES (p_bagian, p_nama, p_lulus, coalesce(p_detail, ''));
END $fn$;

CREATE OR REPLACE FUNCTION t_expect_error(
    p_bagian text, p_nama text, p_sql text, p_errcode text DEFAULT NULL
) RETURNS void LANGUAGE plpgsql AS $fn$
BEGIN
    BEGIN
        EXECUTE p_sql;
        INSERT INTO t_results (bagian, nama, lulus, detail)
        VALUES (p_bagian, p_nama, false, 'HARUSNYA DITOLAK — tapi statement berhasil');
    EXCEPTION WHEN OTHERS THEN
        INSERT INTO t_results (bagian, nama, lulus, detail)
        VALUES (p_bagian, p_nama, p_errcode IS NULL OR SQLSTATE = p_errcode,
                SQLSTATE || ' — ' || left(SQLERRM, 60));
    END;
END $fn$;


-- =====================================================================
--  1. platform_settings punya dua jendela, bukan satu
-- =====================================================================

SELECT t_check('setelan', 'Kolom hold_minutes sudah tidak ada',
    NOT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'platform_settings' AND column_name = 'hold_minutes'));

SELECT t_check('setelan', 'Kolom approval_minutes ada',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'platform_settings' AND column_name = 'approval_minutes'));

SELECT t_check('setelan', 'Kolom payment_minutes ada',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'platform_settings' AND column_name = 'payment_minutes'));

-- 15 menit tidak akan pernah cukup untuk seller membalas; kalau nilai lama itu
-- tertinggal, praktis setiap booking akan kedaluwarsa sebelum sempat dilihat.
SELECT t_check('setelan', 'Jendela persetujuan tidak lagi bernilai 15 menit',
    (SELECT approval_minutes FROM platform_settings) > 15,
    format('approval_minutes = %s', (SELECT approval_minutes FROM platform_settings)));

SELECT t_expect_error('setelan', 'approval_minutes di luar 1..1440 ditolak', $q$
  UPDATE platform_settings SET approval_minutes = 0
$q$, '23514');

SELECT t_expect_error('setelan', 'payment_minutes di luar 1..1440 ditolak', $q$
  UPDATE platform_settings SET payment_minutes = 5000
$q$, '23514');


-- =====================================================================
--  2. Index job pelepas hold mencakup kedua fase
-- =====================================================================

DO $$
DECLARE v_def text;
BEGIN
    SELECT indexdef INTO v_def FROM pg_indexes WHERE indexname = 'ix_bookings_hold_expiry';

    PERFORM t_check('index', 'ix_bookings_hold_expiry mencakup pending DAN confirmed',
        v_def LIKE '%pending%' AND v_def LIKE '%confirmed%',
        coalesce(left(v_def, 90), 'index tidak ditemukan'));

    PERFORM t_check('index', 'ix_bookings_hold_expiry mengecualikan hold yang sudah dikosongkan',
        v_def LIKE '%hold_expires_at IS NOT NULL%',
        'booking lunas mengosongkan hold_expires_at dan harus keluar dari kandidat');
END $$;


-- =====================================================================
--  3. Job pelepas hold menemukan yang benar, dan hanya yang benar
-- =====================================================================

INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0003-0003-0003-111111111111','seller','Seller','s@verify3.id','x'),
  ('22222222-0003-0003-0003-222222222222','renter','Renter','r@verify3.id','x');

INSERT INTO items (id, seller_id, title, category, price, price_unit) VALUES
  ('aaaaaaaa-0003-0003-0003-aaaaaaaaaaaa','11111111-0003-0003-0003-111111111111',
   'Barang Uji Hold','Uji',100000,'day');

-- Empat booking: pending kedaluwarsa, confirmed kedaluwarsa, pending belum jatuh
-- tempo, dan active yang tenggatnya lampau tapi tidak boleh disentuh.
INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units,
    total_rent, deposit_amount,
    platform_fee_rate, platform_fee_mode, platform_fee_amount, hold_expires_at)
VALUES
  ('b0000001-0003-0003-0003-bbbbbbbbbbbb','aaaaaaaa-0003-0003-0003-aaaaaaaaaaaa',
   '22222222-0003-0003-0003-222222222222',
   tstzrange('2027-01-01','2027-01-03','[)'), 'pending',
   100000,'day',2,200000,0,0,'deduct',0, now() - interval '1 hour'),

  ('b0000002-0003-0003-0003-bbbbbbbbbbbb','aaaaaaaa-0003-0003-0003-aaaaaaaaaaaa',
   '22222222-0003-0003-0003-222222222222',
   tstzrange('2027-02-01','2027-02-03','[)'), 'pending',
   100000,'day',2,200000,0,0,'deduct',0, now() - interval '1 hour'),

  ('b0000003-0003-0003-0003-bbbbbbbbbbbb','aaaaaaaa-0003-0003-0003-aaaaaaaaaaaa',
   '22222222-0003-0003-0003-222222222222',
   tstzrange('2027-03-01','2027-03-03','[)'), 'pending',
   100000,'day',2,200000,0,0,'deduct',0, now() + interval '1 hour'),

  ('b0000004-0003-0003-0003-bbbbbbbbbbbb','aaaaaaaa-0003-0003-0003-aaaaaaaaaaaa',
   '22222222-0003-0003-0003-222222222222',
   tstzrange('2027-04-01','2027-04-03','[)'), 'pending',
   100000,'day',2,200000,0,0,'deduct',0, now() - interval '1 hour');

-- Yang kedua naik ke confirmed (tenggatnya tetap lampau), yang keempat sampai active.
UPDATE bookings SET status = 'confirmed' WHERE id = 'b0000002-0003-0003-0003-bbbbbbbbbbbb';
UPDATE bookings SET status = 'confirmed' WHERE id = 'b0000004-0003-0003-0003-bbbbbbbbbbbb';
UPDATE bookings SET status = 'active'    WHERE id = 'b0000004-0003-0003-0003-bbbbbbbbbbbb';

-- Query yang sama persis dengan yang dipakai HoldSweeper.
UPDATE bookings
SET status = 'cancelled',
    cancelled_reason = CASE status
        WHEN 'pending' THEN 'Hold kedaluwarsa: seller tidak menyetujui sampai batas waktu.'
        ELSE 'Hold kedaluwarsa: pembayaran tidak diterima sampai batas waktu.'
    END,
    hold_expires_at = NULL
WHERE status IN ('pending', 'confirmed')
  AND hold_expires_at IS NOT NULL
  AND hold_expires_at < now();

SELECT t_check('sapuan', 'Pending kedaluwarsa dibatalkan',
    (SELECT status FROM bookings WHERE id = 'b0000001-0003-0003-0003-bbbbbbbbbbbb') = 'cancelled');

SELECT t_check('sapuan', 'Confirmed kedaluwarsa ikut dibatalkan',
    (SELECT status FROM bookings WHERE id = 'b0000002-0003-0003-0003-bbbbbbbbbbbb') = 'cancelled');

SELECT t_check('sapuan', 'Hold yang belum jatuh tempo tidak disentuh',
    (SELECT status FROM bookings WHERE id = 'b0000003-0003-0003-0003-bbbbbbbbbbbb') = 'pending');

SELECT t_check('sapuan', 'Booking active tidak ikut tersapu',
    (SELECT status FROM bookings WHERE id = 'b0000004-0003-0003-0003-bbbbbbbbbbbb') = 'active');

-- Alasannya harus menjelaskan fase mana yang gagal, bukan kalimat seragam.
SELECT t_check('sapuan', 'Alasan pembatalan membedakan fase persetujuan dan pembayaran',
    (SELECT cancelled_reason FROM bookings WHERE id = 'b0000001-0003-0003-0003-bbbbbbbbbbbb')
        LIKE '%menyetujui%'
    AND (SELECT cancelled_reason FROM bookings WHERE id = 'b0000002-0003-0003-0003-bbbbbbbbbbbb')
        LIKE '%pembayaran%');

SELECT t_check('sapuan', 'Tenggat dikosongkan supaya tidak tersapu dua kali',
    (SELECT count(*) FROM bookings
     WHERE id IN ('b0000001-0003-0003-0003-bbbbbbbbbbbb','b0000002-0003-0003-0003-bbbbbbbbbbbb')
       AND hold_expires_at IS NULL) = 2);

-- Slot yang dilepas benar-benar bebas: view kalender tidak lagi memuatnya.
SELECT t_check('sapuan', 'Slot yang dilepas hilang dari kalender ketersediaan',
    NOT EXISTS (SELECT 1 FROM item_blocked_ranges
                WHERE source_id = 'b0000001-0003-0003-0003-bbbbbbbbbbbb'));

-- Dan pembatalan massal itu tetap meninggalkan jejak audit, sama seperti
-- pembatalan satu-satu lewat API.
SELECT t_check('sapuan', 'Pembatalan oleh job tetap tercatat di riwayat status',
    EXISTS (SELECT 1 FROM booking_status_history
            WHERE booking_id = 'b0000001-0003-0003-0003-bbbbbbbbbbbb'
              AND from_status = 'pending' AND to_status = 'cancelled'));


-- =====================================================================
--  Hasil
-- =====================================================================
\echo ''
SELECT bagian, nama,
       CASE WHEN lulus THEN 'LULUS' ELSE '>>> GAGAL' END AS hasil,
       detail
FROM t_results ORDER BY seq;

\echo ''
SELECT count(*) FILTER (WHERE lulus)     AS lulus,
       count(*) FILTER (WHERE NOT lulus) AS gagal,
       count(*)                          AS total
FROM t_results;

DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n FROM t_results WHERE NOT lulus;
    IF n > 0 THEN
        RAISE EXCEPTION '% pemeriksaan GAGAL — migrasi 0003 belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

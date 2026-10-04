-- =====================================================================
--  verify_0010.sql — nomor sewa & nomor transaksi.
--
--  Yang dibuktikan di sini:
--    - kedua kolomnya ada, wajib isi, dan punya DEFAULT di database —
--      jadi tidak ada jalan masuk yang bisa melewatkannya,
--    - abjadnya benar-benar tanpa I, O, 0, 1 (inti alasan kolom ini
--      berbentuk begini: kodenya dibacakan lewat telepon),
--    - formatnya ditegakkan, termasuk menolak awalan milik tabel lain,
--    - keunikannya dijamin UNIQUE, bukan sekadar diusahakan fungsinya,
--    - baris lama kebagian saat backfill, dan kodenya tidak berubah lagi
--      setelah barisnya di-update.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0010.sql
-- =====================================================================

\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE t_results (
    seq serial, bagian text, nama text, lulus boolean, detail text
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
                SQLSTATE || ' — ' || left(SQLERRM, 55));
    END;
END $fn$;


-- =====================================================================
--  1. Kolom, constraint, dan DEFAULT
-- =====================================================================

SELECT t_check('kolom', 'bookings.reference ada, text, wajib isi',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'bookings' AND column_name = 'reference'
              AND data_type = 'text' AND is_nullable = 'NO'));

SELECT t_check('kolom', 'payments.reference ada, text, wajib isi',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'payments' AND column_name = 'reference'
              AND data_type = 'text' AND is_nullable = 'NO'));

SELECT t_check('kolom', 'uq_bookings_reference terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_bookings_reference'));

SELECT t_check('kolom', 'uq_payments_reference terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payments_reference'));

SELECT t_check('kolom', 'ck_bookings_reference_format terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_bookings_reference_format'));

SELECT t_check('kolom', 'ck_payments_reference_format terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payments_reference_format'));

-- DEFAULT-nya di database, bukan di C#. Ini yang membuat INSERT manual
-- lewat psql dan data demo tidak bisa melewatkannya.
SELECT t_check('kolom', 'bookings.reference punya DEFAULT gen_booking_reference()',
    (SELECT column_default LIKE '%gen_booking_reference%' FROM information_schema.columns
     WHERE table_name = 'bookings' AND column_name = 'reference'));

SELECT t_check('kolom', 'payments.reference punya DEFAULT gen_payment_reference()',
    (SELECT column_default LIKE '%gen_payment_reference%' FROM information_schema.columns
     WHERE table_name = 'payments' AND column_name = 'reference'));


-- =====================================================================
--  2. Abjadnya benar-benar tanpa huruf yang mudah tertukar
--
--  Inti alasan kolom ini berbentuk begini. Kode dibaca dari layar lalu
--  diketik ulang atau diucapkan lewat telepon; I/1 dan O/0 adalah tempat
--  kesalahan itu terjadi. 2.000 karakter cukup untuk membuat keempatnya
--  pasti muncul seandainya masih ada di dalam abjadnya.
-- =====================================================================

SELECT t_check('abjad', '2.000 karakter tidak pernah memuat I, O, 0, atau 1',
    sewa_reference_body(2000) ~ '^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]+$');

SELECT t_check('abjad', 'Panjangnya persis yang diminta',
    length(sewa_reference_body(6)) = 6 AND length(sewa_reference_body(20)) = 20);

SELECT t_check('abjad', 'Dua panggilan menghasilkan nilai berbeda (bukan konstan)',
    sewa_reference_body(20) <> sewa_reference_body(20));


-- =====================================================================
--  3. Backfill: baris yang sudah ada kebagian, dan semuanya unik
-- =====================================================================

SELECT t_check('backfill', 'Tidak ada sewa tanpa nomor',
    NOT EXISTS (SELECT 1 FROM bookings WHERE reference IS NULL));

SELECT t_check('backfill', 'Tidak ada transaksi tanpa nomor',
    NOT EXISTS (SELECT 1 FROM payments WHERE reference IS NULL));

SELECT t_check('backfill', 'Seluruh nomor sewa yang ada unik',
    (SELECT count(*) = count(DISTINCT reference) FROM bookings));

SELECT t_check('backfill', 'Seluruh nomor transaksi yang ada unik',
    (SELECT count(*) = count(DISTINCT reference) FROM payments));


-- Data uji: satu seller, satu renter, satu barang.
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0010-0010-0010-111111111111','seller','Seller Uji','s@verify10.id','x'),
  ('22222222-0010-0010-0010-222222222222','renter','Renter Uji','r@verify10.id','x');

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount)
VALUES ('33333333-0010-0010-0010-333333333333','11111111-0010-0010-0010-111111111111',
        'Kamera Verify 10','Elektronik', 150000, 'day', 500000);


-- =====================================================================
--  4. DEFAULT benar-benar mengisi tanpa diminta
--
--  Kolomnya sengaja TIDAK disebut di kedua INSERT ini.
-- =====================================================================

-- hold_expires_at ikut diisi: ck_bookings_pending_has_expiry (migrasi 0001)
-- menolak booking pending yang tidak menahan slotnya sampai kapan pun.
INSERT INTO bookings (id, item_id, renter_id, during,
                      price_snapshot, price_unit_snapshot, duration_units, total_rent,
                      hold_expires_at)
VALUES ('44444444-0010-0010-0010-444444444444',
        '33333333-0010-0010-0010-333333333333','22222222-0010-0010-0010-222222222222',
        tstzrange(now() + interval '10 days', now() + interval '12 days', '[)'),
        150000, 'day', 2, 300000, now() + interval '1 hour');

SELECT t_check('default', 'Sewa baru langsung punya nomor berawalan SW-',
    (SELECT reference ~ '^SW-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$'
     FROM bookings WHERE id = '44444444-0010-0010-0010-444444444444'),
    (SELECT reference FROM bookings WHERE id = '44444444-0010-0010-0010-444444444444'));

INSERT INTO payments (id, booking_id, kind, direction, amount, method, idempotency_key)
VALUES ('55555555-0010-0010-0010-555555555555','44444444-0010-0010-0010-444444444444',
        'rent_charge','in', 300000,'gateway_charge','verify10-rent');

SELECT t_check('default', 'Transaksi baru langsung punya nomor berawalan TR-',
    (SELECT reference ~ '^TR-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$'
     FROM payments WHERE id = '55555555-0010-0010-0010-555555555555'),
    (SELECT reference FROM payments WHERE id = '55555555-0010-0010-0010-555555555555'));


-- =====================================================================
--  5. Format ditegakkan
--
--  Termasuk menolak awalan milik tabel lain: "TR-" di kolom sewa berarti
--  nomor transaksi dan nomor sewa bisa tertukar saat disebut, dan justru
--  itulah yang hendak dicegah awalan tersebut.
-- =====================================================================

SELECT t_expect_error('format', 'Huruf ambigu (I, O, 0, 1) ditolak', $$
    UPDATE bookings SET reference = 'SW-IO01AB'
    WHERE id = '44444444-0010-0010-0010-444444444444';
$$, '23514');

SELECT t_expect_error('format', 'Huruf kecil ditolak', $$
    UPDATE bookings SET reference = 'SW-abcdef'
    WHERE id = '44444444-0010-0010-0010-444444444444';
$$, '23514');

SELECT t_expect_error('format', 'Awalan transaksi di kolom sewa ditolak', $$
    UPDATE bookings SET reference = 'TR-ABCDEF'
    WHERE id = '44444444-0010-0010-0010-444444444444';
$$, '23514');

SELECT t_expect_error('format', 'Awalan sewa di kolom transaksi ditolak', $$
    UPDATE payments SET reference = 'SW-ABCDEF'
    WHERE id = '55555555-0010-0010-0010-555555555555';
$$, '23514');

SELECT t_expect_error('format', 'Panjang selain enam ditolak', $$
    UPDATE bookings SET reference = 'SW-ABC'
    WHERE id = '44444444-0010-0010-0010-444444444444';
$$, '23514');

SELECT t_expect_error('format', 'Tanpa awalan ditolak', $$
    UPDATE bookings SET reference = '4F7K2Q'
    WHERE id = '44444444-0010-0010-0010-444444444444';
$$, '23514');


-- =====================================================================
--  6. Keunikan dijamin database, bukan diusahakan fungsinya
--
--  gen_*_reference() memang mengulang sampai sepuluh kali kalau kodenya
--  sudah terpakai, tapi dua transaksi bersamaan bisa sama-sama lolos
--  pemeriksaan EXISTS itu. Yang menolak yang kedua adalah index ini.
-- =====================================================================

SELECT t_expect_error('unik', 'Dua sewa dengan nomor sama ditolak', $$
    INSERT INTO bookings (item_id, renter_id, during, price_snapshot,
                          price_unit_snapshot, duration_units, total_rent,
                          hold_expires_at, reference)
    VALUES ('33333333-0010-0010-0010-333333333333','22222222-0010-0010-0010-222222222222',
            tstzrange(now() + interval '30 days', now() + interval '31 days', '[)'),
            150000, 'day', 1, 150000, now() + interval '1 hour',
            (SELECT reference FROM bookings WHERE id = '44444444-0010-0010-0010-444444444444'));
$$, '23505');

SELECT t_expect_error('unik', 'Dua transaksi dengan nomor sama ditolak', $$
    INSERT INTO payments (booking_id, kind, direction, amount, method,
                          idempotency_key, reference)
    VALUES ('44444444-0010-0010-0010-444444444444','deposit_charge','in', 500000,
            'gateway_charge','verify10-deposit',
            (SELECT reference FROM payments WHERE id = '55555555-0010-0010-0010-555555555555'));
$$, '23505');


-- =====================================================================
--  7. Nomornya tidak berubah lagi setelah barisnya bergerak
--
--  Nomor yang berubah saat status berubah bukan nomor — penyewa yang
--  menyebutkannya ke CS seminggu kemudian akan menyebut sesuatu yang
--  sudah tidak menunjuk ke mana-mana.
-- =====================================================================

CREATE TEMP TABLE t_sebelum ON COMMIT DROP AS
SELECT reference FROM bookings WHERE id = '44444444-0010-0010-0010-444444444444';

UPDATE bookings SET status = 'confirmed'
WHERE id = '44444444-0010-0010-0010-444444444444';

SELECT t_check('stabil', 'Nomor sewa tidak berubah saat statusnya berubah',
    (SELECT b.reference = s.reference
     FROM bookings b, t_sebelum s
     WHERE b.id = '44444444-0010-0010-0010-444444444444'));


-- =====================================================================
--  Hasil
-- =====================================================================
\echo ''
SELECT bagian, nama,
       CASE WHEN lulus THEN 'LULUS' ELSE '>>> GAGAL' END AS hasil, detail
FROM t_results ORDER BY seq;

\echo ''
SELECT count(*) FILTER (WHERE lulus) AS lulus,
       count(*) FILTER (WHERE NOT lulus) AS gagal,
       count(*) AS total
FROM t_results;

DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n FROM t_results WHERE NOT lulus;
    IF n > 0 THEN
        RAISE EXCEPTION '% pemeriksaan GAGAL — nomor sewa/transaksi belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

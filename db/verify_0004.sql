-- =====================================================================
--  verify_0004.sql — milestone 5.
--
--  Sebagian besar pengaman buku besar sudah dibuktikan verify_0001
--  (append-only, kind/direction, disbursement wajib rekening, idempotency
--  key ganda, webhook event ganda). Yang diperiksa di sini adalah yang
--  belum tersentuh dan yang baru: kolom instruksi bayar, konsistensi
--  settled_at, dan bahwa posisi keuangan sebuah booking memang bisa
--  dibaca sebagai AGREGASI baris — bukan sebagai kolom status.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0004.sql
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


-- ---------------------------------------------------------------------
-- Fixture: satu booking yang sudah dibayar penuh
-- ---------------------------------------------------------------------
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0004-0004-0004-111111111111','seller','Seller','s@verify4.id','x'),
  ('22222222-0004-0004-0004-222222222222','renter','Renter','r@verify4.id','x');

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount) VALUES
  ('aaaaaaaa-0004-0004-0004-aaaaaaaaaaaa','11111111-0004-0004-0004-111111111111',
   'Barang Uji Bayar','Uji',100000,'day',500000);

INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units,
    total_rent, deposit_amount,
    platform_fee_rate, platform_fee_mode, platform_fee_amount, hold_expires_at)
VALUES (
    'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','aaaaaaaa-0004-0004-0004-aaaaaaaaaaaa',
    '22222222-0004-0004-0004-222222222222',
    tstzrange('2028-01-01','2028-01-03','[)'), 'pending',
    100000,'day',2, 200000, 500000, 0.0500,'deduct',10000, now() + interval '1 hour');

INSERT INTO payout_accounts (id, user_id, kind, provider_code, account_number, account_holder, is_default)
VALUES ('cccccccc-0004-0004-0004-cccccccccccc','22222222-0004-0004-0004-222222222222',
        'bank','BCA','1234567890','Renter Uji', true);


-- =====================================================================
--  1. Kolom instruksi bayar (yang ditambahkan migrasi 0004)
-- =====================================================================

SELECT t_check('instruksi', 'Kolom gateway_instructions ada dan bertipe jsonb',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'payments' AND column_name = 'gateway_instructions'
              AND data_type = 'jsonb'));


-- =====================================================================
--  2. settled_at konsisten dengan status
--
--  Kalau baris bisa berstatus 'paid' tanpa waktu settle — atau sebaliknya —
--  laporan "uang masuk bulan ini" jadi tidak punya dasar untuk diurutkan.
-- =====================================================================

SELECT t_expect_error('settle', 'paid tanpa settled_at ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method,
                        counterparty_id, idempotency_key)
  VALUES ('bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','rent_charge','in',200000,'paid',
          'gateway_charge','22222222-0004-0004-0004-222222222222','uji-paid-tanpa-settle')
$q$, '23514');

SELECT t_expect_error('settle', 'pending dengan settled_at ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method,
                        counterparty_id, idempotency_key, settled_at)
  VALUES ('bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','rent_charge','in',200000,'pending',
          'gateway_charge','22222222-0004-0004-0004-222222222222','uji-pending-settle', now())
$q$, '23514');


-- =====================================================================
--  3. Posisi keuangan = agregasi baris
--
--  Keputusan terkunci #3: satu baris = satu pergerakan dana. Bagian ini
--  membuktikan bentuk itu benar-benar bisa menjawab pertanyaan uang tanpa
--  satu pun kolom status yang di-mutate.
-- =====================================================================

-- Uang masuk: sewa + deposit, satu transaksi gateway (gateway_ref sama).
INSERT INTO payments (id, booking_id, kind, direction, amount, status, method, channel,
                      counterparty_id, gateway_ref, idempotency_key, settled_at,
                      gateway_instructions)
VALUES
  ('d0000001-0004-0004-0004-dddddddddddd','bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb',
   'rent_charge','in',200000,'paid','gateway_charge','va_bca',
   '22222222-0004-0004-0004-222222222222','SEWA-bbbb-1','rent_charge:bbbb:1', now(),
   '{"VirtualAccountNumber":"8808123456"}'::jsonb),

  ('d0000002-0004-0004-0004-dddddddddddd','bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb',
   'deposit_charge','in',500000,'paid','gateway_charge','va_bca',
   '22222222-0004-0004-0004-222222222222','SEWA-bbbb-1','deposit_charge:bbbb:1', now(), NULL);

SELECT t_check('agregasi', 'Uang masuk terbaca dari agregasi baris',
    (SELECT coalesce(sum(amount), 0) FROM payments
     WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb'
       AND direction = 'in' AND status = 'paid') = 700000);

SELECT t_check('agregasi', 'Instruksi bayar hanya menempel di baris sewa',
    (SELECT count(*) FROM payments
     WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb'
       AND gateway_instructions IS NOT NULL) = 1);

SELECT t_check('agregasi', 'Kedua baris menumpang satu transaksi gateway yang sama',
    (SELECT count(DISTINCT gateway_ref) FROM payments
     WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb' AND direction = 'in') = 1);

-- Pembatalan: kewajiban refund penuh, VA tidak bisa dibalik ke sumber jadi disbursement.
INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                      counterparty_id, payout_account_id, parent_id, idempotency_key)
VALUES
  ('bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','rent_refund','out',200000,'pending',
   'disbursement','va_bca','22222222-0004-0004-0004-222222222222',
   'cccccccc-0004-0004-0004-cccccccccccc','d0000001-0004-0004-0004-dddddddddddd',
   'rent_refund:bbbb:d0000001'),

  ('bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','deposit_refund','out',500000,'pending',
   'disbursement','va_bca','22222222-0004-0004-0004-222222222222',
   'cccccccc-0004-0004-0004-cccccccccccc','d0000002-0004-0004-0004-dddddddddddd',
   'deposit_refund:bbbb:d0000002');

SELECT t_check('agregasi', 'Kewajiban refund terbaca penuh dari agregasi baris',
    (SELECT coalesce(sum(amount), 0) FROM payments
     WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb'
       AND direction = 'out' AND status = 'pending') = 700000);

-- Setelah refund dieksekusi, posisi bersih booking ini nol: tidak ada uang
-- orang yang tertinggal di platform.
UPDATE payments SET status = 'paid', settled_at = now()
WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb' AND direction = 'out';

SELECT t_check('agregasi', 'Setelah refund dieksekusi, posisi bersih booking nol',
    (SELECT coalesce(sum(CASE direction WHEN 'in' THEN amount ELSE -amount END), 0)
     FROM payments
     WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb'
       AND status = 'paid' AND direction IN ('in','out')) = 0);

-- Refund menunjuk balik ke baris tagihan asalnya, jadi jejak uangnya bisa ditelusuri.
SELECT t_check('agregasi', 'Setiap refund menunjuk baris tagihan asalnya',
    NOT EXISTS (SELECT 1 FROM payments
                WHERE booking_id = 'bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb'
                  AND direction = 'out' AND parent_id IS NULL));

-- Dan kunci idempotensi tetap menahan pembuatan kewajiban ganda.
SELECT t_expect_error('agregasi', 'Kewajiban refund ganda ditolak kunci idempotensi', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                        counterparty_id, payout_account_id, idempotency_key)
  VALUES ('bbbbbbbb-0004-0004-0004-bbbbbbbbbbbb','rent_refund','out',200000,'pending',
          'disbursement','va_bca','22222222-0004-0004-0004-222222222222',
          'cccccccc-0004-0004-0004-cccccccccccc','rent_refund:bbbb:d0000001')
$q$, '23505');


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — milestone 5 belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

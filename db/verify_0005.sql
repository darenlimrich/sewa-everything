-- =====================================================================
--  verify_0005.sql — milestone 6 (return + refund deposit + sengketa).
--
--  Yang dibuktikan di sini adalah jaring pengaman yang BARU di milestone
--  ini, dan yang tidak terbaca sendiri dari DDL-nya:
--    - kolom return_window_days beserta batasnya + index penyelesai,
--    - constraint tabel disputes (satu per booking, resolved konsisten),
--    - jenis alokasi internal baru (platform_fee, deposit_forfeit) memang
--      terkunci ke direction 'internal',
--    - dan yang terpenting: bentuk buku besar benar-benar bisa menutup
--      sebuah sewa dengan SEIMBANG — masuk - keluar = komisi, titik —
--      baik tanpa potongan maupun dengan potongan deposit lewat sengketa.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0005.sql
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
--  1. platform_settings.return_window_days
-- =====================================================================

SELECT t_check('return_window', 'Kolom return_window_days ada dan integer',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'platform_settings' AND column_name = 'return_window_days'
              AND data_type = 'integer'));

SELECT t_check('return_window', 'Bawaannya 3 hari',
    (SELECT return_window_days FROM platform_settings) = 3);

SELECT t_expect_error('return_window', '0 hari ditolak (batas bawah 1)',
    $q$ UPDATE platform_settings SET return_window_days = 0 $q$, '23514');

SELECT t_expect_error('return_window', '31 hari ditolak (batas atas 30)',
    $q$ UPDATE platform_settings SET return_window_days = 31 $q$, '23514');

-- Index parsial untuk job penyelesai: hanya baris active yang jadi kandidat.
SELECT t_check('return_window', 'Index ix_bookings_return_due ada, parsial ke status active',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE indexname = 'ix_bookings_return_due'
              AND indexdef ILIKE '%WHERE %status% = ''active''%'));


-- =====================================================================
--  2. Tabel disputes (keputusan terkunci #11)
-- =====================================================================

INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0005-0005-0005-111111111111','seller','Seller','s@verify5.id','x'),
  ('22222222-0005-0005-0005-222222222222','renter','Renter','r@verify5.id','x'),
  ('33333333-0005-0005-0005-333333333333','admin','Admin','a@verify5.id','x');

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount) VALUES
  ('aaaaaaaa-0005-0005-0005-aaaaaaaaaaaa','11111111-0005-0005-0005-111111111111',
   'Barang Uji Sengketa','Uji',100000,'day',500000);

-- Rekening tujuan uang keluar: seller (payout) dan renter (refund disbursement).
INSERT INTO payout_accounts (id, user_id, kind, provider_code, account_number, account_holder, is_default)
VALUES
  ('55555555-0005-0005-0005-555555555555','11111111-0005-0005-0005-111111111111',
   'bank','BCA','1000000001','Seller Uji', true),
  ('66666666-0005-0005-0005-666666666666','22222222-0005-0005-0005-222222222222',
   'bank','BNI','2000000002','Renter Uji', true);

-- Dua booking (satu untuk /return, satu untuk sengketa), keduanya sudah dibayar penuh.
INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units,
    total_rent, deposit_amount,
    platform_fee_rate, platform_fee_mode, platform_fee_amount)
VALUES
  ('b0000001-0005-0005-0005-bbbbbbbbbbbb','aaaaaaaa-0005-0005-0005-aaaaaaaaaaaa',
   '22222222-0005-0005-0005-222222222222',
   tstzrange('2028-02-01','2028-02-03','[)'), 'active',
   100000,'day',2, 200000, 500000, 0.0500,'deduct',10000),
  ('b0000002-0005-0005-0005-bbbbbbbbbbbb','aaaaaaaa-0005-0005-0005-aaaaaaaaaaaa',
   '22222222-0005-0005-0005-222222222222',
   tstzrange('2028-03-01','2028-03-03','[)'), 'active',
   100000,'day',2, 200000, 500000, 0.0500,'deduct',10000);

-- Uang masuk untuk kedua booking: sewa (va_bca) + deposit (va_bca).
INSERT INTO payments (id, booking_id, kind, direction, amount, status, method, channel,
                      counterparty_id, gateway_ref, idempotency_key, settled_at)
VALUES
  ('d1000001-0005-0005-0005-dddddddddddd','b0000001-0005-0005-0005-bbbbbbbbbbbb',
   'rent_charge','in',200000,'paid','gateway_charge','va_bca',
   '22222222-0005-0005-0005-222222222222','SEWA-b1','rent_charge:b1:1', now()),
  ('d1000002-0005-0005-0005-dddddddddddd','b0000001-0005-0005-0005-bbbbbbbbbbbb',
   'deposit_charge','in',500000,'paid','gateway_charge','va_bca',
   '22222222-0005-0005-0005-222222222222','SEWA-b1','deposit_charge:b1:1', now()),
  ('d2000001-0005-0005-0005-dddddddddddd','b0000002-0005-0005-0005-bbbbbbbbbbbb',
   'rent_charge','in',200000,'paid','gateway_charge','va_bca',
   '22222222-0005-0005-0005-222222222222','SEWA-b2','rent_charge:b2:1', now()),
  ('d2000002-0005-0005-0005-dddddddddddd','b0000002-0005-0005-0005-bbbbbbbbbbbb',
   'deposit_charge','in',500000,'paid','gateway_charge','va_bca',
   '22222222-0005-0005-0005-222222222222','SEWA-b2','deposit_charge:b2:1', now());

-- Sengketa untuk booking kedua.
INSERT INTO disputes (id, booking_id, raised_by, reason)
VALUES ('e0000001-0005-0005-0005-eeeeeeeeeeee','b0000002-0005-0005-0005-bbbbbbbbbbbb',
        '11111111-0005-0005-0005-111111111111','Barang kembali lecet di beberapa sisi.');

SELECT t_check('disputes', 'Sengketa tersimpan berstatus open',
    (SELECT status FROM disputes WHERE id = 'e0000001-0005-0005-0005-eeeeeeeeeeee') = 'open');

-- Satu sengketa per booking.
SELECT t_expect_error('disputes', 'Sengketa kedua untuk booking yang sama ditolak (UNIQUE)', $q$
  INSERT INTO disputes (booking_id, raised_by, reason)
  VALUES ('b0000002-0005-0005-0005-bbbbbbbbbbbb','22222222-0005-0005-0005-222222222222','lagi')
$q$, '23505');

-- resolved wajib membawa resolusi + pemutus + waktu putus, ketiganya sekaligus.
SELECT t_expect_error('disputes', 'resolved tanpa resolusi ditolak', $q$
  UPDATE disputes SET status = 'resolved' WHERE id = 'e0000001-0005-0005-0005-eeeeeeeeeeee'
$q$, '23514');

SELECT t_expect_error('disputes', 'status sengketa di luar open/resolved ditolak', $q$
  UPDATE disputes SET status = 'ditutup' WHERE id = 'e0000001-0005-0005-0005-eeeeeeeeeeee'
$q$, '23514');


-- =====================================================================
--  3. Jenis alokasi internal baru terkunci ke direction 'internal'
--
--  platform_fee dan deposit_forfeit adalah pembukuan, bukan uang yang
--  keluar. ck_payments_kind_direction harus menolak kalau ada yang
--  mencoba memberinya arah 'out' atau 'in'.
-- =====================================================================

SELECT t_expect_error('internal', 'platform_fee berarah out ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method,
                        idempotency_key)
  VALUES ('b0000001-0005-0005-0005-bbbbbbbbbbbb','platform_fee','out',10000,'pending',
          'disbursement','uji-fee-out')
$q$, '23514');

SELECT t_expect_error('internal', 'deposit_forfeit berarah in ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method,
                        idempotency_key)
  VALUES ('b0000001-0005-0005-0005-bbbbbbbbbbbb','deposit_forfeit','in',150000,'paid',
          'gateway_charge','uji-forfeit-in')
$q$, '23514');


-- =====================================================================
--  4. Penyelesaian TANPA potongan (/return): deposit kembali penuh
--
--  fee            = 10000
--  deposit_refund = 500000 - 0        = 500000  (keluar → renter)
--  seller_payout  = 200000 - 10000+0  = 190000  (keluar → seller)
--  platform_fee   = 10000             (internal)
-- =====================================================================

INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                      counterparty_id, payout_account_id, parent_id, idempotency_key, settled_at)
VALUES
  -- alokasi internal langsung final
  ('b0000001-0005-0005-0005-bbbbbbbbbbbb','platform_fee','internal',10000,'paid','internal',
   NULL, NULL, NULL, NULL, 'platform_fee:b1', now()),
  -- payout ke seller: wajib disbursement dengan rekening tujuan
  ('b0000001-0005-0005-0005-bbbbbbbbbbbb','seller_payout','out',190000,'pending','disbursement',
   NULL,'11111111-0005-0005-0005-111111111111','55555555-0005-0005-0005-555555555555',
   NULL,'seller_payout:b1', NULL),
  -- refund deposit penuh ke renter: va_bca tidak bisa dibalik, jadi disbursement
  ('b0000001-0005-0005-0005-bbbbbbbbbbbb','deposit_refund','out',500000,'pending','disbursement',
   'va_bca','22222222-0005-0005-0005-222222222222','66666666-0005-0005-0005-666666666666',
   'd1000002-0005-0005-0005-dddddddddddd','deposit_refund:b1:d1000002', NULL);

SELECT t_check('return', 'Tanpa potongan, deposit kembali penuh ke renter',
    (SELECT amount FROM payments
     WHERE booking_id = 'b0000001-0005-0005-0005-bbbbbbbbbbbb' AND kind = 'deposit_refund') = 500000);

SELECT t_check('return', 'Buku besar seimbang: masuk - keluar = komisi',
    (SELECT sum(CASE direction WHEN 'in' THEN amount WHEN 'out' THEN -amount ELSE 0 END)
     FROM payments WHERE booking_id = 'b0000001-0005-0005-0005-bbbbbbbbbbbb') = 10000);

SELECT t_check('return', 'Platform menahan tepat sebesar komisi (baris platform_fee)',
    (SELECT amount FROM payments
     WHERE booking_id = 'b0000001-0005-0005-0005-bbbbbbbbbbbb' AND kind = 'platform_fee') = 10000);

-- Penyelesaian ganda mustahil: kunci payout-nya UNIQUE.
SELECT t_expect_error('return', 'Payout kedua untuk booking yang sama ditolak (idempotensi)', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, status, method,
                        counterparty_id, payout_account_id, idempotency_key)
  VALUES ('b0000001-0005-0005-0005-bbbbbbbbbbbb','seller_payout','out',190000,'pending',
          'disbursement','11111111-0005-0005-0005-111111111111',
          '55555555-0005-0005-0005-555555555555','seller_payout:b1')
$q$, '23505');


-- =====================================================================
--  5. Penyelesaian DENGAN potongan (sengketa): potongan 150000
--
--  deposit_refund = 500000 - 150000       = 350000  (keluar → renter)
--  seller_payout  = 200000 - 10000+150000 = 340000  (keluar → seller)
--  platform_fee   = 10000                 (internal)
--  deposit_forfeit= 150000                (internal)
-- =====================================================================

INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                      counterparty_id, payout_account_id, parent_id, idempotency_key, settled_at)
VALUES
  ('b0000002-0005-0005-0005-bbbbbbbbbbbb','platform_fee','internal',10000,'paid','internal',
   NULL, NULL, NULL, NULL, 'platform_fee:b2', now()),
  ('b0000002-0005-0005-0005-bbbbbbbbbbbb','deposit_forfeit','internal',150000,'paid','internal',
   NULL, NULL, NULL, NULL, 'deposit_forfeit:b2', now()),
  ('b0000002-0005-0005-0005-bbbbbbbbbbbb','seller_payout','out',340000,'pending','disbursement',
   NULL,'11111111-0005-0005-0005-111111111111','55555555-0005-0005-0005-555555555555',
   NULL,'seller_payout:b2', NULL),
  ('b0000002-0005-0005-0005-bbbbbbbbbbbb','deposit_refund','out',350000,'pending','disbursement',
   'va_bca','22222222-0005-0005-0005-222222222222','66666666-0005-0005-0005-666666666666',
   'd2000002-0005-0005-0005-dddddddddddd','deposit_refund:b2:d2000002', NULL);

-- Putuskan sengketanya (konsisten: resolusi + pemutus + waktu sekaligus).
UPDATE disputes SET status = 'resolved',
    resolution = 'Potongan Rp 150.000 untuk lecet; sisa deposit dikembalikan.',
    resolved_by = '33333333-0005-0005-0005-333333333333', resolved_at = now()
WHERE id = 'e0000001-0005-0005-0005-eeeeeeeeeeee';

SELECT t_check('sengketa', 'Sengketa jadi resolved setelah diputus',
    (SELECT status FROM disputes WHERE id = 'e0000001-0005-0005-0005-eeeeeeeeeeee') = 'resolved');

SELECT t_check('sengketa', 'Potongan + sisa refund = deposit yang tertagih',
    (SELECT sum(amount) FROM payments
     WHERE booking_id = 'b0000002-0005-0005-0005-bbbbbbbbbbbb'
       AND kind IN ('deposit_refund','deposit_forfeit')) = 500000);

SELECT t_check('sengketa', 'Seller menerima haknya + bagian deposit yang dipotong',
    (SELECT amount FROM payments
     WHERE booking_id = 'b0000002-0005-0005-0005-bbbbbbbbbbbb' AND kind = 'seller_payout') = 340000);

-- Buku besar tetap seimbang meski deposit dipotong: forfeit itu alokasi internal,
-- bukan uang tambahan yang masuk atau keluar. Masuk - keluar tetap = komisi.
SELECT t_check('sengketa', 'Dengan potongan pun, masuk - keluar tetap = komisi',
    (SELECT sum(CASE direction WHEN 'in' THEN amount WHEN 'out' THEN -amount ELSE 0 END)
     FROM payments WHERE booking_id = 'b0000002-0005-0005-0005-bbbbbbbbbbbb') = 10000);


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — milestone 6 belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

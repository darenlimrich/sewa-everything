-- =====================================================================
--  verify_0001.sql — membuktikan jaring pengaman 0001_init.sql benar-benar
--  menolak apa yang harus ditolak, dan menerima apa yang harus diterima.
--
--  Dijalankan seluruhnya di dalam satu transaksi lalu ROLLBACK:
--  database tidak meninggalkan satu baris pun setelah skrip selesai.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0001.sql
-- =====================================================================

\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE t_results (
    seq    serial,
    aturan text,
    nama   text,
    lulus  boolean,
    detail text
) ON COMMIT DROP;

CREATE OR REPLACE FUNCTION t_expect_error(
    p_aturan text, p_nama text, p_sql text, p_errcode text DEFAULT NULL
) RETURNS void LANGUAGE plpgsql AS $fn$
BEGIN
    BEGIN
        EXECUTE p_sql;
        INSERT INTO t_results (aturan, nama, lulus, detail)
        VALUES (p_aturan, p_nama, false, 'HARUSNYA DITOLAK — tapi statement berhasil');
    EXCEPTION WHEN OTHERS THEN
        IF p_errcode IS NULL OR SQLSTATE = p_errcode THEN
            INSERT INTO t_results (aturan, nama, lulus, detail)
            VALUES (p_aturan, p_nama, true, SQLSTATE || ' — ' || left(SQLERRM, 70));
        ELSE
            INSERT INTO t_results (aturan, nama, lulus, detail)
            VALUES (p_aturan, p_nama, false,
                    'ditolak tapi errcode ' || SQLSTATE || ', diharapkan ' || p_errcode);
        END IF;
    END;
END $fn$;

CREATE OR REPLACE FUNCTION t_expect_ok(
    p_aturan text, p_nama text, p_sql text
) RETURNS void LANGUAGE plpgsql AS $fn$
BEGIN
    BEGIN
        EXECUTE p_sql;
        INSERT INTO t_results (aturan, nama, lulus, detail)
        VALUES (p_aturan, p_nama, true, 'diterima');
    EXCEPTION WHEN OTHERS THEN
        INSERT INTO t_results (aturan, nama, lulus, detail)
        VALUES (p_aturan, p_nama, false,
                'HARUSNYA DITERIMA — ' || SQLSTATE || ' ' || left(SQLERRM, 70));
    END;
END $fn$;


-- ---------------------------------------------------------------------
-- Fixture
-- ---------------------------------------------------------------------
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-1111-1111-1111-111111111111','seller','Seller Satu','seller1@test.id','x'),
  ('22222222-2222-2222-2222-222222222222','renter','Renter Satu','renter1@test.id','x'),
  ('33333333-3333-3333-3333-333333333333','renter','Renter Dua', 'renter2@test.id','x'),
  ('44444444-4444-4444-4444-444444444444','admin', 'Admin',      'admin@test.id',  'x');

INSERT INTO items (id, seller_id, title, category, description,
                   price, price_unit, deposit_amount) VALUES
  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111',
   'Kamera Mirrorless','Elektronik','Body + lensa kit',
   100000.00,'day', 500000.00),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','11111111-1111-1111-1111-111111111111',
   'Tenda Dome 4 Orang','Outdoor',NULL,
   75000.00,'day', 200000.00);

-- Booking acuan: 1–4 Agustus 2026, 3 hari x Rp100.000
INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units,
    total_rent, deposit_amount,
    platform_fee_rate, platform_fee_mode, platform_fee_amount,
    hold_expires_at
) VALUES (
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    '22222222-2222-2222-2222-222222222222',
    tstzrange('2026-08-01 10:00+07','2026-08-04 10:00+07','[)'),
    'pending',
    100000.00,'day',3,
    300000.00, 500000.00,
    0.0500,'deduct', 15000.00,
    now() + interval '15 minutes'
);


-- =====================================================================
--  ATURAN 4.2 — anti double-booking
-- =====================================================================
SELECT t_expect_error('4.2','Tumpang tindih penuh ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-08-01 10:00+07','2026-08-04 10:00+07','[)'),'pending',
      100000.00,'day',3,300000.00,500000.00,0.0500,'deduct',15000.00, now()+interval '15 min')
$q$, '23P01');

SELECT t_expect_error('4.2','Tumpang tindih sebagian ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-08-03 10:00+07','2026-08-06 10:00+07','[)'),'pending',
      100000.00,'day',3,300000.00,500000.00,0.0500,'deduct',15000.00, now()+interval '15 min')
$q$, '23P01');

SELECT t_expect_ok('4.2','Back-to-back diterima (bound half-open [) )', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-08-04 10:00+07','2026-08-06 10:00+07','[)'),'pending',
      75000.00,'day',2,150000.00,500000.00,0.0500,'deduct',7500.00, now()+interval '15 min')
$q$);

SELECT t_expect_ok('4.2','Slot sama untuk barang berbeda diterima', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-08-01 10:00+07','2026-08-04 10:00+07','[)'),'pending',
      75000.00,'day',3,225000.00,200000.00,0.0500,'deduct',11250.00, now()+interval '15 min')
$q$);

SELECT t_expect_error('4.2','Bound non-kanonik [] ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-09-01 10:00+07','2026-09-04 10:00+07','[]'),'pending',
      75000.00,'day',3,225000.00,200000.00,0.0500,'deduct',11250.00, now()+interval '15 min')
$q$, '23514');

SELECT t_expect_error('4.2','Rentang tak berhingga ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-09-01 10:00+07', NULL,'[)'),'pending',
      75000.00,'day',3,225000.00,200000.00,0.0500,'deduct',11250.00, now()+interval '15 min')
$q$, '23514');

-- Slot dibebaskan lagi begitu hold dibatalkan
SELECT t_expect_ok('4.2','Hold dibatalkan -> slot bebas kembali', $q$
  WITH batal AS (
    UPDATE bookings SET status = 'cancelled', cancelled_reason = 'hold kedaluwarsa'
    WHERE id = 'cccccccc-cccc-cccc-cccc-cccccccccccc' RETURNING 1
  )
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  SELECT 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','33333333-3333-3333-3333-333333333333',
      tstzrange('2026-08-01 10:00+07','2026-08-04 10:00+07','[)'),'pending',
      100000.00,'day',3,300000.00,500000.00,0.0500,'deduct',15000.00, now()+interval '15 min'
  FROM batal
$q$);


-- =====================================================================
--  Booking vs blackout seller (dua arah)
-- =====================================================================
INSERT INTO item_blackouts (item_id, during, reason)
VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        tstzrange('2026-10-01 00:00+07','2026-10-10 00:00+07','[)'), 'servis');

SELECT t_expect_error('blackout','Booking menabrak blackout ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222',
      tstzrange('2026-10-05 10:00+07','2026-10-07 10:00+07','[)'),'pending',
      75000.00,'day',2,150000.00,200000.00,0.0500,'deduct',7500.00, now()+interval '15 min')
$q$, '23P01');

SELECT t_expect_error('blackout','Blackout menabrak booking aktif ditolak', $q$
  INSERT INTO item_blackouts (item_id, during, reason)
  VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
          tstzrange('2026-08-02 00:00+07','2026-08-03 00:00+07','[)'), 'mendadak dipakai')
$q$, '23P01');

SELECT t_expect_error('blackout','Blackout tumpang tindih blackout ditolak', $q$
  INSERT INTO item_blackouts (item_id, during)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
          tstzrange('2026-10-05 00:00+07','2026-10-12 00:00+07','[)'))
$q$, '23P01');


-- =====================================================================
--  ATURAN 4.3 — state machine
-- =====================================================================
-- booking uji terpisah supaya tidak mengganggu slot di atas
INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units, total_rent,
    deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
    hold_expires_at
) VALUES (
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    '22222222-2222-2222-2222-222222222222',
    tstzrange('2026-12-01 10:00+07','2026-12-03 10:00+07','[)'),
    'pending', 75000.00,'day',2,150000.00,200000.00,
    0.0500,'deduct',7500.00, now() + interval '15 minutes'
);

SELECT t_expect_error('4.3','pending -> completed ditolak',
  $q$ UPDATE bookings SET status='completed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');
SELECT t_expect_error('4.3','pending -> active ditolak',
  $q$ UPDATE bookings SET status='active' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');
SELECT t_expect_error('4.3','pending -> disputed ditolak',
  $q$ UPDATE bookings SET status='disputed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');

SELECT t_expect_ok('4.3','pending -> confirmed diterima',
  $q$ UPDATE bookings SET status='confirmed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$);
SELECT t_expect_error('4.3','confirmed -> pending ditolak (mundur)',
  $q$ UPDATE bookings SET status='pending' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');
SELECT t_expect_error('4.3','confirmed -> completed ditolak (lompat)',
  $q$ UPDATE bookings SET status='completed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');

SELECT t_expect_ok('4.3','confirmed -> active diterima',
  $q$ UPDATE bookings SET status='active' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$);
SELECT t_expect_error('4.3','active -> cancelled ditolak',
  $q$ UPDATE bookings SET status='cancelled' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');

SELECT t_expect_ok('4.3','active -> disputed diterima',
  $q$ UPDATE bookings SET status='disputed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$);
SELECT t_expect_ok('4.3','disputed -> completed diterima',
  $q$ UPDATE bookings SET status='completed' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$);
SELECT t_expect_error('4.3','completed -> active ditolak (status final)',
  $q$ UPDATE bookings SET status='active' WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');


-- =====================================================================
--  ATURAN 4.1 — server satu-satunya sumber kebenaran angka
-- =====================================================================
SELECT t_expect_error('4.1','total_rent palsu ditolak database', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222',
      tstzrange('2027-01-01 10:00+07','2027-01-04 10:00+07','[)'),'pending',
      100000.00,'day',3, 1.00, 200000.00,0.0500,'deduct',0.05, now()+interval '15 min')
$q$, '23514');

SELECT t_expect_error('4.1','komisi tidak sesuai rate ditolak', $q$
  INSERT INTO bookings (item_id, renter_id, during, status,
      price_snapshot, price_unit_snapshot, duration_units, total_rent,
      deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
      hold_expires_at)
  VALUES ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','22222222-2222-2222-2222-222222222222',
      tstzrange('2027-01-01 10:00+07','2027-01-04 10:00+07','[)'),'pending',
      100000.00,'day',3, 300000.00, 200000.00, 0.0500,'deduct', 0.00, now()+interval '15 min')
$q$, '23514');

SELECT t_expect_error('4.1','syarat booking diubah setelah dibuat ditolak',
  $q$ UPDATE bookings SET total_rent = 1.00 WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');
SELECT t_expect_error('4.1','rentang waktu digeser setelah dibuat ditolak',
  $q$ UPDATE bookings SET during = tstzrange('2027-05-01 10:00+07','2027-05-09 10:00+07','[)')
      WHERE id='dddddddd-dddd-dddd-dddd-dddddddddddd' $q$, '23514');

-- kolom turunan dihitung database
DO $$
DECLARE r record;
BEGIN
    SELECT renter_total, seller_gross INTO r FROM bookings
    WHERE id = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
    -- total_rent 150.000 + deposit 200.000, mode deduct -> renter bayar 350.000
    -- seller_gross = 150.000 - 7.500 = 142.500
    INSERT INTO t_results (aturan, nama, lulus, detail) VALUES ('4.1',
      'renter_total & seller_gross dihitung database',
      r.renter_total = 350000.00 AND r.seller_gross = 142500.00,
      format('renter_total=%s seller_gross=%s', r.renter_total, r.seller_gross));
END $$;


-- =====================================================================
--  ATURAN 4.4 — idempotensi & audit uang
-- =====================================================================
INSERT INTO payments (booking_id, kind, direction, amount, method, channel,
                      counterparty_id, idempotency_key, status)
VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','rent_charge','in',150000.00,
        'gateway_charge','va_bca','22222222-2222-2222-2222-222222222222',
        'bk-dddd:rent','pending');

SELECT t_expect_error('4.4','idempotency_key ganda ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, method, channel,
                        counterparty_id, idempotency_key, status)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','rent_charge','in',150000.00,
          'gateway_charge','va_bca','22222222-2222-2222-2222-222222222222',
          'bk-dddd:rent','pending')
$q$, '23505');

SELECT t_expect_error('4.4','baris payments tidak boleh dihapus',
  $q$ DELETE FROM payments WHERE idempotency_key = 'bk-dddd:rent' $q$, '23514');

SELECT t_expect_error('4.4','nominal payments tidak boleh diubah',
  $q$ UPDATE payments SET amount = 1.00 WHERE idempotency_key = 'bk-dddd:rent' $q$, '23514');

SELECT t_expect_ok('4.4','pending -> paid diterima',
  $q$ UPDATE payments SET status='paid', settled_at=now(), gateway_ref='MT-123'
      WHERE idempotency_key = 'bk-dddd:rent' $q$);

SELECT t_expect_error('4.4','status final tidak boleh diubah lagi',
  $q$ UPDATE payments SET status='failed' WHERE idempotency_key = 'bk-dddd:rent' $q$, '23514');

SELECT t_expect_error('4.4','kind/direction tidak konsisten ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, method,
                        counterparty_id, idempotency_key)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','rent_charge','out',1000.00,
          'disbursement','22222222-2222-2222-2222-222222222222','bk-dddd:salah-arah')
$q$, '23514');

SELECT t_expect_error('4.4','disbursement tanpa rekening tujuan ditolak', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, method,
                        counterparty_id, idempotency_key)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','deposit_refund','out',200000.00,
          'disbursement','22222222-2222-2222-2222-222222222222','bk-dddd:refund-tanpa-rek')
$q$, '23514');

-- Refund ke sumber (kartu/e-wallet) justru TIDAK boleh punya rekening tujuan
SELECT t_expect_ok('4.4','refund reversal ke sumber diterima', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, method, channel,
                        counterparty_id, idempotency_key)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','deposit_refund','out',200000.00,
          'gateway_reversal','gopay','22222222-2222-2222-2222-222222222222',
          'bk-dddd:refund-1')
$q$);

-- Refund lewat transfer (asal bayar VA/QRIS/tunai) butuh rekening terdaftar
INSERT INTO payout_accounts (id, user_id, kind, provider_code, account_number, account_holder, is_default)
VALUES ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee','22222222-2222-2222-2222-222222222222',
        'bank','BCA','1234567890','Renter Satu', true);

SELECT t_expect_ok('4.4','refund disbursement ke rekening terdaftar diterima', $q$
  INSERT INTO payments (booking_id, kind, direction, amount, method, channel,
                        counterparty_id, payout_account_id, idempotency_key)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','deposit_refund','out',200000.00,
          'disbursement','va_bca','22222222-2222-2222-2222-222222222222',
          'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee','bk-dddd:refund-2')
$q$);

-- Idempotensi webhook masuk
INSERT INTO webhook_events (provider, event_id, payload)
VALUES ('midtrans','notif-abc-001','{"order_id":"bk-dddd"}'::jsonb);

SELECT t_expect_error('4.4','webhook event_id ganda ditolak', $q$
  INSERT INTO webhook_events (provider, event_id, payload)
  VALUES ('midtrans','notif-abc-001','{"order_id":"bk-dddd"}'::jsonb)
$q$, '23505');


-- =====================================================================
--  Integritas lain
-- =====================================================================
SELECT t_expect_error('lain','Barang milik non-seller ditolak', $q$
  INSERT INTO items (seller_id, title, category, price, price_unit)
  VALUES ('22222222-2222-2222-2222-222222222222','Barang Ilegal','Lain',1000.00,'day')
$q$, '23514');

SELECT t_expect_error('lain','Email duplikat beda kapital ditolak', $q$
  INSERT INTO users (role, name, email, password_hash)
  VALUES ('renter','Kembar','RENTER1@TEST.ID','x')
$q$, '23505');

SELECT t_expect_error('lain','Rating di luar 1-5 ditolak', $q$
  INSERT INTO reviews (booking_id, rating) VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd', 6)
$q$, '23514');

SELECT t_expect_error('lain','Dua review untuk satu booking ditolak', $q$
  WITH satu AS (
    INSERT INTO reviews (booking_id, rating, comment)
    VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd', 5, 'mantap') RETURNING 1
  )
  INSERT INTO reviews (booking_id, rating) SELECT 'dddddddd-dddd-dddd-dddd-dddddddddddd', 4 FROM satu
$q$, '23505');

SELECT t_expect_error('lain','Sengketa resolved tanpa resolusi ditolak', $q$
  INSERT INTO disputes (booking_id, raised_by, reason, status)
  VALUES ('dddddddd-dddd-dddd-dddd-dddddddddddd','22222222-2222-2222-2222-222222222222',
          'barang tidak sesuai','resolved')
$q$, '23514');

SELECT t_expect_error('lain','Baris kedua platform_settings ditolak (singleton)', $q$
  INSERT INTO platform_settings (id, commission_rate) VALUES (false, 0.10)
$q$, '23514');

SELECT t_expect_error('lain','Harga nol atau negatif ditolak', $q$
  INSERT INTO items (seller_id, title, category, price, price_unit)
  VALUES ('11111111-1111-1111-1111-111111111111','Gratisan','Lain', 0, 'day')
$q$, '23514');

-- Jejak audit terisi otomatis: pending -> confirmed -> active -> disputed -> completed
DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n FROM booking_status_history
    WHERE booking_id = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
    INSERT INTO t_results (aturan, nama, lulus, detail) VALUES ('4.4',
      'Riwayat status booking tercatat otomatis', n = 5,
      format('%s baris riwayat (diharapkan 5)', n));
END $$;


-- =====================================================================
--  Hasil
-- =====================================================================
\echo ''
SELECT aturan, nama,
       CASE WHEN lulus THEN 'LULUS' ELSE '>>> GAGAL' END AS hasil,
       detail
FROM t_results ORDER BY seq;

\echo ''
SELECT count(*) FILTER (WHERE lulus)       AS lulus,
       count(*) FILTER (WHERE NOT lulus)   AS gagal,
       count(*)                            AS total
FROM t_results;

DO $$
DECLARE n integer;
BEGIN
    SELECT count(*) INTO n FROM t_results WHERE NOT lulus;
    IF n > 0 THEN
        RAISE EXCEPTION '% pemeriksaan GAGAL — skema belum memenuhi Bagian 4', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

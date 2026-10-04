-- =====================================================================
--  verify_0016.sql — alamat pengiriman + biaya antar.
--
--  Yang dibuktikan di sini:
--    - buku alamat berbentuk benar, dan hanya boleh punya SATU alamat
--      utama per pengguna,
--    - alamat ikut terhapus saat akunnya dihapus,
--    - items.delivery_fee membedakan TIGA keadaan (NULL = tidak
--      melayani antar, 0 = gratis, > 0 = berbayar) dan menolak minus,
--    - bookings tidak dapat menyimpan bentuk pengiriman yang mustahil:
--      "ambil sendiri" berongkos, atau "diantar" tanpa alamat,
--    - renter_total ikut menghitung ongkir,
--    - seller_gross menambahkan ongkir SESUDAH komisi — ongkir tidak
--      kena potongan,
--    - keduanya tetap GENERATED, jadi klien tetap tidak dapat
--      memalsukannya,
--    - sewa lama tidak berubah nilainya sama sekali,
--    - buku besar menerima delivery_charge/delivery_refund dengan arah
--      yang benar, dan menolak arah yang salah.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0016.sql
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
--  0. Bahan uji
-- =====================================================================

INSERT INTO users (role, name, email, password_hash) VALUES
    ('seller', 'Pemilik Antar', 'antar-s@uji.local', 'x'),
    ('renter', 'Penyewa Antar', 'antar-r@uji.local', 'x');

CREATE TEMP TABLE t_ids AS
SELECT (SELECT id FROM users WHERE email = 'antar-s@uji.local') AS seller,
       (SELECT id FROM users WHERE email = 'antar-r@uji.local') AS renter;

INSERT INTO items (seller_id, title, category, description, price, price_unit, deposit_amount, status, delivery_fee)
SELECT seller, 'Barang Antar', 'Kamera', 'uji', 100000, 'day', 500000, 'active', 25000 FROM t_ids;

INSERT INTO items (seller_id, title, category, description, price, price_unit, deposit_amount, status, delivery_fee)
SELECT seller, 'Barang Ambil Sendiri', 'Kamera', 'uji', 100000, 'day', 500000, 'active', NULL FROM t_ids;

INSERT INTO items (seller_id, title, category, description, price, price_unit, deposit_amount, status, delivery_fee)
SELECT seller, 'Barang Gratis Antar', 'Kamera', 'uji', 100000, 'day', 500000, 'active', 0 FROM t_ids;

CREATE TEMP TABLE t_items AS
SELECT (SELECT id FROM items WHERE title = 'Barang Antar')         AS berbayar,
       (SELECT id FROM items WHERE title = 'Barang Ambil Sendiri') AS tanpa_antar,
       (SELECT id FROM items WHERE title = 'Barang Gratis Antar')  AS gratis;


-- =====================================================================
--  1. Buku alamat
-- =====================================================================

SELECT t_check('alamat', 'Tabel addresses ada',
    EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'addresses'));

INSERT INTO addresses (user_id, label, recipient_name, phone, full_address, is_default)
SELECT renter, 'Rumah', 'Penyewa Antar', '0812000111', 'Jalan Mawar 10, Bandung', true FROM t_ids;

SELECT t_check('alamat', 'Alamat pertama tersimpan sebagai utama',
    (SELECT count(*) FROM addresses a JOIN t_ids i ON a.user_id = i.renter WHERE a.is_default) = 1);

INSERT INTO addresses (user_id, label, recipient_name, phone, full_address, is_default)
SELECT renter, 'Kantor', 'Penyewa Antar', '0812000222', 'Jalan Asia Afrika 1, Bandung', false FROM t_ids;

SELECT t_check('alamat', 'Alamat kedua yang bukan utama boleh berdampingan',
    (SELECT count(*) FROM addresses a JOIN t_ids i ON a.user_id = i.renter) = 2);

SELECT t_expect_error('alamat', 'DUA alamat utama untuk satu pengguna ditolak',
    $$INSERT INTO addresses (user_id, label, recipient_name, phone, full_address, is_default)
      SELECT renter, 'Kos', 'Penyewa Antar', '0812000333', 'Jalan Dago 5', true FROM t_ids$$,
    '23505');

SELECT t_expect_error('alamat', 'Alamat kosong ditolak',
    $$INSERT INTO addresses (user_id, label, recipient_name, phone, full_address)
      SELECT renter, 'Kosong', 'X', '08', '   ' FROM t_ids$$,
    '23514');

SELECT t_expect_error('alamat', 'Label kosong ditolak',
    $$INSERT INTO addresses (user_id, label, recipient_name, phone, full_address)
      SELECT renter, '  ', 'X', '08', 'Jalan Ada' FROM t_ids$$,
    '23514');

SELECT t_check('alamat', 'Alamat pengguna uji berjumlah tepat dua',
    (SELECT count(*) FROM addresses a JOIN t_ids i ON a.user_id = i.renter) = 2);


-- =====================================================================
--  2. items.delivery_fee — tiga keadaan yang berbeda
-- =====================================================================

SELECT t_check('barang', 'NULL berarti tidak melayani antar',
    (SELECT delivery_fee IS NULL FROM items WHERE title = 'Barang Ambil Sendiri'));

SELECT t_check('barang', 'Nol berarti antar gratis — dan itu BUKAN NULL',
    (SELECT delivery_fee = 0 FROM items WHERE title = 'Barang Gratis Antar'));

SELECT t_check('barang', 'Tarif berbayar tersimpan apa adanya',
    (SELECT delivery_fee = 25000 FROM items WHERE title = 'Barang Antar'));

SELECT t_expect_error('barang', 'Tarif antar minus ditolak',
    $$UPDATE items SET delivery_fee = -1 WHERE title = 'Barang Antar'$$,
    '23514');

SELECT t_check('barang', 'Barang lama tidak rusak: delivery_fee-nya NULL',
    NOT EXISTS (SELECT 1 FROM items WHERE delivery_fee < 0));


-- =====================================================================
--  3. Bentuk pengiriman di bookings
-- =====================================================================

INSERT INTO bookings (item_id, renter_id, during, status,
                      price_snapshot, price_unit_snapshot, duration_units,
                      total_rent, deposit_amount,
                      platform_fee_rate, platform_fee_mode, platform_fee_amount,
                      delivery_method, delivery_fee,
                      delivery_recipient, delivery_phone, delivery_address, hold_expires_at)
SELECT berbayar, (SELECT renter FROM t_ids),
       tstzrange('2027-03-01 09:00+07', '2027-03-03 09:00+07', '[)'), 'pending',
       100000, 'day', 2, 200000, 500000,
       0.05, 'deduct', 10000,
       'delivery', 25000, 'Penyewa Antar', '0812000111', 'Jalan Mawar 10, Bandung', now() + interval '1 day'
FROM t_items;

CREATE TEMP TABLE t_bk AS
SELECT b.id FROM bookings b JOIN t_ids i ON b.renter_id = i.renter;

SELECT t_check('bentuk', 'Sewa diantar tersimpan lengkap dengan alamatnya',
    (SELECT count(*) FROM bookings b JOIN t_bk k ON k.id = b.id
     WHERE b.delivery_method = 'delivery' AND b.delivery_address IS NOT NULL) = 1);

SELECT t_expect_error('bentuk', '"Ambil sendiri" BERONGKOS ditolak',
    $$INSERT INTO bookings (item_id, renter_id, during, status,
                            price_snapshot, price_unit_snapshot, duration_units,
                            total_rent, deposit_amount,
                            platform_fee_rate, platform_fee_mode, platform_fee_amount,
                            delivery_method, delivery_fee, hold_expires_at)
      SELECT berbayar, (SELECT renter FROM t_ids),
             tstzrange('2027-04-01 09:00+07', '2027-04-03 09:00+07', '[)'), 'pending',
             100000, 'day', 2, 200000, 500000, 0.05, 'deduct', 10000,
             'pickup', 25000, now() + interval '1 day' FROM t_items$$,
    '23514');

SELECT t_expect_error('bentuk', '"Diantar" TANPA alamat ditolak',
    $$INSERT INTO bookings (item_id, renter_id, during, status,
                            price_snapshot, price_unit_snapshot, duration_units,
                            total_rent, deposit_amount,
                            platform_fee_rate, platform_fee_mode, platform_fee_amount,
                            delivery_method, delivery_fee, hold_expires_at)
      SELECT berbayar, (SELECT renter FROM t_ids),
             tstzrange('2027-05-01 09:00+07', '2027-05-03 09:00+07', '[)'), 'pending',
             100000, 'day', 2, 200000, 500000, 0.05, 'deduct', 10000,
             'delivery', 25000, now() + interval '1 day' FROM t_items$$,
    '23514');

SELECT t_expect_error('bentuk', 'Metode pengiriman karangan ditolak',
    $$INSERT INTO bookings (item_id, renter_id, during, status,
                            price_snapshot, price_unit_snapshot, duration_units,
                            total_rent, deposit_amount,
                            platform_fee_rate, platform_fee_mode, platform_fee_amount,
                            delivery_method, delivery_fee, hold_expires_at)
      SELECT berbayar, (SELECT renter FROM t_ids),
             tstzrange('2027-06-01 09:00+07', '2027-06-03 09:00+07', '[)'), 'pending',
             100000, 'day', 2, 200000, 500000, 0.05, 'deduct', 10000,
             'drone', 0, now() + interval '1 day' FROM t_items$$,
    '23514');


-- =====================================================================
--  4. Uang: ongkir masuk total penyewa, dan TIDAK kena komisi
-- =====================================================================

SELECT t_check('uang', 'renter_total = sewa + deposit + ongkir',
    (SELECT b.renter_total = 200000 + 500000 + 25000
     FROM bookings b JOIN t_bk k ON k.id = b.id),
    (SELECT 'renter_total=' || b.renter_total FROM bookings b JOIN t_bk k ON k.id = b.id));

SELECT t_check('uang', 'seller_gross = sewa - komisi + ongkir (ongkir utuh)',
    (SELECT b.seller_gross = 200000 - 10000 + 25000
     FROM bookings b JOIN t_bk k ON k.id = b.id),
    (SELECT 'seller_gross=' || b.seller_gross FROM bookings b JOIN t_bk k ON k.id = b.id));

SELECT t_check('uang', 'renter_total tetap GENERATED — klien tidak dapat memalsukannya',
    (SELECT is_generated FROM information_schema.columns
     WHERE table_name = 'bookings' AND column_name = 'renter_total') = 'ALWAYS');

SELECT t_check('uang', 'seller_gross tetap GENERATED',
    (SELECT is_generated FROM information_schema.columns
     WHERE table_name = 'bookings' AND column_name = 'seller_gross') = 'ALWAYS');

SELECT t_expect_error('uang', 'renter_total tidak dapat ditulis langsung',
    $$UPDATE bookings SET renter_total = 1 WHERE id IN (SELECT id FROM t_bk)$$,
    '428C9');

SELECT t_check('uang', 'Sewa lama (pickup) totalnya TIDAK berubah',
    NOT EXISTS (
        SELECT 1 FROM bookings
        WHERE delivery_method = 'pickup'
          AND renter_total <> total_rent + deposit_amount
              + CASE WHEN platform_fee_mode = 'on_top' THEN platform_fee_amount ELSE 0 END));


-- =====================================================================
--  5. Buku besar menerima jenis baru
-- =====================================================================

INSERT INTO payments (booking_id, kind, direction, method, amount, status, idempotency_key)
SELECT id, 'delivery_charge', 'in', 'gateway_charge', 25000, 'pending', 'uji-antar-in' FROM t_bk;

SELECT t_check('ledger', 'delivery_charge diterima sebagai uang MASUK',
    (SELECT count(*) FROM payments WHERE kind = 'delivery_charge') = 1);

SELECT t_expect_error('ledger', 'delivery_charge berarah KELUAR ditolak',
    $$INSERT INTO payments (booking_id, kind, direction, method, amount, status, idempotency_key)
      SELECT id, 'delivery_charge', 'out', 'disbursement', 25000, 'pending', 'uji-antar-salah' FROM t_bk$$,
    '23514');

SELECT t_expect_error('ledger', 'delivery_refund berarah MASUK ditolak',
    $$INSERT INTO payments (booking_id, kind, direction, method, amount, status, idempotency_key)
      SELECT id, 'delivery_refund', 'in', 'gateway_charge', 25000, 'pending', 'uji-antar-salah2' FROM t_bk$$,
    '23514');

SELECT t_expect_error('ledger', 'Jenis pembayaran karangan tetap ditolak',
    $$INSERT INTO payments (booking_id, kind, direction, method, amount, status, idempotency_key)
      SELECT id, 'ongkir_kurir', 'in', 'gateway_charge', 25000, 'pending', 'uji-antar-salah3' FROM t_bk$$,
    '23514');


-- =====================================================================
--  6. Alamat ikut mati bersama akunnya
-- =====================================================================

-- Penyewa uji di atas sudah punya baris buku besar, dan payments
-- append-only (keputusan terkunci #3) menolak penghapusan — jadi
-- cascade diuji dengan akun ketiga yang memang bersih.
INSERT INTO users (role, name, email, password_hash)
VALUES ('renter', 'Penyewa Cascade', 'antar-c@uji.local', 'x');

INSERT INTO addresses (user_id, label, recipient_name, phone, full_address, is_default)
SELECT id, 'Rumah', 'Penyewa Cascade', '0813000111', 'Jalan Cascade 1', true
FROM users WHERE email = 'antar-c@uji.local';

SELECT t_check('cascade', 'Alamat akun bersih tercatat lebih dulu',
    (SELECT count(*) FROM addresses a JOIN users u ON u.id = a.user_id
     WHERE u.email = 'antar-c@uji.local') = 1);

DELETE FROM users WHERE email = 'antar-c@uji.local';

SELECT t_check('cascade', 'Alamat ikut terhapus saat akunnya dihapus',
    NOT EXISTS (SELECT 1 FROM addresses a
                WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u.id = a.user_id)));

SELECT t_check('cascade', 'Alamat penyewa lain TIDAK ikut terhapus',
    (SELECT count(*) FROM addresses a JOIN t_ids i ON a.user_id = i.renter) = 2);


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — pengiriman belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

-- =====================================================================
--  verify_0014.sql — keranjang sewa + penanda notifikasi terbaca.
--
--  Yang dibuktikan di sini:
--    - tabel keranjang ada dengan kolom dan tipe yang benar,
--    - rentang terbalik atau kosong ditolak database, bukan cuma C#,
--    - satu penyewa tidak dapat menaruh barang yang sama dua kali,
--    - dua penyewa BOLEH menaruh barang yang sama (keranjang tidak
--      menahan slot — itu urusan bookings),
--    - keranjang tidak menyimpan harga (Aturan 4.1),
--    - barisnya ikut mati bersama akunnya maupun barangnya,
--    - updated_at ditulis trigger dan tidak dapat dipaksa dari luar,
--    - dan users.notifications_seen_at ada, boleh kosong, dan tidak
--      merusak baris yang sudah ada.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0014.sql
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
    ('seller', 'Pemilik Keranjang', 'cart-seller@uji.local', 'x'),
    ('renter', 'Penyewa Keranjang A', 'cart-a@uji.local', 'x'),
    ('renter', 'Penyewa Keranjang B', 'cart-b@uji.local', 'x');

INSERT INTO items (seller_id, title, description, category, price, price_unit, deposit_amount, status)
SELECT id, 'Barang Keranjang', 'uji', 'Kamera', 100000, 'day', 0, 'active'
FROM users WHERE email = 'cart-seller@uji.local';

CREATE TEMP TABLE t_ids AS
SELECT
    (SELECT id FROM users WHERE email = 'cart-a@uji.local')      AS renter_a,
    (SELECT id FROM users WHERE email = 'cart-b@uji.local')      AS renter_b,
    (SELECT id FROM users WHERE email = 'cart-seller@uji.local') AS seller,
    (SELECT id FROM items WHERE title = 'Barang Keranjang')      AS item;


-- =====================================================================
--  1. Bentuk tabel
-- =====================================================================

SELECT t_check('tabel', 'cart_items ada',
    EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'cart_items'));

SELECT t_check('tabel', 'start_at timestamptz NOT NULL',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'cart_items' AND column_name = 'start_at'
              AND data_type = 'timestamp with time zone' AND is_nullable = 'NO'));

SELECT t_check('tabel', 'end_at timestamptz NOT NULL',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'cart_items' AND column_name = 'end_at'
              AND data_type = 'timestamp with time zone' AND is_nullable = 'NO'));

SELECT t_check('tabel', 'Tidak menyimpan harga (Aturan 4.1)',
    NOT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'cart_items'
                  AND (column_name LIKE '%amount%' OR column_name LIKE '%price%'
                       OR column_name LIKE '%total%')));

SELECT t_check('tabel', 'ux_cart_items_renter_item unik',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'cart_items' AND indexname = 'ux_cart_items_renter_item'
              AND indexdef LIKE '%UNIQUE%'));

SELECT t_check('tabel', 'ix_cart_items_renter ada',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'cart_items' AND indexname = 'ix_cart_items_renter'));

SELECT t_check('tabel', 'TIDAK ada exclusion constraint (keranjang tidak menahan slot)',
    NOT EXISTS (SELECT 1 FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                WHERE t.relname = 'cart_items' AND c.contype = 'x'));


-- =====================================================================
--  2. Rentang
-- =====================================================================

INSERT INTO cart_items (renter_id, item_id, start_at, end_at)
SELECT renter_a, item, '2026-10-01 00:00+07', '2026-10-04 00:00+07' FROM t_ids;

SELECT t_check('rentang', 'Rentang sah diterima',
    (SELECT count(*) FROM cart_items) = 1);

SELECT t_expect_error('rentang', 'Rentang terbalik ditolak',
    $$INSERT INTO cart_items (renter_id, item_id, start_at, end_at)
      SELECT renter_b, item, '2026-10-04 00:00+07', '2026-10-01 00:00+07' FROM t_ids$$,
    '23514');

SELECT t_expect_error('rentang', 'Rentang kosong (mulai = selesai) ditolak',
    $$INSERT INTO cart_items (renter_id, item_id, start_at, end_at)
      SELECT renter_b, item, '2026-10-01 00:00+07', '2026-10-01 00:00+07' FROM t_ids$$,
    '23514');


-- =====================================================================
--  3. Keunikan per penyewa
-- =====================================================================

SELECT t_expect_error('unik', 'Penyewa yang sama tidak dapat menaruh barang yang sama dua kali',
    $$INSERT INTO cart_items (renter_id, item_id, start_at, end_at)
      SELECT renter_a, item, '2026-11-01 00:00+07', '2026-11-03 00:00+07' FROM t_ids$$,
    '23505');

INSERT INTO cart_items (renter_id, item_id, start_at, end_at)
SELECT renter_b, item, '2026-10-01 00:00+07', '2026-10-04 00:00+07' FROM t_ids;

SELECT t_check('unik', 'Penyewa LAIN boleh menaruh barang yang sama di rentang yang sama',
    (SELECT count(*) FROM cart_items) = 2,
    'keranjang memang tidak menahan slot');


-- =====================================================================
--  4. updated_at bergerak sendiri
-- =====================================================================

DO $$
DECLARE tersimpan timestamptz; id_baris uuid;
BEGIN
    SELECT id INTO id_baris FROM cart_items ORDER BY created_at LIMIT 1;

    UPDATE cart_items
       SET end_at = end_at + interval '1 day',
           updated_at = timestamptz '2000-01-01 00:00+07'
     WHERE id = id_baris;

    SELECT updated_at INTO tersimpan FROM cart_items WHERE id = id_baris;

    PERFORM t_check('trigger',
        'updated_at ditulis trigger, bukan oleh yang memanggil',
        tersimpan = now(),
        'dicoba dipaksa ke 2000-01-01, tersimpan ' || tersimpan);
END $$;

SELECT t_expect_error('trigger', 'Rentang terbalik ditolak juga saat diubah',
    $$UPDATE cart_items SET end_at = start_at - interval '1 day'$$,
    '23514');


-- =====================================================================
--  5. CASCADE
-- =====================================================================

DO $$
DECLARE sisa integer;
BEGIN
    DELETE FROM users WHERE email = 'cart-b@uji.local';

    SELECT count(*) INTO sisa FROM cart_items
    WHERE renter_id NOT IN (SELECT id FROM users);

    PERFORM t_check('cascade', 'Baris ikut terhapus bersama akun penyewanya', sisa = 0);
END $$;

DO $$
DECLARE sisa integer;
BEGIN
    DELETE FROM items WHERE title = 'Barang Keranjang';

    SELECT count(*) INTO sisa FROM cart_items;

    PERFORM t_check('cascade', 'Baris ikut terhapus bersama barangnya', sisa = 0);
END $$;


-- =====================================================================
--  6. users.notifications_seen_at
-- =====================================================================

SELECT t_check('notifikasi', 'users.notifications_seen_at ada dan boleh kosong',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'notifications_seen_at'
              AND data_type = 'timestamp with time zone' AND is_nullable = 'YES'));

SELECT t_check('notifikasi', 'Akun yang sudah ada berangkat dari NULL (semua belum terbaca)',
    (SELECT notifications_seen_at FROM users WHERE email = 'cart-a@uji.local') IS NULL);

DO $$
DECLARE nilai timestamptz;
BEGIN
    UPDATE users SET notifications_seen_at = now() WHERE email = 'cart-a@uji.local';
    SELECT notifications_seen_at INTO nilai FROM users WHERE email = 'cart-a@uji.local';

    PERFORM t_check('notifikasi', 'Penanda dapat diisi', nilai IS NOT NULL);
END $$;

SELECT t_check('notifikasi', 'Tidak ada tabel notifications (feed diturunkan dari bookings)',
    NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'notifications'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — keranjang belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

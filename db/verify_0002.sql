-- =====================================================================
--  verify_0002.sql — membuktikan pencarian teks dan view kalender dari
--  0002_item_search.sql berperilaku seperti yang diandalkan kode.
--
--  Sama seperti verify_0001.sql: seluruhnya di dalam satu transaksi lalu
--  ROLLBACK, jadi database tidak meninggalkan satu baris pun.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0002.sql
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
        IF p_errcode IS NULL OR SQLSTATE = p_errcode THEN
            INSERT INTO t_results (bagian, nama, lulus, detail)
            VALUES (p_bagian, p_nama, true, SQLSTATE || ' — ' || left(SQLERRM, 60));
        ELSE
            INSERT INTO t_results (bagian, nama, lulus, detail)
            VALUES (p_bagian, p_nama, false,
                    'ditolak tapi errcode ' || SQLSTATE || ', diharapkan ' || p_errcode);
        END IF;
    END;
END $fn$;


-- ---------------------------------------------------------------------
-- Fixture
-- ---------------------------------------------------------------------
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-1111-1111-1111-111111111111','seller','Seller Satu','s1@verify2.id','x'),
  ('22222222-2222-2222-2222-222222222222','renter','Renter Satu','r1@verify2.id','x');

INSERT INTO items (id, seller_id, title, category, description, price, price_unit) VALUES
  ('aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa','11111111-1111-1111-1111-111111111111',
   'Penyewaan Kamera Mirrorless','Elektronik','Termasuk tripod dan tas kamera.',150000,'day'),
  ('bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb','11111111-1111-1111-1111-111111111111',
   'Sepeda Gunung Polygon','Olahraga','Ukuran M, rem cakram.',75000,'day');


-- =====================================================================
--  1. Kolom search_vector
-- =====================================================================

-- Diisi database, bukan aplikasi.
SELECT t_check('search', 'search_vector terisi otomatis saat INSERT',
    (SELECT search_vector IS NOT NULL AND length(search_vector::text) > 0
     FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa'));

-- Konfigurasi 'indonesian' benar-benar terpakai: "Penyewaan" harus tersimpan
-- sebagai lexeme 'sewa'. Kalau seseorang mengganti konfigurasinya jadi 'simple'
-- atau 'english', pemeriksaan ini yang gagal — bukan pengguna yang mengetik
-- "sewa" lalu mendapat hasil kosong tanpa penjelasan.
SELECT t_check('search', 'Imbuhan bahasa Indonesia dipotong (Penyewaan -> sewa)',
    (SELECT search_vector @@ websearch_to_tsquery('indonesian','sewa')
     FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa'));

SELECT t_check('search', 'Kata di deskripsi ikut terindeks',
    (SELECT search_vector @@ websearch_to_tsquery('indonesian','tripod')
     FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa'));

SELECT t_check('search', 'Barang yang tidak relevan tidak ikut cocok',
    NOT (SELECT search_vector @@ websearch_to_tsquery('indonesian','sepeda')
         FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa'));

-- Bobot A untuk judul harus mengalahkan bobot C untuk deskripsi.
DO $$
DECLARE rank_judul real; rank_deskripsi real;
BEGIN
    SELECT ts_rank(search_vector, websearch_to_tsquery('indonesian','kamera'))
      INTO rank_judul FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa';

    SELECT ts_rank(search_vector, websearch_to_tsquery('indonesian','tripod'))
      INTO rank_deskripsi FROM items WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa';

    PERFORM t_check('search', 'Cocok di judul berperingkat lebih tinggi dari cocok di deskripsi',
        rank_judul > rank_deskripsi,
        format('judul=%s deskripsi=%s', rank_judul, rank_deskripsi));
END $$;

-- Ikut berubah kalau judulnya diedit.
UPDATE items SET title = 'Drone DJI Mini'
WHERE id = 'bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb';

SELECT t_check('search', 'search_vector ikut diperbarui saat judul diubah',
    (SELECT search_vector @@ websearch_to_tsquery('indonesian','drone')
     AND NOT search_vector @@ websearch_to_tsquery('indonesian','polygon')
     FROM items WHERE id = 'bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb'));

UPDATE items SET title = 'Sepeda Gunung Polygon'
WHERE id = 'bbbbbbbb-0002-0002-0002-bbbbbbbbbbbb';

-- GENERATED ALWAYS: tidak ada jalur yang bisa menulisinya langsung.
SELECT t_expect_error('search', 'Menulis search_vector langsung ditolak', $q$
  UPDATE items SET search_vector = to_tsvector('indonesian','palsu')
  WHERE id = 'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa'
$q$, '428C9');

-- Index GIN-nya benar-benar ada.
SELECT t_check('search', 'Index GIN ix_items_search terpasang',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'items' AND indexname = 'ix_items_search'
              AND indexdef ILIKE '%USING gin%'));


-- =====================================================================
--  2. View item_blocked_ranges
-- =====================================================================

INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units,
    total_rent, deposit_amount,
    platform_fee_rate, platform_fee_mode, platform_fee_amount, hold_expires_at)
VALUES (
    'cccccccc-0002-0002-0002-cccccccccccc',
    'aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa','22222222-2222-2222-2222-222222222222',
    tstzrange('2026-09-01 00:00+07','2026-09-03 00:00+07','[)'), 'pending',
    150000,'day',2, 300000, 0, 0,'deduct',0, now() + interval '15 minutes');

INSERT INTO item_blackouts (id, item_id, during, reason) VALUES
  ('dddddddd-0002-0002-0002-dddddddddddd','aaaaaaaa-0002-0002-0002-aaaaaaaaaaaa',
   tstzrange('2026-10-01 00:00+07','2026-10-05 00:00+07','[)'), 'Diservis');

SELECT t_check('kalender', 'Booking hidup muncul di view',
    EXISTS (SELECT 1 FROM item_blocked_ranges
            WHERE source = 'booking' AND source_id = 'cccccccc-0002-0002-0002-cccccccccccc'));

SELECT t_check('kalender', 'Blackout muncul di view',
    EXISTS (SELECT 1 FROM item_blocked_ranges
            WHERE source = 'blackout' AND source_id = 'dddddddd-0002-0002-0002-dddddddddddd'));

SELECT t_check('kalender', 'Bound [) diteruskan apa adanya ke starts_at/ends_at',
    (SELECT starts_at = '2026-09-01 00:00+07'::timestamptz
        AND ends_at   = '2026-09-03 00:00+07'::timestamptz
     FROM item_blocked_ranges WHERE source_id = 'cccccccc-0002-0002-0002-cccccccccccc'));

-- Booking yang mati harus keluar dari view dengan sendirinya, persis seperti ia
-- keluar dari index parsial milik constraint no_overlap.
UPDATE bookings SET status = 'cancelled'
WHERE id = 'cccccccc-0002-0002-0002-cccccccccccc';

SELECT t_check('kalender', 'Booking cancelled tidak lagi menahan slot',
    NOT EXISTS (SELECT 1 FROM item_blocked_ranges
                WHERE source_id = 'cccccccc-0002-0002-0002-cccccccccccc'));

-- ---------------------------------------------------------------------
--  Yang paling gampang melenceng diam-diam: daftar status di view harus
--  sama persis dengan klausa WHERE di constraint no_overlap. Kalau
--  berbeda, kalender menjanjikan slot yang ditolak database saat
--  di-booking — atau menyembunyikan slot yang sebenarnya masih bisa
--  dijual. Dua-duanya tidak menghasilkan error apa pun.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_constraint text;
    v_view       text;
    v_status     text;
    v_lulus      boolean := true;
    v_detail     text := '';
BEGIN
    SELECT pg_get_constraintdef(oid) INTO v_constraint
    FROM pg_constraint WHERE conname = 'no_overlap';

    SELECT pg_get_viewdef('item_blocked_ranges'::regclass) INTO v_view;

    FOREACH v_status IN ARRAY ARRAY['pending','confirmed','active'] LOOP
        IF position('''' || v_status || '''' IN v_constraint) = 0
           OR position('''' || v_status || '''' IN v_view) = 0 THEN
            v_lulus  := false;
            v_detail := v_detail || format('%s hilang di salah satunya; ', v_status);
        END IF;
    END LOOP;

    FOREACH v_status IN ARRAY ARRAY['completed','cancelled','disputed'] LOOP
        IF position('''' || v_status || '''' IN v_constraint) > 0
           OR position('''' || v_status || '''' IN v_view) > 0 THEN
            v_lulus  := false;
            v_detail := v_detail || format('%s menyelinap masuk; ', v_status);
        END IF;
    END LOOP;

    PERFORM t_check('kalender',
        'Status di view sama persis dengan status di constraint no_overlap',
        v_lulus, coalesce(nullif(v_detail, ''), 'pending/confirmed/active di keduanya'));
END $$;

SELECT t_check('kalender', 'Index ix_item_blackouts_window terpasang',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'item_blackouts' AND indexname = 'ix_item_blackouts_window'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — migrasi 0002 belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

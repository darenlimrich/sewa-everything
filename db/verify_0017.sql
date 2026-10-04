-- =====================================================================
--  verify_0017.sql — peninjauan listing oleh admin.
--
--  Yang dibuktikan di sini:
--    - keempat kolomnya ada, dan listing baru lahir 'pending' tanpa
--      jejak keputusan apa pun,
--    - keputusan itu satu bentuk utuh: approved wajib bertanggal,
--      rejected wajib bertanggal + berpeninjau + beralasan, pending
--      wajib bersih,
--    - berpindah KE approved/rejected tanpa peninjau ditolak trigger —
--      pengecualian reviewed_by NULL hanya untuk baris hasil backfill,
--    - yang meninjau wajib admin/owner; seller ditolak database,
--    - pemilik dapat menarik listing-nya kembali ke pending (ubah
--      konten) dan itu membersihkan jejak keputusan lama,
--    - jejak keputusan tidak bisa diputus dengan menghapus akunnya,
--    - dan index katalog ikut menyempit ke review_status = 'approved'.
--
--  Seluruh pemeriksaan menyaring ke baris uji yang dibuat skrip ini —
--  data dev boleh berisi listing pending/rejected sungguhan.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0017.sql
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
--  1. Kolom & constraint
-- =====================================================================

SELECT t_check('kolom', 'review_status ada, NOT NULL, bawaan pending',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'review_status'
              AND is_nullable = 'NO' AND column_default LIKE '%pending%'));

SELECT t_check('kolom', 'reviewed_at ada dan timestamptz',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'reviewed_at'
              AND data_type = 'timestamp with time zone'));

SELECT t_check('kolom', 'reviewed_by ada dan uuid',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'reviewed_by'
              AND data_type = 'uuid'));

SELECT t_check('kolom', 'rejection_reason ada',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'rejection_reason'));

SELECT t_check('kolom', 'Constraint ck_items_review_status terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_items_review_status'));

SELECT t_check('kolom', 'Constraint ck_items_review_whole terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_items_review_whole'));

SELECT t_check('kolom', 'Trigger items_review_decision terpasang',
    EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'items_review_decision'));


-- Data uji: satu admin, satu seller terverifikasi, satu barang.
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0017-0017-0017-111111111111','admin','Admin Uji','a@verify17.id','x'),
  ('22222222-0017-0017-0017-222222222222','seller','Seller Uji','s@verify17.id','x');

UPDATE users SET is_verified = true, verified_at = now(),
                 verified_by = '11111111-0017-0017-0017-111111111111'
WHERE id = '22222222-0017-0017-0017-222222222222';

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount)
VALUES ('33333333-0017-0017-0017-333333333333','22222222-0017-0017-0017-222222222222',
        'Kamera Verify 17','Elektronik', 150000, 'day', 500000);

SELECT t_check('kolom', 'Listing baru lahir pending tanpa jejak keputusan',
    (SELECT review_status = 'pending' AND reviewed_at IS NULL
            AND reviewed_by IS NULL AND rejection_reason IS NULL
     FROM items WHERE id = '33333333-0017-0017-0017-333333333333'));

SELECT t_check('kolom', 'Dan listing pending BUKAN "terlihat publik" walau active dan tidak ditangguhkan',
    NOT EXISTS (SELECT 1 FROM items
                WHERE id = '33333333-0017-0017-0017-333333333333'
                  AND status = 'active' AND suspended_at IS NULL
                  AND review_status = 'approved'));

SELECT t_expect_error('kolom', 'Nilai review_status di luar tiga yang dikenal ditolak', $$
    UPDATE items SET review_status = 'maybe'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');


-- =====================================================================
--  2. Keputusan adalah satu bentuk utuh
-- =====================================================================

SELECT t_expect_error('utuh', 'pending yang membawa reviewed_at ditolak', $$
    UPDATE items SET reviewed_at = now()
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'pending yang membawa rejection_reason ditolak', $$
    UPDATE items SET rejection_reason = 'Belum diputus'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'approved tanpa reviewed_at ditolak', $$
    UPDATE items SET review_status = 'approved',
                     reviewed_by = '11111111-0017-0017-0017-111111111111'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'approved yang membawa rejection_reason ditolak', $$
    UPDATE items SET review_status = 'approved', reviewed_at = now(),
                     reviewed_by = '11111111-0017-0017-0017-111111111111',
                     rejection_reason = 'Tidak konsisten'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'rejected tanpa alasan ditolak', $$
    UPDATE items SET review_status = 'rejected', reviewed_at = now(),
                     reviewed_by = '11111111-0017-0017-0017-111111111111'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'rejected dengan alasan kosong (spasi saja) ditolak', $$
    UPDATE items SET review_status = 'rejected', reviewed_at = now(),
                     reviewed_by = '11111111-0017-0017-0017-111111111111',
                     rejection_reason = '   '
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'rejected tanpa peninjau ditolak', $$
    UPDATE items SET review_status = 'rejected', reviewed_at = now(),
                     rejection_reason = 'Foto tidak jelas'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');


-- =====================================================================
--  3. Keputusan wajib punya peninjau, dan peninjaunya wajib staf
--
--  CHECK mengizinkan approved dengan reviewed_by NULL demi baris hasil
--  backfill. Yang menutup celahnya untuk keputusan BARU adalah trigger:
--  berpindah ke approved tanpa peninjau ditolak.
-- =====================================================================

SELECT t_expect_error('peninjau', 'Berpindah ke approved tanpa reviewed_by ditolak trigger', $$
    UPDATE items SET review_status = 'approved', reviewed_at = now()
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('peninjau', 'Seller tidak bisa jadi peninjau', $$
    UPDATE items SET review_status = 'approved', reviewed_at = now(),
                     reviewed_by = '22222222-0017-0017-0017-222222222222'
    WHERE id = '33333333-0017-0017-0017-333333333333';
$$, '23514');

SELECT t_expect_error('peninjau', 'INSERT langsung sebagai approved tanpa peninjau ditolak', $$
    INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount,
                       review_status, reviewed_at)
    VALUES ('44444444-0017-0017-0017-444444444444','22222222-0017-0017-0017-222222222222',
            'Selundup Verify 17','Elektronik', 1000, 'day', 0, 'approved', now());
$$, '23514');

UPDATE items SET review_status = 'rejected', reviewed_at = now(),
                 reviewed_by = '11111111-0017-0017-0017-111111111111',
                 rejection_reason = 'Foto belum menunjukkan kondisi barang'
WHERE id = '33333333-0017-0017-0017-333333333333';

SELECT t_check('peninjau', 'Admin bisa menolak dengan alasan',
    (SELECT review_status = 'rejected' AND rejection_reason IS NOT NULL
     FROM items WHERE id = '33333333-0017-0017-0017-333333333333'));

SELECT t_check('peninjau', 'Dan listing rejected tetap di luar "terlihat publik"',
    NOT EXISTS (SELECT 1 FROM items
                WHERE id = '33333333-0017-0017-0017-333333333333'
                  AND status = 'active' AND suspended_at IS NULL
                  AND review_status = 'approved'));


-- =====================================================================
--  4. Pemilik menarik kembali ke pending — jejak lama harus bersih
-- =====================================================================

UPDATE items SET review_status = 'pending', reviewed_at = NULL,
                 reviewed_by = NULL, rejection_reason = NULL
WHERE id = '33333333-0017-0017-0017-333333333333';

SELECT t_check('ulang', 'Listing yang diperbaiki kembali pending tanpa alasan lama',
    (SELECT review_status = 'pending' AND reviewed_at IS NULL
            AND reviewed_by IS NULL AND rejection_reason IS NULL
     FROM items WHERE id = '33333333-0017-0017-0017-333333333333'));

UPDATE items SET review_status = 'approved', reviewed_at = now(),
                 reviewed_by = '11111111-0017-0017-0017-111111111111'
WHERE id = '33333333-0017-0017-0017-333333333333';

SELECT t_check('ulang', 'Admin menyetujui, listing masuk "terlihat publik"',
    EXISTS (SELECT 1 FROM items
            WHERE id = '33333333-0017-0017-0017-333333333333'
              AND status = 'active' AND suspended_at IS NULL
              AND review_status = 'approved'));

SELECT t_check('ulang', 'Penurunan admin tetap suku yang terpisah dari persetujuan',
    (SELECT review_status = 'approved' AND suspended_at IS NULL
     FROM items WHERE id = '33333333-0017-0017-0017-333333333333'));


-- =====================================================================
--  5. Jejak keputusan tidak bisa diputus
-- =====================================================================

SELECT t_expect_error('jejak', 'Admin yang pernah meninjau tidak bisa dihapus', $$
    DELETE FROM users WHERE id = '11111111-0017-0017-0017-111111111111';
$$, '23503');


-- =====================================================================
--  6. Index katalog ikut menyempit
-- =====================================================================

SELECT t_check('index', 'ix_items_browse menyaring review_status = approved juga',
    (SELECT pg_get_expr(i.indpred, i.indrelid) LIKE '%review_status%approved%'
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_browse'),
    (SELECT pg_get_expr(i.indpred, i.indrelid)
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_browse'));

SELECT t_check('index', 'ix_items_browse masih menyaring suspended_at IS NULL',
    (SELECT pg_get_expr(i.indpred, i.indrelid) LIKE '%suspended_at IS NULL%'
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_browse'));

SELECT t_check('index', 'ix_items_review_pending ada dan parsial',
    (SELECT i.indpred IS NOT NULL
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_review_pending'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — peninjauan listing belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

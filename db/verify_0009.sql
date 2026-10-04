-- =====================================================================
--  verify_0009.sql — moderasi listing.
--
--  Yang dibuktikan di sini:
--    - ketiga kolomnya ada, dan listing baru lahir TIDAK ditangguhkan,
--    - penangguhan itu satu keputusan utuh: tidak bisa ada tanpa alasan,
--      dan tidak bisa ada tanpa yang menangguhkan,
--    - yang menangguhkan wajib admin/owner — seller ditolak database,
--      bukan cuma ditolak controller,
--    - jejak keputusannya tidak bisa diputus dengan menghapus akunnya,
--    - dan index katalog ikut menyempit, jadi predikat "terlihat publik"
--      di kode dan di index tetap satu definisi.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0009.sql
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

SELECT t_check('kolom', 'suspended_at ada dan timestamptz',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'suspended_at'
              AND data_type = 'timestamp with time zone'));

SELECT t_check('kolom', 'suspended_by ada dan uuid',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'suspended_by'
              AND data_type = 'uuid'));

SELECT t_check('kolom', 'suspension_reason ada',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'items' AND column_name = 'suspension_reason'));

SELECT t_check('kolom', 'Ketiganya boleh NULL — ditangguhkan itu pengecualian, bukan keadaan awal',
    (SELECT count(*) FROM information_schema.columns
     WHERE table_name = 'items'
       AND column_name IN ('suspended_at','suspended_by','suspension_reason')
       AND is_nullable = 'YES') = 3);

SELECT t_check('kolom', 'Constraint ck_items_suspension_whole terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_items_suspension_whole'));

SELECT t_check('kolom', 'Trigger items_suspended_by_role terpasang',
    EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'items_suspended_by_role'));


-- Data uji: satu admin, satu seller terverifikasi, satu barang aktif.
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0009-0009-0009-111111111111','admin','Admin Uji','a@verify9.id','x'),
  ('22222222-0009-0009-0009-222222222222','seller','Seller Uji','s@verify9.id','x');

UPDATE users SET is_verified = true, verified_at = now(),
                 verified_by = '11111111-0009-0009-0009-111111111111'
WHERE id = '22222222-0009-0009-0009-222222222222';

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount)
VALUES ('33333333-0009-0009-0009-333333333333','22222222-0009-0009-0009-222222222222',
        'Kamera Verify 9','Elektronik', 150000, 'day', 500000);

SELECT t_check('kolom', 'Listing baru lahir TIDAK ditangguhkan',
    (SELECT suspended_at IS NULL AND suspended_by IS NULL AND suspension_reason IS NULL
     FROM items WHERE id = '33333333-0009-0009-0009-333333333333'));


-- =====================================================================
--  2. Penangguhan adalah satu keputusan utuh
--
--  Kalau ketiga kolomnya bisa diisi sepotong-sepotong, baris bisa
--  berakhir "ditangguhkan tanpa alasan dan tanpa yang menangguhkan" —
--  bentuk yang tidak bisa dipertanggungjawabkan ke pemilik barangnya.
-- =====================================================================

SELECT t_expect_error('utuh', 'Ditangguhkan tanpa alasan ditolak', $$
    UPDATE items SET suspended_at = now(),
                     suspended_by = '11111111-0009-0009-0009-111111111111'
    WHERE id = '33333333-0009-0009-0009-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'Alasan kosong (spasi saja) ditolak', $$
    UPDATE items SET suspended_at = now(),
                     suspended_by = '11111111-0009-0009-0009-111111111111',
                     suspension_reason = '   '
    WHERE id = '33333333-0009-0009-0009-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'Ditangguhkan tanpa yang menangguhkan ditolak', $$
    UPDATE items SET suspended_at = now(), suspension_reason = 'Foto menipu'
    WHERE id = '33333333-0009-0009-0009-333333333333';
$$, '23514');

SELECT t_expect_error('utuh', 'Alasan tanpa penangguhan ditolak', $$
    UPDATE items SET suspension_reason = 'Foto menipu'
    WHERE id = '33333333-0009-0009-0009-333333333333';
$$, '23514');


-- =====================================================================
--  3. Yang menangguhkan wajib admin atau owner
--
--  Ditegakkan database, bukan cuma controller: kolom ini menentukan
--  penghasilan orang hilang atau tidak, dan seller tidak boleh bisa
--  menurunkan listing pesaingnya lewat jalur apa pun.
-- =====================================================================

SELECT t_expect_error('peran', 'Seller tidak bisa jadi yang menangguhkan', $$
    UPDATE items SET suspended_at = now(),
                     suspended_by = '22222222-0009-0009-0009-222222222222',
                     suspension_reason = 'Coba-coba'
    WHERE id = '33333333-0009-0009-0009-333333333333';
$$, '23514');

UPDATE items SET suspended_at = now(),
                 suspended_by = '11111111-0009-0009-0009-111111111111',
                 suspension_reason = 'Foto tidak sesuai barang'
WHERE id = '33333333-0009-0009-0009-333333333333';

SELECT t_check('peran', 'Admin bisa menurunkan listing',
    (SELECT suspended_at IS NOT NULL FROM items
     WHERE id = '33333333-0009-0009-0009-333333333333'));


-- =====================================================================
--  4. Status pemilik dan penangguhan admin saling bebas
--
--  Inti keputusan migrasi 0009. Pemilik menguasai status, admin
--  menguasai penangguhan, dan terlihat di katalog menuntut keduanya.
-- =====================================================================

UPDATE items SET status = 'active' WHERE id = '33333333-0009-0009-0009-333333333333';

SELECT t_check('bebas', 'Pemilik menyalakan status TIDAK membatalkan penangguhan',
    (SELECT status = 'active' AND suspended_at IS NOT NULL FROM items
     WHERE id = '33333333-0009-0009-0009-333333333333'));

SELECT t_check('bebas', 'Dan barangnya tetap di luar definisi "terlihat publik"',
    NOT EXISTS (SELECT 1 FROM items
                WHERE id = '33333333-0009-0009-0009-333333333333'
                  AND status = 'active' AND suspended_at IS NULL));


-- =====================================================================
--  5. Jejak keputusan tidak bisa diputus
--
--  Sepola users.verified_by dan disputes.resolved_by: menghapus admin
--  yang pernah memutuskan akan memutus siapa-memutuskan-apa, dan FK-nya
--  memang menolak.
-- =====================================================================

SELECT t_expect_error('jejak', 'Admin yang pernah menurunkan listing tidak bisa dihapus', $$
    DELETE FROM users WHERE id = '11111111-0009-0009-0009-111111111111';
$$, '23503');


-- =====================================================================
--  6. Pemulihan
-- =====================================================================

UPDATE items SET suspended_at = NULL, suspended_by = NULL, suspension_reason = NULL
WHERE id = '33333333-0009-0009-0009-333333333333';

SELECT t_check('pulih', 'Listing bisa dipulihkan',
    (SELECT suspended_at IS NULL FROM items
     WHERE id = '33333333-0009-0009-0009-333333333333'));

SELECT t_check('pulih', 'Dan kembali masuk definisi "terlihat publik"',
    EXISTS (SELECT 1 FROM items
            WHERE id = '33333333-0009-0009-0009-333333333333'
              AND status = 'active' AND suspended_at IS NULL));


-- =====================================================================
--  7. Index katalog ikut menyempit
--
--  ix_items_browse melayani "barang yang terlihat publik". Definisi itu
--  berubah di migrasi ini; kalau index-nya tidak ikut, ia berhenti cocok
--  dengan predikat kuerinya dan planner tidak akan memakainya lagi.
-- =====================================================================

SELECT t_check('index', 'ix_items_browse menyaring suspended_at IS NULL juga',
    (SELECT pg_get_expr(i.indpred, i.indrelid) LIKE '%suspended_at IS NULL%'
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_browse'),
    (SELECT pg_get_expr(i.indpred, i.indrelid)
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_browse'));

SELECT t_check('index', 'ix_items_suspended ada dan parsial',
    (SELECT i.indpred IS NOT NULL
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_suspended'));

SELECT t_check('index', 'ix_items_search TETAP tidak parsial — moderasi harus bisa mencari yang tersembunyi',
    (SELECT i.indpred IS NULL
     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
     WHERE c.relname = 'ix_items_search'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — moderasi listing belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

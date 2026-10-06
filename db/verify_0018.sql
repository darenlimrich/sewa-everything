-- =====================================================================
--  verify_0018.sql - isi berkas foto di database.
--
--  Yang dibuktikan di sini:
--    - tabelnya ada dengan kunci nama berkas,
--    - nama berkas yang sah (32 hex + jpg/png/webp) diterima,
--    - nama yang bisa dipakai menembus path ditolak database,
--    - Content-Type hanya tiga tipe gambar, dan wajib cocok ekstensinya,
--    - isi kosong ditolak,
--    - nama yang sama tidak bisa ditulis dua kali,
--    - dan tidak ada FK yang menggantung dari tabel bisnis.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0018.sql
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
        VALUES (p_bagian, p_nama, false, 'HARUSNYA DITOLAK - tapi statement berhasil');
    EXCEPTION WHEN OTHERS THEN
        INSERT INTO t_results (bagian, nama, lulus, detail)
        VALUES (p_bagian, p_nama, p_errcode IS NULL OR SQLSTATE = p_errcode,
                SQLSTATE || ' - ' || left(SQLERRM, 55));
    END;
END $fn$;


-- =====================================================================
--  1. Bentuk tabel
-- =====================================================================

SELECT t_check('tabel', 'photo_blobs ada',
    to_regclass('public.photo_blobs') IS NOT NULL);

SELECT t_check('tabel', 'kunci primernya name',
    (SELECT string_agg(a.attname, ',')
     FROM pg_index i
     JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
     WHERE i.indrelid = 'photo_blobs'::regclass AND i.indisprimary) = 'name');

SELECT t_check('tabel', 'content bertipe bytea',
    (SELECT data_type FROM information_schema.columns
     WHERE table_name = 'photo_blobs' AND column_name = 'content') = 'bytea');

SELECT t_check('tabel', 'content tidak dikompresi ulang (STORAGE EXTERNAL)',
    (SELECT attstorage FROM pg_attribute
     WHERE attrelid = 'photo_blobs'::regclass AND attname = 'content') = 'e');


-- =====================================================================
--  2. Yang sah diterima
-- =====================================================================

INSERT INTO photo_blobs (name, content_type, content) VALUES
    ('0123456789abcdef0123456789abcdef.jpg',  'image/jpeg', '\xffd8ff'::bytea),
    ('0123456789abcdef0123456789abcde1.png',  'image/png',  '\x89504e47'::bytea),
    ('0123456789abcdef0123456789abcde2.webp', 'image/webp', '\x52494646'::bytea);

SELECT t_check('sah', 'jpg, png, webp tersimpan',
    (SELECT count(*) FROM photo_blobs
     WHERE name LIKE '0123456789abcdef0123456789abcde%') = 3);

SELECT t_check('sah', 'created_at terisi sendiri',
    (SELECT bool_and(created_at IS NOT NULL) FROM photo_blobs
     WHERE name LIKE '0123456789abcdef0123456789abcde%'));


-- =====================================================================
--  3. Nama berkas
-- =====================================================================

SELECT t_expect_error('nama', 'path traversal ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('../0123456789abcdef0123456789abcdef.jpg', 'image/jpeg', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('nama', 'huruf besar ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789ABCDEF0123456789ABCDEF.jpg', 'image/jpeg', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('nama', 'ekstensi lain ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde3.svg', 'image/jpeg', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('nama', 'nama pendek ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('abc.jpg', 'image/jpeg', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('nama', 'nama yang sama dua kali ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcdef.jpg', 'image/jpeg', '\xff'::bytea)$$, '23505');


-- =====================================================================
--  4. Content-Type
-- =====================================================================

SELECT t_expect_error('tipe', 'text/html ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde4.jpg', 'text/html', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('tipe', 'image/svg+xml ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde5.png', 'image/svg+xml', '\xff'::bytea)$$, '23514');

SELECT t_expect_error('tipe', 'tipe tidak cocok ekstensi ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde6.jpg', 'image/png', '\xff'::bytea)$$, '23514');


-- =====================================================================
--  5. Isi
-- =====================================================================

SELECT t_expect_error('isi', 'isi kosong ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde7.jpg', 'image/jpeg', ''::bytea)$$, '23514');

SELECT t_expect_error('isi', 'isi NULL ditolak',
    $$INSERT INTO photo_blobs (name, content_type, content)
      VALUES ('0123456789abcdef0123456789abcde8.jpg', 'image/jpeg', NULL)$$, '23502');


-- =====================================================================
--  6. Tidak ada FK yang menggantung
-- =====================================================================

SELECT t_check('fk', 'tidak ada FK yang menunjuk photo_blobs',
    NOT EXISTS (SELECT 1 FROM pg_constraint
                WHERE contype = 'f' AND confrelid = 'photo_blobs'::regclass));

SELECT t_check('fk', 'photo_blobs tidak menunjuk tabel lain',
    NOT EXISTS (SELECT 1 FROM pg_constraint
                WHERE contype = 'f' AND conrelid = 'photo_blobs'::regclass));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL - photo_blobs belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

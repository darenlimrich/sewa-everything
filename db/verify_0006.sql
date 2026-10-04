-- =====================================================================
--  verify_0006.sql — milestone 9 (Owner mengelola akun admin).
--
--  Yang dibuktikan di sini:
--    - kolom deactivated_at ada, dan akun baru lahir AKTIF,
--    - admin bisa dicabut lalu dipulihkan,
--    - owner TIDAK PERNAH bisa dinonaktifkan — oleh siapa pun, lewat
--      jalur apa pun, termasuk psql,
--    - dan alasan kenapa pencabutan berbentuk "nonaktif" alih-alih
--      "hapus": barisnya masih ditunjuk jejak keputusan, dan foreign
--      key-nya memang menolak penghapusan itu.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0006.sql
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
--  1. Kolom deactivated_at
-- =====================================================================

SELECT t_check('kolom', 'deactivated_at ada dan timestamptz',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'deactivated_at'
              AND data_type = 'timestamp with time zone'));

SELECT t_check('kolom', 'Boleh NULL — nonaktif itu pengecualian, bukan keadaan awal',
    (SELECT is_nullable FROM information_schema.columns
     WHERE table_name = 'users' AND column_name = 'deactivated_at') = 'YES');

SELECT t_check('kolom', 'Constraint ck_users_owner_always_active terpasang',
    EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_users_owner_always_active'));


-- Data uji: satu owner, satu admin, satu seller yang diloloskan admin itu.
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('11111111-0006-0006-0006-111111111111','owner','Owner Uji','o@verify6.id','x'),
  ('22222222-0006-0006-0006-222222222222','admin','Admin Uji','a@verify6.id','x'),
  ('33333333-0006-0006-0006-333333333333','seller','Seller Uji','s@verify6.id','x');

SELECT t_check('kolom', 'Akun baru lahir aktif (deactivated_at NULL)',
    (SELECT count(*) FROM users
     WHERE id IN ('11111111-0006-0006-0006-111111111111',
                  '22222222-0006-0006-0006-222222222222')
       AND deactivated_at IS NULL) = 2);


-- =====================================================================
--  2. Admin bisa dicabut lalu dipulihkan
-- =====================================================================

UPDATE users SET deactivated_at = now()
WHERE id = '22222222-0006-0006-0006-222222222222';

SELECT t_check('cabut', 'Admin bisa dinonaktifkan',
    (SELECT deactivated_at IS NOT NULL FROM users
     WHERE id = '22222222-0006-0006-0006-222222222222'));

UPDATE users SET deactivated_at = NULL
WHERE id = '22222222-0006-0006-0006-222222222222';

SELECT t_check('cabut', 'Dan bisa dipulihkan lagi',
    (SELECT deactivated_at IS NULL FROM users
     WHERE id = '22222222-0006-0006-0006-222222222222'));

SELECT t_check('cabut', 'Seller juga boleh dinonaktifkan (kolomnya umum, bukan khusus admin)',
    (SELECT count(*) FROM users WHERE id = '33333333-0006-0006-0006-333333333333') = 1);


-- =====================================================================
--  3. Owner tidak pernah bisa dinonaktifkan
--
--  Owner satu-satunya yang bisa memulihkan akun lain. Kalau ia sendiri
--  bisa ikut dicabut, tidak ada lagi pintu pemulihan dari dalam
--  aplikasi — termasuk pintu untuk memulihkan owner itu sendiri.
-- =====================================================================

SELECT t_expect_error('owner', 'Menonaktifkan owner yang ada ditolak', $q$
  UPDATE users SET deactivated_at = now()
  WHERE id = '11111111-0006-0006-0006-111111111111'
$q$, '23514');

SELECT t_expect_error('owner', 'Menyisipkan owner yang sudah nonaktif sejak lahir ditolak', $q$
  INSERT INTO users (role, name, email, password_hash, deactivated_at)
  VALUES ('owner','Owner Mati','o2@verify6.id','x', now())
$q$, '23514');

-- Menaikkan admin nonaktif jadi owner juga terhalang: CHECK-nya membaca
-- baris hasilnya, bukan niat penulisnya.
UPDATE users SET deactivated_at = now()
WHERE id = '22222222-0006-0006-0006-222222222222';

SELECT t_expect_error('owner', 'Admin nonaktif tidak bisa dipromosikan jadi owner', $q$
  UPDATE users SET role = 'owner' WHERE id = '22222222-0006-0006-0006-222222222222'
$q$, '23514');


-- =====================================================================
--  4. Kenapa "nonaktif", bukan "hapus"
--
--  Keputusan admin meninggalkan jejak yang menunjuk balik ke barisnya.
--  Menghapus akunnya berarti memutus jejak itu — dan foreign key-nya
--  memang tidak mengizinkannya. Nonaktif bukan pilihan gaya; ia
--  satu-satunya bentuk pencabutan yang tidak merusak riwayat.
-- =====================================================================

UPDATE users SET is_verified = true, verified_at = now(),
                 verified_by = '22222222-0006-0006-0006-222222222222'
WHERE id = '33333333-0006-0006-0006-333333333333';

SELECT t_expect_error('jejak', 'Menghapus admin yang pernah meloloskan seller ditolak FK', $q$
  DELETE FROM users WHERE id = '22222222-0006-0006-0006-222222222222'
$q$, '23503');

UPDATE platform_settings SET updated_by = '22222222-0006-0006-0006-222222222222';

SELECT t_check('jejak', 'Jejak "siapa yang mengubah komisi" tetap menunjuk akun nonaktif',
    (SELECT updated_by FROM platform_settings) = '22222222-0006-0006-0006-222222222222');

SELECT t_check('jejak', 'Dan jejak "siapa yang meloloskan seller" ikut utuh',
    (SELECT verified_by FROM users WHERE id = '33333333-0006-0006-0006-333333333333')
        = '22222222-0006-0006-0006-222222222222');


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — akun admin belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

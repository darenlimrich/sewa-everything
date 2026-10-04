-- =====================================================================
--  verify_0011.sql — kunci akun sementara.
--
--  Yang dibuktikan di sini:
--    - ketiga kolomnya ada dengan tipe yang benar,
--    - akun baru lahir BERSIH: nol kesalahan, tidak terkunci,
--    - penghitung tidak bisa negatif,
--    - kunci tidak bisa ada tanpa jejak kesalahan yang menyebabkannya,
--    - dan owner TETAP bisa terkunci sementara — 0006 hanya melarang
--      owner DINONAKTIFKAN, dan itu hal yang berbeda.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0011.sql
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
--  1. Kolom
-- =====================================================================

SELECT t_check('kolom', 'failed_login_count ada, integer, NOT NULL',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'failed_login_count'
              AND data_type = 'integer' AND is_nullable = 'NO'));

SELECT t_check('kolom', 'last_failed_login_at ada dan timestamptz',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'last_failed_login_at'
              AND data_type = 'timestamp with time zone'));

SELECT t_check('kolom', 'locked_until ada dan timestamptz',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'locked_until'
              AND data_type = 'timestamp with time zone'));

SELECT t_check('kolom', 'failed_login_count bawaannya 0',
    (SELECT column_default FROM information_schema.columns
      WHERE table_name = 'users' AND column_name = 'failed_login_count') = '0');


-- =====================================================================
--  2. Akun baru lahir bersih
-- =====================================================================

INSERT INTO users (role, name, email, password_hash)
VALUES ('renter', 'Penyewa Kunci', 'kunci-verify@uji.local', 'x');

SELECT t_check('bawaan', 'Akun baru: nol kesalahan, tidak terkunci',
    (SELECT failed_login_count = 0 AND last_failed_login_at IS NULL AND locked_until IS NULL
     FROM users WHERE email = 'kunci-verify@uji.local'));


-- =====================================================================
--  3. Constraint
-- =====================================================================

SELECT t_expect_error('constraint', 'Penghitung negatif ditolak',
    $$UPDATE users SET failed_login_count = -1 WHERE email = 'kunci-verify@uji.local'$$,
    '23514');

SELECT t_expect_error('constraint', 'Kunci tanpa jejak kesalahan ditolak',
    $$UPDATE users SET locked_until = now() + interval '15 minutes'
      WHERE email = 'kunci-verify@uji.local'$$,
    '23514');

UPDATE users
   SET failed_login_count = 3,
       last_failed_login_at = now(),
       locked_until = now() + interval '15 minutes'
 WHERE email = 'kunci-verify@uji.local';

SELECT t_check('constraint', 'Kunci dengan jejak kesalahan diterima',
    (SELECT locked_until IS NOT NULL AND failed_login_count = 3
     FROM users WHERE email = 'kunci-verify@uji.local'));

UPDATE users
   SET failed_login_count = 0, last_failed_login_at = NULL, locked_until = NULL
 WHERE email = 'kunci-verify@uji.local';

SELECT t_check('constraint', 'Reset ke bersih diterima',
    (SELECT failed_login_count = 0 AND locked_until IS NULL
     FROM users WHERE email = 'kunci-verify@uji.local'));


-- =====================================================================
--  4. Owner ikut bisa terkunci sementara — beda dari dinonaktifkan
-- =====================================================================

INSERT INTO users (role, name, email, password_hash)
VALUES ('owner', 'Owner Verify', 'owner-kunci-verify@uji.local', 'x');

UPDATE users
   SET failed_login_count = 3,
       last_failed_login_at = now(),
       locked_until = now() + interval '15 minutes'
 WHERE email = 'owner-kunci-verify@uji.local';

SELECT t_check('owner', 'Owner boleh terkunci sementara',
    (SELECT locked_until IS NOT NULL FROM users WHERE email = 'owner-kunci-verify@uji.local'));

SELECT t_expect_error('owner', 'Owner tetap tidak boleh dinonaktifkan',
    $$UPDATE users SET deactivated_at = now() WHERE email = 'owner-kunci-verify@uji.local'$$,
    '23514');


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — kunci akun belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

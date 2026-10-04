-- =====================================================================
--  verify_0013.sql — faktor kedua (TOTP) untuk akun staf.
--
--  Yang dibuktikan di sini:
--    - kedua tabelnya ada dengan kolom dan tipe yang benar,
--    - langkah TOTP HANYA BOLEH MAJU (inilah yang mematikan pemakaian
--      ulang sebuah kode di dalam jendela 30 detiknya),
--    - langkah tidak dapat lahir sebelum pendaftaran dikonfirmasi,
--    - konfirmasi tidak dapat dicabut, dan rahasianya tidak dapat
--      diganti di tempat setelah dikonfirmasi,
--    - kode pemulihan mentah mustahil tersimpan (hash wajib 32 byte),
--    - kode pemulihan yang sudah terpakai tidak dapat dihidupkan,
--    - dan mencabut 2FA ikut membuang kode pemulihannya (CASCADE),
--      termasuk saat yang dibuang adalah akunnya sendiri.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0013.sql
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

INSERT INTO users (role, name, email, password_hash)
VALUES ('admin', 'Admin TOTP', 'totp-verify@uji.local', 'x'),
       ('admin', 'Admin Kedua', 'totp-verify-2@uji.local', 'x');

CREATE TEMP TABLE t_ids AS
SELECT
    (SELECT id FROM users WHERE email = 'totp-verify@uji.local')   AS satu,
    (SELECT id FROM users WHERE email = 'totp-verify-2@uji.local') AS dua;


-- =====================================================================
--  1. Bentuk tabel
-- =====================================================================

SELECT t_check('bentuk', 'Tabel user_totp ada',
    to_regclass('public.user_totp') IS NOT NULL);

SELECT t_check('bentuk', 'Tabel totp_recovery_codes ada',
    to_regclass('public.totp_recovery_codes') IS NOT NULL);

SELECT t_check('bentuk', 'user_totp.user_id adalah primary key',
    (SELECT count(*) FROM information_schema.key_column_usage k
      JOIN information_schema.table_constraints c
        ON c.constraint_name = k.constraint_name
     WHERE k.table_name = 'user_totp'
       AND c.constraint_type = 'PRIMARY KEY'
       AND k.column_name = 'user_id') = 1);

SELECT t_check('bentuk', 'user_totp.last_step bertipe bigint',
    (SELECT data_type FROM information_schema.columns
      WHERE table_name = 'user_totp' AND column_name = 'last_step') = 'bigint');

SELECT t_check('bentuk', 'user_totp.secret bertipe text dan NOT NULL',
    (SELECT data_type || '/' || is_nullable FROM information_schema.columns
      WHERE table_name = 'user_totp' AND column_name = 'secret') = 'text/NO');

SELECT t_check('bentuk', 'totp_recovery_codes.code_hash bertipe bytea',
    (SELECT data_type FROM information_schema.columns
      WHERE table_name = 'totp_recovery_codes' AND column_name = 'code_hash') = 'bytea');

SELECT t_check('bentuk', 'Kode pemulihan menunjuk user_totp, bukan users',
    (SELECT ccu.table_name
       FROM information_schema.table_constraints tc
       JOIN information_schema.constraint_column_usage ccu
         ON ccu.constraint_name = tc.constraint_name
      WHERE tc.table_name = 'totp_recovery_codes'
        AND tc.constraint_type = 'FOREIGN KEY') = 'user_totp');


-- =====================================================================
--  2. CHECK saat menyisipkan
-- =====================================================================

SELECT t_expect_error('check', 'Rahasia kosong ditolak',
    $$INSERT INTO user_totp (user_id, secret)
      SELECT satu, '   ' FROM t_ids$$,
    '23514');

SELECT t_expect_error('check', 'Langkah tanpa konfirmasi ditolak',
    $$INSERT INTO user_totp (user_id, secret, last_step)
      SELECT satu, 'RAHASIA', 100 FROM t_ids$$,
    '23514');

SELECT t_expect_error('check', 'Konfirmasi sebelum dibuat ditolak',
    $$INSERT INTO user_totp (user_id, secret, created_at, confirmed_at)
      SELECT satu, 'RAHASIA', now(), now() - interval '1 hour' FROM t_ids$$,
    '23514');

INSERT INTO user_totp (user_id, secret)
SELECT satu, 'RAHASIABASE32' FROM t_ids;

SELECT t_check('check', 'Baris belum dikonfirmasi tersimpan apa adanya',
    (SELECT confirmed_at IS NULL AND last_step IS NULL FROM user_totp));

SELECT t_expect_error('check', 'Satu akun tidak dapat punya dua rahasia',
    $$INSERT INTO user_totp (user_id, secret) SELECT satu, 'LAIN' FROM t_ids$$,
    '23505');


-- =====================================================================
--  3. Trigger: langkah hanya boleh maju
-- =====================================================================

UPDATE user_totp SET confirmed_at = now(), last_step = 1000;

SELECT t_check('langkah', 'Konfirmasi pertama menyimpan langkahnya',
    (SELECT confirmed_at IS NOT NULL AND last_step = 1000 FROM user_totp));

UPDATE user_totp SET last_step = 1001;

SELECT t_check('langkah', 'Langkah berikutnya yang lebih besar diterima',
    (SELECT last_step = 1001 FROM user_totp));

SELECT t_expect_error('langkah', 'Langkah yang sama ditolak — kode tidak dapat dipakai ulang',
    $$UPDATE user_totp SET last_step = 1001$$,
    '23514');

SELECT t_expect_error('langkah', 'Langkah mundur ditolak',
    $$UPDATE user_totp SET last_step = 1000$$,
    '23514');

SELECT t_expect_error('langkah', 'Langkah dikosongkan lagi ditolak',
    $$UPDATE user_totp SET last_step = NULL$$,
    '23514');


-- =====================================================================
--  4. Trigger: konfirmasi & rahasia beku
-- =====================================================================

SELECT t_expect_error('beku', 'Konfirmasi tidak dapat dicabut',
    $$UPDATE user_totp SET confirmed_at = NULL$$,
    '23514');

SELECT t_expect_error('beku', 'Konfirmasi tidak dapat digeser waktunya',
    $$UPDATE user_totp SET confirmed_at = now() + interval '1 day'$$,
    '23514');

SELECT t_expect_error('beku', 'Rahasia yang sudah dikonfirmasi tidak dapat diganti',
    $$UPDATE user_totp SET secret = 'RAHASIALAIN'$$,
    '23514');

SELECT t_expect_error('beku', 'created_at tidak dapat digeser',
    $$UPDATE user_totp SET created_at = now() - interval '1 year'$$,
    '23514');

SELECT t_expect_error('beku', 'Rahasia tidak dapat berpindah pemilik',
    $$UPDATE user_totp SET user_id = (SELECT dua FROM t_ids)$$,
    '23514');


-- =====================================================================
--  5. Kode pemulihan
-- =====================================================================

SELECT t_expect_error('pemulihan', 'Hash bukan 32 byte ditolak',
    $$INSERT INTO totp_recovery_codes (user_id, code_hash)
      SELECT satu, decode(repeat('aa', 16), 'hex') FROM t_ids$$,
    '23514');

INSERT INTO totp_recovery_codes (user_id, code_hash)
SELECT satu, decode(repeat('a1', 32), 'hex') FROM t_ids;

INSERT INTO totp_recovery_codes (user_id, code_hash)
SELECT satu, decode(repeat('b2', 32), 'hex') FROM t_ids;

SELECT t_check('pemulihan', 'Dua kode hidup tersimpan',
    (SELECT count(*) FROM totp_recovery_codes WHERE used_at IS NULL) = 2);

SELECT t_expect_error('pemulihan', 'Dua baris tidak dapat memegang kode yang sama',
    $$INSERT INTO totp_recovery_codes (user_id, code_hash)
      SELECT satu, decode(repeat('a1', 32), 'hex') FROM t_ids$$,
    '23505');

SELECT t_expect_error('pemulihan', 'Kode pemulihan tanpa rahasia TOTP ditolak',
    $$INSERT INTO totp_recovery_codes (user_id, code_hash)
      SELECT dua, decode(repeat('c3', 32), 'hex') FROM t_ids$$,
    '23503');

UPDATE totp_recovery_codes SET used_at = now()
WHERE code_hash = decode(repeat('a1', 32), 'hex');

SELECT t_check('pemulihan', 'Satu kode ditandai terpakai',
    (SELECT count(*) FROM totp_recovery_codes WHERE used_at IS NULL) = 1);

SELECT t_expect_error('pemulihan', 'Kode terpakai tidak dapat dihidupkan lagi',
    $$UPDATE totp_recovery_codes SET used_at = NULL
       WHERE code_hash = decode(repeat('a1', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('pemulihan', 'Kode terpakai tidak dapat digeser waktunya',
    $$UPDATE totp_recovery_codes SET used_at = now() + interval '1 day'
       WHERE code_hash = decode(repeat('a1', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('pemulihan', 'Isi kode pemulihan beku',
    $$UPDATE totp_recovery_codes SET code_hash = decode(repeat('d4', 32), 'hex')
       WHERE code_hash = decode(repeat('b2', 32), 'hex')$$,
    '23514');


-- =====================================================================
--  6. CASCADE
-- =====================================================================

DELETE FROM user_totp WHERE user_id = (SELECT satu FROM t_ids);

SELECT t_check('cascade', 'Mencabut 2FA ikut membuang kode pemulihannya',
    (SELECT count(*) FROM totp_recovery_codes) = 0);

INSERT INTO user_totp (user_id, secret) SELECT satu, 'LAGI' FROM t_ids;
INSERT INTO totp_recovery_codes (user_id, code_hash)
SELECT satu, decode(repeat('e5', 32), 'hex') FROM t_ids;

DELETE FROM users WHERE id = (SELECT satu FROM t_ids);

SELECT t_check('cascade', 'Menghapus akun ikut membuang rahasia TOTP-nya',
    (SELECT count(*) FROM user_totp) = 0);

SELECT t_check('cascade', 'Menghapus akun ikut membuang kode pemulihannya',
    (SELECT count(*) FROM totp_recovery_codes) = 0);


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — faktor kedua belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

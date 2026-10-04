-- =====================================================================
--  verify_0012.sql — tautan atur ulang kata sandi.
--
--  Yang dibuktikan di sini:
--    - tabelnya ada dengan kolom dan tipe yang benar,
--    - token mentah mustahil tersimpan (hash wajib tepat 32 byte),
--    - dua baris tidak pernah memegang token yang sama,
--    - token yang sudah terpakai TIDAK BISA dihidupkan lagi,
--    - identitas dan masa berlakunya beku setelah diterbitkan,
--    - barisnya ikut mati bersama akunnya (CASCADE),
--    - dan refresh_tokens sekarang menerima alasan 'password_reset'
--      tanpa ikut menerima alasan karangan.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0012.sql
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
VALUES ('renter', 'Penyewa Reset', 'reset-verify@uji.local', 'x');

CREATE TEMP TABLE t_ids AS
SELECT id AS user_id FROM users WHERE email = 'reset-verify@uji.local';


-- =====================================================================
--  1. Bentuk tabel
-- =====================================================================

SELECT t_check('tabel', 'password_resets ada',
    EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'password_resets'));

SELECT t_check('tabel', 'token_hash bytea NOT NULL',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'password_resets' AND column_name = 'token_hash'
              AND data_type = 'bytea' AND is_nullable = 'NO'));

SELECT t_check('tabel', 'expires_at timestamptz NOT NULL',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'password_resets' AND column_name = 'expires_at'
              AND data_type = 'timestamp with time zone' AND is_nullable = 'NO'));

SELECT t_check('tabel', 'used_at boleh kosong',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'password_resets' AND column_name = 'used_at'
              AND is_nullable = 'YES'));

SELECT t_check('tabel', 'Tidak ada kolom yang menyimpan token mentah',
    NOT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'password_resets'
                  AND data_type IN ('text', 'character varying')));

SELECT t_check('tabel', 'ux_password_resets_hash unik',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'password_resets' AND indexname = 'ux_password_resets_hash'
              AND indexdef LIKE '%UNIQUE%'));


-- =====================================================================
--  2. Constraint
-- =====================================================================

SELECT t_expect_error('constraint', 'Hash pendek ditolak (bukan SHA-256)',
    $$INSERT INTO password_resets (user_id, token_hash, expires_at)
      SELECT user_id, decode('abcd', 'hex'), now() + interval '1 hour' FROM t_ids$$,
    '23514');

SELECT t_expect_error('constraint', 'Masa berlaku yang sudah lewat saat lahir ditolak',
    $$INSERT INTO password_resets (user_id, token_hash, expires_at)
      SELECT user_id, decode(repeat('11', 32), 'hex'), now() - interval '1 hour' FROM t_ids$$,
    '23514');

INSERT INTO password_resets (user_id, token_hash, expires_at)
SELECT user_id, decode(repeat('aa', 32), 'hex'), now() + interval '1 hour' FROM t_ids;

SELECT t_check('constraint', 'Token yang benar bentuknya diterima',
    (SELECT count(*) = 1 FROM password_resets
      WHERE token_hash = decode(repeat('aa', 32), 'hex')));

SELECT t_check('constraint', 'Baris baru lahir belum terpakai',
    (SELECT used_at IS NULL FROM password_resets
      WHERE token_hash = decode(repeat('aa', 32), 'hex')));

SELECT t_expect_error('constraint', 'Token kembar ditolak',
    $$INSERT INTO password_resets (user_id, token_hash, expires_at)
      SELECT user_id, decode(repeat('aa', 32), 'hex'), now() + interval '1 hour' FROM t_ids$$,
    '23505');


-- =====================================================================
--  3. Sekali terpakai, selamanya terpakai
-- =====================================================================

UPDATE password_resets SET used_at = now() WHERE token_hash = decode(repeat('aa', 32), 'hex');

SELECT t_check('trigger', 'Menandai terpakai diterima',
    (SELECT used_at IS NOT NULL FROM password_resets
      WHERE token_hash = decode(repeat('aa', 32), 'hex')));

SELECT t_expect_error('trigger', 'Token terpakai tidak bisa dihidupkan lagi',
    $$UPDATE password_resets SET used_at = NULL
       WHERE token_hash = decode(repeat('aa', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('trigger', 'Token terpakai tidak bisa dipindah waktunya',
    $$UPDATE password_resets SET used_at = now() + interval '1 minute'
       WHERE token_hash = decode(repeat('aa', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('trigger', 'Masa berlaku beku setelah diterbitkan',
    $$UPDATE password_resets SET expires_at = now() + interval '99 hours'
       WHERE token_hash = decode(repeat('aa', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('trigger', 'Token tidak bisa dipindah ke akun lain',
    $$UPDATE password_resets SET token_hash = decode(repeat('bb', 32), 'hex')
       WHERE token_hash = decode(repeat('aa', 32), 'hex')$$,
    '23514');

DELETE FROM password_resets WHERE token_hash = decode(repeat('aa', 32), 'hex');

SELECT t_check('trigger', 'DELETE tetap diizinkan (baris mati harus bisa disapu)',
    (SELECT count(*) = 0 FROM password_resets
      WHERE token_hash = decode(repeat('aa', 32), 'hex')));


-- =====================================================================
--  4. Ikut mati bersama akunnya
-- =====================================================================

INSERT INTO password_resets (user_id, token_hash, expires_at)
SELECT user_id, decode(repeat('cc', 32), 'hex'), now() + interval '1 hour' FROM t_ids;

DELETE FROM users WHERE email = 'reset-verify@uji.local';

SELECT t_check('cascade', 'Token ikut terhapus saat akunnya dihapus',
    (SELECT count(*) = 0 FROM password_resets
      WHERE token_hash = decode(repeat('cc', 32), 'hex')));


-- =====================================================================
--  5. Sesi lama dicabut dengan alasan yang tercatat
-- =====================================================================

INSERT INTO users (role, name, email, password_hash)
VALUES ('renter', 'Penyewa Sesi', 'reset-sesi-verify@uji.local', 'x');

INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at)
SELECT id, decode(repeat('dd', 32), 'hex'), gen_random_uuid(), now() + interval '30 days'
FROM users WHERE email = 'reset-sesi-verify@uji.local';

UPDATE refresh_tokens
   SET revoked_at = now(), revoked_reason = 'password_reset'
 WHERE token_hash = decode(repeat('dd', 32), 'hex');

SELECT t_check('alasan', $$'password_reset' diterima sebagai alasan pencabutan$$,
    (SELECT revoked_reason = 'password_reset' FROM refresh_tokens
      WHERE token_hash = decode(repeat('dd', 32), 'hex')));

INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at)
SELECT id, decode(repeat('ee', 32), 'hex'), gen_random_uuid(), now() + interval '30 days'
FROM users WHERE email = 'reset-sesi-verify@uji.local';

SELECT t_expect_error('alasan', 'Alasan karangan tetap ditolak',
    $$UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = 'karena-saya-mau'
       WHERE token_hash = decode(repeat('ee', 32), 'hex')$$,
    '23514');

SELECT t_expect_error('alasan', 'Pencabutan tanpa alasan tetap ditolak',
    $$UPDATE refresh_tokens SET revoked_at = now()
       WHERE token_hash = decode(repeat('ee', 32), 'hex')$$,
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
        RAISE EXCEPTION '% pemeriksaan GAGAL — tautan reset belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

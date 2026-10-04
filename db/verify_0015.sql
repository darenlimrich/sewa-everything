-- =====================================================================
--  verify_0015.sql — foto profil pengguna.
--
--  Yang dibuktikan di sini:
--    - kolomnya ada, bertipe text, dan boleh kosong,
--    - baris lama tidak rusak: avatar_url-nya NULL, bukan string kosong,
--    - string kosong dan string berisi spasi saja DITOLAK database,
--      bukan cuma oleh C# — jadi hanya ada satu cara menulis
--      "belum ada foto", yaitu NULL,
--    - URL yang sah tersimpan apa adanya,
--    - mengosongkannya kembali ke NULL tetap boleh,
--    - kolomnya tidak menyeret indeks maupun constraint lain,
--    - dan tidak ada tabel avatar terpisah yang lahir diam-diam.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0015.sql
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
    ('renter', 'Penyewa Avatar', 'avatar-a@uji.local', 'x');

CREATE TEMP TABLE t_ids AS
SELECT (SELECT id FROM users WHERE email = 'avatar-a@uji.local') AS renter;


-- =====================================================================
--  1. Bentuk kolom
-- =====================================================================

SELECT t_check('kolom', 'users.avatar_url ada',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'users' AND column_name = 'avatar_url'));

SELECT t_check('kolom', 'avatar_url bertipe text',
    (SELECT data_type FROM information_schema.columns
     WHERE table_name = 'users' AND column_name = 'avatar_url') = 'text',
    (SELECT data_type FROM information_schema.columns
     WHERE table_name = 'users' AND column_name = 'avatar_url'));

SELECT t_check('kolom', 'avatar_url boleh kosong (nullable)',
    (SELECT is_nullable FROM information_schema.columns
     WHERE table_name = 'users' AND column_name = 'avatar_url') = 'YES');

SELECT t_check('kolom', 'avatar_url tanpa DEFAULT',
    (SELECT column_default FROM information_schema.columns
     WHERE table_name = 'users' AND column_name = 'avatar_url') IS NULL);


-- =====================================================================
--  2. Baris lama tidak rusak
-- =====================================================================

SELECT t_check('lama', 'Akun yang baru dibuat ber-avatar_url NULL',
    (SELECT avatar_url IS NULL FROM users WHERE email = 'avatar-a@uji.local'));

SELECT t_check('lama', 'Tidak ada baris users ber-avatar_url string kosong',
    NOT EXISTS (SELECT 1 FROM users WHERE avatar_url = ''));


-- =====================================================================
--  3. Hanya NULL yang berarti "belum ada foto"
-- =====================================================================

SELECT t_expect_error('kosong', 'String kosong ditolak',
    format('UPDATE users SET avatar_url = %L WHERE id = %L',
           '', (SELECT renter FROM t_ids)),
    '23514');

SELECT t_expect_error('kosong', 'Spasi saja ditolak',
    format('UPDATE users SET avatar_url = %L WHERE id = %L',
           '   ', (SELECT renter FROM t_ids)),
    '23514');

SELECT t_expect_error('kosong', 'Tab dan baris baru saja ditolak',
    format('UPDATE users SET avatar_url = %L WHERE id = %L',
           E'\t\n', (SELECT renter FROM t_ids)),
    '23514');


-- =====================================================================
--  4. URL yang sah tersimpan apa adanya
-- =====================================================================

UPDATE users SET avatar_url = '/uploads/9f2c0b1a4d5e4f6a8b3c2d1e0f9a8b7c.jpg'
WHERE id = (SELECT renter FROM t_ids);

SELECT t_check('isi', 'URL tersimpan persis',
    (SELECT avatar_url FROM users WHERE id = (SELECT renter FROM t_ids))
        = '/uploads/9f2c0b1a4d5e4f6a8b3c2d1e0f9a8b7c.jpg',
    (SELECT avatar_url FROM users WHERE id = (SELECT renter FROM t_ids)));

SELECT t_check('isi', 'updated_at ikut bergerak saat avatar diganti',
    (SELECT updated_at >= created_at FROM users WHERE id = (SELECT renter FROM t_ids)));

UPDATE users SET avatar_url = NULL WHERE id = (SELECT renter FROM t_ids);

SELECT t_check('isi', 'Foto dapat dicabut kembali ke NULL',
    (SELECT avatar_url IS NULL FROM users WHERE id = (SELECT renter FROM t_ids)));


-- =====================================================================
--  5. Tidak menyeret apa pun yang tidak diminta
-- =====================================================================

SELECT t_check('bentuk', 'Tidak ada tabel avatar terpisah',
    NOT EXISTS (SELECT 1 FROM information_schema.tables
                WHERE table_name IN ('user_avatars', 'avatars', 'user_photos')));

SELECT t_check('bentuk', 'Tidak ada indeks atas avatar_url',
    NOT EXISTS (
        SELECT 1 FROM pg_index i
        JOIN pg_class c ON c.oid = i.indrelid
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (i.indkey)
        WHERE c.relname = 'users' AND a.attname = 'avatar_url'));

SELECT t_check('bentuk', 'Constraint ck_users_avatar_url terpasang',
    EXISTS (SELECT 1 FROM pg_constraint
            WHERE conname = 'ck_users_avatar_url' AND contype = 'c'));

SELECT t_check('bentuk', 'avatar_url bukan bagian dari foreign key mana pun',
    NOT EXISTS (
        SELECT 1 FROM pg_constraint co
        JOIN pg_class c ON c.oid = co.conrelid
        JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (co.conkey)
        WHERE c.relname = 'users' AND a.attname = 'avatar_url' AND co.contype = 'f'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — foto profil belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

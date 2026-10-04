-- =====================================================================
--  verify_0008.sql — milestone 8 (refresh token: sesi yang bisa
--  diperpanjang).
--
--  Yang dibuktikan di sini bukan "endpointnya jalan" — itu tugas
--  RefreshTokenTests — melainkan bahwa mekanismenya tetap benar walau
--  kode C#-nya dilewati sama sekali:
--
--    - satu token tidak bisa ditukar dua kali, dan yang menjaganya
--      predikat UPDATE-nya, bukan pemeriksaan di aplikasi,
--    - token bekas tidak bisa dihidupkan kembali,
--    - pencabutan tidak bisa dibatalkan,
--    - identitas dan masa berlaku token beku setelah diterbitkan,
--    - token mentah memang tidak punya tempat di tabel ini,
--    - dan sesi tidak bisa hidup lebih lama daripada akun pemiliknya.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0008.sql
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


-- Data uji: satu renter dengan satu sesi (satu keluarga, dua generasi).
INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('88888888-0008-0008-0008-888888888888','renter','Renter Uji','r@verify8.id','x'),
  ('99999999-0008-0008-0008-999999999999','renter','Renter Lain','r2@verify8.id','x');

INSERT INTO refresh_tokens (id, user_id, token_hash, family_id, expires_at) VALUES
  ('aaaa0008-0000-0000-0000-00000000aaaa',
   '88888888-0008-0008-0008-888888888888',
   sha256('token-generasi-1'::bytea),
   'ffff0008-0000-0000-0000-00000000ffff',
   now() + interval '30 days');


-- =====================================================================
--  1. Bentuk tabelnya
-- =====================================================================

SELECT t_check('bentuk', 'Tabel refresh_tokens ada',
    EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'refresh_tokens'));

SELECT t_check('bentuk', 'token_hash bertipe bytea dan NOT NULL',
    (SELECT data_type = 'bytea' AND is_nullable = 'NO'
     FROM information_schema.columns
     WHERE table_name = 'refresh_tokens' AND column_name = 'token_hash'));

-- Tidak ada kolom yang menyimpan tokennya apa adanya. Kalau suatu saat ada
-- yang menambahkannya "supaya gampang di-debug", di sinilah ketahuannya.
SELECT t_check('bentuk', 'Tidak ada kolom teks untuk token mentah',
    NOT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_name = 'refresh_tokens'
                  AND data_type IN ('text', 'character varying')
                  AND column_name <> 'revoked_reason'));

SELECT t_check('bentuk', 'Index unik ux_refresh_tokens_hash terpasang',
    EXISTS (SELECT 1 FROM pg_indexes
            WHERE tablename = 'refresh_tokens' AND indexname = 'ux_refresh_tokens_hash'));

SELECT t_check('bentuk', 'Index parsial sesi hidup terpasang',
    (SELECT indexdef LIKE '%used_at IS NULL%' AND indexdef LIKE '%revoked_at IS NULL%'
     FROM pg_indexes
     WHERE tablename = 'refresh_tokens' AND indexname = 'ix_refresh_tokens_user_live'));

SELECT t_check('bentuk', 'Token baru lahir hidup: belum terpakai, belum dicabut',
    (SELECT used_at IS NULL AND revoked_at IS NULL AND revoked_reason IS NULL
     FROM refresh_tokens WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'));


-- =====================================================================
--  2. Yang ditolak mentah-mentah
-- =====================================================================

SELECT t_expect_error('tolak', 'Dua baris dengan hash yang sama ditolak', $q$
  INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at)
  VALUES ('99999999-0008-0008-0008-999999999999', sha256('token-generasi-1'::bytea),
          gen_random_uuid(), now() + interval '30 days')
$q$, '23505');

SELECT t_expect_error('tolak', 'Hash yang bukan 32 byte ditolak', $q$
  INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at)
  VALUES ('88888888-0008-0008-0008-888888888888', '\x0102030405'::bytea,
          gen_random_uuid(), now() + interval '30 days')
$q$, '23514');

SELECT t_expect_error('tolak', 'Token yang kedaluwarsa sebelum diterbitkan ditolak', $q$
  INSERT INTO refresh_tokens (user_id, token_hash, family_id, issued_at, expires_at)
  VALUES ('88888888-0008-0008-0008-888888888888', sha256('mundur'::bytea),
          gen_random_uuid(), now(), now() - interval '1 second')
$q$, '23514');

SELECT t_expect_error('tolak', 'Dicabut tanpa alasan ditolak', $q$
  UPDATE refresh_tokens SET revoked_at = now()
  WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
$q$, '23514');

SELECT t_expect_error('tolak', 'Alasan tanpa pencabutan ditolak', $q$
  UPDATE refresh_tokens SET revoked_reason = 'logout'
  WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
$q$, '23514');

SELECT t_expect_error('tolak', 'Alasan pencabutan di luar daftar ditolak', $q$
  UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = 'karena-pengin'
  WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
$q$, '23514');

SELECT t_expect_error('tolak', 'Token milik akun yang tidak ada ditolak FK', $q$
  INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at)
  VALUES ('00000000-0000-0000-0000-000000000000', sha256('hantu'::bytea),
          gen_random_uuid(), now() + interval '30 days')
$q$, '23503');


-- =====================================================================
--  3. Sekali pakai — dan yang menjaganya bukan kode aplikasi
--
--  Penukaran dilakukan dengan UPDATE ... WHERE used_at IS NULL. Dua
--  penukaran bersamaan atas token yang sama karena itu tidak mungkin
--  dua-duanya berhasil: yang kedua mengenai nol baris, dan nol baris
--  itulah sinyal "sudah ada yang menukarnya lebih dulu".
-- =====================================================================

DO $$
DECLARE n1 integer; n2 integer;
BEGIN
    UPDATE refresh_tokens SET used_at = now()
    WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
      AND used_at IS NULL AND revoked_at IS NULL;
    GET DIAGNOSTICS n1 = ROW_COUNT;

    UPDATE refresh_tokens SET used_at = now()
    WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
      AND used_at IS NULL AND revoked_at IS NULL;
    GET DIAGNOSTICS n2 = ROW_COUNT;

    PERFORM t_check('sekali-pakai', 'Penukaran pertama mengenai tepat satu baris',
        n1 = 1, 'baris=' || n1);

    PERFORM t_check('sekali-pakai', 'Penukaran kedua mengenai NOL baris',
        n2 = 0, 'baris=' || n2);
END $$;

-- Rotasi: generasi berikutnya lahir di keluarga yang sama.
INSERT INTO refresh_tokens (id, user_id, token_hash, family_id, expires_at) VALUES
  ('bbbb0008-0000-0000-0000-00000000bbbb',
   '88888888-0008-0008-0008-888888888888',
   sha256('token-generasi-2'::bytea),
   'ffff0008-0000-0000-0000-00000000ffff',
   now() + interval '30 days');

SELECT t_check('sekali-pakai', 'Satu sesi = satu keluarga dengan beberapa generasi',
    (SELECT count(*) FROM refresh_tokens
     WHERE family_id = 'ffff0008-0000-0000-0000-00000000ffff') = 2);


-- =====================================================================
--  4. Keadaan token hanya bisa maju
--
--  Seluruh deteksi pemakaian ulang berdiri di atas ini. Kalau used_at
--  bisa dikosongkan lagi, token bekas hidup kembali dan mekanismenya
--  tinggal teater.
-- =====================================================================

SELECT t_expect_error('maju', 'used_at tidak bisa dikosongkan lagi', $q$
  UPDATE refresh_tokens SET used_at = NULL
  WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
$q$, '23514');

SELECT t_expect_error('maju', 'used_at tidak bisa digeser ke waktu lain', $q$
  UPDATE refresh_tokens SET used_at = now() + interval '1 hour'
  WHERE id = 'aaaa0008-0000-0000-0000-00000000aaaa'
$q$, '23514');

SELECT t_expect_error('maju', 'Hash token beku setelah diterbitkan', $q$
  UPDATE refresh_tokens SET token_hash = sha256('token-lain'::bytea)
  WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'
$q$, '23514');

SELECT t_expect_error('maju', 'Masa berlaku tidak bisa diperpanjang di tempat', $q$
  UPDATE refresh_tokens SET expires_at = now() + interval '10 years'
  WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'
$q$, '23514');

SELECT t_expect_error('maju', 'Token tidak bisa dipindahkan ke akun lain', $q$
  UPDATE refresh_tokens SET user_id = '99999999-0008-0008-0008-999999999999'
  WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'
$q$, '23514');

SELECT t_expect_error('maju', 'Token tidak bisa pindah keluarga', $q$
  UPDATE refresh_tokens SET family_id = gen_random_uuid()
  WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'
$q$, '23514');


-- =====================================================================
--  5. Mencabut satu keluarga = mengakhiri satu sesi
--
--  Termasuk baris yang sudah terpakai: alasannya harus tercatat di
--  seluruh rantai, supaya "kenapa sesi ini mati" masih terjawab nanti.
-- =====================================================================

UPDATE refresh_tokens SET revoked_at = now(), revoked_reason = 'reuse_detected'
WHERE family_id = 'ffff0008-0000-0000-0000-00000000ffff' AND revoked_at IS NULL;

SELECT t_check('cabut', 'Seluruh anggota keluarga tercabut, termasuk yang sudah terpakai',
    (SELECT count(*) FROM refresh_tokens
     WHERE family_id = 'ffff0008-0000-0000-0000-00000000ffff'
       AND revoked_at IS NOT NULL AND revoked_reason = 'reuse_detected') = 2);

SELECT t_expect_error('cabut', 'Pencabutan tidak bisa dibatalkan', $q$
  UPDATE refresh_tokens SET revoked_at = NULL, revoked_reason = NULL
  WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'
$q$, '23514');


-- =====================================================================
--  6. Baris mati boleh dipangkas, dan sesi tidak hidup lebih lama
--     daripada akunnya
--
--  Beda dengan payments: tabel ini bukan catatan uang. Barisnya sampah
--  begitu mati, dan penyapunya nanti perlu bisa menghapusnya — jadi
--  DELETE sengaja tidak dilarang, dan FK ke users satu-satunya yang
--  CASCADE di skema ini.
-- =====================================================================

DELETE FROM refresh_tokens WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb';

SELECT t_check('pangkas', 'Baris mati bisa dihapus (penyapu nanti perlu ini)',
    NOT EXISTS (SELECT 1 FROM refresh_tokens
                WHERE id = 'bbbb0008-0000-0000-0000-00000000bbbb'));

INSERT INTO refresh_tokens (user_id, token_hash, family_id, expires_at) VALUES
  ('99999999-0008-0008-0008-999999999999', sha256('sesi-akun-lain'::bytea),
   gen_random_uuid(), now() + interval '30 days');

DELETE FROM users WHERE id = '99999999-0008-0008-0008-999999999999';

SELECT t_check('pangkas', 'Akun terhapus membawa sesinya sekalian (ON DELETE CASCADE)',
    NOT EXISTS (SELECT 1 FROM refresh_tokens
                WHERE user_id = '99999999-0008-0008-0008-999999999999'));


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — refresh token belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

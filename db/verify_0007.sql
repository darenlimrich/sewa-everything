-- =====================================================================
--  verify_0007.sql — milestone 9 (kredensial gateway diisi Owner).
--
--  Yang dibuktikan di sini:
--    - ketiga kolom kredensial ada, dan bawaannya "belum diatur"
--      (NULL) dengan mode sandbox — bukan produksi,
--    - kunci kosong/spasi ditolak, karena kunci yang blank bukan kunci
--      dan hanya akan gagal jauh belakangan di sisi gateway,
--    - singleton platform_settings tetap singleton setelah ditambahi
--      kolom baru.
--
--  Yang SENGAJA tidak diuji di sini: kecocokan kunci dengan mode produksi.
--  Itu milik Midtrans, bukan invarian data kita, jadi tempatnya di
--  controller — lihat catatan di migrasi 0007 dan test
--  OwnerPaymentGatewayTests. Sejak 17 Agu 2026 kecocokan itu tidak lagi
--  ditebak dari awalan 'SB-' (Midtrans sudah tidak memakainya) melainkan
--  ditanyakan langsung ke Midtrans sebelum kunci disimpan.
--
--  Jalankan:  psql -d sewa_everything -f db/verify_0007.sql
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
--  1. Kolom kredensial
-- =====================================================================

SELECT t_check('kolom', 'midtrans_server_key ada dan text',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'platform_settings' AND column_name = 'midtrans_server_key'
              AND data_type = 'text'));

SELECT t_check('kolom', 'midtrans_client_key ada dan text',
    EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_name = 'platform_settings' AND column_name = 'midtrans_client_key'
              AND data_type = 'text'));

SELECT t_check('kolom', 'midtrans_is_production ada, NOT NULL',
    (SELECT is_nullable FROM information_schema.columns
     WHERE table_name = 'platform_settings' AND column_name = 'midtrans_is_production') = 'NO');

-- Bawaannya harus "belum diatur" DAN sandbox. Kalau bawaan modenya
-- produksi, instalasi baru akan mengarah ke Midtrans sungguhan sejak
-- detik pertama — sebelum ada yang sempat memutuskannya.
--
-- Diperiksa dari DEFAULT kolomnya, BUKAN dari isi baris yang sedang ada.
-- Isi baris berubah begitu Owner mengisi kuncinya lewat /owner/gateway —
-- itu justru keadaan yang benar untuk instalasi yang sudah berjalan, dan
-- versi lama pemeriksaan ini menyatakannya gagal (kena 17 Agu 2026 di
-- database dev, tepat setelah kunci sandbox sungguhan dipasang).
SELECT t_check('bawaan', 'Kunci tanpa DEFAULT — instalasi baru "belum diatur"',
    (SELECT count(*) FROM information_schema.columns
      WHERE table_name = 'platform_settings'
        AND column_name IN ('midtrans_server_key', 'midtrans_client_key')
        AND column_default IS NULL) = 2);

SELECT t_check('bawaan', 'Mode bawaan sandbox, bukan produksi',
    (SELECT column_default FROM information_schema.columns
      WHERE table_name = 'platform_settings'
        AND column_name = 'midtrans_is_production') = 'false');


-- =====================================================================
--  2. Kunci blank ditolak
--
--  String kosong bukan "belum diatur" — ia lolos semua pemeriksaan
--  "sudah terisi?" lalu gagal jauh belakangan sebagai penolakan gateway
--  yang tidak jelas sebabnya. Yang berarti kosong hanyalah NULL.
-- =====================================================================

SELECT t_expect_error('blank', 'Server key string kosong ditolak', $q$
  UPDATE platform_settings SET midtrans_server_key = ''
$q$, '23514');

SELECT t_expect_error('blank', 'Server key berisi spasi saja ditolak', $q$
  UPDATE platform_settings SET midtrans_server_key = '   '
$q$, '23514');

SELECT t_expect_error('blank', 'Client key string kosong ditolak', $q$
  UPDATE platform_settings SET midtrans_client_key = ''
$q$, '23514');

UPDATE platform_settings
   SET midtrans_server_key = 'SB-Mid-server-UJI-0007',
       midtrans_client_key = 'SB-Mid-client-UJI-0007';

SELECT t_check('blank', 'Kunci yang benar-benar berisi diterima',
    (SELECT midtrans_server_key FROM platform_settings) = 'SB-Mid-server-UJI-0007');

SELECT t_check('blank', 'NULL tetap diterima — artinya "belum diatur"',
    (SELECT count(*) FROM platform_settings) = 1);


-- =====================================================================
--  3. Singleton tetap singleton
--
--  Kredensial ganda berarti dua kunci yang sama-sama mengaku berlaku,
--  dan tidak ada aturan yang menentukan mana yang menang.
-- =====================================================================

SELECT t_expect_error('singleton', 'Baris kredensial kedua tetap mustahil', $q$
  INSERT INTO platform_settings (id, midtrans_server_key) VALUES (false, 'SB-Mid-server-KEDUA')
$q$);


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
        RAISE EXCEPTION '% pemeriksaan GAGAL — kredensial gateway belum berperilaku benar', n;
    END IF;
    RAISE NOTICE 'Semua pemeriksaan lulus.';
END $$;

ROLLBACK;

-- =====================================================================
--  Sewa Everything — 0007_payment_credentials.sql
--  Milestone 9: kredensial gateway diisi Owner, bukan dari berkas.
--
--  Keputusan terkunci #12. Sampai sekarang kunci Midtrans datang dari
--  appsettings / environment variable, artinya mengganti kunci menuntut
--  akses server dan deploy ulang — pekerjaan yang tidak bisa dilakukan
--  orang yang sebenarnya memiliki akun Midtrans-nya.
--
--  Kolomnya menumpang platform_settings karena persoalannya sama:
--  konfigurasi platform bersifat singleton, dimiliki Owner, dan sudah
--  dijaga PK boolean ber-CHECK supaya baris kedua mustahil ada.
--
--  CATATAN KEAMANAN: server key tersimpan apa adanya. Yang menjaganya
--  adalah izin akses database dan endpoint yang tidak pernah
--  mengembalikannya ke klien mana pun (owner sekalipun cuma melihat
--  potongan ekornya). Enkripsi at-rest belum dipasang — lihat "utang
--  teknis yang diketahui" di CLAUDE.md.
-- =====================================================================

BEGIN;

ALTER TABLE platform_settings
    ADD COLUMN midtrans_server_key text
        CONSTRAINT ck_platform_settings_server_key_not_blank
        CHECK (midtrans_server_key IS NULL OR btrim(midtrans_server_key) <> ''),

    ADD COLUMN midtrans_client_key text
        CONSTRAINT ck_platform_settings_client_key_not_blank
        CHECK (midtrans_client_key IS NULL OR btrim(midtrans_client_key) <> ''),

    ADD COLUMN midtrans_is_production boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN platform_settings.midtrans_server_key IS
    'RAHASIA. Basic auth ke Core API dan kunci verifikasi tanda tangan notifikasi. Tidak pernah dikirim ke klien. NULL = jatuh kembali ke konfigurasi.';

COMMENT ON COLUMN platform_settings.midtrans_client_key IS
    'Bukan rahasia — dipakai Snap di sisi browser, jadi boleh dikembalikan utuh.';

COMMENT ON COLUMN platform_settings.midtrans_is_production IS
    'false memakai sandbox. Menentukan base URL Midtrans yang dipanggil.';

-- ---------------------------------------------------------------------
--  Kenapa TIDAK ada CHECK "kunci sandbox harus berpasangan dengan mode
--  sandbox", padahal salah pasang di situ berarti menagih kartu
--  sungguhan saat mengira sedang menguji?
--
--  Karena aturannya bukan milik kita: yang membedakan hanyalah awalan
--  'SB-' pada kunci terbitan Midtrans. Constraint di sini menegakkan
--  invarian data kita sendiri — keseimbangan buku besar, state machine,
--  keunikan. Mengunci konvensi penamaan pihak ketiga ke dalam DDL
--  berarti perubahan sepihak di sisi mereka hanya bisa dijawab dengan
--  migrasi.
--
--  Pemeriksaannya tetap ada, satu lapis di atas: OwnerController menolak
--  pasangan yang tidak cocok dengan 400 dan pesan yang menjelaskan.
--  Itu input manusia yang salah ketik, dan tempatnya memang di sana.
-- ---------------------------------------------------------------------

COMMIT;

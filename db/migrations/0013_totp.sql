-- =====================================================================
--  Sewa Everything — 0013_totp.sql
--  Faktor kedua (TOTP, RFC 6238) untuk akun staf.
--
--  Diminta pemilik produk 20 Agu 2026 dengan penundaan eksplisit
--  ("buat 2FA nanti aja, tapi ingetin"), lalu diminta dikerjakan
--  30 Agu 2026. Bentuknya mengikuti apa yang sudah dicatat sebagai
--  rencana waktu itu: TOTP untuk akun staf lebih dulu, karena /staf
--  adalah pintu paling berharga bagi penyerang dan jumlah akunnya
--  sedikit.
--
--  Kenapa tabel terpisah, bukan kolom di users. ActiveAccountCheck
--  membaca baris users pada SETIAP permintaan ber-token; menaruh
--  rahasia TOTP di sana berarti rahasia itu ikut termuat ke memori
--  ribuan kali sehari untuk keperluan yang hanya muncul sekali saat
--  masuk. Kolom kunci akun (0011) memang di users, dan itu tetap
--  benar — nilainya dibaca justru pada jalur yang sama dengan
--  autentikasi kata sandi.
--
--  Yang ditegakkan di berkas ini, bukan cuma di C#:
--    - kode yang sudah dipakai TIDAK dapat dipakai lagi (last_step
--      hanya boleh maju),
--    - rahasia yang sudah dikonfirmasi tidak dapat diganti di tempat,
--    - konfirmasi tidak dapat dicabut tanpa membuang barisnya,
--    - kode pemulihan mentah tidak pernah tersimpan,
--    - dan kode pemulihan yang sudah terpakai tidak dapat dihidupkan.
--
--  Tanpa yang pertama, TOTP kehilangan separuh gunanya: kode yang
--  tertangkap di jaringan atau di bahu pemiliknya masih berlaku sampai
--  jendela 30 detiknya habis, dan itu cukup untuk dipakai ulang.
-- =====================================================================

BEGIN;

CREATE TABLE user_totp (
    -- Satu akun paling banyak satu rahasia. Bukan tabel riwayat: mencabut
    -- 2FA berarti MEMBUANG barisnya, bukan menandainya mati. Rahasia yang
    -- masih tersimpan setelah dicabut hanya menambah bahan bocoran tanpa
    -- menjawab pertanyaan apa pun.
    user_id      uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,

    -- Base32 (RFC 4648) dari 20 byte acak, DISIMPAN TERENKRIPSI oleh
    -- ISecretProtector yang sama dengan server key Midtrans (AES-256-GCM,
    -- awalan enc.v1.). Kolomnya text dan bukan bytea justru karena itu:
    -- yang disimpan bentuk terbungkusnya, bukan rahasianya sendiri.
    --
    -- Kenapa tidak di-hash seperti kata sandi: verifikasi TOTP menuntut
    -- rahasia aslinya untuk menghitung ulang kodenya. Enkripsi berbalik
    -- adalah satu-satunya bentuk yang mungkin di sini, dan karena itu
    -- Security:SecretKey wajib ada sebelum 2FA dapat dinyalakan.
    secret       text NOT NULL,

    created_at   timestamptz NOT NULL DEFAULT now(),

    -- NULL = pendaftaran belum selesai. Baris yang belum dikonfirmasi
    -- TIDAK menahan siapa pun saat masuk: orang yang memindai QR lalu
    -- menutup halamannya sebelum membuktikan satu kode pun tidak boleh
    -- terkunci di luar akunnya sendiri.
    confirmed_at timestamptz,

    -- Langkah waktu (unixtime / 30) dari kode terakhir yang diterima.
    -- Inilah pengaman pemakaian ulang, dan ia dijaga trigger di bawah.
    last_step    bigint,

    CONSTRAINT ck_user_totp_secret_not_blank
        CHECK (length(btrim(secret)) > 0),

    -- Langkah hanya lahir dari verifikasi yang berhasil, dan verifikasi
    -- pertama yang berhasil adalah konfirmasinya sendiri.
    CONSTRAINT ck_user_totp_step_needs_confirmation
        CHECK (last_step IS NULL OR confirmed_at IS NOT NULL),

    CONSTRAINT ck_user_totp_confirmed_after_created
        CHECK (confirmed_at IS NULL OR confirmed_at >= created_at)
);

COMMENT ON TABLE user_totp IS
    'Faktor kedua TOTP. Satu baris = satu akun; dicabut dengan menghapus barisnya.';

COMMENT ON COLUMN user_totp.secret IS
    'Base32 dari 20 byte acak, tersimpan terenkripsi AES-256-GCM (awalan enc.v1.).';

COMMENT ON COLUMN user_totp.confirmed_at IS
    'NULL = pendaftaran belum selesai dan belum menahan apa pun saat masuk.';

COMMENT ON COLUMN user_totp.last_step IS
    'Langkah waktu kode terakhir yang diterima. Hanya boleh maju — itu yang mematikan pemakaian ulang.';


-- ---------------------------------------------------------------------
--  Kode yang sudah dipakai tidak dapat dipakai lagi.
--
--  Dijaga di sini dan bukan hanya di C# karena inilah satu-satunya
--  jaminan yang tidak dapat dilewati oleh jalur masuk mana pun —
--  termasuk psql, termasuk kekeliruan di kode aplikasi yang menyimpan
--  langkah lebih kecil daripada yang sudah tercatat.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_user_totp_monotonic() RETURNS trigger
LANGUAGE plpgsql AS $fn$
BEGIN
    IF  NEW.user_id    IS DISTINCT FROM OLD.user_id
     OR NEW.created_at IS DISTINCT FROM OLD.created_at
    THEN
        RAISE EXCEPTION 'identitas rahasia TOTP beku setelah dibuat'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.confirmed_at IS NOT NULL
       AND NEW.confirmed_at IS DISTINCT FROM OLD.confirmed_at
    THEN
        RAISE EXCEPTION 'konfirmasi TOTP tidak dapat dicabut tanpa membuang barisnya'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.confirmed_at IS NOT NULL AND NEW.secret IS DISTINCT FROM OLD.secret THEN
        RAISE EXCEPTION 'rahasia TOTP yang sudah dikonfirmasi tidak dapat diganti di tempat'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.last_step IS NOT NULL
       AND (NEW.last_step IS NULL OR NEW.last_step <= OLD.last_step)
    THEN
        RAISE EXCEPTION 'langkah TOTP tidak boleh mundur — kode yang sudah dipakai tidak dapat dipakai ulang'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $fn$;

CREATE TRIGGER user_totp_monotonic
    BEFORE UPDATE ON user_totp
    FOR EACH ROW EXECUTE FUNCTION trg_user_totp_monotonic();


-- ---------------------------------------------------------------------
--  Kode pemulihan.
--
--  Bukan pelengkap, melainkan syarat: tanpa ini, staf yang kehilangan
--  ponselnya kehilangan akunnya PERMANEN, dan tidak ada seorang pun yang
--  dapat menolongnya — tautan atur ulang kata sandi pun tidak, karena
--  faktor kedua memang ada justru untuk tidak tunduk pada penguasaan
--  kotak masuk. Untuk akun owner itu berarti platform kehilangan
--  satu-satunya superadmin-nya.
--
--  Sepuluh kode sekali-pakai, ditampilkan satu kali saat pendaftaran
--  dikonfirmasi, tersimpan hanya sebagai SHA-256-nya.
-- ---------------------------------------------------------------------
CREATE TABLE totp_recovery_codes (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Menunjuk user_totp, BUKAN users. Dengan begitu "mencabut 2FA
    -- membuang kode pemulihannya" jadi jaminan database alih-alih
    -- kedisiplinan di C#: kode yang hidup lebih lama daripada rahasia
    -- yang dipulihkannya adalah kunci cadangan untuk pintu yang sudah
    -- tidak ada. Jalur cascade dari users tetap utuh lewat user_totp.
    user_id    uuid  NOT NULL REFERENCES user_totp(user_id) ON DELETE CASCADE,

    code_hash  bytea NOT NULL,

    created_at timestamptz NOT NULL DEFAULT now(),

    used_at    timestamptz,

    CONSTRAINT ck_totp_recovery_codes_hash_size
        CHECK (octet_length(code_hash) = 32)
);

COMMENT ON TABLE totp_recovery_codes IS
    'Kode pemulihan sekali-pakai untuk akun ber-2FA. Yang mentah hanya pernah terlihat sekali.';

-- Jaring pengaman: dua baris tidak pernah memegang kode yang sama.
-- Pencocokannya sendiri TETAP disaring user_id — tanpa itu, kode milik
-- satu akun akan membuka akun lain.
CREATE UNIQUE INDEX ux_totp_recovery_codes_hash ON totp_recovery_codes (code_hash);

CREATE INDEX ix_totp_recovery_codes_live
    ON totp_recovery_codes (user_id)
    WHERE used_at IS NULL;

CREATE OR REPLACE FUNCTION trg_totp_recovery_codes_monotonic() RETURNS trigger
LANGUAGE plpgsql AS $fn$
BEGIN
    IF  NEW.id         IS DISTINCT FROM OLD.id
     OR NEW.user_id    IS DISTINCT FROM OLD.user_id
     OR NEW.code_hash  IS DISTINCT FROM OLD.code_hash
     OR NEW.created_at IS DISTINCT FROM OLD.created_at
    THEN
        RAISE EXCEPTION 'identitas kode pemulihan beku setelah diterbitkan'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.used_at IS NOT NULL AND NEW.used_at IS DISTINCT FROM OLD.used_at THEN
        RAISE EXCEPTION 'kode pemulihan yang sudah terpakai tidak dapat dipakai ulang'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $fn$;

CREATE TRIGGER totp_recovery_codes_monotonic
    BEFORE UPDATE ON totp_recovery_codes
    FOR EACH ROW EXECUTE FUNCTION trg_totp_recovery_codes_monotonic();

COMMIT;

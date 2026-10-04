-- =====================================================================
--  Sewa Everything — 0008_refresh_tokens.sql
--  Milestone 8: sesi yang bisa diperpanjang tanpa meminta kata sandi.
--
--  Utang teknis sejak milestone 2. Access token berumur 12 jam dan tidak
--  bisa diperpanjang, jadi setiap pengguna dipaksa login ulang dua kali
--  sehari. Di web itu menjengkelkan; di aplikasi mobile itu mematikan —
--  tidak ada aplikasi yang menuntut kata sandi setiap kali dibuka.
--
--  Bentuknya rotasi: satu refresh token hanya berlaku SEKALI. Ditukar
--  jadi access token baru + refresh token baru, dan yang lama ditandai
--  terpakai. Kalau token yang sudah terpakai muncul lagi, itu berarti
--  ada dua pihak yang memegang token yang sama — entah karena dicuri
--  entah karena klien keliru — dan satu-satunya jawaban yang aman adalah
--  membunuh seluruh KELUARGA token itu (family_id), bukan cuma barisnya.
--  Pencurian tidak bisa dibedakan dari bug klien, jadi yang dipilih
--  adalah kesalahan yang bisa dipulihkan pengguna: login ulang.
--
--  Yang ditegakkan di berkas ini, bukan cuma di C#:
--    - token mentah tidak pernah tersimpan (cuma SHA-256-nya),
--    - satu token tidak bisa "dikembalikan" jadi belum terpakai,
--    - pencabutan tidak bisa dibatalkan,
--    - identitas dan masa berlaku token beku setelah diterbitkan.
--  Tanpa tiga yang terakhir, deteksi pemakaian ulang cuma konvensi:
--  satu UPDATE yang keliru sudah cukup membuat token bekas hidup lagi.
-- =====================================================================

BEGIN;

CREATE TABLE refresh_tokens (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- ON DELETE CASCADE, satu-satunya FK ke users yang bukan RESTRICT di
    -- skema ini. Tabel ini bukan buku besar dan bukan jejak keputusan —
    -- ia state sesi yang diturunkan dari akunnya. Sesi yang hidup lebih
    -- lama daripada akun pemiliknya lebih berbahaya daripada sesi yang
    -- ikut terhapus bersamanya. (Akun yang punya riwayat transaksi tetap
    -- tidak bisa dihapus; yang menahannya FK di items/bookings/payments.)
    user_id        uuid  NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    -- SHA-256 dari token mentah. Yang mentah hanya pernah ada di respons
    -- HTTP dan di penyimpanan klien. Bocornya isi tabel ini karena itu
    -- tidak memberi siapa pun satu sesi pun — beda dengan password_hash
    -- yang masih perlu dilawan-tebak, di sini tebakan bahkan tidak mungkin.
    token_hash     bytea NOT NULL,

    -- Satu keluarga = satu sesi login. Rotasi menambah anggota baru ke
    -- keluarga yang sama, jadi "cabut sesi ini" = cabut satu keluarga,
    -- dan sesi lain di perangkat lain tidak ikut mati.
    family_id      uuid  NOT NULL,

    issued_at      timestamptz NOT NULL DEFAULT now(),
    expires_at     timestamptz NOT NULL,

    -- Terisi = sudah ditukar. Inilah yang membuat token sekali-pakai:
    -- penukaran dilakukan dengan UPDATE ... WHERE used_at IS NULL, jadi
    -- dua penukaran bersamaan atas token yang sama hanya bisa dimenangkan
    -- satu pihak (pola yang sama dengan anti double-booking, Aturan 4.2).
    used_at        timestamptz,

    revoked_at     timestamptz,
    revoked_reason text,

    CONSTRAINT ck_refresh_tokens_hash_size
        CHECK (octet_length(token_hash) = 32),

    CONSTRAINT ck_refresh_tokens_lifetime
        CHECK (expires_at > issued_at),

    -- Pencabutan selalu punya alasan yang tercatat, dan alasan tanpa
    -- pencabutan tidak berarti apa-apa.
    CONSTRAINT ck_refresh_tokens_revoked_consistent
        CHECK ((revoked_at IS NULL) = (revoked_reason IS NULL)),

    CONSTRAINT ck_refresh_tokens_revoked_reason
        CHECK (revoked_reason IS NULL OR revoked_reason IN ('logout', 'reuse_detected'))
);

COMMENT ON TABLE refresh_tokens IS
    'Sesi yang bisa diperpanjang. Satu baris = satu refresh token sekali-pakai; satu family_id = satu sesi login.';

COMMENT ON COLUMN refresh_tokens.token_hash IS
    'SHA-256 dari token mentah. Yang mentah tidak pernah disimpan di mana pun.';

COMMENT ON COLUMN refresh_tokens.used_at IS
    'Terisi = sudah ditukar. Kemunculannya lagi setelah ini dianggap pemakaian ulang dan membunuh seluruh keluarga.';

-- Sekaligus jalan masuk satu-satunya (lookup by hash) dan jaminan bahwa
-- dua baris tidak pernah memegang token yang sama.
CREATE UNIQUE INDEX ux_refresh_tokens_hash ON refresh_tokens (token_hash);

-- "Bunuh seluruh keluarga" dan "cabut sesi ini" berjalan lewat kolom ini.
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);

-- Sesi hidup milik seorang pengguna — dipakai pencabutan massal dan
-- (nanti) penyapu baris mati. Parsial supaya tetap ramping walau tiap
-- perpanjangan menambah satu baris bekas.
CREATE INDEX ix_refresh_tokens_user_live ON refresh_tokens (user_id, expires_at)
    WHERE used_at IS NULL AND revoked_at IS NULL;


-- ---------------------------------------------------------------------
--  Sekali terpakai, selamanya terpakai.
--
--  Deteksi pemakaian ulang berdiri di atas satu asumsi: used_at dan
--  revoked_at hanya bisa maju, tidak pernah mundur. Kalau asumsi itu
--  cuma dijaga kode C#, satu UPDATE yang keliru — atau psql iseng —
--  sudah cukup membangkitkan token bekas dan membuat seluruh mekanisme
--  ini teater. Identitas token (hash, pemilik, keluarga) dan masa
--  berlakunya ikut dibekukan: memperpanjang expires_at sebuah token yang
--  sudah diterbitkan sama dengan menerbitkan token baru tanpa jejak.
--
--  DELETE sengaja TIDAK dilarang, beda dengan payments. Tabel ini bukan
--  catatan uang; barisnya sampah begitu mati, dan penyapunya nanti perlu
--  bisa menghapusnya.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_refresh_tokens_monotonic() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF  NEW.id         IS DISTINCT FROM OLD.id
     OR NEW.user_id    IS DISTINCT FROM OLD.user_id
     OR NEW.token_hash IS DISTINCT FROM OLD.token_hash
     OR NEW.family_id  IS DISTINCT FROM OLD.family_id
     OR NEW.issued_at  IS DISTINCT FROM OLD.issued_at
     OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
    THEN
        RAISE EXCEPTION 'identitas dan masa berlaku refresh token beku setelah diterbitkan'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.used_at IS NOT NULL AND NEW.used_at IS DISTINCT FROM OLD.used_at THEN
        RAISE EXCEPTION 'refresh token yang sudah ditukar tidak bisa dipakai ulang'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.revoked_at IS NOT NULL AND NEW.revoked_at IS DISTINCT FROM OLD.revoked_at THEN
        RAISE EXCEPTION 'pencabutan refresh token tidak bisa dibatalkan'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER refresh_tokens_monotonic
    BEFORE UPDATE ON refresh_tokens
    FOR EACH ROW EXECUTE FUNCTION trg_refresh_tokens_monotonic();

COMMIT;

-- =====================================================================
--  Sewa Everything — 0012_password_reset.sql
--  Lupa kata sandi: pemilik akun memulihkan aksesnya sendiri.
--
--  Keadaan sebelum berkas ini: TIDAK ADA jalan sama sekali. /auth hanya
--  punya register, login, staff/login, refresh, logout, me — tidak ada
--  endpoint ganti sandi, apalagi reset. Pengguna yang lupa sandinya
--  kehilangan akunnya permanen, dan owner pun tidak dapat menolongnya.
--
--  Diminta pemilik produk dengan syarat yang menentukan bentuknya:
--  "sediakan lupa password AGAR TIDAK ADMIN YANG MENGUBAH". Jadi yang
--  dibangun bukan tombol "reset sandi pengguna" di panel admin — itu
--  justru yang dilarang. Yang membuktikan kepemilikan akun adalah akses
--  ke kotak masuk emailnya, bukan penilaian seorang admin. Admin tidak
--  pernah tahu, tidak pernah menyetujui, dan tidak pernah melihat sandi
--  barunya.
--
--  Bentuknya meniru refresh_tokens (0008), dan itu disengaja — soalnya
--  memang serupa: kredensial berumur pendek yang hanya boleh ditukar
--  sekali. Yang ditegakkan di berkas ini, bukan cuma di C#:
--    - token mentah tidak pernah tersimpan (cuma SHA-256-nya),
--    - satu token tidak bisa "dikembalikan" jadi belum terpakai,
--    - identitas dan masa berlaku beku setelah diterbitkan.
--  Tanpa yang kedua, sekali-pakai cuma konvensi: satu UPDATE keliru
--  sudah cukup menghidupkan kembali tautan yang sudah dipakai.
-- =====================================================================

BEGIN;

CREATE TABLE password_resets (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- ON DELETE CASCADE, sepola refresh_tokens dan dengan alasan yang
    -- sama: ini bukan buku besar dan bukan jejak keputusan, melainkan
    -- state berumur pendek yang diturunkan dari akunnya. Tautan reset
    -- yang hidup lebih lama daripada akun pemiliknya adalah lubang,
    -- bukan riwayat yang perlu dijaga.
    user_id    uuid  NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    -- SHA-256 dari token mentah. Yang mentah hanya pernah ada di badan
    -- email dan di URL yang diketuk pemiliknya. Bocornya isi tabel ini
    -- karena itu tidak memberi siapa pun satu akun pun — termasuk tidak
    -- kepada admin database, yang justru pihak yang ingin dijauhkan dari
    -- kemampuan mengambil alih akun orang.
    token_hash bytea NOT NULL,

    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,

    -- Terisi = tidak dapat ditukar lagi. Dua sebab, dan keduanya sama
    -- benarnya: tautannya memang sudah dipakai, ATAU reset lain milik
    -- pengguna yang sama sudah berhasil lebih dulu. Yang kedua penting —
    -- setelah sandi berganti, tautan lama yang masih di kotak masuk
    -- tidak boleh membuka apa pun lagi.
    used_at    timestamptz,

    CONSTRAINT ck_password_resets_hash_size
        CHECK (octet_length(token_hash) = 32),

    CONSTRAINT ck_password_resets_lifetime
        CHECK (expires_at > created_at)
);

COMMENT ON TABLE password_resets IS
    'Tautan atur ulang kata sandi. Satu baris = satu token sekali-pakai berumur pendek.';

COMMENT ON COLUMN password_resets.token_hash IS
    'SHA-256 dari token mentah. Yang mentah tidak pernah disimpan di mana pun.';

COMMENT ON COLUMN password_resets.used_at IS
    'Terisi = tidak dapat ditukar lagi, baik karena sudah dipakai maupun karena reset lain sudah berhasil.';

-- Sekaligus jalan masuk satu-satunya (lookup by hash) dan jaminan bahwa
-- dua baris tidak pernah memegang token yang sama.
CREATE UNIQUE INDEX ux_password_resets_hash ON password_resets (token_hash);

-- Melayani tiga hal sekaligus: jeda antar permintaan per akun, pembatalan
-- massal saat sandi berhasil diganti, dan pembersihan baris mati.
CREATE INDEX ix_password_resets_user ON password_resets (user_id, created_at DESC);


-- ---------------------------------------------------------------------
--  Sekali terpakai, selamanya terpakai.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_password_resets_monotonic() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF  NEW.id         IS DISTINCT FROM OLD.id
     OR NEW.user_id    IS DISTINCT FROM OLD.user_id
     OR NEW.token_hash IS DISTINCT FROM OLD.token_hash
     OR NEW.created_at IS DISTINCT FROM OLD.created_at
     OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
    THEN
        RAISE EXCEPTION 'identitas dan masa berlaku token reset beku setelah diterbitkan'
            USING ERRCODE = 'check_violation';
    END IF;

    IF OLD.used_at IS NOT NULL AND NEW.used_at IS DISTINCT FROM OLD.used_at THEN
        RAISE EXCEPTION 'token reset yang sudah terpakai tidak bisa dipakai ulang'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER password_resets_monotonic
    BEFORE UPDATE ON password_resets
    FOR EACH ROW EXECUTE FUNCTION trg_password_resets_monotonic();


-- ---------------------------------------------------------------------
--  Sandi berganti = seluruh sesi lama diakhiri.
--
--  Kalau sandi diganti justru karena akunnya dicurigai diambil orang,
--  membiarkan refresh token lama tetap hidup membuat penggantian sandi
--  itu tidak ada gunanya: yang menyusup tetap dapat memperpanjang
--  sesinya tanpa pernah menyentuh sandi baru. Pencabutannya butuh alasan
--  yang sah menurut ck_refresh_tokens_revoked_reason di 0008, jadi
--  daftarnya ditambah satu di sini.
-- ---------------------------------------------------------------------
ALTER TABLE refresh_tokens
    DROP CONSTRAINT ck_refresh_tokens_revoked_reason;

ALTER TABLE refresh_tokens
    ADD CONSTRAINT ck_refresh_tokens_revoked_reason
        CHECK (revoked_reason IS NULL
               OR revoked_reason IN ('logout', 'reuse_detected', 'password_reset'));

COMMIT;

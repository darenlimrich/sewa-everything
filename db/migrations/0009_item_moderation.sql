-- =====================================================================
--  Sewa Everything — 0009_item_moderation.sql
--  Milestone 9: admin menurunkan listing yang bermasalah.
--
--  Tabel ROLE menjanjikan "moderasi listing" untuk admin, dan
--  design-system §8 mendaftarkannya sebagai layar yang harus ada. Sampai
--  sekarang tidak ada satu pun jalan untuk itu: barang yang menipu,
--  ilegal, atau salah kategori tetap tayang sampai pemiliknya sendiri
--  berkenan menurunkannya — dan pemilik yang menipu justru yang paling
--  tidak berkenan.
--
--  KENAPA KOLOM BARU, BUKAN status = 'inactive'
--
--  items.status milik PEMILIK barang. Ia yang menyalakan dan mematikan
--  listing-nya lewat PUT /items/{id}, dan itu memang haknya. Kalau
--  moderasi memakai kolom yang sama, penurunan oleh admin bisa dibatalkan
--  pemiliknya sendiri satu detik kemudian dengan menekan "aktifkan" —
--  dan tidak ada yang bisa membedakan listing yang diturunkan admin dari
--  listing yang sedang diistirahatkan pemiliknya.
--
--  Karena itu penangguhan hidup di kolomnya sendiri. Terlihat di katalog
--  = status 'active' DAN tidak sedang ditangguhkan. Pemilik menguasai
--  suku pertama, admin menguasai suku kedua, dan tidak ada yang bisa
--  membatalkan keputusan yang lain.
--
--  Alasannya dicatat karena penurunan listing adalah keputusan terhadap
--  penghasilan orang. Sepola disputes.resolution: yang memutus harus
--  meninggalkan sebabnya, dan jejaknya menunjuk ke orangnya lewat
--  suspended_by.
-- =====================================================================

BEGIN;

ALTER TABLE items
    ADD COLUMN suspended_at      timestamptz,
    ADD COLUMN suspended_by      uuid REFERENCES users(id) ON DELETE RESTRICT,
    ADD COLUMN suspension_reason text;

COMMENT ON COLUMN items.suspended_at IS
    'Terisi = listing diturunkan admin. Hilang dari katalog publik apa pun status-nya, dan pemiliknya tidak bisa mengembalikannya sendiri.';
COMMENT ON COLUMN items.suspended_by IS
    'Admin/owner yang memutuskan. ON DELETE RESTRICT: jejak keputusan tidak boleh putus, sepola users.verified_by.';
COMMENT ON COLUMN items.suspension_reason IS
    'Sebab penurunan, dibaca pemiliknya. Wajib ada selama listing ditangguhkan.';

-- Ketiganya satu keputusan, jadi ketiganya hidup dan mati bersama. Tanpa
-- ini, baris bisa berakhir "ditangguhkan tanpa alasan dan tanpa yang
-- menangguhkan" — persis bentuk yang tidak bisa dipertanggungjawabkan ke
-- pemilik barangnya.
ALTER TABLE items
    ADD CONSTRAINT ck_items_suspension_whole CHECK (
        (suspended_at IS NULL AND suspended_by IS NULL AND suspension_reason IS NULL)
        OR
        (suspended_at IS NOT NULL AND suspended_by IS NOT NULL
            AND suspension_reason IS NOT NULL AND btrim(suspension_reason) <> '')
    );

-- ---------------------------------------------------------------------
--  Yang menurunkan listing harus admin atau owner.
--
--  Sepola trg_items_seller_role di 0001: peran ditegakkan di database,
--  bukan cuma di controller, karena kolom ini menentukan penghasilan
--  orang hilang atau tidak. Seller tidak boleh bisa menurunkan listing
--  pesaingnya lewat jalur apa pun.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_items_suspended_by_role() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE v_role text;
BEGIN
    IF NEW.suspended_by IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT role INTO v_role FROM users WHERE id = NEW.suspended_by;

    IF v_role NOT IN ('admin', 'owner') THEN
        RAISE EXCEPTION 'items.suspended_by harus admin atau owner, bukan %', v_role
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER items_suspended_by_role
    BEFORE INSERT OR UPDATE OF suspended_by ON items
    FOR EACH ROW EXECUTE FUNCTION trg_items_suspended_by_role();

-- ---------------------------------------------------------------------
--  Index penelusuran katalog ikut menyempit.
--
--  ix_items_browse melayani "barang yang terlihat publik". Definisi itu
--  baru saja berubah: bukan lagi sekadar status 'active', tapi juga tidak
--  sedang ditangguhkan. Kalau index-nya tidak ikut, ia berhenti cocok
--  dengan predikat kuerinya dan planner tidak akan memakainya lagi.
-- ---------------------------------------------------------------------
DROP INDEX ix_items_browse;
CREATE INDEX ix_items_browse ON items (category, price)
    WHERE status = 'active' AND suspended_at IS NULL;

-- Antrean moderasi membaca "yang sedang ditangguhkan", dan itu selalu
-- sedikit dibanding seluruh katalog — parsial, seperti ix_bookings_return_due.
CREATE INDEX ix_items_suspended ON items (suspended_at DESC)
    WHERE suspended_at IS NOT NULL;

COMMIT;

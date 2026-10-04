-- =====================================================================
--  Sewa Everything — 0017_item_review.sql
--  Listing baru ditinjau admin sebelum tampil di katalog.
--
--  Diminta pemilik produk 21 Sep 2026: "ketika barang ingin dijual
--  harus diverifikasi terlebih dahulu oleh admin."
--
--  ---------------------------------------------------------------
--  KENAPA KOLOM BARU, BUKAN suspended_at YANG DIPAKAI TERBALIK
--
--  0009 sudah punya mesin "admin menyembunyikan listing" — penangguhan.
--  Godaannya: lahirkan tiap listing dalam keadaan ditangguhkan, lalu
--  "pulihkan" berarti "setujui". Itu salah pada dua hal sekaligus.
--  Pertama, ck_items_suspension_whole menuntut ALASAN dan PENANGGUH
--  untuk tiap penangguhan, dan listing yang baru lahir tidak punya
--  keduanya — belum ada admin yang memutuskan apa pun. Kedua, artinya
--  berbeda: ditangguhkan = "pernah tayang, lalu diputus bermasalah";
--  menunggu = "belum pernah dilihat siapa pun". Pemilik barang berhak
--  tahu yang mana, dan admin berikutnya juga.
--
--  Jadi peninjauan hidup di kolomnya sendiri, dan "terlihat publik"
--  bertambah satu suku:
--
--    status = 'active'  AND suspended_at IS NULL  AND review_status = 'approved'
--    └── pemilik ──┘        └─ admin: penurunan ─┘    └── admin: peninjauan ──┘
--
--  KENAPA TIGA KEADAAN, BUKAN BOOLEAN
--
--  'pending' dan 'rejected' sama-sama "tidak disetujui", tetapi pemilik
--  barang harus diperlakukan berbeda: yang pertama tinggal menunggu,
--  yang kedua harus MEMPERBAIKI sesuatu — dan alasannya wajib ada,
--  sepola suspension_reason. Boolean is_approved tidak dapat menyimpan
--  "ditolak karena apa".
--
--  KENAPA BARIS LAMA DI-BACKFILL 'approved' DENGAN reviewed_by NULL
--
--  Listing yang sudah ada dipasang di bawah aturan lama, dan menyembunyikan
--  seluruh katalog sampai admin meninjau ratusan baris satu per satu
--  bukan yang diminta. Tetapi menuliskan seorang admin sebagai
--  peninjaunya adalah kebohongan — tidak ada yang meninjau. Karena itu
--  'approved' mengizinkan reviewed_by NULL, dan NULL di sana berarti
--  "disetujui otomatis saat aturan ini mulai berlaku". Yang menjaga
--  bahwa TIDAK ADA persetujuan baru tanpa peninjau adalah trigger
--  items_review_decision, yang sengaja dipasang SETELAH backfill.
--
--  'rejected' tidak punya pengecualian itu: penolakan selalu keputusan
--  seseorang, dan alasannya dibaca pemilik barang.
-- =====================================================================

BEGIN;

ALTER TABLE items
    ADD COLUMN review_status    text NOT NULL DEFAULT 'pending',
    ADD COLUMN reviewed_at      timestamptz,
    ADD COLUMN reviewed_by      uuid REFERENCES users(id) ON DELETE RESTRICT,
    ADD COLUMN rejection_reason text;

COMMENT ON COLUMN items.review_status IS
    'pending = belum ditinjau admin, tidak tampil di katalog. approved = lolos. rejected = ditolak, alasannya di rejection_reason; pemilik memperbaiki lalu otomatis kembali pending.';
COMMENT ON COLUMN items.reviewed_at IS
    'Kapan keputusan terakhir dibuat. NULL hanya saat pending.';
COMMENT ON COLUMN items.reviewed_by IS
    'Admin yang memutus. NULL pada approved = disetujui otomatis saat migrasi 0017 (listing yang sudah ada sebelum aturan ini). ON DELETE RESTRICT sepola suspended_by.';
COMMENT ON COLUMN items.rejection_reason IS
    'Sebab penolakan, dibaca pemiliknya. Wajib ada selama rejected, wajib kosong selainnya.';

UPDATE items SET review_status = 'approved', reviewed_at = now();

ALTER TABLE items
    ADD CONSTRAINT ck_items_review_status
        CHECK (review_status IN ('pending', 'approved', 'rejected')),
    ADD CONSTRAINT ck_items_review_whole CHECK (
        (review_status = 'pending'
            AND reviewed_at IS NULL AND reviewed_by IS NULL AND rejection_reason IS NULL)
        OR
        (review_status = 'approved'
            AND reviewed_at IS NOT NULL AND rejection_reason IS NULL)
        OR
        (review_status = 'rejected'
            AND reviewed_at IS NOT NULL AND reviewed_by IS NOT NULL
            AND rejection_reason IS NOT NULL AND btrim(rejection_reason) <> '')
    );

-- ---------------------------------------------------------------------
--  Yang meninjau harus admin atau owner, dan tiap KEPUTUSAN harus punya
--  peninjau.
--
--  Aturan pertama sepola items_suspended_by_role: kolom ini menentukan
--  listing seseorang tayang atau tidak, jadi seller tidak boleh bisa
--  "menyetujui" listing-nya sendiri lewat jalur apa pun.
--
--  Aturan kedua yang membuat pengecualian reviewed_by NULL di CHECK
--  aman: berpindah KE approved/rejected tanpa peninjau ditolak. Baris
--  yang sudah approved sejak backfill tidak berpindah, jadi tidak kena.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_items_review_decision() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE v_role text;
BEGIN
    IF NEW.reviewed_by IS NOT NULL THEN
        SELECT role INTO v_role FROM users WHERE id = NEW.reviewed_by;

        IF v_role NOT IN ('admin', 'owner') THEN
            RAISE EXCEPTION 'items.reviewed_by harus admin atau owner, bukan %', v_role
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    IF NEW.review_status IN ('approved', 'rejected')
       AND NEW.reviewed_by IS NULL
       AND (TG_OP = 'INSERT' OR OLD.review_status IS DISTINCT FROM NEW.review_status) THEN
        RAISE EXCEPTION 'keputusan peninjauan listing harus punya peninjau (reviewed_by)'
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER items_review_decision
    BEFORE INSERT OR UPDATE OF review_status, reviewed_by ON items
    FOR EACH ROW EXECUTE FUNCTION trg_items_review_decision();

-- ---------------------------------------------------------------------
--  Index katalog ikut menyempit, sepola 0009: definisi "terlihat publik"
--  berubah, dan index parsialnya harus tetap cocok dengan predikat
--  kuerinya supaya planner masih memakainya.
-- ---------------------------------------------------------------------
DROP INDEX ix_items_browse;
CREATE INDEX ix_items_browse ON items (category, price)
    WHERE status = 'active' AND suspended_at IS NULL AND review_status = 'approved';

-- Antrean peninjauan dibaca urut lahirnya (yang paling lama menunggu
-- di atas), dan selalu sedikit dibanding seluruh katalog.
CREATE INDEX ix_items_review_pending ON items (created_at)
    WHERE review_status = 'pending';

COMMIT;

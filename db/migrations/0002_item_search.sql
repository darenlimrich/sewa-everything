-- =====================================================================
--  Sewa Everything — 0002_item_search.sql
--  Milestone 3: pencarian teks + kalender ketersediaan.
--
--  Dijanjikan di docs/schema-decisions.md bagian "Sengaja belum dibuat":
--  kolom tsvector GENERATED + index GIN, migrasi terpisah.
-- =====================================================================

BEGIN;


-- ---------------------------------------------------------------------
--  PENCARIAN TEKS
--
--  Konfigurasi 'indonesian' ditulis eksplisit, bukan mengandalkan
--  default_text_search_config. Alasannya bukan gaya: bentuk dua argumen
--  to_tsvector(regconfig, text) itu IMMUTABLE, sedangkan bentuk satu
--  argumen cuma STABLE karena hasilnya ikut berubah kalau setelan sesi
--  berubah. Kolom GENERATED menolak ekspresi yang tidak IMMUTABLE, jadi
--  bentuk satu argumen memang tidak akan bisa dipakai di sini.
--
--  Bobot: judul (A) > kategori (B) > deskripsi (C). ts_rank membaca bobot
--  ini, jadi barang yang katanya ada di judul naik di atas barang yang
--  katanya cuma lewat di deskripsi.
-- ---------------------------------------------------------------------
ALTER TABLE items ADD COLUMN search_vector tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('indonesian', title), 'A')
        || setweight(to_tsvector('indonesian', category), 'B')
        || setweight(to_tsvector('indonesian', coalesce(description, '')), 'C')
    ) STORED;

-- Sengaja TIDAK parsial 'WHERE status = active', beda dengan ix_items_browse.
-- Moderasi listing (milestone 9) harus bisa mencari barang yang sudah
-- dinonaktifkan juga, dan index parsial tidak bisa melayani query itu.
CREATE INDEX ix_items_search ON items USING gin (search_vector);


-- ---------------------------------------------------------------------
--  KALENDER KETERSEDIAAN
--
--  Dua tabel berbeda memblok waktu untuk item yang sama: booking yang
--  masih hidup, dan blackout yang dipasang seller. Setiap kali ada kode
--  yang menanyakan "item ini kosong tidak?", kode itu harus memeriksa
--  KEDUANYA — dan itulah persis bentuk bug yang menunggu terjadi: satu
--  code path ingat memeriksa blackout, satu lagi lupa.
--
--  View ini membuat jawabannya cuma punya satu definisi. GET /items
--  (filter tanggal available) dan GET /items/{id} (kalender) membaca
--  view yang sama, bukan masing-masing menyusun ulang UNION-nya.
--
--  PENTING: daftar status di bawah WAJIB sama persis dengan klausa WHERE
--  di constraint `no_overlap` pada tabel bookings. Kalau salah satu
--  berubah tanpa yang lain, kalender akan menampilkan slot kosong yang
--  sebenarnya ditolak database saat di-booking — atau sebaliknya.
--  db/verify_0002.sql membandingkan keduanya dan gagal kalau melenceng.
-- ---------------------------------------------------------------------
CREATE VIEW item_blocked_ranges AS
    SELECT b.item_id,
           b.starts_at,
           b.ends_at,
           'booking'::text AS source,
           b.id            AS source_id
    FROM bookings b
    WHERE b.status IN ('pending', 'confirmed', 'active')

    UNION ALL

    SELECT bo.item_id,
           bo.starts_at,
           bo.ends_at,
           'blackout'::text AS source,
           bo.id            AS source_id
    FROM item_blackouts bo;

-- Pencarian "kosong antara tanggal X dan Y" memeriksa blackout lewat
-- starts_at/ends_at, bukan lewat operator range. Index gist milik
-- constraint no_blackout_overlap ada di atas (item_id, during), jadi
-- tidak melayani bentuk query itu.
CREATE INDEX ix_item_blackouts_window ON item_blackouts (item_id, starts_at, ends_at);


COMMIT;

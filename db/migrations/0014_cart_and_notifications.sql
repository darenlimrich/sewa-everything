-- =====================================================================
--  Sewa Everything — 0014_cart_and_notifications.sql
--  Keranjang sewa + penanda "notifikasi sudah dilihat".
--
--  Diminta pemilik produk 8 Sep 2026, keduanya dalam satu jawaban.
--
--  ---------------------------------------------------------------
--  cart_items — kenapa ia BUKAN booking, dan kenapa ia menyimpan
--  tanggal.
--
--  Keranjang di marketplace jual-beli hanya menyimpan "barang apa".
--  Di marketplace sewa itu tidak cukup: yang menentukan harga, dan
--  yang menentukan bisa atau tidaknya sebuah barang diambil, adalah
--  RENTANG WAKTU-nya. Baris keranjang tanpa tanggal karena itu tidak
--  dapat berubah jadi pengajuan sewa tanpa menanyakan ulang segalanya,
--  dan keranjang yang menanyakan ulang segalanya bukan keranjang.
--
--  Yang TIDAK dilakukan baris ini, dan ini pembeda pentingnya dari
--  bookings: ia TIDAK menahan slot. Tidak ada exclusion constraint di
--  sini, dan itu disengaja. Aturan 4.2 menjaga tabrakan pada saat sewa
--  benar-benar diajukan (POST /bookings); kalau keranjang ikut
--  menahan, satu orang dapat mengunci seluruh katalog tanpa membayar
--  apa pun dan tanpa batas waktu — persis bahaya yang ditutup jendela
--  hold di bookings, dibuka lagi lewat pintu belakang.
--
--  Konsekuensinya wajib disadari, dan UI harus mengatakannya:
--  barang di keranjang DAPAT diambil orang lain lebih dulu. Keranjang
--  ini niat, bukan hak.
--
--  UNIQUE (renter_id, item_id) — satu barang paling banyak satu baris.
--  Menambahkan barang yang sudah ada berarti MENGGANTI tanggalnya,
--  bukan menumpuk baris kedua untuk barang yang sama. Dua baris untuk
--  satu barang hanya dapat berarti dua sewa terpisah atas barang yang
--  sama, dan itu justru bentuk yang exclusion constraint di bookings
--  akan tolak begitu diajukan.
--
--  Tidak ada snapshot harga di sini. Aturan 4.1: harga dihitung server
--  saat sewa diajukan. Menyalin harga ke keranjang berarti menyimpan
--  angka yang akan berbohong begitu pemiliknya mengubah tarif.
--
--  ---------------------------------------------------------------
--  users.notifications_seen_at — kenapa satu kolom, bukan tabel
--  notifikasi.
--
--  Notifikasi di produk ini DITURUNKAN dari bookings, tidak ditulis
--  sebagai kejadian tersendiri. Alasannya dua.
--
--  Pertama, tidak ada satu pun tabel riwayat kejadian di sini; yang
--  ada bookings.status beserta updated_at-nya, dan itu memang sudah
--  merupakan "apa yang terjadi terakhir pada sewa ini, dan kapan".
--  Tabel notifikasi kedua yang menyalin hal yang sama adalah dua
--  sumber kebenaran untuk satu fakta, dan cepat atau lambat salah
--  satunya berbohong.
--
--  Kedua, tabel kejadian hanya terisi oleh kejadian BARU. Di instalasi
--  yang sudah berjalan — termasuk database dev ini — daftarnya akan
--  kosong sampai ada yang bergerak, padahal sewanya jelas-jelas ada.
--  Feed turunan menampilkan keadaan yang benar sejak permintaan
--  pertama.
--
--  Yang tidak dapat diturunkan cuma satu: sudah dibaca atau belum.
--  Itulah kolom ini. Satu timestamp per akun, bukan per notifikasi —
--  "sudah saya lihat sampai kapan". Konsekuensinya: menandai terbaca
--  berlaku untuk seluruh daftar sekaligus, dan tidak ada cara menandai
--  satu baris saja. Itu pertukaran yang disadari; per-baris menuntut
--  tabel yang justru sedang dihindari.
--
--  NULL berarti belum pernah membuka daftarnya sama sekali — semua
--  dihitung belum terbaca. Itu keadaan yang benar untuk akun baru
--  maupun untuk seluruh akun yang sudah ada saat migrasi ini jalan.
-- =====================================================================

BEGIN;

ALTER TABLE users
    ADD COLUMN notifications_seen_at timestamptz;

CREATE TABLE cart_items (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    renter_id  uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    item_id    uuid        NOT NULL REFERENCES items(id) ON DELETE CASCADE,

    start_at   timestamptz NOT NULL,
    end_at     timestamptz NOT NULL,

    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_cart_items_range CHECK (end_at > start_at)
);

CREATE UNIQUE INDEX ux_cart_items_renter_item ON cart_items (renter_id, item_id);

CREATE INDEX ix_cart_items_renter ON cart_items (renter_id, created_at DESC);

CREATE TRIGGER cart_items_set_updated_at
    BEFORE UPDATE ON cart_items
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

COMMIT;

-- =====================================================================
--  Sewa Everything — 0016_delivery_and_addresses.sql
--  Alamat pengiriman + biaya antar.
--
--  Diminta pemilik produk 10 Sep 2026: "tambahin biaya transportasi deh
--  kali ini (biaya antar) (mirip kayak shopee gitu) … nanti disuruh
--  masukan alamat (bisa dibuat default) abis itu nanti ada opsi
--  pengiriman dll."
--
--  ---------------------------------------------------------------
--  KENAPA TARIFNYA MILIK BARANG, BUKAN MILIK PLATFORM
--
--  NON-GOALS melarang kurir/logistik internal: "Serah-terima diatur
--  seller & penyewa langsung." Jadi yang mengantar barang adalah
--  PEMILIKNYA, dan dialah satu-satunya pihak yang tahu ongkosnya —
--  jarak dari gudangnya, ukuran barangnya, apakah ia perlu menyewa
--  kendaraan. Satu tarif tetap milik platform akan salah untuk semua
--  orang sekaligus: terlalu mahal untuk antar dalam satu kecamatan,
--  terlalu murah untuk lintas kota.
--
--  Tarif per zona sempat dipertimbangkan dan ditolak untuk sekarang:
--  items TIDAK punya kolom kota — kotanya masih menempel di dalam
--  title ("Lensa Canon RF 50mm — Bandung"), cacat yang sudah tercatat
--  sejak 9 Sep. Zona menuntut kolom itu dibereskan lebih dulu, plus
--  tabel tarif per pasangan zona. Itu pekerjaan tersendiri.
--
--  KENAPA items.delivery_fee BOLEH NULL, DAN APA BEDANYA DENGAN 0
--
--  Tiga keadaan yang benar-benar berbeda, dan ketiganya nyata:
--    NULL  = pemilik TIDAK melayani antar. Penyewa hanya boleh ambil
--            sendiri; opsi "Diantar" tidak digambar sama sekali.
--    0     = melayani antar, GRATIS. Opsi "Diantar" digambar Rp0.
--    > 0   = melayani antar, berbayar.
--  Kalau NULL dan 0 disamakan, "gratis ongkir" mustahil dinyatakan —
--  dan itu justru penawaran yang paling mungkin dipakai pemilik barang
--  untuk bersaing.
--
--  KENAPA ALAMATNYA DI-SNAPSHOT KE bookings
--
--  Sepola price_snapshot dan deposit_amount: alamat yang dipakai
--  sebuah sewa harus tetap terbaca apa adanya walau penyewa kemudian
--  menyunting atau menghapus alamat itu dari buku alamatnya. FK ke
--  addresses saja tidak cukup — ON DELETE akan memaksa memilih antara
--  melarang penghapusan (buku alamat jadi tidak bisa dirapikan) atau
--  kehilangan alamat pengantaran sewa yang sedang berjalan. Snapshot
--  teks menutup keduanya, dan pemilik barang tetap dapat membaca ke
--  mana ia harus mengantar walau alamatnya sudah dihapus penyewa.
--
--  KENAPA renter_total DAN seller_gross DIBUAT ULANG
--
--  Keduanya GENERATED ALWAYS ... STORED — "dihitung DATABASE, tidak
--  bisa dipalsukan client" (0001). PostgreSQL tidak dapat mengubah
--  ekspresi kolom generated di tempat, jadi satu-satunya jalan adalah
--  membuang lalu membuatnya kembali. Diperiksa lebih dulu: NOL
--  constraint, NOL index, dan NOL view yang menggantung pada kedua
--  kolom itu, jadi tidak ada yang ikut runtuh.
--
--  Biaya antar masuk ke renter_total (penyewa membayarnya di muka,
--  sepola deposit) dan masuk ke seller_gross SETELAH komisi — karena
--  keputusan pemilik produk 10 Sep 2026: ongkir TIDAK kena komisi.
--  Ia penggantian bensin dan waktu pemilik barang, bukan pendapatan
--  sewa; mengambil potongan darinya berarti pemilik rugi setiap kali
--  ia mengantar.
--
--  KENAPA delivery_charge JADI JENIS PEMBAYARAN TERSENDIRI
--
--  payments adalah buku besar append-only (keputusan terkunci #3):
--  satu baris = satu pergerakan dana, dan tiap rupiah harus dapat
--  ditelusuri ke asalnya. Menumpangkan ongkir ke dalam rent_charge
--  akan membuat "berapa yang benar-benar sewa" tidak dapat dijawab
--  lagi — dan justru angka itulah yang dipakai BookingCompletion untuk
--  menghitung komisi. Barisnya sendiri karena itu wajib.
--
--  delivery_refund ada karena keputusan terkunci #14: sewa yang
--  dibatalkan direfund PENUH. Pembatalan selalu terjadi sebelum
--  pengantaran — state machine tidak mengizinkan active -> cancelled —
--  jadi ongkir yang sudah dibayar memang belum menghasilkan apa pun
--  dan wajib kembali utuh.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
--  ADDRESSES — buku alamat penyewa.
-- ---------------------------------------------------------------------
CREATE TABLE addresses (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id        uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,

    label          text NOT NULL CHECK (btrim(label) <> '' AND length(label) <= 40),
    recipient_name text NOT NULL CHECK (btrim(recipient_name) <> '' AND length(recipient_name) <= 120),
    phone          text NOT NULL CHECK (btrim(phone) <> '' AND length(phone) <= 30),
    full_address   text NOT NULL CHECK (btrim(full_address) <> '' AND length(full_address) <= 500),
    notes          text CHECK (notes IS NULL OR (btrim(notes) <> '' AND length(notes) <= 200)),

    is_default     boolean     NOT NULL DEFAULT false,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_addresses_user ON addresses (user_id);

-- Satu alamat utama per pengguna, sepola ux_payout_accounts_default.
CREATE UNIQUE INDEX ux_addresses_default ON addresses (user_id)
    WHERE is_default;

CREATE TRIGGER addresses_set_updated_at
    BEFORE UPDATE ON addresses
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();


-- ---------------------------------------------------------------------
--  ITEMS — tarif antar yang ditetapkan pemiliknya.
-- ---------------------------------------------------------------------
ALTER TABLE items
    ADD COLUMN delivery_fee numeric(12,2)
        CHECK (delivery_fee IS NULL OR (delivery_fee >= 0 AND delivery_fee <= 999999999999.99));


-- ---------------------------------------------------------------------
--  BOOKINGS — metode, tarif, dan salinan alamat.
-- ---------------------------------------------------------------------
ALTER TABLE bookings
    ADD COLUMN delivery_method text NOT NULL DEFAULT 'pickup'
        CHECK (delivery_method IN ('pickup','delivery')),
    ADD COLUMN delivery_fee numeric(12,2) NOT NULL DEFAULT 0
        CHECK (delivery_fee >= 0 AND delivery_fee <= 999999999999.99),
    ADD COLUMN delivery_recipient text
        CHECK (delivery_recipient IS NULL OR btrim(delivery_recipient) <> ''),
    ADD COLUMN delivery_phone text
        CHECK (delivery_phone IS NULL OR btrim(delivery_phone) <> ''),
    ADD COLUMN delivery_address text
        CHECK (delivery_address IS NULL OR btrim(delivery_address) <> ''),
    ADD COLUMN delivery_notes text
        CHECK (delivery_notes IS NULL OR btrim(delivery_notes) <> '');

-- Ambil sendiri tidak punya ongkos dan tidak punya alamat; diantar
-- wajib punya ketiganya. Tanpa ini, "pickup" bertarif Rp50.000 atau
-- "delivery" tanpa alamat sama-sama dapat tersimpan, dan pemilik
-- barang tidak punya cara tahu ke mana barangnya harus dikirim.
ALTER TABLE bookings
    ADD CONSTRAINT ck_bookings_delivery_shape CHECK (
        (delivery_method = 'pickup'
            AND delivery_fee = 0
            AND delivery_recipient IS NULL
            AND delivery_phone     IS NULL
            AND delivery_address   IS NULL
            AND delivery_notes     IS NULL)
     OR (delivery_method = 'delivery'
            AND delivery_recipient IS NOT NULL
            AND delivery_phone     IS NOT NULL
            AND delivery_address   IS NOT NULL)
    );


-- ---------------------------------------------------------------------
--  Kolom turunan dibuat ulang supaya ongkir ikut terhitung.
-- ---------------------------------------------------------------------
ALTER TABLE bookings DROP COLUMN renter_total;
ALTER TABLE bookings DROP COLUMN seller_gross;

ALTER TABLE bookings
    ADD COLUMN renter_total numeric(14,2) GENERATED ALWAYS AS (
        total_rent + deposit_amount + delivery_fee
        + CASE WHEN platform_fee_mode = 'on_top' THEN platform_fee_amount ELSE 0 END
    ) STORED;

-- Ongkir ditambahkan SESUDAH komisi: ia tidak kena potongan.
ALTER TABLE bookings
    ADD COLUMN seller_gross numeric(14,2) GENERATED ALWAYS AS (
        total_rent
        - CASE WHEN platform_fee_mode = 'deduct' THEN platform_fee_amount ELSE 0 END
        + delivery_fee
    ) STORED;


-- ---------------------------------------------------------------------
--  PAYMENTS — dua jenis baru di buku besar.
-- ---------------------------------------------------------------------
-- DUA constraint membatasi kind, bukan satu: payments_kind_check adalah
-- CHECK inline pada kolomnya (daftar nilai yang sah), sementara
-- ck_payments_kind_direction memasangkan tiap kind dengan arahnya.
-- Melewatkan yang pertama membuat delivery_charge tetap ditolak.
ALTER TABLE payments DROP CONSTRAINT payments_kind_check;

ALTER TABLE payments
    ADD CONSTRAINT payments_kind_check CHECK (
        kind IN ('rent_charge','deposit_charge','delivery_charge',
                 'platform_fee','deposit_forfeit',
                 'rent_refund','deposit_refund','delivery_refund',
                 'seller_payout')
    );

ALTER TABLE payments DROP CONSTRAINT ck_payments_kind_direction;

ALTER TABLE payments
    ADD CONSTRAINT ck_payments_kind_direction CHECK (
           (kind IN ('rent_charge','deposit_charge','delivery_charge')          AND direction = 'in')
        OR (kind IN ('rent_refund','deposit_refund','delivery_refund',
                     'seller_payout')                                           AND direction = 'out')
        OR (kind IN ('platform_fee','deposit_forfeit')                          AND direction = 'internal')
    );

COMMIT;

-- =====================================================================
--  Sewa Everything — 0010_reference_codes.sql
--  Nomor sewa dan nomor transaksi yang bisa disebut manusia.
--
--  KENAPA BUKAN UUID-NYA SAJA
--
--  Sampai sekarang satu-satunya identitas sebuah sewa adalah uuid-nya, dan
--  panel staf menampilkan delapan karakter pertamanya ("f8db49f6").
--  Potongan itu punya dua cacat sekaligus:
--
--    1. Tidak bisa diucapkan. Penyewa yang menelepon CS tidak punya apa
--       pun untuk disebut selain nama barang dan tanggal — dan itu tidak
--       cukup begitu ia menyewa barang yang sama dua kali.
--    2. Bukan identitas, cuma awalan. Delapan karakter pertama uuid TIDAK
--       dijamin unik oleh apa pun; ia kebetulan unik selama barisnya
--       sedikit. Menyebutnya "id" di layar staf adalah janji yang tidak
--       ditepati siapa-siapa.
--
--  Karena itu kodenya jadi kolom sendiri, dengan UNIQUE yang ditegakkan
--  database — bukan hasil pemotongan yang dihitung ulang di tiap klien.
--
--  BENTUKNYA
--
--  SW-4F7K2Q untuk sewa, TR-9QM3XB untuk baris buku besar. Awalannya
--  membedakan "nomor sewa" dari "nomor transaksi" saat keduanya disebut
--  dalam satu kalimat — persoalan nyata di antrean pencairan, yang
--  menampilkan keduanya berdampingan.
--
--  Abjadnya 32 huruf TANPA I, O, 0, dan 1. Kode ini dibuat untuk dibaca
--  dari layar lalu diketik ulang atau diucapkan lewat telepon, dan di
--  situlah pasangan huruf itu tertukar. Membuang empatnya lebih murah
--  daripada menerima aduan "kodenya tidak ketemu" yang sebabnya salah
--  ketik satu huruf.
--
--  ACAK, BUKAN BERURUTAN
--
--  Nomor urut akan membocorkan volume: siapa pun yang menyewa dua kali
--  bisa mengurangkan kedua nomornya dan tahu berapa banyak sewa terjadi
--  di antaranya. Enam karakter = 32^6 ≈ 1,07 miliar kemungkinan.
--
--  Tabrakan karena itu mungkin, dan sengaja tidak disembunyikan:
--  peluangnya per baris baru = (jumlah baris) / 1,07 miliar — pada satu
--  juta sewa masih 0,09%. Fungsi di bawah mencoba ulang sampai sepuluh
--  kali sebelum menyerah, tapi yang MENJAMIN keunikan tetap constraint
--  UNIQUE, bukan pengulangan itu: dua transaksi bersamaan bisa sama-sama
--  lolos pemeriksaan EXISTS, dan yang kedua ditolak index. Kalau suatu
--  hari penolakan itu benar-benar terlihat di produksi, obatnya melebarkan
--  kodenya jadi tujuh karakter — bukan menambah putaran.
--
--  KODE INI BUKAN RAHASIA. Ia ditempel di email, dibacakan lewat telepon,
--  dan muncul di tangkapan layar. Tidak ada satu pun endpoint yang boleh
--  memberi akses karena pemanggilnya tahu kodenya; otorisasi tetap lewat
--  id pemegang token, persis seperti sebelum kolom ini ada.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
--  Badan kode: n karakter acak dari abjad tanpa huruf yang mudah tertukar.
--
--  random() sudah cukup — lihat "KODE INI BUKAN RAHASIA" di atas. Kalau
--  suatu saat kodenya dipakai sebagai tautan yang bisa dibuka tanpa masuk,
--  yang harus berubah bukan fungsi ini melainkan keputusan itu sendiri.
-- ---------------------------------------------------------------------
CREATE OR REPLACE FUNCTION sewa_reference_body(len integer) RETURNS text
LANGUAGE plpgsql VOLATILE AS $$
DECLARE
    -- Tanpa I, O, 0, 1.
    alphabet constant text := '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
    hasil text := '';
BEGIN
    FOR i IN 1..len LOOP
        hasil := hasil || substr(alphabet, 1 + floor(random() * length(alphabet))::int, 1);
    END LOOP;

    RETURN hasil;
END $$;

COMMENT ON FUNCTION sewa_reference_body(integer) IS
    'Badan kode referensi: karakter acak dari abjad 32 huruf tanpa I, O, 0, 1.';

-- ---------------------------------------------------------------------
--  Kolom di bookings.
--
--  Ditambah dulu tanpa NOT NULL supaya baris yang sudah ada bisa diisi;
--  keduanya dipasang setelah backfill selesai.
-- ---------------------------------------------------------------------
ALTER TABLE bookings ADD COLUMN reference text;

CREATE OR REPLACE FUNCTION gen_booking_reference() RETURNS text
LANGUAGE plpgsql VOLATILE AS $$
DECLARE calon text;
BEGIN
    FOR i IN 1..10 LOOP
        calon := 'SW-' || sewa_reference_body(6);
        IF NOT EXISTS (SELECT 1 FROM bookings WHERE reference = calon) THEN
            RETURN calon;
        END IF;
    END LOOP;

    -- Sepuluh kali berturut-turut bertabrakan berarti ruang kodenya memang
    -- sudah sesak, bukan sial. Kembalikan yang terakhir dan biarkan UNIQUE
    -- yang menolak: gagal keras di sini lebih mudah dilacak daripada
    -- berputar selamanya di dalam satu INSERT.
    RETURN calon;
END $$;

CREATE OR REPLACE FUNCTION gen_payment_reference() RETURNS text
LANGUAGE plpgsql VOLATILE AS $$
DECLARE calon text;
BEGIN
    FOR i IN 1..10 LOOP
        calon := 'TR-' || sewa_reference_body(6);
        IF NOT EXISTS (SELECT 1 FROM payments WHERE reference = calon) THEN
            RETURN calon;
        END IF;
    END LOOP;

    RETURN calon;
END $$;

ALTER TABLE payments ADD COLUMN reference text;

-- ---------------------------------------------------------------------
--  Backfill.
--
--  Baris per baris, bukan satu UPDATE untuk semuanya: di dalam satu
--  pernyataan, EXISTS di gen_*_reference() membaca snapshot SEBELUM
--  pernyataan itu berjalan, jadi ia tidak akan pernah melihat baris yang
--  baru saja diisi pernyataan yang sama — dan dua baris bisa kebagian kode
--  yang sama. Perulangan di bawah membuat tiap baris jadi pernyataannya
--  sendiri, sehingga yang berikutnya melihat yang sebelumnya.
--
--  UNIQUE tetap dipasang setelah ini. Kalau backfill-nya toh menghasilkan
--  duplikat, migrasinya gagal di situ — bukan diam-diam lolos.
-- ---------------------------------------------------------------------
DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT id FROM bookings WHERE reference IS NULL LOOP
        UPDATE bookings SET reference = gen_booking_reference() WHERE id = r.id;
    END LOOP;

    FOR r IN SELECT id FROM payments WHERE reference IS NULL LOOP
        UPDATE payments SET reference = gen_payment_reference() WHERE id = r.id;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------
--  Baru sekarang aturannya dipasang.
--
--  DEFAULT ada di database, bukan di C#, supaya tidak ada jalan masuk yang
--  bisa melewatkannya — termasuk INSERT manual lewat psql dan data demo.
--  Sepola id yang memakai gen_random_uuid().
-- ---------------------------------------------------------------------
ALTER TABLE bookings
    ALTER COLUMN reference SET NOT NULL,
    ALTER COLUMN reference SET DEFAULT gen_booking_reference(),
    ADD CONSTRAINT uq_bookings_reference UNIQUE (reference),
    ADD CONSTRAINT ck_bookings_reference_format
        CHECK (reference ~ '^SW-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$');

ALTER TABLE payments
    ALTER COLUMN reference SET NOT NULL,
    ALTER COLUMN reference SET DEFAULT gen_payment_reference(),
    ADD CONSTRAINT uq_payments_reference UNIQUE (reference),
    ADD CONSTRAINT ck_payments_reference_format
        CHECK (reference ~ '^TR-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{6}$');

COMMENT ON COLUMN bookings.reference IS
    'Nomor sewa yang disebut manusia, SW-XXXXXX. Bukan rahasia: tidak pernah menggantikan otorisasi.';
COMMENT ON COLUMN payments.reference IS
    'Nomor transaksi yang disebut manusia, TR-XXXXXX. Bukan rahasia: tidak pernah menggantikan otorisasi.';

COMMIT;

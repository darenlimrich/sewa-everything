-- =====================================================================
--  Sewa Everything — 0005_return_window.sql
--  Milestone 6: jendela konfirmasi pengembalian.
--
--  Alur yang diputuskan pemilik produk: SELLER yang mengonfirmasi barang
--  sudah kembali. Itu memberi seller kesempatan memeriksa kondisinya
--  sebelum deposit cair — dan sekaligus memberinya cara menyandera
--  deposit hanya dengan tidak berbuat apa-apa.
--
--  Karena itu jendelanya wajib punya batas. Lewat batas ini, job
--  penyelesai otomatis menutup booking dan deposit cair PENUH ke renter.
--  Bentuknya sama persis dengan job pelepas hold di migrasi 0003: diam
--  bukan keputusan, dan tidak boleh berakibat seperti keputusan.
-- =====================================================================

BEGIN;

ALTER TABLE platform_settings
    ADD COLUMN return_window_days integer NOT NULL DEFAULT 3
    CHECK (return_window_days BETWEEN 1 AND 30);

COMMENT ON COLUMN platform_settings.return_window_days IS
    'Berapa hari setelah sewa berakhir seller boleh menahan konfirmasi sebelum booking diselesaikan otomatis.';


-- ---------------------------------------------------------------------
--  Index untuk job penyelesai otomatis.
--
--  Query-nya: booking `active` yang `ends_at`-nya sudah lewat lebih dari
--  jendela di atas. Index parsial supaya tetap ramping — sewa yang sudah
--  selesai jauh lebih banyak daripada yang sedang berjalan, dan yang
--  sudah selesai tidak pernah jadi kandidat.
-- ---------------------------------------------------------------------
CREATE INDEX ix_bookings_return_due ON bookings (ends_at)
    WHERE status = 'active';

COMMIT;

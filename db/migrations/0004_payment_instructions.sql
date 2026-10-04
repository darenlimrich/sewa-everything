-- =====================================================================
--  Sewa Everything — 0004_payment_instructions.sql
--  Milestone 5: menyimpan instruksi bayar yang dikembalikan gateway.
--
--  Kenapa perlu kolom sendiri: `POST /bookings/{id}/pay` wajib idempotent.
--  Renter yang menutup aplikasi lalu kembali harus melihat nomor VA yang
--  SAMA, bukan tagihan baru. Memanggil ulang gateway bukan jalan keluar —
--  Midtrans menolak `order_id` yang sudah pernah dipakai — jadi jawabannya
--  harus tersimpan di sisi kita sejak tagihan pertama dibuat.
--
--  Ditaruh di baris `rent_charge`, yang selalu ada karena `total_rent`
--  dijamin lebih besar dari nol. Baris `deposit_charge` menumpang transaksi
--  gateway yang sama (`gateway_ref` keduanya sama) dan tidak menyimpan
--  salinannya sendiri.
-- =====================================================================

BEGIN;

ALTER TABLE payments ADD COLUMN gateway_instructions jsonb;

COMMENT ON COLUMN payments.gateway_instructions IS
    'Instruksi bayar dari gateway (nomor VA, kode bayar, QR, tautan). Diisi pada baris rent_charge saja.';

COMMIT;

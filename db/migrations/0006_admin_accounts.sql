-- =====================================================================
--  Sewa Everything — 0006_admin_accounts.sql
--  Milestone 9: Owner mengelola akun admin.
--
--  Admin dibuat Owner, bukan mendaftar sendiri (keputusan terkunci #6).
--  Yang belum ada sampai sekarang: cara MENCABUTNYA kembali. Panel yang
--  cuma bisa menambah admin dan tidak pernah bisa mencopotnya bukan
--  "kelola admin" — admin yang sudah tidak berhak tetap memegang kunci
--  verifikasi seller, keputusan sengketa, dan tombol pencairan.
--
--  Dicabut dengan MENONAKTIFKAN, bukan menghapus barisnya. Setiap
--  keputusan admin meninggalkan jejak yang menunjuk balik ke dia —
--  users.verified_by, disputes.resolved_by, platform_settings.updated_by.
--  Menghapus akunnya berarti memutus jejak itu (dan FK-nya memang akan
--  menolak), sehingga riwayat kehilangan siapa yang memutuskan apa.
--  Sejalan dengan buku besar append-only: catatan tidak dihapus, cuma
--  ditutup.
-- =====================================================================

BEGIN;

ALTER TABLE users
    ADD COLUMN deactivated_at timestamptz;

COMMENT ON COLUMN users.deactivated_at IS
    'Terisi = akun dicabut aksesnya. Login ditolak dan token yang masih hidup ikut mati. Barisnya tetap ada supaya jejak keputusannya tidak putus.';

-- ---------------------------------------------------------------------
--  Owner tidak pernah bisa dinonaktifkan.
--
--  Owner adalah satu-satunya role yang bisa membuat dan memulihkan admin
--  (POST /owner/admins). Kalau akun owner bisa ikut dinonaktifkan — oleh
--  bug, oleh SQL manual, atau oleh dirinya sendiri yang salah klik —
--  platform kehilangan satu-satunya pintu untuk memulihkan siapa pun,
--  termasuk pintu untuk memulihkan owner itu sendiri. Tidak ada jalan
--  balik lewat aplikasi; harus lewat psql.
--
--  Ditegakkan di sini, bukan cuma di controller, karena akibatnya tidak
--  bisa dibatalkan dari dalam aplikasi.
-- ---------------------------------------------------------------------
ALTER TABLE users
    ADD CONSTRAINT ck_users_owner_always_active
    CHECK (role <> 'owner' OR deactivated_at IS NULL);

COMMIT;

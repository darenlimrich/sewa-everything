-- =====================================================================
--  Sewa Everything — 0015_user_avatar.sql
--  Foto profil pengguna.
--
--  Diminta pemilik produk 9 Sep 2026: "ini profilenya kok gak bisa
--  diubah? harusnya bisa dan itu harusnya muncul di dashboard di awal
--  awal banget."
--
--  Sebelum ini avatar di seluruh produk adalah HURUF PERTAMA nama,
--  dan catatan 8 Sep menuliskan alasannya apa adanya: tidak ada
--  endpoint avatar pengguna, dan tombol unggah yang tidak mengunggah
--  adalah kontrol mati. Yang berubah bukan penilaian itu — yang
--  berubah endpoint-nya sekarang dibuat.
--
--  ---------------------------------------------------------------
--  KENAPA SATU KOLOM DI users, BUKAN TABEL SENDIRI
--
--  item_photos adalah tabel karena satu barang punya BANYAK foto yang
--  BERURUTAN: ia butuh baris sendiri untuk sort_order dan untuk
--  dihapus satu per satu. Avatar tidak punya keduanya — satu akun satu
--  foto, tidak ada urutan, tidak ada riwayat. Tabel untuk itu berarti
--  satu baris per pengguna dengan UNIQUE (user_id), yaitu sebuah kolom
--  yang dibayar dengan JOIN di setiap pembacaan profil.
--
--  KENAPA text BERISI URL, BUKAN bytea
--
--  Sepola item_photos.url: berkasnya hidup di IPhotoStorage (disk lokal
--  sekarang, object storage nanti) dan dilayani di /uploads. Menaruh
--  pikselnya di database berarti tiap pembacaan profil menyeret ratusan
--  kilobyte lewat koneksi yang sama dengan yang dipakai transaksi uang,
--  dan cadangan database ikut membengkak oleh data yang bukan
--  kebenaran bisnis.
--
--  KENAPA NULL, DAN KENAPA STRING KOSONG DILARANG
--
--  NULL berarti "belum ada foto" — dan itu keadaan yang sah selamanya,
--  karena avatar huruf pertama nama tetap dipertahankan sebagai
--  cadangan. Kalau string kosong ikut diizinkan, akan ada DUA cara
--  menulis "tidak ada foto" dan setiap pembaca harus memeriksa
--  keduanya; cepat atau lambat ada yang cuma memeriksa satu. CHECK-nya
--  meniru item_photos.url apa adanya.
--
--  YANG SENGAJA TIDAK DILAKUKAN
--
--  Tidak ada FK ke tabel berkas, dan tidak ada trigger yang menghapus
--  berkas lama saat kolomnya berubah. Database tidak tahu apa-apa soal
--  isi disk; yang membuang berkas lama adalah endpoint-nya, di dalam
--  transaksi yang sama dengan penulisan kolomnya. Berkas yatim yang
--  lolos dari situ tidak merusak apa pun — ia cuma memakan disk, dan
--  itu pertukaran yang sama dengan yang sudah diterima item_photos.
--
--  CATATAN atas bentuk CHECK-nya: btrim() tanpa argumen kedua hanya
--  membuang SPASI, jadi 'btrim(x) <> ''' — bentuk yang dipakai
--  item_photos.url — masih meloloskan string berisi tab atau baris
--  baru saja. Di sini daftar karakternya disebut eksplisit supaya
--  CHECK-nya benar-benar berarti 'tidak kosong'. verify_0015
--  mengadu ketiganya.
-- =====================================================================

ALTER TABLE users
    ADD COLUMN avatar_url text;

ALTER TABLE users
    ADD CONSTRAINT ck_users_avatar_url
    CHECK (avatar_url IS NULL OR btrim(avatar_url, E' \t\n\r') <> '');

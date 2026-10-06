-- =====================================================================
--  Sewa Everything - 0018_photo_blobs.sql
--  Isi berkas foto (barang + profil) disimpan di database.
--
--  Diminta pemilik produk 6 Okt 2026: aplikasi dipindah ke hosting
--  gratis tanpa kartu kredit - web di Cloudflare Pages, API di Render,
--  database di Neon. API di Render free TIDAK punya disk yang bertahan:
--  setiap restart, deploy, atau bangun dari tidur menghapus seluruh
--  isi storage/photos. LocalDiskPhotoStorage di sana berarti semua foto
--  hilang diam-diam setiap kali server dinyalakan ulang.
--
--  ---------------------------------------------------------------
--  INI MEMBALIK SEBAGIAN ALASAN 0015, DAN ITU DISADARI
--
--  0015 menolak menaruh piksel di database dengan dua alasan: tiap
--  pembacaan profil menyeret ratusan kilobyte, dan cadangan database
--  membengkak. Yang pertama TIDAK berlaku di sini - item_photos.url
--  dan users.avatar_url tetap text berisi URL, dan bytes-nya hidup di
--  tabel TERPISAH yang hanya dibaca oleh endpoint /uploads/{nama}.
--  Tidak ada query bisnis yang pernah menyentuhnya. Yang kedua memang
--  dibayar: cadangan ikut membawa foto. Di skala sekarang (169 foto,
--  27 MB) itu jauh di bawah batas 0,5 GB Neon free.
--
--  Alternatifnya object storage gratis (Supabase Storage) - satu akun
--  lagi, satu kredensial lagi, dan satu layanan lagi yang dapat tidur
--  atau mengubah paket gratisnya. Ditolak untuk sekarang. IPhotoStorage
--  tetap jadi sambungannya, jadi pindah ke sana nanti = satu kelas.
--
--  KENAPA URL TIDAK BERUBAH
--
--  Kolomnya dikunci ke nama berkas yang dibuat LocalDiskPhotoStorage
--  ('<32 hex>.<ext>'), dan endpoint-nya dilayani di path yang sama
--  (/uploads/<nama>). Jadi baris item_photos dan avatar_url yang sudah
--  ada tetap benar apa adanya - memindahkan foto = memasukkan berkasnya
--  ke tabel ini, tanpa satu pun UPDATE di tabel bisnis.
--
--  KENAPA content_type DISIMPAN, BUKAN DITEBAK DARI EKSTENSI
--
--  Ia dibatasi ke tiga tipe yang sama dengan ImageSniffer, dan CHECK
--  menuntut ekstensinya cocok dengan tipenya. Dengan begitu endpoint
--  tidak pernah dapat melayani berkas dengan Content-Type di luar
--  daftar itu, apa pun yang tertulis di baris.
--
--  YANG SENGAJA TIDAK DILAKUKAN
--
--  Tidak ada FK dari item_photos/users ke sini. Urutan tulis-simpan-
--  buang di endpoint (0015) tetap yang menjaga konsistensinya; berkas
--  yatim tetap mungkin dan tetap tidak merusak apa pun.
-- =====================================================================

CREATE TABLE photo_blobs (
    name          text        PRIMARY KEY,
    content_type  text        NOT NULL,
    content       bytea       NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_photo_blobs_name
        CHECK (name ~ '^[0-9a-f]{32}\.(jpg|png|webp)$'),

    CONSTRAINT ck_photo_blobs_content_type
        CHECK (
               (content_type = 'image/jpeg' AND name LIKE '%.jpg')
            OR (content_type = 'image/png'  AND name LIKE '%.png')
            OR (content_type = 'image/webp' AND name LIKE '%.webp')
        ),

    CONSTRAINT ck_photo_blobs_not_empty
        CHECK (octet_length(content) > 0)
);

ALTER TABLE photo_blobs ALTER COLUMN content SET STORAGE EXTERNAL;

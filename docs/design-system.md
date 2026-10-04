# Design system — "Merah Putih"

> Baca berkas ini **sebelum menulis satu baris pun UI**, di web maupun MAUI. Isinya mengikat.

Arah visual: **pola layout Shopee** (familiar untuk pengguna Indonesia), tapi identitas warna
**merah-putih** yang bersih dan TIDAK ramai. Prinsip utama: **putih mendominasi, merah adalah
aksen** — bukan sebaliknya. Jangan tiru Shopee yang membanjiri layar dengan warna brand; di
sini merah dipakai hemat supaya terasa premium dan tidak melelahkan mata. **Permukaan besar yang
boleh berwarna brand: bilah header web, dan di MAUI bilah cari Beranda/Hasil/Kategori plus pita
identitas tab Akun** — pengecualian yang diminta pemilik produk, lihat bagian 1.

---

## 1. Token warna

| Token | Hex | Pemakaian |
|---|---|---|
| `primary` | `#C8102E` | Tombol utama, harga, badge aktif, ikon nav aktif, logo |
| `primary-dark` | `#A00D25` | Hover/pressed state tombol utama |
| `primary-tint` | `#FDECEF` | Latar badge/chip merah muda tipis, highlight tipis |
| `surface` | `#FFFFFF` | Kartu, header, bottom nav |
| `background` | `#F7F7F8` | Latar halaman |
| `text-primary` | `#1F1F1F` | Teks utama |
| `text-secondary` | `#6B6B6B` | Teks sekunder, meta |
| `border` | `#E7E7E9` | Garis kartu & pemisah (hairline) |
| `success` | `#1B873F` | Status selesai / dana cair |
| `warning` | `#B45309` | Status pending / menunggu |
| `danger` | `#C8102E` | Sengketa / error (samakan dengan primary) |

**Aturan warna keras:**
- Merah HANYA untuk: aksi utama (1 tombol per layar), harga, elemen aktif, brand, dan **bilah
  header** (lihat pengecualian di bawah). Selain itu netral.
- Maksimal SATU tombol merah solid per layar; aksi sekunder = outline abu/putih.
- Tidak ada gradasi warna-warni, tidak ada banner beraneka warna. Flat & bersih.
- Teks di atas merah selalu putih; kontras minimal WCAG AA.

**Pengecualian bilah header (diminta pemilik produk, 31 Agu 2026; MAUI menyusul 31 Agu).**
Header web memakai latar merah penuh — sepola Shopee. Badan halaman tetap putih di atas latar
abu. Di tema gelap bilahnya **tidak** ikut merah muda: token `--header-bg` menjadi `#1B1B1F`,
karena `--primary` versi gelap adalah warna teks, bukan warna latar. Token header
(`--header-bg`, `--header-fg`, `--header-muted`, `--header-line`, `--header-field`) hidup di
`app.css`; MAUI punya padanannya (`LightHeaderBg` / `DarkHeaderBg`, `HeaderFg`, `HeaderField`)
untuk bilah cari di Beranda · Hasil · Kategori dan pita identitas di tab Akun. Di Android
tema terang, status bar ikut `#C8102E` dengan ikon sistem putih.

**Logo.** Sejak 9 Sep 2026 logonya **berkas gambar dari pemilik produk**, bukan SVG yang kita
gambar sendiri: lingkaran berisi kumpulan barang sewaan (kamera, sepeda, bor, sofa, laptop, tenda,
gitar, koper, skuter, dan seterusnya) + wordmark **SEWAKU**. Sumbernya di `assets/brand/`; yang
dipakai aplikasi diturunkan dari situ.

Warna merek logonya `#D50012` — **sedikit berbeda dari `primary` (`#C8102E`)**, dan itu memang
begitu di berkas aslinya. Jangan "dibetulkan" jadi `primary`: yang tampil di layar merah selalu
versi putih, jadi selisihnya tidak pernah bersebelahan.

Di atas latar merah (bilah header web, bilah cari dan pita akun MAUI) logonya **putih**; di footer
tema terang ia merah; di footer tema gelap ia putih lagi. Di web keputihan itu dihasilkan
`filter: brightness(0) invert(1)` atas berkas yang sama — bukan berkas kedua, supaya tidak ada dua
aset yang dapat menyimpang. Jangan ganti jadi ikon lucide generik, dan jangan gambar ulang
mark-nya sebagai SVG.

## 2. Tipografi

- Font: **Inter** (web) / default platform (MAUI). Bersih, mudah dibaca, netral.
- Skala: 24px (judul halaman) · 18px (judul seksi) · 15–16px (body) · 13px (meta/caption).
- Weight hanya 2: 400 (regular) & 600 (semibold). Harga pakai 600 warna `primary`.

## 3. Pola layout

### Mobile app (MAUI)
- **Bottom nav 4 tab**: Beranda · Kategori · Sewaanku · Akun. Ikon aktif merah, tidak aktif abu.
  (Tab Notifikasi tidak ada — API tidak punya endpointnya.)
- **Beranda / Hasil / Kategori**: bilah merah (gelap di tema gelap) berisi mark logo + kolom
  pencarian putih dengan tombol cari merah di dalamnya, sepola header Shopee. Di bawahnya
  Beranda menampilkan ubin kategori bergulir (foto bulat dari barang nyata) lalu grid 2 kolom.
- **Kartu produk**: foto rasio 1:1 → pita deposit di pojok kiri-bawah foto → judul max 2 baris
  → harga merah semibold + "/hari" abu kecil → rating bintang + jumlah sewa.
- **Detail barang**: galeri foto → judul & harga → **kalender ketersediaan** (tanggal terisi =
  abu dicoret, tersedia = putih, terpilih = merah) → info deposit → info seller → tombol
  sticky bawah "Ajukan Sewa" (merah, full-width).
- **Sewaanku**: tab status (Menunggu · Dikonfirmasi · Berlangsung · Selesai · Sengketa) —
  badge status pakai warna semantik, bukan pelangi.

### Web
Tiga permukaan penyewa meniru **struktur** Shopee (diminta pemilik produk 31 Agu 2026: "miripin
hampir 90%, kecuali warnanya"). Yang ditiru tata letaknya; paletnya tetap merah-putih.

- **Header dua tingkat, latar merah, TANPA garis pemisah.** Baris atas 40px (huruf 13px):
  pintasan peran di kiri berjarak 24px tanpa `|` di antaranya, sakelar tema + akun di kanan.
  **Tidak ada `border-bottom` di antara kedua tingkat** — pita merah yang dibelah garis putih
  terbaca seperti dua bilah yang ditempel (koreksi pemilik produk 8 Sep 2026). Baris utama:
  **logo mark tas + wordmark Sewaku** putih, kolom pencarian putih dengan tombol cari merah
  menempel di dalamnya, dan tidak ada apa pun di kanannya — ikon pintasan dibuang 7 Sep 2026 karena
  ia mengulang tautan yang sudah ada di bilah atas dan di menu akun (dan untuk pengunjung anonim
  menjanjikan "Sewaanku" lalu mendarat di `/masuk`). Di bawahnya baris tautan kategori (turun dari
  data, disembunyikan di bawah 900px dan di halaman staf/pemilik barang). Tanpa mega-banner ramai.
- **Logo memenuhi tinggi baris utama, ke bawah saja.** Di permukaan publik yang punya baris kata
  kunci, logonya 76px (mark 72x72, huruf 36px) sehingga tepi bawahnya bertemu tepi bawah baris
  kategori; tepi atasnya terkunci ke tepi atas kolom pencarian oleh `align-items: flex-start`, jadi
  ia tidak pernah tumbuh ke atas. Di halaman staf/pemilik barang dan di bawah 900px — di mana baris
  kata kunci memang tidak ada — ia kembali 44px, karena tidak ada ruang untuk diisi dan §3 meminta
  panel admin padat. Mark-nya SVG persegi yang diberi lebar **dan** tinggi sama; jangan pernah
  memberinya satu sisi saja.
- **Menu profil dibuka dengan mouseover, bukan klik** (sepola Shopee), dan isinya **hanya aksi**:
  Akun Saya · (Sewaan Saya / Jualan Saya / Panel admin) · Keluar. Peran pemakai **tidak** ditulis di
  sana — tempatnya `/akun`. Menunya tetap terbuka selama penunjuk berada di dalamnya; celah antara
  tombol dan kartu wajib berupa `padding` milik popover-nya sendiri, bukan `top` berjarak, supaya
  `mouseleave` tidak menyala saat penunjuk menyeberang. Papan ketik dilayani `:focus-within`,
  **bukan** `@onfocusout` — kejadian itu mendahului klik dan akan menelan ketukan butir menu.
  **Menutupnya diberi masa tenggang `--dur-hold`** (lihat bagian 9): menu yang lenyap pada milidetik
  penunjuk meleset terasa rapuh, dan selama tenggang itu ia wajib masih dapat diraih kembali.
- **Halaman akun memakai pola "Profil Saya" Shopee**, dan **KELIMA rutenya hidup di shell yang
  sama** (`/akun` · `/akun/rekening` · `/akun/sandi` · `/sewaanku` · `/keranjang`): sidebar kiri
  (avatar + nama + email, garis, grup "Akun Saya" berisi sub-baris, lalu pintasan peran dan Keluar)
  + panel putih kanan (judul + keterangan, garis, baris label-kanan/nilai-kiri, kolom potret bergaris
  tegak berisi avatar inisial + peran). Sidebar-nya **satu komponen** dipakai kedua halaman.
  Yang **tidak** ditiru dan alasannya: unggah foto profil (tidak ada endpoint-nya), penyamaran email
  (tanpa tautan "Ubah" ia hanya menyembunyikan email dari pemiliknya sendiri), serta Username /
  Jenis Kelamin / Tanggal lahir / Alamat / Notifikasi (tidak ada kolom maupun endpoint-nya).
  **Isian hanya boleh ada kalau ada endpoint yang menyimpannya** — nama dan telepon lewat
  `PUT /auth/me`; email dan peran ditampilkan sebagai nilai, bukan isian.
- **Baris sidebar TIDAK BOLEH menautkan ke halaman di luar shell-nya.** Aturan ini lahir dari cacat
  8 Sep 2026: "Ubah Kata Sandi" sempat menunjuk `/lupa-sandi`, halaman auth berdiri sendiri, jadi
  mengetuknya membuang pengguna keluar dari sidebar yang baru saja ia pakai. Kalau halamannya belum
  ada, **buat halamannya di dalam shell** — jangan menautkan keluar. Halaman auth yang memang berdiri
  sendiri tetap boleh dicapai, tetapi sebagai tombol di dalam isi, bukan sebagai baris nav.
- **Bilah atas membawa lonceng notifikasi** di kiri sakelar tema (penyewa & pemilik barang; staf
  tidak, karena feed-nya diturunkan dari sewa dan akan selalu kosong untuk mereka), dan **bilah utama
  membawa ikon keranjang** di kanan kolom pencarian (penyewa saja). Keduanya memakai komponen popover
  yang sama dengan menu akun — lihat bagian 9.
- **Sewaan Saya sepola "Pesanan Saya"**: panel kanan tanpa padding supaya bilah tab status menempel
  ke tepinya, lalu kotak cari, lalu kartu per sewa (kepala: pemilik + Kunjungi Toko + status;
  badan: foto, judul, tanggal, nomor sewa, harga satuan; kaki: Total Sewa + tombol aksi).
- **Keranjang menyimpan niat, bukan hak**, dan halamannya wajib mengatakannya: barang di keranjang
  dapat diambil penyewa lain lebih dulu, karena keranjang **tidak** menahan slot. Baris yang
  tanggalnya keburu diambil ditandai dan menyuruh memilih tanggal lain.
- **Footer** (hanya permukaan publik, bukan `/jual` `/admin` `/owner` `/staf`): empat kolom
  Layanan pelanggan · Jelajahi · Cara sewa · Pembayaran (chip channel yang benar-benar ditawarkan),
  lalu bilah hak cipta + logo. Tautan hanya ke rute yang ada; jangan mengarang pusat bantuan,
  media sosial, atau toko aplikasi.
- **Katalog tanpa filter** = panel "KATEGORI" (ubin bergaris pemisah, foto bulat dari barang
  nyata — **fotonya datang dari `GET /items/categories`**, bukan dipungut dari halaman katalog yang
  sedang termuat; cara lama membuat kategori di luar halaman 1 jatuh ke huruf awal) lalu seksi "BARANG TERBARU" bergaris merah di judulnya + grid. **Katalog terfilter** =
  remah-roti + sidebar filter kiri (kategori, rentang harga, satuan sewa, tanggal sewa, hapus
  semua) + bilah urutkan (Terkait · Terbaru · dropdown Harga + nomor halaman) + grid. Di bawah
  900px sidebar-nya jadi tombol "Filter" yang membuka panelnya.
- **Detail barang**: remah-roti · panel putih berisi galeri kiri (kotak 1:1 + strip thumbnail) dan
  kolom kanan berisi judul, baris statistik, pita harga, baris label/nilai (Kategori · Deposit ·
  Pilih tanggal berisi kalender), lalu tombol aksi. Identitas pemilik barang di bawah galeri,
  **dapat diketuk** menuju halaman tokonya (`/toko/{id}`). Di bawahnya panel Spesifikasi ·
  Deskripsi · Penilaian (chip saringan bintang 1–5).
- **Pita harga mengikuti tanggal yang dipilih**, bukan angka mati: begitu rentangnya lengkap ia
  menampilkan sewa untuk seluruh durasi (`Rp1.200.000/6 hari`) beserta rincian `harga satuan ×
  jumlah satuan` di bawahnya, dan pembulatan ke atas atas satuan (8 hari pada barang mingguan =
  2 minggu) jadi terlihat sebelum sewa diajukan. Yang ditampilkan **sewa saja** — deposit dan komisi
  bukan urusan klien, dan kalimat "Total dihitung server saat pengajuan." tetap menemaninya.
- **Katalog, harga, ketersediaan, dan penilaian TERBUKA untuk pengunjung anonim.** Tombol aksi
  tetap berbunyi "Ajukan Sewa"; permintaan masuk baru muncul **saat tombolnya diketuk**, dan
  mengembalikan pengguna ke barang yang sama lewat `returnUrl`. Jangan pernah memasang tombol
  bertuliskan "Masuk untuk …" sebagai gerbang — itu instruksi pemilik produk 31 Agu 2026.
- Grid produk 4–5 kolom desktop, kartu sama seperti mobile.
- **Kartu produk web** menambah pita "Deposit Rp…" di pojok kiri-bawah foto dan pil rating merah
  muda — sepadan badge promo Shopee.
- **Seller dashboard**: sidebar kiri putih (Listing · Booking Masuk · Pendapatan · Kalender),
  konten kanan tabel bersih.
- **Admin panel**: fungsional & padat — tabel verifikasi seller, antrean sengketa, log
  pembayaran. Tidak perlu dekorasi.

## 4. Komponen kunci

- **Badge status booking** (konsisten di semua layar):
  pending = latar amber-tint teks `warning` · confirmed = `primary-tint` teks `primary` ·
  active = biru-netral tipis · completed = hijau-tint teks `success` · disputed =
  `primary-tint` teks `danger` dengan ikon peringatan.
- **Halaman bayar menggambar ARTEFAK bayarnya, bukan barisan tabel.** QRIS dan GoPay menampilkan
  gambar QR 240px yang **diterbitkan gerbang bayarnya sendiri** (aksi `generate-qr-code` Midtrans) —
  jangan menggambar QR sendiri; QR yang salah mengirim uang ke tempat yang salah. Nomor VA dan kode
  minimarket ditampilkan besar (22px, berspasi). Tagihan yang sudah terbit wajib **muncul lagi saat
  halaman dibuka ulang**, dibaca lewat `GET /bookings/{id}/payment` — bukan dengan menembak ulang
  verba yang membuatnya.
- **Daftar tindakan (`ActionList`) berada DI ATAS daftar riwayat**, dan hanya memuat sewa yang
  menunggu tindakan pembacanya: pemilik barang melihat permintaan yang belum diputus dan barang yang
  belum kembali, penyewa melihat sewa yang sudah disetujui dan belum dibayar. Aturan "status apa yang
  butuh tindakan" hidup di `Labels.NeedsAction`, jadi dropdown notifikasi menandainya dengan aturan
  yang sama. **Hanya masukkan keadaan yang benar tanpa syarat tambahan** — mis. pemilik barang pada
  `confirmed` TIDAK masuk, karena boleh-tidaknya ia menyerahkan barang bergantung pada lunas atau
  belum, dan daftar sewa tidak membawa fakta itu.
- **Kalender ketersediaan** = komponen paling penting di produk ini. Harus jelas dalam sekali
  lihat tanggal mana yang bisa dipilih.
- **Empty state & error**: jelaskan apa yang terjadi + aksi berikutnya ("Belum ada barang di
  kategori ini — coba kategori lain"). Tanpa nada minta maaf berlebihan.
- **Kotak `.notice` hanya untuk sesuatu yang harus dikerjakan PEMBACANYA.** Kalau satu-satunya
  jawaban benar atas peringatan itu adalah "tunggu orang lain", ia bukan peringatan — jangan
  gambarkan. Aturan ini lahir dari banner sengketa di panel superadmin (dibuang 8 Sep 2026): owner
  memang melihat angkanya, tetapi keputusannya milik admin, jadi memanggilnya lewat kotak peringatan
  mengarahkan ke pintu yang sengaja dikunci untuknya.
- **Angka yang dapat berubah karena orang lain WAJIB hidup sendiri.** Lencana notifikasi, jumlah
  keranjang, daftar tindakan, antrean staf, dan status pembayaran menyegarkan diri lewat satu denyut
  bersama (`LiveState` + `GET /pulse`, 5 detik) — bukan lewat tombol "muat ulang" dan bukan lewat
  pemantau sendiri-sendiri per halaman. Denyutnya **berhenti saat tab tersembunyi** dan **tidak
  pernah berjalan untuk pengunjung anonim**. Jangan menambah pemantau kedua: tambahkan kolomnya ke
  denyut yang sudah ada.
- **Kerangka pemuatan, bukan layar "Memuat…".** Halaman yang belum siap menggambar bentuk dirinya
  sendiri sebagai blok abu berkilau (`.skeleton`), memakai lebar dan breakpoint yang sama dengan isi
  sungguhannya supaya tidak melompat saat digantikan. **Nol tulisan di dalam kerangka** — kerangka
  yang memuat kata "Memuat…" hanya memindahkan layar tunggu ke bentuk baru. Kerangka boot aplikasi
  hidup sebagai HTML statis di `index.html`, karena ia harus tergambar sebelum runtime-nya ada.
- **Permukaan yang dapat diketuk naik saat di-hover, tidak berganti warna.** Ubin kategori dan kartu
  produk memakai `translateY(-4px)` + `--shadow-pop`; warnanya tidak berubah. Merah disimpan untuk
  **keadaan** (kategori yang sedang disaring), bukan untuk hover.
- Sudut kartu 8px, tombol 8px. Bayangan sangat tipis atau cukup border hairline.

## 5. Kualitas minimum

- Responsive sampai layar kecil; target sentuh ≥ 44px.
- Fokus keyboard terlihat (web); label form jelas; state loading pakai skeleton putih-abu
  (bukan spinner warna-warni).

## 6. KONSISTENSI (wajib, lintas web + app + admin)

- **Satu sumber token.** Semua warna, font, radius, dan spacing HANYA boleh diambil dari
  bagian 1–2 di atas. Definisikan sekali sebagai variabel/resource terpusat (CSS variables di
  web, `ResourceDictionary` di MAUI). **Tidak boleh ada nilai hardcode di komponen.**
- **Komponen yang sama tampil identik di mana pun.** Tombol utama, kartu produk, badge status,
  form input — satu desain, dipakai ulang. Jangan bikin varian baru per halaman.
- **Skala spacing tetap**: 4 / 8 / 12 / 16 / 24 px. Radius selalu 8px. Jangan campur nilai lain.
- **Satu set ikon, satu gaya.** Pilih satu library (mis. outline style) dan pakai konsisten —
  jangan campur ikon outline dengan filled.
- **Istilah konsisten di seluruh aplikasi.** Aksi yang sama selalu pakai kata yang sama:
  "Ajukan Sewa" ya "Ajukan Sewa" di semua layar — jangan kadang "Booking", kadang "Pesan",
  kadang "Sewa Sekarang". Status booking pakai label persis dari state machine.
- Web dan app boleh beda layout (menyesuaikan platform), tapi **identitas visual & istilahnya
  harus terasa satu produk**.

## 7. LARANGAN: gaya "desain AI" pasaran

Desain TIDAK BOLEH terlihat seperti template AI-generated yang sekarang ada di mana-mana.
Daftar larangan eksplisit:

- **Dilarang gradasi ungu/biru-violet** (ciri khas AI landing page), gradient text, dan glow
  effect.
- **Dilarang glassmorphism** (kartu blur transparan) dan neumorphism.
- **Dilarang** kombinasi klise: latar krem + font serif besar + aksen terracotta; atau latar
  gelap + aksen hijau neon. Palet HANYA dari bagian 1.
- **Dilarang emoji sebagai ikon/dekorasi** di UI. Ikon ya ikon beneran.
- **Dilarang hero section generik**: headline gede di tengah + subteks + dua tombol "Mulai
  Sekarang / Pelajari Lebih Lanjut" + ilustrasi 3D mengambang. Halaman depan langsung
  fungsional: search + kategori + grid barang (ini marketplace, bukan landing page startup).
- **Dilarang** statistik/testimoni palsu ("Dipercaya 10.000+ pengguna") — jangan tampilkan
  angka yang tidak ada datanya.
- **Dilarang animasi berlebihan**: fade-in di setiap elemen saat scroll, elemen melayang-layang.
  Animasi hanya untuk feedback fungsional (loading skeleton, transisi tab) dan durasinya singkat.
- **Dilarang shadow tebal di mana-mana** (gaya template Tailwind default). Pakai border
  hairline atau shadow sangat tipis sesuai bagian 4.

- **Dilarang menjelaskan ALASAN sebuah fitur dibangun begini di dalam UI.** Tempatnya CLAUDE.md.
  Uji cepatnya: kalau sebuah kalimat di layar dapat diawali dengan kata "karena", ia hampir pasti
  salah tempat. Aturan ini sudah dilanggar tiga kali (9 Agu, 7 Sep, 8 Sep) — selalu oleh kalimat
  yang terasa "membantu" saat ditulis.

**Uji sederhana:** kalau screenshot halamannya bisa ketuker dengan template AI startup
generik, berarti gagal — revisi. Identitasnya harus jelas: marketplace sewa Indonesia,
merah-putih, bersih, fungsional.

---

## 8. Layar yang harus dibangun

**Renter** — Beranda · Hasil pencarian/kategori · Detail barang (+kalender) · Form booking &
pembayaran · Sewaanku (list + detail status) · Ajukan sengketa · Beri review · Akun.

**Seller** — Dashboard · Kelola listing (CRUD + foto) · Kalender & blackout · Booking masuk
(approve/reject, handover, terima kembali) · Pendapatan.

**Admin** — Verifikasi seller · Moderasi listing (antrean verifikasi listing baru + penurunan) ·
Antrean sengketa (+resolusi) · Log pembayaran/pencairan.

**Owner** — Ringkasan revenue & komisi · Kelola admin · Konfigurasi fee · Gerbang bayar.

**Kedua daftar itu tidak beririsan, dan sejak 3 Sep 2026 servernya ikut menegakkannya:** keempat
antrean Admin di atas dijawab 403 untuk Owner. Owner melihat angkanya di layar Pendapatan; yang
memutus verifikasi, penurunan listing, potongan deposit, dan pencairan hanya Admin. Nav panel
staf karena itu berbeda isinya per peran — itu bukan varian per halaman yang dilarang §6,
melainkan dua daftar layar yang memang berbeda.

---

## 9. Gerak & animasi

Bagian 7 tetap yang mengikat: **animasi hanya untuk umpan balik fungsional, durasinya singkat.**
Bagian ini hanya menetapkan nilainya supaya tidak ada lagi `.12s ease` yang ditulis ulang per
komponen (pelanggaran §6 "satu sumber token").

| Token | Nilai | Untuk |
|---|---|---|
| `--dur-1` | 120ms | umpan balik mikro: warna, latar, border, keadaan tekan |
| `--dur-2` | 180ms | popover, panel yang membuka, putaran caret |
| `--dur-3` | 260ms | foto membesar saat hover, tukar foto galeri, popover memudar keluar |
| `--dur-hold` | 240ms | masa tenggang sebelum popover hover mulai menutup |
| `--pop-ease` | `cubic-bezier(.34, 1.4, .5, 1)` | popover yang membesar saat muncul |
| `--ease` | `cubic-bezier(.2, .7, .3, 1)` | bawaan |
| `--ease-out` | `cubic-bezier(.16, 1, .3, 1)` | elemen yang bergerak masuk |

Aturannya:

- **Setiap kontrol yang berubah tampilan saat hover/fokus/tekan wajib mentransisikannya.** Perubahan
  yang mendadak terbaca sebagai kedipan, bukan sebagai umpan balik.
- **Elemen yang perlu animasi keluar tidak boleh dibuang dari markup.** Sembunyikan dengan
  `opacity` + `visibility` (jadwalkan `visibility` sesudah durasinya), jangan `@if`.
- **Yang menggerbangi “dapat disentuh” itu `visibility`, bukan `pointer-events`.** `pointer-events`
  tidak tertransisikan di Chrome — diukur, ia melompat seketika — jadi memakainya untuk menahan
  popover selama masa tenggang menghasilkan kartu yang tergambar tetapi mati. `visibility: hidden`
  sudah otomatis tidak dapat disentuh; satu properti mengurus keduanya.
- **Popover yang dibuka hover menutup dengan tenggang, membuka tanpa tenggang.** Yang lambat muncul
  terasa rusak; yang lambat hilang terasa sabar.
- **Popover MEMBESAR saat masuk dan MENGECIL saat keluar** (`scale(.88)` → `scale(1)`), dengan
  `transform-origin` di sudut tombolnya supaya ia tumbuh dari sana alih-alih meluncur. Arah membuka
  memakai `--pop-ease` yang sedikit melewati 1 lalu menetap; arah menutup memakai easing biasa,
  karena pantulan saat menghilang terbaca sebagai kegagapan.
- **Semua popover hover memakai satu komponen** (`HoverMenu`), bukan salinan per tempat. Tiga popover
  yang ditulis terpisah akan menyimpang pada percobaan pertama (§6).
- **Isi popover dimuat saat dibuka, bukan saat halaman dimuat** — kecuali angka pada badge-nya, yang
  harus sudah benar sebelum disentuh.
- **`transform` hanya `translateY` maksimal 4px, `scale` maksimal 1.08, dan hanya sebagai jawaban
  atas tindakan pengguna.** Tidak ada yang bergerak sendiri.
- **Dilarang animasi yang dipicu guliran** (fade-in saat elemen masuk layar) — §7.
- **`@media (prefers-reduced-motion: reduce)` wajib mematikan semuanya**, termasuk `transform`
  hover. Gerak yang tidak dapat dimatikan adalah gerak yang memaksa.
- **Fokus papan ketik wajib terlihat** (§5): satu aturan `:focus-visible` global, `--primary` di
  badan halaman dan `--header-fg` di dalam bilah header.

MAUI belum punya padanan bagian ini — animasinya masih bawaan platform. Kalau nanti dikerjakan,
angkanya disalin dari tabel di atas, bukan dikarang ulang (§6).

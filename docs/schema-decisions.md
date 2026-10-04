# Catatan skema — kenapa bentuknya begini

Pendamping `db/migrations/0001_init.sql`. Isinya keputusan yang tidak terbaca sendiri dari
DDL-nya. Kalau nanti ada yang tergoda "menyederhanakan" salah satu poin di sini, baca dulu
alasannya.

---

## 1. Bound rentang waktu wajib `[)`

`timestamptz` itu tipe kontinu, jadi PostgreSQL **tidak** menormalkan bound `tstzrange`
sendiri — beda dengan `daterange`/`int4range` yang otomatis jadi `[)`. Artinya
`tstzrange(a, b, '[]')` akan tersimpan apa adanya.

Kalau bound `[]` lolos masuk, sewa yang berakhir pukul 10:00 dan sewa berikutnya yang mulai
pukul 10:00 dianggap **bertabrakan**, dan back-to-back rental jadi mustahil. Untuk
marketplace sewa itu kerugian nyata: slot paling laku justru yang sambung-menyambung.

Karena itu ada `ck_bookings_range_canonical` dan `ck_blackouts_range_canonical` yang memaksa
`lower_inc AND NOT upper_inc`, bound tidak NULL, dan `lower < upper`. Sudah diuji dua arah:
tumpang tindih ditolak, back-to-back diterima.

## 2. `during` kolom tersimpan, `starts_at`/`ends_at` turunan

Arah generate-nya sengaja `during` → `starts_at`/`ends_at`, bukan sebaliknya. Alasannya
exclusion constraint membangun index di atas kolomnya, dan menaruh index itu di atas kolom
tersimpan biasa lebih aman ketimbang di atas kolom `GENERATED`. `starts_at`/`ends_at` cuma
untuk query, sorting, dan render kalender.

## 3. Kenapa exclusion constraint, bukan cek di kode

`SELECT ... WHERE overlaps` lalu `INSERT` adalah race condition klasik: dua transaksi
bersamaan sama-sama membaca "kosong" lalu sama-sama menulis. Isolation level `READ
COMMITTED` (default) tidak menolongnya — tidak ada baris yang dikunci karena baris yang
bertabrakan belum ada.

`EXCLUDE USING gist` menyelesaikannya di level index: penulis kedua **diblokir** sampai
transaksi pertama commit, lalu ditolak `23P01`. Sudah dibuktikan `db/race_test.ps1` — dua
proses psql sungguhan, mulai di milidetik yang sama, tepat satu lolos. Pemenangnya
berganti-ganti antar run, jadi race-nya asli.

Kode C# tetap harus mengecek ketersediaan lebih dulu — supaya renter dapat pesan yang
manusiawi, bukan stack trace. Tapi cek itu adalah UX, bukan pengaman. Pengamannya constraint.

## 4. Blackout dijaga trigger, bukan exclusion constraint

Exclusion constraint tidak bisa lintas tabel, padahal `bookings` dan `item_blackouts`
dua-duanya memblok waktu untuk item yang sama. Solusinya trigger dua arah
(`bookings_vs_blackout`, `blackouts_vs_bookings`) yang keduanya mengambil
`pg_advisory_xact_lock(item_id)` sebelum memeriksa.

Advisory lock itu yang membuatnya aman: tanpa lock, "booking masuk" dan "seller blok
tanggal" bisa berjalan bersamaan dan sama-sama lolos. Dengan lock per item, transaksi kedua
menunggu yang pertama selesai.

Alternatif yang sempat dipertimbangkan: satu tabel `item_reservations` gabungan supaya cukup
satu exclusion constraint. Ditolak karena status booking harus disinkronkan ke tabel itu —
dua sumber kebenaran untuk hal yang sama, dan itu justru sumber bug.

## 5. `pending` ikut mengunci slot

Keputusan pemilik produk. Spesifikasi awal menulis `WHERE status IN ('confirmed','active')`,
tapi di kalimat yang sama menyebut pending sebagai "soft-hold dengan expiry". Keduanya tidak
bisa benar bersamaan — hold yang tidak menahan slot tidak menahan apa pun, dan akibatnya
banyak renter bisa membayar untuk slot yang sama lalu semuanya kecuali satu harus di-refund.

Konsekuensinya: **job pelepas hold kedaluwarsa itu wajib, bukan opsional.** Tanpa job itu,
booking pending yang tidak dibayar akan mengunci slot selamanya. Index
`ix_bookings_hold_expiry` disiapkan khusus untuk job ini. Dikerjakan di milestone 4.

## 6. `payments` buku besar append-only

ERD awal memakai satu baris per tagihan dengan status `pending -> paid -> refunded`. Bentuk
itu tidak bisa merepresentasikan hal yang hampir pasti terjadi di marketplace sewa: **deposit
dipotong sebagian.** Barang balik lecet, deposit Rp 500.000 dipotong Rp 150.000, sisanya
dikembalikan. Dengan satu baris berstatus, kolom `amount` tetap 500.000 dan tidak ada tempat
mencatat potongannya.

Masalah kedua: Aturan 4.4 minta setiap operasi uang punya idempotency key sendiri. Refund
adalah operasi uang tersendiri dengan webhook-nya sendiri — kalau ia cuma perubahan status
di baris charge, tidak ada kolom untuk kunci idempotensinya.

Bentuk sekarang: satu baris = satu pergerakan dana, `DELETE` dilarang trigger, nominal beku,
status hanya boleh maju sekali dari `pending`. Posisi keuangan booking = agregasi baris.

`kind` menentukan `direction` lewat CHECK, jadi kombinasi ngawur seperti `rent_charge` +
`direction='out'` ditolak database.

## 7. Refund tidak selalu bisa balik ke sumber

Renter yang bayar tunai di Alfamart atau transfer ke VA tidak punya "sumber" yang bisa
dibalik gateway. Refund untuk mereka harus berupa **disbursement** ke rekening bank atau
e-wallet yang mereka daftarkan — makanya ada tabel `payout_accounts`, dipakai bersama oleh
seller (terima payout) dan renter (terima refund).

`ck_payments_disbursement_target` menegakkan pasangannya: `method='disbursement'` wajib punya
`payout_account_id`, dan `gateway_reversal` justru tidak boleh punya.

~~Implikasi produk untuk milestone 6: kalau renter bayar lewat channel non-reversible dan belum
mendaftarkan rekening, baris `deposit_refund` tetap dibuat berstatus `pending` sebagai
kewajiban platform, dan settle begitu rekeningnya masuk.~~

> **Dikoreksi di milestone 5 — rencana di atas tidak bisa dijalankan.** `ck_payments_disbursement_target`
> menuntut `method='disbursement'` punya `payout_account_id`, jadi baris `deposit_refund` tanpa
> rekening tujuan **mustahil disisipkan** — bukan "dibuat pending lalu disettle nanti", tapi
> ditolak database mentah-mentah. Constraint-nya sendiri benar; yang keliru rencananya.
> Penggantinya ada di poin 18.

## 8. Aturan 4.1 ditegakkan database

- `ck_bookings_total_is_derived` — `total_rent = price_snapshot * duration_units`
- `ck_bookings_fee_is_derived` — `platform_fee_amount = round(total_rent * platform_fee_rate, 2)`
- `renter_total` dan `seller_gross` kolom `GENERATED`, tidak bisa ditulisi sama sekali
- trigger `bookings_immutable_terms` membekukan item, renter, rentang waktu, dan semua kolom
  uang setelah baris dibuat

Client mengirim angka palsu → ditolak database, bukan cuma oleh validator C# yang mungkin
suatu hari kelewat di satu code path.

Semua kolom uang `numeric(14,2)`. Tidak ada `float` di mana pun yang menyentuh uang.

## 9. Snapshot harga di booking

`price_snapshot`, `price_unit_snapshot`, `platform_fee_rate`, `platform_fee_mode` disalin ke
booking saat dibuat. Seller menaikkan harga besok atau Owner mengubah komisi — booking lama
tidak ikut berubah. Tanpa ini, laporan pendapatan bulan lalu akan berubah angkanya setiap
kali ada yang mengedit listing.

## 10. State machine sebagai trigger

Aturan 4.3 minta transisi dikunci "di server". Ditaruh di trigger `BEFORE UPDATE OF status`
supaya juga berlaku untuk migrasi manual, skrip perbaikan data, dan psql langsung — bukan
cuma untuk request yang lewat controller.

`booking_status_history` diisi trigger `AFTER`, jadi tidak ada jalur kode yang bisa mengubah
status tanpa meninggalkan jejak audit.

## 11. Idempotensi dua arah

Dua mekanisme berbeda untuk dua arah yang berbeda:

- `payments.idempotency_key` UNIQUE — menjaga operasi yang **kita kirim** ke gateway
- `webhook_events (provider, event_id)` UNIQUE — menjaga notifikasi yang **gateway kirim**
  ke kita

Pola pemrosesan webhook di milestone 5: insert ke `webhook_events` lebih dulu di dalam
transaksi. Kalau kena unique violation, event itu sudah pernah diproses — keluar tanpa efek.

---

# Migrasi 0002 — pencarian & kalender

## 12. `search_vector` kolom `GENERATED`, bukan diisi trigger atau aplikasi

Tiga cara lazim mengisi kolom `tsvector`: aplikasi menghitungnya sebelum INSERT, trigger
`BEFORE INSERT OR UPDATE`, atau kolom `GENERATED ALWAYS ... STORED`. Yang ketiga dipilih
karena ia satu-satunya yang tidak bisa dilewati: aplikasi bisa lupa, trigger bisa di-disable,
tapi kolom generated dihitung ulang oleh PostgreSQL setiap kali `title`, `category`, atau
`description` berubah — termasuk waktu ada yang memperbaiki data langsung lewat psql.

Konsekuensinya konfigurasi text search harus ditulis eksplisit sebagai
`to_tsvector('indonesian', ...)`. Bentuk satu argumen `to_tsvector(text)` cuma `STABLE`, bukan
`IMMUTABLE`, karena hasilnya ikut berubah kalau `default_text_search_config` di sesi itu
berubah — dan kolom `GENERATED` menolak ekspresi yang tidak `IMMUTABLE`. Jadi bentuknya
memang tidak bisa lain.

Bobotnya `A` judul, `B` kategori, `C` deskripsi, dan `ts_rank` membacanya saat mengurutkan
hasil. Tanpa bobot, barang yang kebetulan menyebut "kamera" sekali di deskripsi berperingkat
sama dengan barang yang memang bernama "Kamera".

`websearch_to_tsquery`, bukan `to_tsquery`, yang dipakai di sisi query. Yang pertama menerima
apa pun yang diketik orang di kotak pencarian tanpa pernah melempar syntax error; yang kedua
menuntut sintaks operator yang benar, jadi input seperti `kamera &` akan berubah jadi 500.

## 13. View `item_blocked_ranges` — satu definisi untuk "waktu ini terpakai"

Dua tabel memblok waktu untuk barang yang sama: `bookings` yang masih hidup dan
`item_blackouts`. Artinya setiap kali ada kode bertanya "barang ini kosong tidak?", kode itu
harus memeriksa keduanya. Itu bentuk bug yang menunggu terjadi — satu code path ingat
memeriksa blackout, satu lagi lupa, dan yang muncul bukan error melainkan jawaban yang salah.

View ini membuat jawabannya cuma punya satu definisi. Filter tanggal di `GET /items` dan
kalender di `GET /items/{id}` membaca view yang sama.

**Daftar status di view wajib sama persis dengan klausa `WHERE` di constraint `no_overlap`.**
Kalau melenceng, kalender menjanjikan slot yang ditolak database saat di-booking, atau
menyembunyikan slot yang sebenarnya masih bisa dijual. Keduanya sunyi: tidak ada yang gagal,
cuma salah. Karena itu kecocokannya diperiksa dua kali — di `db/verify_0002.sql` dan di
`SchemaMappingTests`, keduanya membandingkan `pg_get_viewdef` dengan `pg_get_constraintdef`.

## 14. Ketersediaan ditanya lewat `starts_at`/`ends_at`, bukan operator range

Query "kosong antara X dan Y" ditulis `starts_at < Y AND ends_at > X`, bukan
`during && tstzrange(X, Y, '[)')`. Hasilnya identik untuk range `[)` — dan itu dijamin
`ck_bookings_range_canonical` serta `ck_blackouts_range_canonical` — tapi bentuk ini bisa
diekspresikan LINQ biasa, jadi tipe milik driver tidak perlu bocor ke lapisan Api.

Gantinya `item_blackouts` dapat index btree sendiri (`ix_item_blackouts_window`): index gist
milik `no_blackout_overlap` ada di atas `(item_id, during)` dan tidak melayani bentuk query
itu.

Filter ini **bukan** jaminan booking akan berhasil, dan tidak dimaksudkan begitu. Antara hasil
pencarian tampil dan renter menekan "sewa", orang lain bisa mengambil slotnya. Yang memutuskan
tetap `no_overlap` saat INSERT (Aturan 4.2) — sama seperti pemeriksaan ketersediaan di C#,
filter ini urusan UX, bukan pengaman.

---

# Migrasi 0003 — dua jendela hold

## 15. Satu `hold_minutes` jadi `approval_minutes` + `payment_minutes`

Rancangan 0001 mengandaikan pembayaran yang mengonfirmasi booking, jadi satu jendela sudah
cukup: `pending` = menunggu bayar. Pemilik produk memutuskan lain di milestone 4 — **seller
menyetujui dulu, baru renter membayar**. Itu memecah satu tunggu jadi dua, dan dua-duanya
menahan slot:

```
pending    menunggu seller menyetujui   -> approval_minutes
confirmed  menunggu renter membayar     -> payment_minutes
```

Poin 5 di atas menjelaskan kenapa `pending` harus punya tenggat: hold yang tidak pernah lepas
mengunci tanggal barang selamanya. Alasan yang sama persis berlaku untuk `confirmed` dalam
alur baru ini. Kalau cuma `pending` yang disapu, bahayanya tidak hilang — ia pindah satu
status ke kanan, dan justru jadi lebih buruk karena booking yang sudah disetujui terlihat
sah bagi semua pihak.

Kolomnya diganti nama, bukan dipakai ulang diam-diam. `hold_minutes` sudah tidak menggambarkan
isinya begitu ada dua jendela, dan setelan yang namanya menyesatkan adalah cara Owner salah
mengatur batas waktu tanpa sadar. Nilai bawaannya juga naik dari 15 menit ke 1440: 15 menit
masuk akal sebagai jendela bayar, tapi sebagai jendela balas seller ia praktis menjamin setiap
booking kedaluwarsa sebelum sempat dilihat.

**`hold_expires_at IS NULL` berarti tidak ada tenggat.** Itu yang mengeluarkan booking dari
daftar kandidat kedaluwarsa, dan itu pula yang akan dipakai milestone 5: begitu pembayaran
masuk, kolomnya dikosongkan dan booking berhenti bisa tersapu. Karena itu predikat index
`ix_bookings_hold_expiry` ikut memuat `hold_expires_at IS NOT NULL`.

## 16. Durasi memakai satuan berpanjang tetap, bukan kalender

`duration_units` dihitung dengan pembulatan ke atas atas satuan yang panjangnya tetap: jam =
1 jam, hari = 24 jam, minggu = 7 hari, **bulan = 30 hari**.

Bulan kalender sempat dipertimbangkan dan ditolak. 1 Jan→1 Feb dan 1 Feb→1 Mar sama-sama
"satu bulan" tapi berbeda tiga hari, jadi harga efektif per harinya berubah-ubah tergantung
kapan orang menyewa. Lebih buruk lagi, batas bulan bergantung zona waktu — penyewa di Jakarta
dan di Makassar bisa mendapat jumlah bulan berbeda untuk rentang yang sama. Untuk kolom yang
dijaga CHECK `total_rent = price_snapshot * duration_units`, aritmetika yang bergantung pada
kalender dan zona waktu adalah sumber selisih yang tidak ada gunanya.

Pembagiannya dikerjakan dengan bilangan bulat, bukan `Math.Ceiling` atas hasil bagi `double`.
Pada rentang yang persis kelipatan satuan, pembagian pecahan bisa meleset satu satuan — dan
satu satuan di sini adalah satu hari sewa di tagihan orang.

Satu jebakan lagi yang sudah ditutup: **pembulatan komisi**. PostgreSQL `round(x, 2)`
membulatkan menjauhi nol, sedangkan `Math.Round` bawaan .NET membulatkan ke genap (0,125 →
0,12). Karena `ck_bookings_fee_is_derived` menghitung ulang nilainya di sisi database, selisih
satu sen membuat INSERT-nya ditolak mentah-mentah. `BookingCalculator.Round2` memakai
`MidpointRounding.AwayFromZero` supaya keduanya sepakat.

## 17. Salinan state machine di C# diuji terhadap trigger-nya

Aturan 4.3 ditegakkan trigger `bookings_status_transition` (poin 10). Tapi API tetap butuh
tabel transisi sendiri supaya bisa menolak lebih awal dengan pesan yang menyebut transisi apa
yang sebenarnya boleh — bukan melemparkan `check_violation` mentah ke muka pengguna.

Salinan itu bisa melenceng, dan melencengnya sunyi: API akan mengizinkan sesuatu yang database
tolak (500 mendadak) atau menolak sesuatu yang sebenarnya sah. Karena itu
`BookingTransitionTests` mencoba **seluruh 30 pasangan status** terhadap database sungguhan
dan gagal kalau kedua sumber tidak sepakat — pola yang sama dengan pemeriksaan view versus
constraint di poin 13.

---

# Milestone 5 — uang benar-benar bergerak

## 18. Rekening pengembalian diminta sebelum uang diterima, bukan sesudah

Poin 7 semula merencanakan: kalau renter membayar lewat channel non-reversible tanpa punya
rekening terdaftar, baris refund tetap dibuat `pending` dan disettle begitu rekeningnya masuk.
**Rencana itu tidak bisa dijalankan.** `ck_payments_disbursement_target` menuntut
`method='disbursement'` punya `payout_account_id`, jadi barisnya mustahil disisipkan sama
sekali — bukan tertunda, tapi ditolak.

Pilihannya tinggal dua: melonggarkan constraint, atau memindahkan syaratnya lebih awal.
Yang dipilih yang kedua. `POST /bookings/{id}/pay` menolak channel non-reversible kalau renter
belum punya rekening terdaftar, dengan pesan yang menyebutkan jalan keluarnya (daftarkan
rekening, atau bayar dengan GoPay/kartu).

Alasannya bukan sekadar menyenangkan constraint: **jangan menerima uang yang belum tentu bisa
dikembalikan.** Kalau syaratnya ditaruh di belakang, platform bisa memegang uang orang tanpa
punya bentuk untuk mencatat kewajibannya — persis keadaan yang poin 7 ingin hindari.
Melonggarkan constraint akan menghasilkan hal yang sama, hanya lebih sunyi.

Konsekuensinya renter yang mau bayar lewat VA harus mengisi rekening dulu. Itu gesekan nyata,
dan disengaja.

## 19. Komisi baru lahir saat sewa selesai, bukan saat uang masuk

`platform_fee` tidak dibuat ketika pembayaran diterima. Escrow menahan uang; alokasinya
menyusul ketika sewa benar-benar selesai (milestone 6).

Kalau baris komisi dibuat di muka, pembatalan akan menuntut pembalikannya — dan `payments`
adalah buku besar append-only tanpa jenis "pembatalan komisi". Yang tersisa cuma pilihan
buruk: menambah `kind` baru hanya demi membatalkan sesuatu yang belum pernah benar-benar
menjadi pendapatan, atau membiarkan alokasi yang salah tercatat selamanya.

Menundanya menghapus persoalan itu seluruhnya: booking yang batal tidak punya baris komisi
untuk dibalik, jadi refund penuh cukup dua baris keluar. Itu juga lebih jujur secara
akuntansi — platform belum mengerjakan apa pun ketika uang baru masuk ke escrow.

## 20. Satu transaksi gateway, dua baris buku besar

Renter membayar sekali, tapi tercatat sebagai `rent_charge` dan `deposit_charge` terpisah.
Keduanya berbagi `gateway_ref` yang sama karena memang satu transaksi.

Dipisah karena nasib keduanya berbeda di akhir sewa: uang sewa dicairkan ke seller, deposit
dikembalikan ke renter — dan deposit bisa dipotong sebagian (poin 6). Satu baris gabungan
tidak bisa merepresentasikan dua takdir itu.

Instruksi bayar dari gateway (nomor VA, kode bayar, isi QR) disimpan di baris `rent_charge`
saja, lewat kolom `gateway_instructions` di migrasi 0004. Perlu disimpan karena `/pay` wajib
idempotent: renter yang menutup aplikasi lalu kembali harus melihat nomor VA yang sama, dan
menanyakannya ulang ke gateway bukan jalan keluar — Midtrans menolak `order_id` yang pernah
dipakai. Karena itu pula nomor percobaan ikut masuk ke dalam `order_id`
(`SEWA-{bookingId}-{attempt}`): tagihan yang kedaluwarsa harus bisa diganti tagihan baru
dengan id yang belum pernah ada.

## 21. Uang yang datang untuk booking yang sudah batal

Bisa terjadi tanpa ada yang berbuat salah: tenggat bayar lewat, job pelepas hold membatalkan
booking-nya, lalu transfer yang sudah terlanjur dikirim renter tiba beberapa detik kemudian.

Uangnya nyata dan sudah ada di kami. Jadi ia **tetap dicatat masuk** — lalu langsung dicatat
sebagai kewajiban untuk dikembalikan, dalam transaksi yang sama. Menandainya "gagal" karena
booking-nya sudah batal berarti menyimpan uang orang tanpa jejak apa pun di buku besar.

## 22. Pembulatan ke rupiah utuh

Rupiah tidak punya satuan pecahan dan Midtrans menolak `gross_amount` yang bukan bilangan
bulat, sementara `platform_fee_amount` disimpan `numeric(14,2)` — pada mode `on_top` ia ikut
ditagihkan ke renter, jadi tagihan bisa jatuh di angka bersen. Nominal tagihan dibulatkan
**ke atas** supaya platform tidak pernah menagih kurang dari yang tercatat; selisihnya selalu
di bawah satu rupiah.

---

# Milestone 9 — panel Owner

## 23. Akses dicabut dengan menonaktifkan, bukan menghapus

`users.deactivated_at` (0006), bukan `DELETE FROM users`.

Setiap keputusan staf meninggalkan jejak yang menunjuk balik ke barisnya: `users.verified_by`
untuk verifikasi seller, `disputes.resolved_by` untuk putusan sengketa,
`platform_settings.updated_by` untuk perubahan komisi. Menghapus akunnya berarti memutus jejak
itu — dan foreign key-nya memang menolak, sama seperti untuk user bertransaksi. Ini bentuk yang
sama dengan buku besar append-only: catatan tidak dihapus, cuma ditutup.

Satu pengecualian ditegakkan CHECK, bukan cuma controller: **owner tidak pernah bisa
dinonaktifkan** (`ck_users_owner_always_active`). Owner satu-satunya yang bisa memulihkan akun
lain, jadi menonaktifkannya menutup pintu pemulihan terakhir — termasuk untuk dirinya sendiri.
Akibatnya tidak bisa dibatalkan dari dalam aplikasi, dan itulah syarat sebuah aturan naik ke
level database.

## 24. Keadaan akun diperiksa ulang tiap permintaan ber-token

JWT tidak bisa ditarik kembali: sekali diterbitkan ia sah sampai kedaluwarsa, dan di sini itu
12 jam. Kalau pencabutan baru berlaku saat token habis, admin yang aksesnya baru dicabut masih
bisa meloloskan seller, memutus sengketa, dan menandai pencairan sepanjang sisa hari itu.
Pencabutan yang baru berlaku besok bukan pencabutan.

`ActiveAccountCheck` karena itu dipasang di `JwtBearerEvents.OnTokenValidated` dan membaca
`deactivated_at` dari database setiap permintaan — satu query kunci primer, ditanggung
sadar-sadar. Alternatifnya (daftar cabut yang di-cache) menukar biaya itu dengan jendela basi,
yang justru persis masalah yang sedang ditutup. Sekalian menutup kasus tetangganya: token milik
akun yang barisnya sudah tidak ada.

## 25. Kredensial gateway di database, konfigurasi jadi cadangan

Keputusan terkunci #12 menaruh kunci Midtrans di panel Owner. Kolomnya menumpang
`platform_settings` (0007) karena persoalannya sama: konfigurasi platform yang singleton dan
dimiliki Owner, sudah dijaga PK boolean ber-CHECK.

Urutannya database dulu, konfigurasi belakangan. Cadangan itu bukan kesopanan — ia yang membuat
instalasi yang sudah berjalan dengan environment variable tidak mendadak berhenti menerima
pembayaran saat migrasi diterapkan, dan yang membuat seluruh test pembayaran tetap berdiri di
atas kunci uji tanpa menyemai baris konfigurasi.

Yang **tidak** naik ke DDL: kecocokan kunci dengan mode produksi. Salah pasang di situ berbahaya
(mode sandbox dengan kunci produksi = menagih kartu sungguhan sementara semua orang mengira sedang
menguji), tapi aturannya milik Midtrans, bukan invarian data kita. Constraint di berkas ini
menegakkan invarian sendiri — keseimbangan buku besar, state machine, keunikan. Mengunci konvensi
penamaan pihak ketiga ke dalam DDL berarti perubahan sepihak di sisi mereka hanya bisa dijawab
dengan migrasi. Pemeriksaannya ada satu lapis di atas, di `OwnerController`, dengan 400 dan pesan
yang menjelaskan.

**Keputusan itu terbayar 17 Agu 2026, dan sekaligus menghukum cara pemeriksaannya dulu.** Midtrans
berhenti memberi awalan `SB-` pada kunci sandbox; kedua lingkungan kini sama persis bentuknya.
Karena aturannya tidak pernah ditulis sebagai CHECK, tidak ada migrasi yang perlu dibuat — cukup
satu lapis di atas yang diganti. Yang menggantikannya bukan tebakan awalan yang lebih pintar,
melainkan **pertanyaan langsung ke Midtrans** sebelum kunci disimpan (`VerifyServerKeyAsync`):
konvensi penamaan pihak ketiga ternyata tidak layak dipercaya di lapisan mana pun, DDL maupun C#.

## 26. Revenue diturunkan, bukan disimpan

`GET /owner/revenue` tidak menambah satu pun kolom saldo. Angkanya penjumlahan baris `payments`
dengan filter arah dan status, dan yang paling penting **`InEscrow` diturunkan** dari yang lain
(`CashHeld − PayoutsDue − CommissionEarned`), bukan dijumlah sendiri. Dengan begitu identitas

```
CashHeld = PayoutsDue + CommissionEarned + InEscrow
```

berlaku karena konstruksi, bukan karena kebetulan dua penjumlahan berbeda menghasilkan hal yang
sama. Kalau ia pernah tidak seimbang, yang salah buku besarnya — dan itu memang yang ingin
kelihatan.

## 27. Refresh token dirotasi, dan pemakaian ulang membunuh seluruh keluarga

Access token 12 jam yang tidak bisa diperpanjang memaksa login ulang dua kali sehari. Di web itu
menjengkelkan; di aplikasi mobile itu mematikan. Yang menutupnya tabel `refresh_tokens` (0008) —
dan bentuknya **rotasi**, bukan satu token abadi berumur 30 hari.

Bedanya bukan gaya. Token abadi yang bocor memberi penyusup akses sebulan **tanpa satu pun jejak**.
Dengan rotasi, satu token berlaku sekali: menukarnya menerbitkan pengganti dan mematikan yang lama,
jadi token yang sama muncul dua kali berarti ada dua pihak yang memegangnya. Itu satu-satunya
sinyal pencurian yang bisa didapat tanpa memata-matai pengguna.

Kalau sinyal itu muncul, **seluruh keluarga** (`family_id`, satu keluarga = satu sesi login)
dicabut — yang dicuri maupun yang asli. Pencurian tidak bisa dibedakan dari klien yang keliru
mengirim token lama dua kali, jadi yang dipilih adalah kesalahan yang bisa dipulihkan pengguna
(login ulang) alih-alih membiarkan penyusup ikut berjalan. Harganya: **klien wajib menyerialkan
perpanjangannya**, karena dua permintaan paralel dengan token yang sama akan saling membunuh.

Yang naik ke DDL, dan alasannya masing-masing:

| Ditegakkan database | Kalau cuma di C# |
|---|---|
| `used_at`/`revoked_at` hanya bisa maju (trigger `refresh_tokens_monotonic`) | satu UPDATE keliru menghidupkan token bekas, dan deteksi pemakaian ulang jadi teater |
| hash, pemilik, keluarga, dan `expires_at` beku setelah diterbitkan | memperpanjang `expires_at` = menerbitkan token baru tanpa jejak |
| `UNIQUE (token_hash)` | dua baris bisa memegang token yang sama |
| `octet_length(token_hash) = 32` | kolom yang diisi token mentah lolos tanpa ada yang menyadarinya |
| `(revoked_at IS NULL) = (revoked_reason IS NULL)` | sesi mati tanpa keterangan kenapa |

Sekali-pakai sendiri **tidak** dijaga pemeriksaan `used_at is null` di C# — di antara pemeriksaan
itu dan penulisannya, permintaan lain bisa menyelip. Yang menjaganya predikat di
`UPDATE … WHERE used_at IS NULL`: dari dua penukaran bersamaan, tepat satu memperoleh satu baris.
Pola yang sama dengan anti double-booking (Aturan 4.2), dan diuji dengan cara yang sama — lima
permintaan paralel, tepat satu berhasil.

Dua hal yang **berbeda** dari tabel-tabel lain di skema ini, keduanya konsekuensi dari "ini state
sesi, bukan catatan uang":

- **`ON DELETE CASCADE` ke `users`**, satu-satunya FK ke `users` yang bukan RESTRICT. Sesi yang
  hidup lebih lama daripada akun pemiliknya lebih berbahaya daripada sesi yang ikut terhapus.
  (Akun yang punya riwayat transaksi tetap tidak bisa dihapus; yang menahannya FK di `items`,
  `bookings`, dan `payments`.)
- **DELETE tidak dilarang**, beda dengan `payments`. Barisnya sampah begitu mati, dan penyapunya
  nanti perlu bisa menghapusnya.

Token mentahnya tidak disimpan, cuma SHA-256-nya — tanpa salt dan tanpa pelambatan, dan itu memang
benar: yang di-hash bukan rahasia pilihan manusia melainkan 32 byte acak, jadi tidak ada kamus
untuk melawannya. Yang dibutuhkan cuma satu arah, supaya isi tabel yang bocor tidak bisa ditukar
jadi sesi.

**Yang tidak ikut berubah:** `Jwt:AccessTokenHours` tetap 12. Refresh token biasanya jadi alasan
memperpendek access token, tapi di sini alasan itu sudah dijawab lebih dulu oleh poin 24 —
`ActiveAccountCheck` membuat pencabutan berlaku seketika tanpa bergantung pada umur token.
Memperpendeknya sekarang cuma menambah lalu lintas `/auth/refresh` tanpa menutup apa pun.

---

## 28. Penangguhan listing hidup di kolomnya sendiri, bukan di `items.status`

Tabel ROLE menjanjikan "moderasi listing" untuk admin. Godaan pertamanya jelas: `items.status`
sudah punya nilai `inactive`, tinggal admin menyetelnya. Itu tidak bisa dipakai, dan sebabnya
bukan estetika.

`items.status` **milik pemilik barang**. Dia yang menyalakan dan mematikan listing-nya lewat
`PUT /items/{id}`, dan itu memang haknya. Kalau moderasi memakai kolom yang sama, penurunan oleh
admin bisa dibatalkan pemiliknya satu detik kemudian dengan menekan "aktifkan" — dan pemilik yang
listing-nya diturunkan karena menipu justru yang paling berkepentingan menekannya. Keputusan
moderasi yang bisa dianulir pihak yang dimoderasi bukan moderasi.

Ada akibat kedua yang sama merepotkannya: dengan satu kolom, tidak ada yang bisa membedakan
listing yang **diturunkan admin** dari listing yang sedang **diistirahatkan pemiliknya**. Pemilik
melihat barangnya "nonaktif" tanpa pernah tahu kenapa, dan admin berikutnya tidak tahu apakah
barang itu sudah pernah diputus atau belum.

Karena itu migrasi 0009 menambah `suspended_at`, `suspended_by`, `suspension_reason`:

```
terlihat di katalog  =  status = 'active'   AND   suspended_at IS NULL
                        └── pemilik ──┘           └──── admin ────┘
```

Dua suku, dua penguasa, dan tidak ada yang bisa membatalkan keputusan yang lain. Predikat itu
sekarang jadi satu-satunya definisi "terlihat publik" dan dipakai di empat tempat sekaligus:
`GET /items`, `CanView` (dipakai `GET /items/{id}` dan daftar review-nya), `POST /bookings`, dan
index parsial `ix_items_browse` — yang ikut diubah supaya tetap sepadan dengan kuerinya.

Ketiga kolomnya hidup dan mati bersama (`ck_items_suspension_whole`) karena penurunan listing
adalah keputusan terhadap penghasilan orang: tanpa constraint itu, baris bisa berakhir
"ditangguhkan tanpa alasan dan tanpa yang menangguhkan". Yang menangguhkan wajib admin/owner,
ditegakkan trigger `items_suspended_by_role` sepola `trg_items_seller_role` — seller tidak boleh
bisa menurunkan listing pesaingnya lewat jalur apa pun, termasuk psql. Dan `suspended_by` memakai
`ON DELETE RESTRICT` seperti `users.verified_by`: jejak siapa-memutuskan-apa tidak boleh putus.

**Yang sengaja tidak ikut:** menurunkan listing **tidak** membatalkan sewa yang sudah berjalan.
Uangnya sudah bergerak dan barangnya mungkin sudah di tangan penyewa; membatalkannya sepihak
memindahkan kerugian ke orang yang tidak melakukan apa-apa. Yang berhenti cuma pemesanan baru.

**Yang sengaja tidak dibuat:** riwayat moderasi. Satu baris menyimpan keputusan **terakhir**, dan
memulihkan menghapusnya. Kalau nanti perlu "listing ini sudah tiga kali diturunkan", bentuknya
tabel `item_moderation_events` sepola `booking_status_history` — bukan menambah kolom di sini.

---

## 29. Nomor sewa & transaksi jadi kolom sendiri, bukan potongan UUID

`bookings.reference` (`SW-4F7K2Q`) dan `payments.reference` (`TR-9QM3XB`), migrasi 0010.

**Kenapa bukan uuid-nya saja.** Sebelum ini satu-satunya identitas sebuah sewa adalah uuid, dan
panel staf menampilkan delapan karakter pertamanya. Potongan itu punya dua cacat sekaligus: ia
**tidak bisa diucapkan** (penyewa yang menelepon CS tidak punya apa pun untuk disebut selain nama
barang dan tanggal — tidak cukup begitu ia menyewa barang yang sama dua kali), dan ia **bukan
identitas melainkan awalan** — delapan karakter pertama uuid tidak dijamin unik oleh apa pun, ia
kebetulan unik selama barisnya sedikit.

**Abjadnya 32 huruf tanpa I, O, 0, dan 1.** Kode ini dibuat untuk dibaca dari layar lalu diketik
ulang atau diucapkan lewat telepon, dan di situlah pasangan huruf itu tertukar. Membuang empatnya
lebih murah daripada menerima aduan "kodenya tidak ketemu" yang sebabnya salah ketik satu huruf.

**Acak, bukan berurutan.** Nomor urut membocorkan volume: siapa pun yang menyewa dua kali bisa
mengurangkan kedua nomornya dan tahu berapa banyak sewa terjadi di antaranya.

**Tabrakan mungkin, dan sengaja tidak disembunyikan.** Enam karakter = 32⁶ ≈ 1,07 miliar. Peluang
per baris baru = (jumlah baris) / 1,07 miliar — pada satu juta sewa masih 0,09%. `gen_*_reference()`
mengulang sampai sepuluh kali, tapi yang **menjamin** keunikan tetap constraint `UNIQUE`, bukan
pengulangan itu: dua transaksi bersamaan bisa sama-sama lolos pemeriksaan `EXISTS`, dan yang kedua
ditolak index. Kalau penolakan itu suatu hari terlihat di produksi, obatnya melebarkan kodenya jadi
tujuh karakter — bukan menambah putaran.

**DEFAULT-nya di database, bukan di C#.** Sepola `id` yang memakai `gen_random_uuid()`: tidak ada
jalan masuk yang bisa melewatkannya, termasuk `INSERT` manual lewat psql dan data demo. Di sisi EF,
propertinya `null` sampai tersimpan — diisi `string.Empty`, EF akan mengirim string kosong dan
CHECK format menolaknya.

**Awalannya berbeda antar tabel** supaya nomor sewa dan nomor transaksi tidak tertukar saat disebut
berdampingan — persoalan nyata di antrean pencairan, yang menampilkan keduanya sekaligus. CHECK-nya
menolak awalan milik tabel lain, jadi `TR-` tidak bisa masuk ke kolom sewa.

**Kode ini bukan rahasia.** Ia ditempel di email, dibacakan lewat telepon, dan muncul di tangkapan
layar. Tidak ada satu pun endpoint yang boleh memberi akses karena pemanggilnya tahu kodenya;
otorisasi tetap lewat id pemegang token, persis seperti sebelum kolom ini ada. Kalau suatu saat
kodenya dipakai sebagai tautan yang bisa dibuka tanpa masuk, yang harus berubah bukan `random()` di
`sewa_reference_body()` melainkan keputusan itu sendiri.

---

## 30. Peninjauan listing hidup di kolomnya sendiri, dan baris lama disetujui tanpa peninjau

Migrasi 0017, diminta pemilik produk 21 Sep 2026: listing harus diverifikasi admin sebelum tampil.

**Kenapa bukan `suspended_at` yang dipakai terbalik.** 0009 sudah punya "admin menyembunyikan
listing"; godaannya melahirkan tiap listing dalam keadaan ditangguhkan lalu memaknai "pulihkan"
sebagai "setujui". `ck_items_suspension_whole` langsung menolaknya — penangguhan wajib punya alasan
dan penangguh, dan listing yang baru lahir tidak punya keduanya. Lebih penting lagi, artinya memang
berbeda: ditangguhkan = "pernah tayang, lalu diputus bermasalah"; menunggu = "belum pernah dilihat
siapa pun". Dua keadaan yang pemilik barang berhak bedakan tidak boleh berbagi satu kolom.

**Kenapa tiga nilai, bukan boolean.** `pending` dan `rejected` sama-sama "belum boleh tayang",
tetapi yang pertama tinggal menunggu dan yang kedua harus **memperbaiki** sesuatu — dan sesuatu itu
wajib tertulis (`rejection_reason`, sepola `suspension_reason`). `is_approved` tidak dapat menyimpan
"ditolak karena apa".

Predikat "terlihat publik" karena itu bertambah satu suku:

```
status = 'active'  AND  suspended_at IS NULL  AND  review_status = 'approved'
└── pemilik ──┘         └─ admin: penurunan ─┘      └── admin: peninjauan ──┘
```

**Baris lama di-backfill `approved` dengan `reviewed_by` NULL — dan itu disengaja dua arah.**
Menyembunyikan seluruh katalog sampai admin meninjau ratusan listing yang dipasang di bawah aturan
lama bukan yang diminta; tetapi menulis seorang admin sebagai peninjau listing yang tidak pernah
ia lihat adalah kebohongan di kolom yang justru ada untuk jejak keputusan. NULL di sana berarti
"disetujui otomatis saat aturan ini mulai berlaku".

Celah yang dibuka pengecualian itu ditutup trigger `items_review_decision`, yang **sengaja
dipasang sesudah backfill**: berpindah **ke** `approved`/`rejected` tanpa `reviewed_by` ditolak,
dan `reviewed_by` wajib admin/owner (sepola `items_suspended_by_role`). Baris hasil backfill tidak
berpindah, jadi tidak kena; keputusan baru mana pun kena. `rejected` tidak punya pengecualian
sama sekali — penolakan selalu keputusan seseorang.

`ix_items_browse` ikut menyempit ke `review_status = 'approved'`, sepola 0009, dan
`ix_items_review_pending` (parsial, `created_at`) melayani antrean yang dibaca urut lahirnya.

**Yang sengaja tidak dibuat:** versi konten (selama ditinjau ulang yang tampil adalah tidak ada,
bukan versi lama) dan riwayat peninjauan (satu baris = keputusan terakhir, sepola penangguhan).

---

## Menyimpang dari ERD Bagian 5

| Perubahan | Alasan |
|---|---|
| `payments.status` tidak punya `refunded` | refund adalah baris sendiri (poin 6) |
| `users.password_hash` ditambahkan | dibutuhkan milestone 2 |
| `bookings` menyimpan snapshot harga & komisi | poin 9 |
| exclusion constraint mencakup `pending` | poin 5 |
| tabel baru `platform_settings` | Owner atur komisi + `hold_minutes` |
| tabel baru `payout_accounts` | poin 7 |
| tabel baru `webhook_events` | poin 11 |
| tabel baru `booking_status_history` | poin 10 |
| kolom `items.search_vector` (0002) | poin 12 |
| view `item_blocked_ranges` (0002) | poin 13 |
| `hold_minutes` → `approval_minutes` + `payment_minutes` (0003) | poin 15 |
| kolom `payments.gateway_instructions` (0004) | poin 20 |
| kolom `users.deactivated_at` (0006) | poin 23 |
| kolom kredensial Midtrans di `platform_settings` (0007) | poin 25 |
| tabel baru `refresh_tokens` (0008) | poin 27 |
| kolom `items.suspended_at/_by/_reason` (0009) | poin 28 |

## Sengaja belum dibuat

- **Tabel `categories`** — `items.category` masih `text` sesuai ERD. Kalau nanti butuh
  ikon/urutan/terjemahan per kategori (tab "Kategori" di mobile), ini yang pertama berubah.
  Selama masih teks bebas, filter kategori di `GET /items` dicocokkan tanpa peduli huruf
  besar-kecil — dan itu membuat index parsial `ix_items_browse` tidak terpakai.
- **Multi-currency** — kolom `currency` ada di `payments` khusus untuk rekonsiliasi dengan
  payload gateway; isinya selalu `IDR`. Tidak ada konversi mata uang.
- **Penyapu `refresh_tokens`** — setiap perpanjangan menambah satu baris, dan yang mati tidak
  pernah dibuang. Sekitar 60 baris per pengguna per bulan dengan access token 12 jam; kecil, tapi
  tumbuh terus. DDL-nya sudah menyiapkan jalannya (DELETE tidak dilarang, dan index parsial
  `ix_refresh_tokens_user_live` membuat baris mati tidak membebani pencarian sesi hidup), jadi
  menutupnya = satu job sepola `HoldSweeper` yang menghapus baris kedaluwarsa lewat tenggat
  tertentu. Sengaja belum dibuat supaya milestone 8 tidak menyeret hosted service baru.
- **Soft delete user** — belum ada, dan `deactivated_at` (0006) **bukan** itu: ia mencabut
  akses masuk, tidak menyembunyikan barisnya dari query mana pun. Endpoint yang mengelolanya
  pun baru untuk akun admin. FK ke `users` tetap `ON DELETE RESTRICT` supaya penghapusan user
  yang punya riwayat transaksi gagal keras, bukan diam-diam menghapus jejak uang.

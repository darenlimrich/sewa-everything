# Deploy VPS — 22 September 2026

Server: `darren@172.104.178.244`, Ubuntu 24.04, runtime .NET 10, PostgreSQL 16.

URL saat verifikasi: https://included-paragraphs-disciplines-over.trycloudflare.com

Ini Cloudflare Quick Tunnel. URL berubah ketika proses tunnel dimulai ulang. Aplikasi berjalan di VPS, bukan di komputer pengembang. Untuk alamat permanen, gunakan named tunnel dengan domain dari akun Cloudflare.

## Susunan layanan

| Service | Fungsi |
|---|---|
| `sewaku` | API Production, `127.0.0.1:5000` |
| `sewaku-proxy` | Nginx untuk web Blazor, API, foto dan webhook, `127.0.0.1:8088` |
| `sewaku-tunnel` | Cloudflare HTTPS menuju proxy |

Ketiganya enabled dan active saat diperiksa. Apache yang sudah memakai port 80 tetap berjalan. Nginx Sewaku mempunyai konfigurasi dan PID tersendiri, sehingga tidak berebut port dengan Apache.

Web dan API memakai origin yang sama. `/api/` diteruskan ke API tanpa prefiks; `/uploads/` dan `/webhooks/payment` diteruskan utuh. Proxy hanya mendengarkan loopback; header alamat pengunjung berasal dari Cloudflare. API tetap menerapkan HTTPS dan pembatasan Host. CSP dikirim melalui header; hash import map dihitung dari `index.html` hasil publish.

## Lokasi penting

- Rilis: `/var/www/sewaku/releases/20260922-0800/{api,web}`.
- Tautan aktif: `/var/www/sewaku/api` dan `/var/www/sewaku/web`.
- Foto: `/var/lib/sewaku/photos`, 169 berkas saat diperiksa.
- Konfigurasi rahasia API: `/etc/sewaku.env`, permission 600; kredensial yang sudah ada dipertahankan.
- Proxy: `/etc/nginx/sewaku-proxy.conf`.
- Pengendali tunnel: `/usr/local/lib/sewaku-tunnel.py`.
- Pembaruan URL: `/usr/local/lib/sewaku-configure.py`.
- URL tunnel terbaru: `/var/lib/sewaku/tunnel-url`.

Pengendali tunnel memperbarui `Api:BaseUrl` pada konfigurasi web, AllowedHosts, CORS, alamat reset kata sandi, dan callback Midtrans ketika menerima URL baru, kemudian me-restart API. Alamat lama tidak otomatis menjadi redirect. Tagihan sandbox yang dibuat sebelum pergantian URL masih dapat membawa callback lama.

```sh
cat /var/lib/sewaku/tunnel-url
sudo systemctl status sewaku sewaku-proxy sewaku-tunnel
sudo journalctl -u sewaku -u sewaku-proxy -u sewaku-tunnel -n 60 --no-pager
```

## Cadangan dan data

Database VPS yang sudah ada digunakan langsung, tanpa diisi ulang dari database lokal. Saat verifikasi terdapat 26 akun, 166 barang, 55 sewa, dan 106 pembayaran. Skema peninjauan barang 0017 sudah ada. Midtrans tetap sandbox.

Cadangan database sebelum deploy: `/home/darren/sewaku-deploy-20260922/database-before.dump`, format custom PostgreSQL, permission 600. Direktori cadangan berpermission 700 dan juga menyimpan konfigurasi awal. Aplikasi sebelumnya berada di `/var/www/sewaku/api-before-20260922` dan `/var/www/sewaku/web-before-20260922`.

Rollback aplikasi dapat mengarahkan kedua symlink aktif ke direktori sebelumnya lalu me-restart `sewaku`. Sesuaikan kembali konfigurasi web dengan URL tunnel yang berlaku. Tidak perlu memulihkan database untuk rollback rilis ini karena deploy tidak menjalankan migrasi. Pemulihan database merupakan tindakan terpisah yang dapat menghilangkan transaksi setelah cadangan dibuat.

## Bukti verifikasi

- Publish Release proyek API dan Web berhasil.
- Paket unggahan SHA256: `7F6ED9F2F05B62D9555B52DF115EEE15828EEF4D2A79CA9EA94F49B9AF05F8A5`.
- Paket tidak membawa `appsettings.Development.json` atau contoh konfigurasi development.
- HTTPS publik menjawab 200 untuk beranda, `/masuk`, `/staf`, `/checkout`, `/akun/alamat`, CSS, JavaScript Blazor, katalog, kategori, tiga detail barang, dan tiga foto.
- Konfigurasi web menunjuk URL HTTPS aktif dengan prefiks `/api/`.
- Endpoint akun, owner dan admin menjawab 401 tanpa token.
- Webhook dengan payload kosong ditolak 400; ini menguji routing dan penolakan payload, bukan pembayaran sungguhan.
- Jumlah akun, barang, sewa dan pembayaran tetap setelah pemeriksaan.
- Verifikasi visual browser belum dilakukan karena konektor browser tidak tersedia. Transaksi Midtrans ujung ke ujung tidak dijalankan pada deploy ini.

Suite integrasi tidak diulang karena tidak ada perubahan kode aplikasi maupun skema. Validasi difokuskan pada hasil publish dan jalur HTTP instalasi VPS.

-- =====================================================================
--  Sewa Everything — 0011_login_lockout.sql
--  Kunci akun sementara setelah beberapa kali salah kata sandi.
--
--  Diminta pemilik produk 20 Agu 2026: "jika password sudah salah lebih
--  dari 3x maka akan terblokir sementara".
--
--  KENAPA DI DATABASE, BUKAN DI MEMORI
--
--  Rate limiter yang dipasang 20 Agu hidup di memori proses. Ia menahan
--  gedoran dari satu alamat IP, tapi tiga hal lolos darinya: ia lupa
--  segalanya saat proses restart, ia tidak berbagi antar instance, dan
--  ia tidak tahu apa-apa soal AKUN — penebak yang berpindah-pindah IP
--  (botnet, VPN berputar, jaringan seluler) mendapat jatah penuh di tiap
--  alamat baru. Penghitung yang menempel ke barisnya sendiri di tabel
--  users tidak punya satu pun dari ketiga celah itu.
--
--  KENAPA JENDELA WAKTUNYA SAMA PANJANG DENGAN LAMA KUNCIAN
--
--  failed_login_count TIDAK direset saat kunciannya habis; yang mereset
--  adalah lewatnya jendela sejak kesalahan terakhir. Dengan jendela dan
--  lama kunci sama-sama 15 menit, akun yang baru terbuka otomatis mulai
--  dari nol lagi — tanpa itu, satu salah ketik sesudah terbuka langsung
--  mengunci ulang, dan pemilik akun yang sah justru yang paling sering
--  kena.
--
--  HARGA YANG DIBAYAR, DAN INI SUDAH DISAMPAIKAN
--
--  Siapa pun yang tahu sebuah email dapat mengunci akunnya dengan sengaja
--  salah tiga kali. Itu melekat pada kunci-per-akun mana pun, dan
--  ditukar sadar-sadar dengan tertutupnya penebakan lintas-IP. Yang
--  menahan penyalahgunaannya: kuncinya sementara (bukan permanen, bukan
--  butuh admin), dan rate limiter per-IP tetap membatasi berapa cepat
--  seseorang dapat melakukannya berulang kali.
--
--  Kolom ini TIDAK berlaku untuk email yang tidak terdaftar — tidak ada
--  baris yang bisa dihitung. Penebakan terhadap email acak tetap ditahan
--  rate limiter per-IP saja.
-- =====================================================================

BEGIN;

ALTER TABLE users
    ADD COLUMN failed_login_count   integer     NOT NULL DEFAULT 0,
    ADD COLUMN last_failed_login_at timestamptz,
    ADD COLUMN locked_until         timestamptz;

COMMENT ON COLUMN users.failed_login_count IS
    'Kesalahan kata sandi beruntun di dalam jendela waktu. Direset oleh login berhasil ATAU oleh lewatnya jendela sejak last_failed_login_at.';

COMMENT ON COLUMN users.last_failed_login_at IS
    'Kapan kesalahan terakhir terjadi. Dipakai memutuskan apakah failed_login_count masih relevan.';

COMMENT ON COLUMN users.locked_until IS
    'Terisi dan masih di masa depan = login ditolak tanpa memeriksa kata sandi. Selalu sementara; tidak ada penguncian permanen.';

-- Penghitung tidak boleh negatif. Kalau ada jalur yang mengurangi tanpa
-- alasan, biar database yang menolaknya, bukan ketahuan belakangan.
ALTER TABLE users
    ADD CONSTRAINT ck_users_failed_login_count_positive
        CHECK (failed_login_count >= 0);

-- Kunci tanpa jejak kesalahan tidak masuk akal: satu-satunya yang
-- mengisi locked_until adalah jalur yang juga mengisi last_failed_login_at.
ALTER TABLE users
    ADD CONSTRAINT ck_users_lock_has_failure
        CHECK (locked_until IS NULL OR last_failed_login_at IS NOT NULL);

-- Owner tetap tidak bisa terkunci selamanya oleh siapa pun, tapi ia TETAP
-- ikut aturan ini — justru akun owner yang paling layak ditebak. Yang
-- dijamin 0006 adalah owner tidak bisa DINONAKTIFKAN; kunci sementara
-- adalah hal yang berbeda dan memang boleh mengenainya.

COMMIT;

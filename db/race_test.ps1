# =====================================================================
#  race_test.ps1 — Bagian 9 Test #1, versi level database.
#
#  Dua sesi PostgreSQL sungguhan, dijalankan bersamaan, mencoba mem-booking
#  BARANG dan RENTANG WAKTU yang persis sama. Tepat satu harus lolos.
#
#  Pola "cek dulu lalu insert" di kode C# akan meloloskan keduanya di sini.
#  Yang menyelamatkan adalah exclusion constraint: sesi kedua diblokir di
#  tingkat index sampai sesi pertama commit, lalu ditolak 23P01.
#
#  Catatan implementasi: dua sesi dijalankan lewat Start-Process, bukan
#  Start-Job. Di PowerShell 5.1, stderr dari executable native yang lewat
#  Start-Job dibungkus jadi NativeCommandError dan menggagalkan skrip
#  padahal psql-nya berperilaku benar.
#
#  Jalankan:  powershell -ExecutionPolicy Bypass -File db/race_test.ps1
# =====================================================================

$ErrorActionPreference = 'Stop'
$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = 'sewa_dev'
$conn = @('-U','sewa','-h','127.0.0.1','-d','sewa_everything')

$SELLER  = 'f0000000-0000-0000-0000-000000000001'
$RENTERA = 'f0000000-0000-0000-0000-00000000000a'
$RENTERB = 'f0000000-0000-0000-0000-00000000000b'
$ITEM    = 'f0000000-0000-0000-0000-0000000000ee'

$tmp = Join-Path $env:TEMP ("race_" + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    # -----------------------------------------------------------------
    # Bersihkan sisa run sebelumnya, lalu siapkan fixture
    # -----------------------------------------------------------------
    $setup = @"
DELETE FROM bookings WHERE item_id = '$ITEM';
DELETE FROM items    WHERE id = '$ITEM';
DELETE FROM users    WHERE id IN ('$SELLER','$RENTERA','$RENTERB');

INSERT INTO users (id, role, name, email, password_hash) VALUES
  ('$SELLER', 'seller','Seller Race','race.seller@test.id','x'),
  ('$RENTERA','renter','Renter A',   'race.a@test.id',     'x'),
  ('$RENTERB','renter','Renter B',   'race.b@test.id',     'x');

INSERT INTO items (id, seller_id, title, category, price, price_unit, deposit_amount)
VALUES ('$ITEM','$SELLER','Proyektor Epson','Elektronik',150000.00,'day',750000.00);
"@
    Write-Host "== Menyiapkan fixture =="
    $setupFile = Join-Path $tmp 'setup.sql'
    Set-Content -Path $setupFile -Value $setup -Encoding UTF8
    & $psql @conn -v ON_ERROR_STOP=1 -q -f $setupFile
    if ($LASTEXITCODE -ne 0) { throw "setup gagal" }

    # -----------------------------------------------------------------
    # Naskah tiap sesi — identik kecuali id booking & renter.
    # pg_sleep SETELAH insert memastikan transaksi pemenang masih terbuka
    # saat lawannya mencoba insert, jadi yang teruji benar-benar jalur
    # konkurensi, bukan sekadar bentrok dengan baris yang sudah ter-commit.
    # -----------------------------------------------------------------
    function Write-SesiSql($label, $bookingId, $renterId, $path) {
        $sql = @"
BEGIN;
SELECT '$label  mulai        ' || to_char(clock_timestamp(),'HH24:MI:SS.MS');
INSERT INTO bookings (
    id, item_id, renter_id, during, status,
    price_snapshot, price_unit_snapshot, duration_units, total_rent,
    deposit_amount, platform_fee_rate, platform_fee_mode, platform_fee_amount,
    hold_expires_at)
VALUES ('$bookingId','$ITEM','$renterId',
    tstzrange('2026-11-10 09:00+07','2026-11-13 09:00+07','[)'),'pending',
    150000.00,'day',3,450000.00,750000.00,0.0500,'deduct',22500.00,
    now() + interval '15 minutes');
SELECT '$label  INSERT lolos ' || to_char(clock_timestamp(),'HH24:MI:SS.MS');
SELECT pg_sleep(2);
COMMIT;
SELECT '$label  COMMIT       ' || to_char(clock_timestamp(),'HH24:MI:SS.MS');
"@
        Set-Content -Path $path -Value $sql -Encoding UTF8
    }

    $fileA = Join-Path $tmp 'a.sql'; $fileB = Join-Path $tmp 'b.sql'
    Write-SesiSql 'A' 'f000000a-0000-0000-0000-000000000000' $RENTERA $fileA
    Write-SesiSql 'B' 'f000000b-0000-0000-0000-000000000000' $RENTERB $fileB

    function Start-Sesi($file, $tag) {
        # Jangan pakai nama $args — itu variabel otomatis PowerShell.
        $argv = @('-U','sewa','-h','127.0.0.1','-d','sewa_everything',
                  '-v','ON_ERROR_STOP=1','-q','-t','-A','-f',$file)
        $p = Start-Process -FilePath $psql -ArgumentList $argv -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $tmp "$tag.out") `
            -RedirectStandardError  (Join-Path $tmp "$tag.err")
        # Menyentuh .Handle meng-cache handle proses. Tanpa ini .ExitCode
        # bernilai null setelah proses selesai — perilaku Start-Process -PassThru.
        $null = $p.Handle
        return $p
    }

    Write-Host "`n== Menjalankan dua sesi BERSAMAAN =="
    $t0 = Get-Date
    $pA = Start-Sesi $fileA 'a'
    $pB = Start-Sesi $fileB 'b'
    $pA.WaitForExit(); $pB.WaitForExit()
    $durasi = ((Get-Date) - $t0).TotalSeconds

    function Show-Sesi($tag, $proc, $nama) {
        Write-Host "`n--- Sesi $nama (exit $($proc.ExitCode)) ---"
        $o = Get-Content (Join-Path $tmp "$tag.out") -Raw -ErrorAction SilentlyContinue
        $e = Get-Content (Join-Path $tmp "$tag.err") -Raw -ErrorAction SilentlyContinue
        if ($o) { Write-Host $o.TrimEnd() }
        if ($e) { Write-Host $e.TrimEnd() }
    }
    Show-Sesi 'a' $pA 'A'
    Show-Sesi 'b' $pB 'B'
    Write-Host ("`nDurasi total: {0:N2} detik" -f $durasi)

    # -----------------------------------------------------------------
    # Putusan
    # -----------------------------------------------------------------
    $jml = (& $psql @conn -tAc "SELECT count(*) FROM bookings WHERE item_id='$ITEM'").Trim()
    $pemenang = (& $psql @conn -tAc "SELECT u.name || ' (booking ' || left(b.id::text,8) || ', status ' || b.status || ')' FROM bookings b JOIN users u ON u.id = b.renter_id WHERE b.item_id='$ITEM'").Trim()

    $kode    = @($pA.ExitCode, $pB.ExitCode)
    $sukses  = @($kode | Where-Object { $_ -eq 0 }).Count
    $ditolak = @($kode | Where-Object { $_ -ne 0 }).Count

    Write-Host "`n================= PUTUSAN ================="
    Write-Host "Sesi berhasil        : $sukses   (diharapkan 1)"
    Write-Host "Sesi ditolak         : $ditolak   (diharapkan 1)"
    Write-Host "Baris booking di DB  : $jml   (diharapkan 1)"
    Write-Host "Pemenang             : $pemenang"

    $lulus = ($sukses -eq 1) -and ($ditolak -eq 1) -and ($jml -eq '1')
    if ($lulus) {
        Write-Host "`nHASIL: LULUS - double-booking dicegah database, bukan oleh kode aplikasi." -ForegroundColor Green
    } else {
        Write-Host "`nHASIL: GAGAL - double-booking lolos." -ForegroundColor Red
    }

    # Bersihkan agar bisa dijalankan berulang
    & $psql @conn -q -c "DELETE FROM bookings WHERE item_id='$ITEM'" -c "DELETE FROM items WHERE id='$ITEM'" -c "DELETE FROM users WHERE id IN ('$SELLER','$RENTERA','$RENTERB')" | Out-Null

    if (-not $lulus) { exit 1 }
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

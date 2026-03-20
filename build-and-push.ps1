# --- Magic Start ---
Clear-Host
Write-Host "--- Menyiapkan peluncuran project C# kamu ke awan! ---`n" -ForegroundColor Cyan -BackgroundColor DarkBlue

# 1. Cari file .csproj di folder saat ini
$projectFiles = Get-ChildItem -Filter *.csproj

if ($projectFiles.Count -eq 0) {
    Write-Host "!!! Aduh! Gak ketemu file .csproj di sini. Coba cek lagi ya! !!!" -ForegroundColor Red
    exit 1
}

# Jika ada lebih dari satu project, minta user pilih
if ($projectFiles.Count -gt 1) {
    Write-Host "Wah, banyak project keren di sini! Pilih yang mana nih?:" -ForegroundColor Yellow
    for ($i = 0; $i -lt $projectFiles.Count; $i++) {
        Write-Host "  [$i] : $($projectFiles[$i].Name)" -ForegroundColor Gray
    }
    $choice = Read-Host "Ketik nomor pilihannya (default 0)"
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = 0 }
    $selectedProject = $projectFiles[$choice].FullName
} else {
    $selectedProject = $projectFiles[0].FullName
}

$projectName = [System.IO.Path]::GetFileName($selectedProject)
Write-Host "`nProject Terpilih: " -NoNewline; Write-Host "$projectName" -ForegroundColor Green -BackgroundColor Black

# 2. Minta input versi dari user
Write-Host "`nVersi berapa yang mau kita rilis hari ini?" -ForegroundColor Yellow
$newVersion = Read-Host "Masukkan versi (misal: 1.0.0)"
if ([string]::IsNullOrWhiteSpace($newVersion)) {
    Write-Host "Versi gak boleh kosong ya!" -ForegroundColor Red
    exit 1
}

# 3. Update Versi di file XML .csproj 🛠️
Write-Host "Sedang mengukir versi $newVersion ke dalam $projectName..." -ForegroundColor DarkGray
[xml]$xml = Get-Content $selectedProject
$propertyGroup = $xml.Project.PropertyGroup | Select-Object -First 1

if ($null -eq $propertyGroup.Version) {
    $versionNode = $xml.CreateElement("Version")
    $versionNode.InnerText = [string]$newVersion
    $propertyGroup.AppendChild($versionNode)
} else {
    # Perbaikan di sini: Tambahkan .#text atau .InnerText
    $propertyGroup.Version = [string]$newVersion
}
$xml.Save($selectedProject)
Write-Host "Sip! Versi berhasil diupdate." -ForegroundColor Green

# 4. Proses Build & Pack
Write-Host "`nSedang membangun (Build) project... Tunggu bentar ya!" -ForegroundColor Cyan
dotnet build $selectedProject --configuration Release /nologo
if ($LASTEXITCODE -ne 0) { 
    Write-Host "Build-nya error! Cek kodenya lagi ya." -ForegroundColor Red
    exit 1 
}

Write-Host "Membungkus paket NuGet (Pack)..." -ForegroundColor Magenta
$outputDir = "./nupkg"
if (!(Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir | Out-Null }

dotnet pack $selectedProject --configuration Release --output $outputDir --no-build /p:PackageVersion=$newVersion /nologo

# 5. Konfirmasi Push ke NuGet
Write-Host "`nSiap untuk terbang ke NuGet?" -ForegroundColor Yellow
$confirmation = Read-Host "Kirim sekarang? (y/n)"
if ($confirmation -eq 'y') {
    # Perbaikan: Coba baca API Key dari file config jika env var tidak ada
    $apiKey = $env:NUGET_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        Write-Host "Variabel `$env:NUGET_API_KEY tidak ditemukan. Mari coba cara lain..." -ForegroundColor Yellow
        
        # Coba baca dari file nuget.config
        $nugetConfigPath = Join-Path $env:USERPROFILE ".nuget\NuGet\NuGet.Config"
        if (Test-Path $nugetConfigPath) {
            $apiKey = (Get-Content $nugetConfigPath | Select-String "apikey") -replace ".*<add key=""apikey"" value=""(.*)"" />.*", '$1'
        }
        
        if ([string]::IsNullOrWhiteSpace($apiKey)) {
            # Jika masih kosong, minta input manual
            $apiKey = Read-Host "Masukkan API Key NuGet kamu"
            if ([string]::IsNullOrWhiteSpace($apiKey)) {
                Write-Host "API Key tidak boleh kosong!" -ForegroundColor Red
                exit 1
            }
        }
    }

    $nupkgFile = Get-ChildItem "$outputDir/*$newVersion.nupkg" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    
    if ($null -eq $nupkgFile) {
        Write-Host "File paketnya nggak ketemu!" -ForegroundColor Red
        exit 1
    }

    Write-Host "Mengirim paket ke NuGet..." -ForegroundColor DarkCyan
    dotnet nuget push $nupkgFile.FullName --api-key $apiKey --source "https://api.nuget.org/v3/index.json" --skip-duplicate
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nHOREEE! Paket kamu sudah mendarat di NuGet!" -ForegroundColor Green -BackgroundColor Black
    } else {
        Write-Host "`nProses pengiriman gagal." -ForegroundColor Red
    }
} else {
    Write-Host "Paket disimpan di folder ./nupkg." -ForegroundColor Yellow
}

Write-Host "`nHappy Coding and Have a Great Day!`n" -ForegroundColor White -BackgroundColor DarkMagenta
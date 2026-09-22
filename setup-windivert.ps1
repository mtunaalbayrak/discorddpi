$ErrorActionPreference = 'Stop'
$downloadUrl = 'https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip'
$expectedHash = '63CB41763BB4B20F600B6DE04E991A9C2BE73279E317D4D82F237B150C5F3F15'
$cachePath = Join-Path $PSScriptRoot 'work\windivert'
$binaryPath = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $cachePath,$binaryPath -Force | Out-Null
$archivePath = Join-Path $cachePath 'WinDivert.zip'
if (!(Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'WinDivert arşiv özeti beklenen değerle uyuşmuyor. Dosyalar kurulmadı.'
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $cachePath -Force
$packagePath = Join-Path $cachePath 'WinDivert-2.2.2-A'
Copy-Item -LiteralPath (Join-Path $packagePath 'x64\WinDivert.dll') -Destination $binaryPath
Copy-Item -LiteralPath (Join-Path $packagePath 'x64\WinDivert64.sys') -Destination $binaryPath
Copy-Item -LiteralPath (Join-Path $packagePath 'LICENSE') -Destination (Join-Path $binaryPath 'LICENSE-WinDivert.txt')
Write-Output 'WinDivert 2.2.2 hazır. Sürücü yüklenmedi veya başlatılmadı.'

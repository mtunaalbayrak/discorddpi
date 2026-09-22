$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path "$PSScriptRoot\bin" -Force | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath "$PSScriptRoot\src" -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compilerPath /nologo /target:exe /main:Program /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$PSScriptRoot\bin\DiscordDpi.exe" "/codepage:65001" $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
& $compilerPath /nologo /target:winexe /main:DesktopProgram /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32manifest:$PSScriptRoot\desktop.manifest" "/out:$PSScriptRoot\bin\DiscordDpiDesktop.exe" "/codepage:65001" $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Arayüz derlenemedi.' }

# Сборка артефактов релиза. Запуск из корня репозитория:
#   powershell -ExecutionPolicy Bypass -File tools/build-release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ver = '1.4.8'

# NuGet-кэш для MAUI-сборок
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget-packages' }

Write-Host "1/3 Публикация приложения для Windows (MAUI)..."
Remove-Item -Recurse -Force (Join-Path $root 'build/TimeTool-app') -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/TimeTool/TimeTool.csproj') -f net10.0-windows10.0.19041.0 -c Release -o (Join-Path $root 'build/TimeTool-app') | Out-Null
if (-not (Test-Path (Join-Path $root 'build/TimeTool-app/TimeTool.exe'))) { throw "Публикация не создала TimeTool.exe" }

Write-Host "2/3 Сборка одного exe (SFX-лаунчер)..."
$sfx = Join-Path $root 'build/sfx'
Remove-Item -Recurse -Force $sfx -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $sfx | Out-Null
Copy-Item (Join-Path $root 'tools/sfxstub/*') $sfx -Recurse -Force
Push-Location (Join-Path $root 'build/TimeTool-app')
& 'D:\android-tools\7za.exe' a -tzip -mx=5 (Join-Path $sfx 'app.zip') '*' -r '-x!*.pdb' | Out-Null
$zipCode = $LASTEXITCODE
Pop-Location
if ($zipCode -gt 1) { throw "7za завершился с ошибкой $zipCode" }
dotnet build (Join-Path $sfx 'sfxstub.csproj') -c Release | Out-Null
Copy-Item (Join-Path $sfx 'bin/Release/net472/TimeTool.exe') (Join-Path $root "release/TimeTool-app-windows-x64-v$ver.exe") -Force

Write-Host "3/3 Архив APK в release/..."
$apk = Join-Path $root 'src/TimeTool/bin/Debug/net10.0-android/com.timetool.app-Signed.apk'
if (-not (Test-Path $apk)) {
    Write-Host "APK не найден (пропуск — сборка только для Windows)"
} else {
    Copy-Item $apk (Join-Path $root "release/TimeTool-android-v$ver.apk") -Force
}

Write-Host "Готово:"
Write-Host "  release/TimeTool-app-windows-x64-v$ver.exe"

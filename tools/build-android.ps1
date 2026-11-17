# Сборка APK для Android. Запуск из корня репозитория:
#   powershell -ExecutionPolicy Bypass -File tools/build-android.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ver = '1.4.0'

# Тулчейн (пути как в других проектах пользователя)
$env:JAVA_HOME = 'D:\android-tools\jdk-17'
$env:ANDROID_SDK_ROOT = 'D:\Android\Sdk'
$env:NUGET_PACKAGES = 'D:\nuget-packages'

Write-Host "Сборка APK (net10.0-android, Debug, автономный)..."
dotnet build (Join-Path $root 'src/TimeTool/TimeTool.App.csproj') -f net10.0-android -c Debug | Out-Null

$apk = Join-Path $root 'src/TimeTool/bin/Debug/net10.0-android/com.timetool.app-Signed.apk'
if (-not (Test-Path $apk)) { throw "APK не найден: $apk" }

Copy-Item $apk (Join-Path $root "release/TimeTool-android-v$ver.apk") -Force
Write-Host "Готово: release/TimeTool-android-v$ver.apk"

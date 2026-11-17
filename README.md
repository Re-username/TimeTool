# TimeTool

Таймер обратного отсчёта, секундомер и калькулятор времени. Одно приложение для Windows (нативное окно) и Android (APK) — MAUI Blazor Hybrid, один код на C# + Blazor.

## Возможности

- **Таймер**: часы/минуты/секунды, быстрые пресеты (1/5/15/30 мин, 1 час), пауза/продолжение/сброс, звуковой сигнал (3 сигнала) и мигающий дисплей по окончании. Пробел — старт/пауза.
- **Секундомер**: сотые доли секунды, круги (время круга + суммарное), пауза/сброс. Пробел — старт/пауза.
- **Расчёт времени**: разница между двумя моментами (дата+время → «N дней X часов Y минут» и общие суммы); сдвиг даты на интервал (минуты/часы/дни/недели, плюс/минус).
- Тёмная/светлая тема, последние настройки запоминаются между запусками. Справка — F1.

## Структура

- `src/TimeTool/` — приложение (MAUI Blazor Hybrid: net10.0-windows + net10.0-android)
- `tools/make-icon.ps1` — генератор иконки (часы)
- `tools/build-release.ps1` — приложение Windows одним exe (SFX-лаунчер) + копия APK в `release/`
- `tools/build-android.ps1` — сборка APK
- `tools/sfxstub/` — лаунчер net472 (один exe: распаковывает приложение в `%LOCALAPPDATA%\TimeTool\App` один раз на версию)
- `release/` — готовые сборки

## Сборка

```bash
dotnet build src/TimeTool/TimeTool.csproj -c Release                 # Windows (net10.0-windows)
powershell -ExecutionPolicy Bypass -File tools/build-release.ps1     # exe одним файлом → release/
powershell -ExecutionPolicy Bypass -File tools/build-android.ps1     # APK → release/
```

Сборка APK требует JDK 17 (`D:\android-tools\jdk-17`), Android SDK (`D:\Android\Sdk`) и workload `maui-android`.

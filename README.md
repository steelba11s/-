# Тренажер набора текста на клавиатуре
Файзуллин Артур Раилевич
Группа: 231-322

## Запуск из терминала

Откройте PowerShell в этой папке. Нужен .NET SDK 9 или 10 с установленной средой выполнения .NET 9.

Тренажёр:

```powershell
dotnet run --project .\TypingTrainer\TypingTrainer.csproj
```

## Запуск через файлы

- Тренажёр: дважды нажмите `TypingTrainer\Start.cmd`.

Этот файл собирает и запускает актуальные исходники через .NET SDK. При первом запуске нужен доступ к NuGet для загрузки зависимостей.

После сборки можно открыть программу напрямую (нужна соответствующая среда выполнения .NET):

- `TypingTrainer\bin\Debug\net9.0\TypingTrainer.exe`.

Для работы в IDE откройте `TypingTrainerApp.slnx`.

В решении один проект приложения. Логика включена в него; отдельных проектов Core и Tests нет.

## Простая карта

В папке `SimpleMap` находится отдельное приложение: карта OpenStreetMap через интернет, перемещение, масштабирование и поиск адреса.

```powershell
dotnet run --project .\SimpleMap\SimpleMap.csproj
```

Можно также запустить `SimpleMap\Start.cmd` или открыть `SimpleMap\SimpleMap.slnx`. Подробности — в [SimpleMap/README.md](SimpleMap/README.md).

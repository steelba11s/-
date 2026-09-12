# Тренажер набора текста на клавиатуре
Файзуллин Артур Раилевич
Группа: 231-322

## Запуск из терминала

Откройте PowerShell в этой папке. Нужен .NET SDK: для тренажёра — 9 или 10 с установленной средой выполнения .NET 9, для Markdown — 10.

Тренажёр:

```powershell
dotnet run --project .\TypingTrainer\TypingTrainer.csproj
```

Markdown-редактор:

```powershell
dotnet run --project .\MarkdownEditor\MarkdownEditor.App\MarkdownEditor.App.csproj
```

## Запуск через файлы

- Тренажёр: дважды нажмите `TypingTrainer\Start.cmd`.
- Markdown-редактор: дважды нажмите `MarkdownEditor\Start.cmd`.

Эти файлы собирают и запускают актуальные исходники через .NET SDK. При первом запуске нужен доступ к NuGet для загрузки зависимостей.

После сборки можно открыть программы напрямую (нужна соответствующая среда выполнения .NET):

- `TypingTrainer\bin\Debug\net9.0\TypingTrainer.exe`.
- `MarkdownEditor\MarkdownEditor.App\bin\Debug\net10.0\MarkdownEditor.exe`.

Для работы в IDE откройте `TypingTrainerApp.slnx` или `MarkdownEditor\MarkdownEditor.slnx`.

В каждом решении один проект приложения. Логика включена в него; отдельных проектов Core и Tests нет.

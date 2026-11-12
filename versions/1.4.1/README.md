# RiverGuard 1.4.1

**Автор приложения: Apdulium-beep.**

## Запуск

[Скачать готовую программу](https://github.com/Apdulium-beep/RiverGuard/releases/tag/v1.4.1), распаковать ZIP целиком и запустить `RiverGuard.exe`. Установка .NET или Office не требуется.

На компьютере разработчика готовая программа находится в `app/RiverGuard.exe`. Папка `app` не хранится в Git: готовая сборка доступна отдельным вложением релиза, без повторного хранения большого EXE в истории исходников.

## Содержимое этой версии

```text
1.4.1/
├── src/                     исходники приложения, ядра и тестов
│   └── RiverGuard.sln        решение Visual Studio
├── docs/                    модель, инструкция и проверки
├── app/                     локальная готовая сборка; исключена из Git
├── AUTHOR.md
├── THIRD_PARTY_NOTICES.txt
└── version.json
```

## Возможности

- Стартовый экран с созданием, импортом и библиотекой проектов.
- Четыре механизма: заданный проран, перелив, открытие прямоугольного затвора и круглое отверстие.
- Адаптация расчётных полей, графиков и отчётов к выбранному механизму.
- Створы, береговая геометрия, расчётный баланс, графики и воспроизведение времени.
- Переименование проекта; удаление в корзину после подтверждения.
- XLSX, ODS, DOCX, HTML, CSV, JSON, RTF, TXT, Markdown, XML, PNG и ZIP. PDF — через печать HTML в браузере.

## Сборка

Windows, .NET SDK 8 или новее. Команды выполняются из этой папки:

```powershell
dotnet build src/RiverGuard.sln -c Release
dotnet run --project src/RiverGuard.Tests -c Release -- --catalog-tests --catalog-report docs/core-checks.json
dotnet publish src/RiverGuard.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o app
```

Проверка интерфейса: `app/RiverGuard.exe --smoke-test`. Проверки используют изолированное хранилище, не пользовательские проекты.

[Инструкция](docs/user-guide.md) · [Расчётная модель](docs/model.md) · [Каталог примеров](docs/catalog.md) · [Проверки](docs/verification.md)

Модель исследовательская, не предназначена для самостоятельных реальных решений об эвакуации. Авторство приложения не распространяется на сторонние библиотеки и общеизвестные математические методы.

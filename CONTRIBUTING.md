# Contributing to BaldGrabber

Thank you for your interest in contributing! This guide will help you get started.

## How to Contribute

### Reporting Bugs

- Check existing issues to avoid duplicates
- Include Windows version, .NET version, and steps to reproduce
- Attach logs from `%APPDATA%\AudioGrabber\Logs\` or `Data\Logs\` (portable)

### Suggesting Features

- Open an issue with the "enhancement" label
- Describe the use case and expected behavior

### Submitting Code

1. Fork the repository
2. Create a branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Test on a clean Windows 10+ system (x64)
5. Commit with a clear message
6. Open a Pull Request

## Development Setup

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for build requirements and instructions.

## Code Style

- C# with standard .NET conventions
- Use CommunityToolkit.Mvvm for MVVM patterns
- Keep `MainViewModel.cs` clean — move heavy logic to services
- No external dependencies without discussion (open an issue first)

## Project Structure

```
BaldGrabber/
├── ViewModels/    — business logic
├── Models/        — data models
├── Services/      — yt-dlp/FFmpeg interaction, settings
├── Controls/      — custom UI controls
├── Converters/    — XAML binding converters
└── Assets/        — icons, fonts
```

## Localization

- All user-facing strings go through the `Localization` class in `MainViewModel.cs`
- To add a language: add a `CreateXx()` method and a case in `Localization.Create()`
- Supported: Russian (`ru`), English (`en`), Ukrainian (`uk`)

## Pull Request Guidelines

- Keep PRs focused on one change
- Describe what changed and why
- Make sure the app builds and runs without errors
- Update documentation if needed

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).

---

# Участие в разработке BaldGrabber

Спасибо за интерес к проекту! Этот гид поможет вам начать.

## Как внести вклад

### Сообщения об ошибках

- Проверьте существующие issues, чтобы избежать дублей
- Укажите версию Windows, версию .NET и шаги для воспроизведения
- Приложите логи из `%APPDATA%\AudioGrabber\Logs\` или `Data\Logs\` (портативная версия)

### Предложения по функционалу

- Откройте issue с меткой "enhancement"
- Опишите используйте кейс и ожидаемое поведение

### Отправка кода

1. форкните репозиторий
2. Создайте ветку: `git checkout -b feature/my-feature`
3. Внесите изменения
4. Протестируйте на чистой системе Windows 10+ (x64)
5. Коммитьте с понятным сообщением
6. Откройте Pull Request

## Настройка окружения разработки

Смотрите [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) для требований к сборке и инструкций.

## Стиль кода

- C# со стандартными соглашениями .NET
- Используйте CommunityToolkit.Mvvm для MVVM-паттернов
- Держите `MainViewModel.cs` чистым — тяжёлую логику выносите в сервисы
- Без внешних зависимостей без обсуждения (сначала откройте issue)

## Структура проекта

```
BaldGrabber/
├── ViewModels/    — бизнес-логика
├── Models/        — модели данных
├── Services/      — взаимодействие с yt-dlp/FFmpeg, настройки
├── Controls/      — кастомные UI-компоненты
├── Converters/    — конвертеры XAML-привязок
└── Assets/        — иконки, шрифты
```

## Локализация

- Все строки интерфейса проходят через класс `Localization` в `MainViewModel.cs`
- Чтобы добавить язык: создайте метод `CreateXx()` и добавьте case в `Localization.Create()`
- Поддерживаемые: русский (`ru`), английский (`en`), украинский (`uk`)

## Требования к Pull Request

- Один PR — одно изменение
- Опишите, что изменилось и зачем
- Убедитесь, что приложение собирается и запускается без ошибок
- Обновите документацию при необходимости

## Лицензия

Участвуя в разработке, вы соглашаетесь, что ваши вклады будут распространяться под лицензией [MIT](LICENSE).

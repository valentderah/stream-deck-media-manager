# Stream Deck Media Manager

Плагин для Stream Deck, который отображает информацию о текущем воспроизводимом медиа в Windows.

## Функции

- Отображение названия трека
- Отображение списка авторов/исполнителей
- Отображение обложки альбома
- Автоматическое обновление каждые 3 секунды
- Обновление при нажатии на кнопку

## Требования

- Node.js 20 или выше
- .NET 8.0 SDK или выше (для сборки C# утилиты)
- Windows 10 или выше
- Stream Deck приложение версии 6.9 или выше

## Установка и сборка

### 1. Установите зависимости Node.js

```bash
npm install
```

### 2. Соберите проект

Запустите одну команду для сборки всего проекта (C# утилита и TypeScript код):

```bash
npm run build
```

Это автоматически:
- Соберет C# утилиту `MediaManager.exe`
- Скопирует её в папку плагина
- Соберет TypeScript код в `ru.valentderah.media-manager.sdPlugin/bin/plugin.js`

**Важно:** Убедитесь, что установлен .NET 8.0 SDK, так как он требуется для сборки C# утилиты.

### Отдельная сборка компонентов (опционально)

Если нужно собрать только часть проекта:

```bash
# Только C# утилита
npm run build:helper

# Только TypeScript код
npm run build:ts
```

## Установка плагина

### Автоматическая установка

```bash
npx streamdeck install ru.valentderah.media-manager.sdPlugin
```

### Ручная установка

1. Скопируйте папку `ru.valentderah.media-manager.sdPlugin` в:
   - Windows: `%appdata%\Elgato\StreamDeck\Plugins\`
2. Перезапустите Stream Deck приложение

## Разработка

Для разработки с автоматической перезагрузкой:

```bash
npm run watch
```

Это будет:
- Автоматически собирать проект при изменениях
- Перезапускать плагин в Stream Deck

## Использование

1. Откройте Stream Deck приложение
2. Найдите плагин "Media Manager" в списке действий
3. Перетащите действие "Media Info" на Stream Deck
4. Информация о медиа будет автоматически обновляться каждые 3 секунды
5. Нажмите на кнопку для немедленного обновления

## Логи

Логи плагина находятся в:
- Windows: `%appdata%\Elgato\StreamDeck\Plugins\ru.valentderah.media-manager.sdPlugin\logs\`

## Структура проекта

```
media-manager/
├── MediaManager/                # Утилита для получения информации о медиа
│   └── platforms/               # Платформенные реализации
│       ├── windows/             # Реализация для Windows
│       │   ├── Program.cs
│       │   ├── MediaManager.Windows.csproj
│       │   └── build.bat
│       └── macos/               # Реализация для macOS (в разработке)
│           └── README.md
├── src/
│   ├── actions/
│   │   ├── increment-counter.ts # Пример действия (счетчик)
│   │   └── media-info.ts        # Действие для отображения информации о медиа
│   ├── utils/
│   │   ├── marquee.ts           # Логика бегущей строки
│   │   └── media-manager.ts     # Платформенно-зависимый модуль работы с медиа
│   └── plugin.ts                # Главный файл плагина
└── ru.valentderah.media-manager.sdPlugin/
    ├── bin/
    │   ├── plugin.js            # Скомпилированный JavaScript
    │   └── MediaManager.exe     # Утилита (Windows) или MediaManager (macOS)
    └── manifest.json            # Манифест плагина
```

## Устранение неполадок

### Плагин не загружается

- Убедитесь, что `MediaManager.exe` находится в папке `bin/`
- Проверьте логи в папке `logs/`
- Убедитесь, что Node.js версии 20 установлен

### Ошибка "ENOENT" или "File Not Found"

Эта ошибка означает, что файл `MediaManager.exe` не найден. Решение:

1. Убедитесь, что вы собрали проект:
   ```bash
   npm run build
   ```

2. Если сборка не помогла, попробуйте собрать только C# утилиту:
   ```bash
   npm run build:helper
   ```

3. Проверьте, что файл существует:
   ```cmd
   dir "ru.valentderah.media-manager.sdPlugin\bin\MediaManager.exe"
   ```

4. Если файл все еще отсутствует, выполните сборку вручную:
   ```cmd
   cd MediaManager\platforms\windows
   build.bat
   ```

### Информация о медиа не отображается

- Убедитесь, что медиа-плеер запущен и воспроизводит музыку
- Проверьте, что `MediaManager.exe` работает (запустите его вручную)
- Проверьте логи плагина

### Ошибки компиляции C#

- Убедитесь, что установлен .NET 8.0 SDK
- Проверьте, что проект использует правильный Target Framework


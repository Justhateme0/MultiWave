# MultiWave

## English
MultiWave is a modern WPF tool for working with multiple Telegram accounts. It combines Supabase-based login/registration with Telegram session management, fast messaging, and complaint/abuse reporting. The UI is monochrome, minimal, and optimized for keyboard/mouse without clutter.

### Features
- Supabase auth (email/login + password), Supabase URL/Key stored in config and encrypted at rest.
- Telegram multi-account login with API ID/Hash, sessions saved under `%AppData%\MultiWave\sessions`.
- Bulk messaging: choose dialog or paste `@user`, phone, `t.me/...`, invite link, or raw ID; set repeat count and delay.
- Complaints: select target or dialog, pick reason (spam/violence/porn/other), optional text, repeat sending.
- Auto-join by invite link when needed, auto-resolve recipients by username/phone/id.
- Native Windows notifications for start/finish of sends.
- Config is encrypted via DPAPI (`DataProtectionScope.CurrentUser`), so stored values aren’t readable as plain JSON.

### Setup
1. Install .NET 10.0 SDK (or newer).
2. Put your Supabase values into `config.json` (they will be encrypted after first save):
   ```json
   {
     "SupabaseUrl": "https://<your-project>.supabase.co",
     "SupabaseKey": "<anon-or-service-key>"
   }
   ```
3. Add Telegram API credentials (https://my.telegram.org) in the app; once saved, the API block hides.
4. Build/run:
   ```bash
   dotnet build
   dotnet run
   ```

### Usage tips
- First screen: Supabase login/registration (email/login + password).
- Main screen:
  - Left: API credentials (hidden after save) and Telegram login (phone → code → optional 2FA).
  - Accounts: select all/delete; sessions persist.
  - Dialogs: refresh and pick a chat, or use the recipient field for `@user`/phone/link/id.
  - Tabs:
    - Messages: enter text, repeats, delay.
    - Complaints: choose reason, repeats, optional description.
- Status line shows errors/success; balloon notifications show start/end.

## Русский
MultiWave — WPF‑приложение для работы с несколькими аккаунтами Telegram. Авторизация/регистрация через Supabase, управление сессиями Telegram, массовая отправка сообщений и отправка жалоб. Интерфейс минималистичный, ч/б, без лишнего визуального шума.

### Возможности
- Вход в Supabase (логин/email + пароль), Supabase URL/Key в конфиге, хранение зашифровано.
- Мультиаккаунт Telegram: API ID/Hash, сессии сохраняются в `%AppData%\MultiWave\sessions`.
- Массовые сообщения: выбрать диалог или вставить `@user`, телефон, `t.me/...`, invite или ID; задать повторы и задержку.
- Жалобы: выбрать цель или диалог, причина (спам/насилие/порно/другое), текст по желанию, повторы.
- Авто-вступление по invite, авто-резолв по нику/телефону/ID.
- Системные уведомления Windows о старте/завершении.
- Конфиг шифруется (DPAPI CurrentUser), так что содержимое не читается как обычный JSON.

### Настройка
1. Установите .NET 10.0 SDK или новее.
2. В `config.json` добавьте Supabase URL/Key (после сохранения файл станет шифрованным):
   ```json
   {
     "SupabaseUrl": "https://<your-project>.supabase.co",
     "SupabaseKey": "<anon-or-service-key>"
   }
   ```
3. В приложении сохраните Telegram API ID/Hash (после этого блок API скрывается).
4. Сборка/запуск:
   ```powershell
   dotnet build
   dotnet run
   ```

### Работа
- Экран авторизации: Supabase логин/регистрация (логин/email + пароль).
- Главный экран:
  - Слева: блок API (прячется после сохранения), логин Telegram (телефон → код → 2FA если есть).
  - Аккаунты: выбрать все/удалить, сессии сохраняются.
  - Диалоги: обновить, выбрать чат, либо ввести получателя в поле (@ник/телефон/ссылка/id).
  - Вкладки:
    - Сообщения: текст, повторы, задержка.
    - Жалобы: причина, повторы, описание по желанию.
- Строка статуса показывает ошибки/успех, всплывающие уведомления сообщают о старте/конце отправки.

# Vikunja Claude Setup

**Русский** | [English](README.en.md)

Установщик для Windows, который подключает Claude к вашей self-hosted [Vikunja](https://vikunja.io): после него Claude
читает и правит ваши задачи, проекты и метки по протоколу MCP. Заодно ставит ярлык веб-приложения Vikunja.

Vikunja 2.7 получила встроенный MCP-сервер, но его подключение к Claude — это ручная правка JSON-файла и перенос токена.
Этот установщик делает то же самое за минуту, проверяет токен и адрес до записи и умеет всё откатить штатным удалением.

<p><img src="docs/setup-welcome.png" width="420" alt="Мастер: приветствие"> <img src="docs/setup-options.png" width="420" alt="Мастер: адрес и токен"></p>

## Что получается

| Где | Что делает установщик | Результат |
|---|---|---|
| Vikunja | Вызывает MCP-эндпоинт `…/api/v2/mcp` с вашим токеном: `initialize`, `tools/list` | Токен и адрес проверены до любой записи |
| Claude Code | Пишет сервер `vikunja` (HTTP, заголовок `Authorization`) в `%USERPROFILE%\.claude.json`, с резервной копией | В новой сессии Claude Code появляются инструменты Vikunja |
| Рабочий стол и меню «Пуск» | Ярлык «Vikunja»: Chrome или Edge в режиме приложения (окно без вкладок) | Трекер открывается как программа |
| «Параметры → Приложения» | Запись Vikunja Claude Setup | Удаление одним щелчком: сервер из конфигурации, ярлыки, своя папка |

Токен сохраняется только в вашей конфигурации Claude Code и никуда, кроме вашей Vikunja, не отправляется. Права
администратора не нужны: всё ставится для текущего пользователя Windows.

Обычный Claude (сайт, настольное приложение, телефон) конфигурацию Claude Code не читает. Для него тот же сервер
добавляется один раз в настройках claude.ai: «Коннекторы → Добавить пользовательский коннектор», URL `…/api/v2/mcp`,
Authentication — «No sign-in», заголовок `Authorization` = `Bearer <токен>`. Установщик показывает эти значения на последней странице.

## Установка

1. В Vikunja (2.7 или новее) под своей учётной записью: **Настройки → MCP → «Создать токен»**. Сохраните токен — он
   показывается один раз. Claude получит ровно те права, что у этой учётки.
2. Скачайте **[Vikunja-Claude-Setup.exe](https://github.com/gp131313/vikunja-claude-setup/releases/latest/download/Vikunja-Claude-Setup.exe)**
   и запустите: «Далее» → адрес Vikunja и токен → «Установить».
3. Откройте новую сессию Claude Code — сервер `vikunja` уже в списке `/mcp`.

Если Windows покажет «Система Windows защитила ваш компьютер» — «Подробнее» → «Выполнить в любом случае»: установщик не
подписан. Не хотите запускать exe — в том же релизе есть `Vikunja-Claude-Setup-*.zip`: распакуйте и дважды щёлкните
`Setup.cmd`, он спросит адрес и токен и сделает то же самое.

### Тихая установка

`Vikunja-Claude-Setup-Silent.exe` (или `Vikunja-Claude-Setup.exe /silent`) не показывает окон:

```
Vikunja-Claude-Setup-Silent.exe /url=https://tasks.example.com /tokenfile=C:\path\token.txt [/noshortcut] [/noclaude]
```

Токен берётся из файла (`/tokenfile`) или из переменной окружения `VIKUNJA_MCP_TOKEN`; в командной строке он не
передаётся. Коды выхода: 0 — готово, 2 — токен отклонён, 3 — адрес недоступен, 4 — по адресу не MCP Vikunja, 1 — прочее.
Ошибка тихого режима пишется в `%TEMP%\VikunjaClaudeSetup-install.log`.

## Как это работает

```
Vikunja 2.7+  ──POST /api/v2/mcp (Bearer tk_…)──  Claude Code (%USERPROFILE%\.claude.json → mcpServers.vikunja)
                                                   claude.ai / Claude Desktop / телефон (пользовательский коннектор)
```

- `windows/install.ps1` — вся логика: проверка, запись конфигурации, ярлык, регистрация удаления.
- `windows/uninstall.ps1` — обратные действия; токен в Vikunja не трогает.
- `windows/setup/Setup.cs` — мастер установки (WinForms), внутри exe лежат оба скрипта. Токен передаётся скрипту через
  переменную окружения процесса, а не через командную строку.
- `windows/Setup.cmd` — запуск без exe.

## Сборка из исходников

SDK не нужен, хватает компилятора C# из состава Windows (.NET Framework 4.x):

```
powershell -NoProfile -ExecutionPolicy Bypass -File windows\setup\build.ps1
```

Результат — `windows\setup\out\Vikunja-Claude-Setup.exe` и `…-Silent.exe` (один и тот же файл, режим определяется по имени).

## Ограничения

- Только Windows 10/11 и Windows PowerShell 5.1 (есть в любой Windows). Для Linux и macOS достаточно одной команды:
  `claude mcp add --transport http vikunja https://<ваша vikunja>/api/v2/mcp --header "Authorization: Bearer <токен>"`.
- Нужна Vikunja 2.7 или новее: в ней появился встроенный MCP-сервер. Сторонние MCP-серверы для старых версий установщик не ставит.
- Если Vikunja стоит за обратным прокси с HTTP Basic-авторизацией, заголовок `Authorization` до неё не дойдёт и токен
  будет отклонён. Снимите Basic-авторизацию для этого адреса или откройте MCP другим путём.
- Если в аккаунте claude.ai уже есть пользовательский коннектор на тот же адрес, настольное приложение Claude может
  убрать локальную запись из `.claude.json` и пользоваться коннектором аккаунта. Это нормально: работает один путь из двух.
- MCP-токен Vikunja имеет срок действия (задаётся при создании). Когда он истечёт, запустите установщик ещё раз с новым токеном.

## Поддержать

Если установщик пригодился — можно кинуть на кофе: Dogecoin `D7z9UaBsmcV7EqJo5Y5fdLG9xUNw47dNgr` (подробнее — [DONATE.md](DONATE.md)).

## Лицензия

[MIT](LICENSE). Vikunja — отдельный проект под лицензией AGPL-3.0, Claude — продукт Anthropic; этот установщик с ними не связан.

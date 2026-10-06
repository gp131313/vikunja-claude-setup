# install.ps1 — подключение Claude к вашей Vikunja (встроенный MCP-сервер Vikunja 2.7+), для текущего пользователя Windows,
# без прав администратора. Запускается из Setup.cmd или из установщика Vikunja-Claude-Setup.exe. Что делает:
#   1) проверяет адрес и MCP-токен: вызывает MCP-эндпоинт Vikunja (initialize, tools/list);
#   2) записывает сервер `vikunja` в пользовательскую конфигурацию Claude Code (%USERPROFILE%\.claude.json), с резервной копией;
#   3) по желанию создаёт ярлык веб-приложения Vikunja (окно браузера без вкладок) на рабочем столе и в меню «Пуск»;
#   4) регистрирует себя в «Параметры → Приложения», чтобы всё это можно было удалить одним щелчком.
# Токен берётся из переменной окружения VIKUNJA_MCP_TOKEN или из файла (-TokenFile); в командной строке он не передаётся.
# Ключи: -Url <адрес Vikunja>  -TokenFile <файл>  -NoClaudeCode  -NoShortcut  -Silent  -NoFinishBox
#        -ConfigPath <путь к .claude.json>  -Dir <папка установки>
# Коды выхода: 0 — готово; 2 — токен отклонён (401); 3 — адрес недоступен; 4 — по адресу не MCP Vikunja; 1 — прочая ошибка.
param(
    [string]$Url,
    [string]$TokenFile,
    [switch]$NoClaudeCode,
    [switch]$NoShortcut,
    [switch]$Silent,
    [switch]$NoFinishBox,
    [string]$ConfigPath,
    [string]$Dir
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$Title = 'Vikunja Claude Setup'
$Version = '1.0.0'
$Ru = ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ru')   # язык окон — по языку Windows
function T([string]$ru, [string]$en) { if ($Ru) { $ru } else { $en } }
function Say([string]$m, [string]$icon = 'Information') { if ($Silent -or $NoFinishBox) { return }; [void][System.Windows.Forms.MessageBox]::Show($m, $Title, 'OK', $icon) }
function Fail([int]$code, [string]$m) {
    if ($Silent) { try { Add-Content -Path (Join-Path $env:TEMP 'VikunjaClaudeSetup-install.log') -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $m) -Encoding UTF8 } catch {} }
    [Console]::Error.WriteLine($m)
    exit $code
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    # --- адрес и токен ---
    if (-not $Url) { Fail 1 (T 'Не указан адрес Vikunja (-Url).' 'Vikunja address (-Url) is not set.') }
    $Url = $Url.Trim().TrimEnd('/')
    if ($Url -notmatch '^https?://') { $Url = 'https://' + $Url }
    $mcpUrl = "$Url/api/v2/mcp"

    $token = $env:VIKUNJA_MCP_TOKEN
    if ($TokenFile) { $token = (Get-Content -LiteralPath $TokenFile -Raw -Encoding UTF8).Trim() }
    if (-not $token) { Fail 1 (T 'Не указан MCP-токен (переменная VIKUNJA_MCP_TOKEN или -TokenFile).' 'MCP token is not set (VIKUNJA_MCP_TOKEN variable or -TokenFile).') }
    $token = $token.Trim()
    if ($token -match '^Bearer\s+') { $token = $token -replace '^Bearer\s+', '' }
    if ($token -notmatch '^tk_[0-9a-fA-F]{16,}$') { Fail 2 (T 'Это не похоже на токен Vikunja: он начинается с tk_ и состоит из шестнадцатеричных символов.' 'This does not look like a Vikunja token: it starts with tk_ followed by hex characters.') }

    # --- 1. проверка MCP ---
    $hdr = @{ Authorization = "Bearer $token"; Accept = 'application/json, text/event-stream' }
    $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"vikunja-claude-setup","version":"' + $Version + '"}}}'
    try {
        $r = Invoke-WebRequest -Uri $mcpUrl -Method Post -Headers $hdr -ContentType 'application/json' -Body $init -TimeoutSec 25 -UseBasicParsing
    } catch {
        $code = 0; try { $code = [int]$_.Exception.Response.StatusCode } catch {}
        if ($code -eq 401) { Fail 2 (T 'Vikunja отклонила токен (401): он неверный, просроченный или без права mcp.access. Создайте токен на странице «Настройки → MCP».' 'Vikunja rejected the token (401): wrong, expired or missing the mcp.access permission. Create one on the Settings → MCP page.') }
        if ($code -eq 404 -or $code -eq 405) { Fail 4 (T "По адресу $mcpUrl нет MCP-эндпоинта. Нужна Vikunja 2.7 или новее." "No MCP endpoint at $mcpUrl. Vikunja 2.7 or newer is required.") }
        Fail 3 ((T "Адрес $mcpUrl недоступен: " "Cannot reach $mcpUrl`: ") + $_.Exception.Message)
    }
    $body = [string]$r.Content
    if ($body -notmatch '"serverInfo"' -or $body -notmatch '"jsonrpc"') { Fail 4 (T "По адресу $mcpUrl отвечает не MCP-сервер (пришёл HTML или другой ответ). Проверьте адрес и версию Vikunja (нужна 2.7+)." "The response at $mcpUrl is not from an MCP server (HTML or something else came back). Check the address and the Vikunja version (2.7+ needed).") }
    $serverName = ''
    if ($body -match '"serverInfo":\{[^}]*"name":"([^"]+)"') { $serverName = $Matches[1] }
    $serverVer = ''
    if ($body -match '"serverInfo":\{[^}]*"version":"([^"]+)"') { $serverVer = $Matches[1] }

    $tools = -1
    $sid = $null; try { $sid = $r.Headers['Mcp-Session-Id'] } catch {}
    if ($sid) {
        $h2 = $hdr.Clone(); $h2['Mcp-Session-Id'] = "$sid"
        try {
            Invoke-WebRequest -Uri $mcpUrl -Method Post -Headers $h2 -ContentType 'application/json' -Body '{"jsonrpc":"2.0","method":"notifications/initialized"}' -TimeoutSec 15 -UseBasicParsing | Out-Null
            $tl = Invoke-WebRequest -Uri $mcpUrl -Method Post -Headers $h2 -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' -TimeoutSec 25 -UseBasicParsing
            $tools = ([regex]::Matches([string]$tl.Content, '"name":"[a-z_]+"')).Count
        } catch {}
    }

    # --- 2. Claude Code ---
    $cfgPath = if ($ConfigPath) { $ConfigPath } else { Join-Path $env:USERPROFILE '.claude.json' }
    $cfgWritten = $false
    if (-not $NoClaudeCode) {
        if (Test-Path -LiteralPath $cfgPath) {
            $bak = "$cfgPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
            Copy-Item -LiteralPath $cfgPath -Destination $bak
            $raw = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8
            $cfg = if ([string]::IsNullOrWhiteSpace($raw)) { [pscustomobject]@{} } else { $raw | ConvertFrom-Json }
        } else {
            $cfg = [pscustomobject]@{}
        }
        if (-not $cfg.PSObject.Properties['mcpServers'] -or $null -eq $cfg.mcpServers) {
            $cfg | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{}) -Force
        }
        $srv = [pscustomobject]@{
            type    = 'http'
            url     = $mcpUrl
            headers = [pscustomobject]@{ Authorization = "Bearer $token" }
        }
        $cfg.mcpServers | Add-Member -NotePropertyName vikunja -NotePropertyValue $srv -Force
        $json = $cfg | ConvertTo-Json -Depth 64
        [IO.File]::WriteAllText($cfgPath, $json, [Text.UTF8Encoding]::new($false))
        $cfgWritten = $true
    }

    # --- 3. ярлык веб-приложения ---
    $shortcutMade = $false
    $browser = $null
    if (-not $NoShortcut) {
        $browser = @(
            "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
            "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
            "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
            "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
            "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($browser) {
            $sh = New-Object -ComObject WScript.Shell
            foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
                $s = $sh.CreateShortcut((Join-Path $folder 'Vikunja.lnk'))
                $s.TargetPath = $browser
                $s.Arguments = "--app=$Url/"
                $s.IconLocation = "$browser,0"
                $s.Description = (T 'Vikunja — трекер дел' 'Vikunja — task tracker')
                $s.Save()
            }
            $shortcutMade = $true
        }
    }

    # --- 4. регистрация для удаления ---
    $dst = if ($Dir) { $Dir } else { Join-Path $env:LOCALAPPDATA 'VikunjaClaudeSetup' }
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    $srcUn = Join-Path $PSScriptRoot 'uninstall.ps1'
    $dstUn = Join-Path $dst 'uninstall.ps1'
    if ((Test-Path -LiteralPath $srcUn) -and ([IO.Path]::GetFullPath($srcUn) -ne [IO.Path]::GetFullPath($dstUn))) { Copy-Item -LiteralPath $srcUn -Destination $dstUn -Force }
    @{ url = $Url; mcpUrl = $mcpUrl; configPath = $cfgPath; configWritten = $cfgWritten; shortcut = $shortcutMade; installed = (Get-Date).ToString('s') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dst 'state.json') -Encoding UTF8
    Get-ChildItem -LiteralPath $dst -File | Unblock-File
    $un = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $dstUn
    $reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VikunjaClaudeSetup'
    New-Item -Path $reg -Force | Out-Null
    Set-ItemProperty -Path $reg -Name DisplayName -Value $Title
    Set-ItemProperty -Path $reg -Name DisplayVersion -Value $Version
    Set-ItemProperty -Path $reg -Name Publisher -Value 'gp131313'
    Set-ItemProperty -Path $reg -Name URLInfoAbout -Value 'https://github.com/gp131313/vikunja-claude-setup'
    Set-ItemProperty -Path $reg -Name InstallLocation -Value $dst
    Set-ItemProperty -Path $reg -Name UninstallString -Value $un
    Set-ItemProperty -Path $reg -Name QuietUninstallString -Value ($un + ' -Silent')
    Set-ItemProperty -Path $reg -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $reg -Name NoRepair -Value 1 -Type DWord
    Set-ItemProperty -Path $reg -Name EstimatedSize -Value 32 -Type DWord

    # --- итог (одна строка для установщика + окно для Setup.cmd) ---
    Write-Output ("RESULT tools={0} server={1} version={2} config={3} shortcut={4}" -f $tools, $serverName, $serverVer, [int]$cfgWritten, [int]$shortcutMade)
    $lines = @()
    $lines += (T "Готово! MCP Vikunja отвечает" "Done! The Vikunja MCP responds") + $(if ($tools -ge 0) { (T ", инструментов: $tools." ", tools: $tools.") } else { '.' })
    if ($cfgWritten) { $lines += (T "Claude Code: сервер vikunja записан в $cfgPath. Он появится в следующей сессии Claude Code." "Claude Code: server vikunja written to $cfgPath. It appears in the next Claude Code session.") }
    if ($shortcutMade) { $lines += (T 'Ярлык «Vikunja» — на рабочем столе и в меню «Пуск».' 'The "Vikunja" shortcut is on the desktop and in the Start menu.') }
    elseif (-not $NoShortcut) { $lines += (T 'Chrome или Edge не найдены — ярлык веб-приложения не создан.' 'Chrome or Edge not found — no web-app shortcut was created.') }
    $lines += (T "Для обычного Claude (сайт, приложение, телефон): Настройки → Коннекторы → Добавить пользовательский коннектор; URL $mcpUrl; Authentication — No sign-in; заголовок Authorization = Bearer <токен>." "For regular Claude (web, desktop, phone): Settings → Connectors → Add custom connector; URL $mcpUrl; Authentication — No sign-in; header Authorization = Bearer <token>.")
    $lines += (T "Удалить: «Параметры» → «Приложения» → $Title." "Uninstall: Settings → Apps → $Title.")
    Say ($lines -join "`n`n")
    exit 0
}
catch {
    Fail 1 ((T "Установка не удалась: " "Installation failed: ") + $_.Exception.Message)
}

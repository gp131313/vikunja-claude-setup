# uninstall.ps1 — удаление Vikunja Claude Setup: убирает сервер `vikunja` из конфигурации Claude Code (с резервной копией),
# ярлыки «Vikunja», запись в «Приложениях» и свою папку. Саму Vikunja, Claude Code и токен на сервере не трогает.
# -Silent: без вопросов и окон.
param([switch]$Silent)
Add-Type -AssemblyName System.Windows.Forms
$Title = 'Vikunja Claude Setup'
$Ru = ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ru')
function T([string]$ru, [string]$en) { if ($Ru) { $ru } else { $en } }
if (-not $Silent -and [System.Windows.Forms.MessageBox]::Show((T 'Отключить Vikunja от Claude Code и убрать ярлыки?' 'Disconnect Vikunja from Claude Code and remove the shortcuts?'), $Title, 'YesNo', 'Question') -ne 'Yes') { exit 0 }

$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VikunjaClaudeSetup'
$dst = (Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue).InstallLocation
if (-not $dst) { $dst = Join-Path $env:LOCALAPPDATA 'VikunjaClaudeSetup' }
$state = $null
try { $state = Get-Content -LiteralPath (Join-Path $dst 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json } catch {}
$cfgPath = if ($state -and $state.configPath) { $state.configPath } else { Join-Path $env:USERPROFILE '.claude.json' }

# 1. сервер vikunja из конфигурации Claude Code
if (Test-Path -LiteralPath $cfgPath) {
    try {
        $cfg = Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($cfg.PSObject.Properties['mcpServers'] -and $cfg.mcpServers.PSObject.Properties['vikunja']) {
            Copy-Item -LiteralPath $cfgPath -Destination "$cfgPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
            $cfg.mcpServers.PSObject.Properties.Remove('vikunja')
            [IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json -Depth 64), [Text.UTF8Encoding]::new($false))
        }
    } catch {
        if (-not $Silent) { [void][System.Windows.Forms.MessageBox]::Show((T "Не удалось изменить $cfgPath`: " "Could not edit $cfgPath`: ") + $_.Exception.Message, $Title, 'OK', 'Warning') }
    }
}

# 2. ярлыки и запись в «Приложениях»
foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    Remove-Item -LiteralPath (Join-Path $folder 'Vikunja.lnk') -Force -ErrorAction SilentlyContinue
}
Remove-Item -Path $reg -Recurse -Force -ErrorAction SilentlyContinue

# 3. папку удаляем после выхода этого скрипта (он сам лежит внутри неё)
Start-Process cmd.exe -ArgumentList '/c', ('timeout /t 2 /nobreak >nul & rmdir /s /q "{0}"' -f $dst) -WindowStyle Hidden
if (-not $Silent) { [void][System.Windows.Forms.MessageBox]::Show((T 'Готово. Токен в Vikunja остался — при желании удалите его на странице «Настройки → MCP».' 'Done. The token still exists in Vikunja — delete it on the Settings → MCP page if you wish.'), $Title, 'OK', 'Information') }

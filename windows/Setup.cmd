@echo off
rem Подключение Claude к Vikunja без exe: двойной щелчок по этому файлу.
rem Спросит адрес Vikunja и MCP-токен (ввод скрыт), затем запустит install.ps1.
setlocal
set /p VIKUNJA_URL=Vikunja address (https://...):
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$s = Read-Host -AsSecureString 'MCP token (hidden)'; $b=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($s); $env:VIKUNJA_MCP_TOKEN=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($b); & '%~dp0install.ps1' -Url '%VIKUNJA_URL%'"
endlocal

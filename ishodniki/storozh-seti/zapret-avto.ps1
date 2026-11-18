# Автоматический прогон zapret: снять службу -> тест всех стратегий -> поставить лучшую.
# С параметром -Install "general (ALT13)" — только поставить эту стратегию, без теста.
# Запускается с правами администратора. Журнал: zapret-avto.log рядом.
param([string]$Install)
$ErrorActionPreference = 'Continue'
# самая новая версия zapret в Загрузках (папка с service.bat)
$Z = (Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads') -Directory -Filter 'zapret-discord-youtube-*' | Where-Object { Test-Path (Join-Path $_.FullName 'service.bat') } |
    Sort-Object { [version](($_.Name -replace '^zapret-discord-youtube-', '') -replace '[^0-9.]', '') } | Select-Object -Last 1).FullName
$LOG = Join-Path $PSScriptRoot 'zapret-avto.log'
$FALLBACK = 'general (ALT13)'   # что стояло раньше — вернуть, если тест не удался
function L($m) { "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m | Out-File $LOG -Append -Encoding utf8 }

function Make-AutoBat {
    # копия service.bat, которая сразу выполняет нужный пункт тем же кодом, без меню и пауз
    $src = [IO.File]::ReadAllText("$Z\service.bat", [Text.Encoding]::UTF8)
    $lines = $src -split "`r?`n"
    $done = $false
    $res = foreach ($ln in $lines) {
        $t = $ln.Trim()
        if (-not $done -and $t -eq 'setlocal EnableDelayedExpansion') {
            # как настоящее меню: сначала загрузить состояние Game Filter и IPSet, иначе порты пустые
            $ln
            'if defined AUTO_ACTION call :ipset_switch_status'
            'if defined AUTO_ACTION call :game_switch_status'
            'if defined AUTO_ACTION goto %AUTO_ACTION%'
            $done = $true; continue
        }
        if ($t -like 'set /p "choice=Input option*') { $ln -replace 'set /p "choice=.*', 'set "choice=%AUTO_CHOICE%"'; continue }
        if ($t -eq 'pause') { $ln -replace 'pause', 'rem pause'; continue }
        if ($t -eq 'goto menu') { $ln -replace 'goto menu', 'exit /b 0'; continue }
        $ln
    }
    [IO.File]::WriteAllText("$Z\service_auto.bat", ($res -join "`r`n"), (New-Object Text.UTF8Encoding($false)))
}

function Menu($action, $choice = '') {
    $out = Join-Path $env:TEMP 'zapret-menu-out.txt'
    $env:AUTO_ACTION = $action; $env:AUTO_CHOICE = $choice
    $p = Start-Process cmd.exe -ArgumentList "/c `"`"$Z\service_auto.bat`" admin < NUL`"" -WorkingDirectory $Z `
        -RedirectStandardOutput $out -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(120000)) { L "действие $action не закончилось за 2 минуты — снимаю"; $p.Kill() }
    Get-Content $out -ErrorAction SilentlyContinue | Out-File (Join-Path $PSScriptRoot 'zapret-menu-output.log') -Append -Encoding utf8
    Remove-Item $out -ErrorAction SilentlyContinue
    Remove-Item Env:AUTO_ACTION, Env:AUTO_CHOICE -ErrorAction SilentlyContinue
}

function Strategies {
    Get-ChildItem -LiteralPath $Z -Filter '*.bat' | Where-Object { $_.Name -notlike 'service*' } |
        Sort-Object { [Regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) } |
        ForEach-Object { $_.BaseName }
}

function Install($name) {
    $list = @(Strategies)
    $idx = [array]::IndexOf($list, $name) + 1
    if ($idx -lt 1) { L "не нашёл стратегию '$name' в списке"; return $false }
    L "ставлю службу: $name (пункт $idx)"
    Menu 'service_install' "$idx"
    Start-Sleep 3
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    $reg = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -ErrorAction SilentlyContinue).'zapret-discord-youtube'
    $img = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -ErrorAction SilentlyContinue).ImagePath
    L ("служба: {0}, стратегия в реестре: {1}, папка новой версии: {2}" -f $svc.Status, $reg, ($img -like "*1.10.4*"))
    return ($svc.Status -eq 'Running' -and $reg -eq $name)
}

if ($Install) {
    L "=== только установка: $Install"
    Make-AutoBat
    $ok = Install $Install
    if (-not $ok) { L 'не встала — возвращаю прежнюю'; [void](Install $FALLBACK) }
    Remove-Item "$Z\service_auto.bat" -ErrorAction SilentlyContinue
    L '=== готово'
    exit
}

L '=== старт'
$upd = Join-Path $Z 'utils\check_updates.enabled'
$updOff = $false
if (Test-Path $upd) { Rename-Item $upd 'check_updates.enabled.off'; $updOff = $true }

try {
    # 1. снять службу
    Make-AutoBat
    L 'снимаю службу zapret'
    Menu 'service_remove'
    Start-Sleep 2
    L ("служба после снятия: " + [bool](Get-Service zapret -ErrorAction SilentlyContinue))

    # 2. тест: копия скрипта с автоответами и без ожидания клавиши
    $orig = Join-Path $Z 'utils\test zapret.ps1'
    $auto = Join-Path $Z 'utils\test zapret auto.ps1'
    $pre = @'
$global:__ans = New-Object System.Collections.Queue
'1','1' | ForEach-Object { $global:__ans.Enqueue($_) }
function Read-Host { param($Prompt) if ($global:__ans.Count) { $a = $global:__ans.Dequeue() } else { $a = '' }; Write-Host "$Prompt> $a"; return $a }
'@
    $body = (Get-Content $orig -Raw -Encoding utf8) -replace 'function Wait-AnyKey \{', "function Wait-AnyKey { param([string]`$message) }`r`nfunction Wait-AnyKey-Orig {"
    Set-Content -Path $auto -Value ($pre + "`r`n" + $body) -Encoding utf8
    $before = Get-Date
    L 'запускаю тест всех стратегий'
    & $auto *>&1 | Out-File (Join-Path $PSScriptRoot 'zapret-test-output.log') -Encoding utf8
    Remove-Item $auto -ErrorAction SilentlyContinue

    # 3. лучшая стратегия
    $res = Get-ChildItem (Join-Path $Z 'utils\test results') -Filter 'test_results_*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $before } | Sort-Object LastWriteTime | Select-Object -Last 1
    $best = $null
    if ($res) {
        $line = Select-String -Path $res.FullName -Pattern '^Best strategy:\s*(.+?)(\.bat)?\s*$' | Select-Object -Last 1
        if ($line) { $best = $line.Matches[0].Groups[1].Value.Trim() }
        Get-Content $res.FullName | Select-String -Pattern '^general.*: OK:' | ForEach-Object { L ("  " + $_.Line) }
    }
    if ($best) {
        L "лучшая стратегия: $best"
        if (-not (Install $best)) { L 'лучшая не встала — возвращаю прежнюю'; [void](Install $FALLBACK) }
    } else {
        L 'тест не дал результата — возвращаю прежнюю стратегию'
        [void](Install $FALLBACK)
    }
} catch {
    L ("ошибка: " + $_)
    if (-not (Get-Service zapret -ErrorAction SilentlyContinue)) { [void](Install $FALLBACK) }
} finally {
    Remove-Item "$Z\service_auto.bat" -ErrorAction SilentlyContinue
    if ($updOff) { Rename-Item (Join-Path $Z 'utils\check_updates.enabled.off') 'check_updates.enabled' -ErrorAction SilentlyContinue }
    L '=== готово'
}

# Автоматический прогон zapret: снять службу -> тест всех стратегий -> поставить лучшую.
# С параметром -Install "general (ALT13)" — только поставить эту стратегию, без теста.
# Запускается с правами администратора. Журнал: zapret-avto.log рядом.
# С параметром -Hod "<путь>" пишет ход для окна (шаги, «проверяю настройку 7 из 19», итог); без него — как раньше.
param([string]$Install, [string]$Hod)
$ErrorActionPreference = 'Continue'
# ---------- ход для окна «Сторож сети» (договор storozh-okno\DOGOVOR-hod.md) ----------
# Путь к файлу хода окно даёт параметром -Hod. Нет его — функции Hod-* ничего не пишут (ни ход, ни историю).
# Пишет фоновый поток: при каждой смене шага и раз в 1,5 с — скрипт подолгу ждёт службу, тест или скачивание.
# Ошибки записи хода делу не мешают: всё внутри try/catch. Эти функции одинаковые во всех zapret-*.ps1
# (проверка testy\proverka_drug.py сверяет). История длительностей — hod-vremya-ps.txt рядом со скриптом.
function Hod-Pisar {
    # код записи файла — текстом: скриптблок привязан к своему потоку, поэтому и поток, и скрипт создают свой
    return @'
param($S)
[Threading.Monitor]::Enter($S.SyncRoot)
try {
    if ($S.zapisanKonec) { return }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $t = ((Get-Date).ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds
    $n = $S.shagi.Count; $k = [int]$S.shag; $w = $S.ves
    if (-not $S.konec) {
        $vsego = 0.0; foreach ($x in $w) { $vsego += $x }
        $sdelano = 0.0; for ($i = 0; $i -lt $k; $i++) { $sdelano += $w[$i] }
        $vShage = $t - $S.nachShaga[$k]
        $dolya = 0.0
        if ($S.iz -gt 0) {
            $dolya = $S.schet / $S.iz
            if ($S.schet -gt 0) {
                $naOdin = ($S.tScheta - $S.nachShaga[$k]) / $S.schet
                if ($naOdin -gt 0) { $dolya += [math]::Min(0.9, ($t - $S.tScheta) / $naOdin) / $S.iz }
            }
        } elseif ($w[$k] -gt 0) { $dolya = [math]::Min(0.9, $vShage / $w[$k]) }
        $dolya = [math]::Min(1.0, $dolya)
        $p = [int][math]::Floor(100 * ($sdelano + $w[$k] * $dolya) / [math]::Max(0.001, $vsego))
        $S.procent = [math]::Max([int]$S.procent, [math]::Min(99, $p))
        $ost = $null
        if ($S.estIst) {
            if ($S.iz -gt 0 -and $S.schet -gt 0) { $o = ($S.tScheta - $S.nachShaga[$k]) / $S.schet * ($S.iz - $S.schet) - ($t - $S.tScheta) }
            else { $o = $w[$k] - $vShage }
            $o = [math]::Max(0.0, $o)
            for ($i = $k + 1; $i -lt $n; $i++) { $o += $w[$i] }
            $ost = $o
        } else {
            $proshlo = $t - $S.nachalo
            if ($S.procent -ge 3 -and $proshlo -ge 3) { $ost = $proshlo * (100 - $S.procent) / $S.procent }
        }
        $S.ostalos = if ($null -eq $ost) { $null } else { [int][math]::Round($ost) }
    }
    $str = { param($x) '"' + ((([string]$x) -replace '\\', '\\' -replace '"', '\"' -replace "`r", '\r' -replace "`n", '\n' -replace "`t", '\t') -replace '[\x00-\x1f]', '') + '"' }
    $j = '{"chto":' + (& $str $S.chto) + ',"shagi":[' + ((@($S.shagi) | ForEach-Object { & $str $_ }) -join ',') + ']' +
        ',"shag":' + $k + ',"tekst":' + (& $str $S.tekst) + ',"procent":' + [int]$S.procent +
        ',"ostalos":' + $(if ($null -eq $S.ostalos) { 'null' } else { [string][int]$S.ostalos }) +
        ',"nachalo":' + ([double]$S.nachalo).ToString('0.000', $inv) + ',"obnovleno":' + $t.ToString('0.000', $inv) +
        ',"konec":' + $(if ($S.konec) { 'true' } else { 'false' }) +
        ',"ok":' + $(if ($null -eq $S.ok) { 'null' } elseif ($S.ok) { 'true' } else { 'false' }) +
        ',"itog":' + (& $str $S.itog) + '}'
    $tmp = $S.put + '.' + $PID + '.tmp'
    [IO.File]::WriteAllText($tmp, $j, (New-Object Text.UTF8Encoding($false)))
    $popytok = if ($S.konec) { 20 } else { 3 }
    for ($i = 0; $i -lt $popytok; $i++) {
        try { Move-Item -LiteralPath $tmp -Destination $S.put -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds (50 * ($i + 1)) }
    }
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    if ($S.konec) { $S.zapisanKonec = $true }
} catch { } finally { [Threading.Monitor]::Exit($S.SyncRoot) }
'@
}
function Hod-Potok {
    # фоновый поток: пишет при смене шага (nado) и не реже раза в 1,5 с, пока скрипт не скажет stop
    return @'
param($S)
$pisar = [scriptblock]::Create($S.pisar)
$posl = [Environment]::TickCount
while (-not $S.stop) {
    if ($S.nado -or ([Environment]::TickCount - $posl) -ge 1500) {
        $S.nado = $false
        try { & $pisar $S } catch { }
        $posl = [Environment]::TickCount
    }
    Start-Sleep -Milliseconds 100
}
'@
}
function Hod-Nachat($chto, $shagi, $klyuch, $vesa) {
    # начать ход; уже идёт (вложенный вызов) — ничего не меняет; vesa — примерные секунды шагов, пока нет истории
    try {
        if (-not $Hod) { return }
        if ($script:HodS -and -not $script:HodS.konec) { return }
        $shagi = [string[]]@(@($shagi) | Where-Object { $_ })
        if (-not $shagi.Count) { $shagi = [string[]]@([string]$chto) }
        $n = $shagi.Count
        $ves = New-Object 'double[]' $n
        for ($i = 0; $i -lt $n; $i++) { $ves[$i] = if ($vesa -and @($vesa).Count -eq $n) { [math]::Max(0.1, [double]@($vesa)[$i]) } else { 1.0 } }
        $t = ((Get-Date).ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds
        $S = [hashtable]::Synchronized(@{})
        $S.put = [string]$Hod; $S.chto = [string]$chto; $S.shagi = $shagi; $S.shag = 0; $S.tekst = $shagi[0]
        $S.procent = 0; $S.ostalos = $null; $S.nachalo = $t; $S.konec = $false; $S.ok = $null; $S.itog = ''
        $S.schet = 0; $S.iz = 0; $S.tScheta = $t; $S.nado = $false; $S.stop = $false; $S.zapisanKonec = $false
        $S.nachShaga = New-Object 'double[]' $n
        for ($i = 1; $i -lt $n; $i++) { $S.nachShaga[$i] = -1 }
        $S.nachShaga[0] = $t
        $S.klyuch = if ($klyuch) { [string]$klyuch } else { [string]$chto }
        $S.ist = if ($script:HodIstoriya) { $script:HodIstoriya } elseif ($PSScriptRoot) { Join-Path $PSScriptRoot 'hod-vremya-ps.txt' } else { '' }
        $S.ves = $ves; $S.estIst = $false
        try {
            if ($S.ist -and (Test-Path -LiteralPath $S.ist)) {
                $inv = [Globalization.CultureInfo]::InvariantCulture
                $zap = New-Object System.Collections.ArrayList
                foreach ($stroka in [IO.File]::ReadAllLines($S.ist, [Text.Encoding]::UTF8)) {
                    $c = $stroka -split "`t"
                    if ($c.Count -ne 3 -or $c[0] -ne $S.klyuch) { continue }
                    $d = @($c[2] -split ';')
                    if ($d.Count -ne $n) { continue }
                    [void]$zap.Add([double[]]@($d | ForEach-Object { [double]::Parse($_, $inv) }))
                }
                if ($zap.Count) {
                    $med = New-Object 'double[]' $n
                    for ($i = 0; $i -lt $n; $i++) {
                        $v = @($zap | ForEach-Object { $_[$i] } | Sort-Object)
                        $m = if ($v.Count % 2) { $v[[int][math]::Floor($v.Count / 2)] } else { ($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2 }
                        $med[$i] = [math]::Max(0.2, $m)
                    }
                    $S.ves = $med; $S.estIst = $true
                }
            }
        } catch { }
        $S.pisar = Hod-Pisar
        $script:HodS = $S
        & ([scriptblock]::Create($S.pisar)) $S   # первая запись сразу — чтобы файл появился
        $rs = [runspacefactory]::CreateRunspace()
        $rs.Open()
        $ps = [powershell]::Create()
        $ps.Runspace = $rs
        [void]$ps.AddScript((Hod-Potok)).AddArgument($S)
        $S.rs = $rs; $S.ps = $ps; $S.h = $ps.BeginInvoke()
    } catch { }
}
function Hod-Shag($nomer, $tekst) {
    # перейти к шагу (номер с нуля); назад ход не идёт — тогда меняется только подпись
    try {
        $S = $script:HodS
        if (-not $S -or $S.konec) { return }
        [Threading.Monitor]::Enter($S.SyncRoot)
        try {
            $nomer = [math]::Max(0, [math]::Min([int]$nomer, $S.shagi.Count - 1))
            if ($nomer -gt $S.shag) {
                $t = ((Get-Date).ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds
                for ($i = $S.shag + 1; $i -le $nomer; $i++) { $S.nachShaga[$i] = $t }
                $S.shag = $nomer; $S.schet = 0; $S.iz = 0; $S.tScheta = $t
                $S.tekst = if ($tekst) { [string]$tekst } else { $S.shagi[$nomer] }
            } elseif ($tekst) { $S.tekst = [string]$tekst }
            $S.nado = $true
        } finally { [Threading.Monitor]::Exit($S.SyncRoot) }
    } catch { }
}
function Hod-Tekst($tekst) {
    # сменить подпись внутри текущего шага
    try {
        $S = $script:HodS
        if (-not $S -or $S.konec -or -not $tekst) { return }
        $S.tekst = [string]$tekst
        $S.nado = $true
    } catch { }
}
function Hod-Schet($n, $iz, $tekst) {
    # счёт внутри шага: «проверяю настройку 7 из 19»
    try {
        $S = $script:HodS
        if (-not $S -or $S.konec) { return }
        [Threading.Monitor]::Enter($S.SyncRoot)
        try {
            $n = [math]::Max(0, [int]$n); $iz = [math]::Max(0, [int]$iz)
            if ($iz -gt 0 -and ($iz -ne $S.iz -or $n -ge $S.schet)) {
                if ($n -ne $S.schet) { $S.tScheta = ((Get-Date).ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds }
                $S.schet = [math]::Min($n, $iz); $S.iz = $iz
            }
            $S.tekst = if ($tekst) { [string]$tekst } else { '{0}: {1} из {2}' -f $S.shagi[$S.shag], $n, $iz }
            $S.nado = $true
        } finally { [Threading.Monitor]::Exit($S.SyncRoot) }
    } catch { }
}
function Hod-Konec($ok, $itog) {
    # конец: konec true, ok, итог одной фразой; повторный вызов ничего не меняет. Удачный запуск — в историю
    try {
        $S = $script:HodS
        if (-not $S -or $S.konec) { return }
        $ok = ($ok -eq $true)
        $S.stop = $true
        try { [void]$S.h.AsyncWaitHandle.WaitOne(3000) } catch { }
        try { $S.ps.Dispose(); $S.rs.Dispose() } catch { }
        $t = ((Get-Date).ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds
        [Threading.Monitor]::Enter($S.SyncRoot)
        try {
            $S.konec = $true; $S.ok = $ok
            $S.itog = if ($itog) { ([string]$itog).Trim() } elseif ($ok) { 'Готово' } else { 'Не вышло' }
            $S.procent = 100; $S.ostalos = 0
            if ($ok) { $S.shag = $S.shagi.Count - 1 }
        } finally { [Threading.Monitor]::Exit($S.SyncRoot) }
        & ([scriptblock]::Create($S.pisar)) $S
        if ($ok -and $S.ist) {
            try {
                $inv = [Globalization.CultureInfo]::InvariantCulture
                $nach = [double[]]$S.nachShaga.Clone()
                for ($i = $nach.Count - 1; $i -ge 0; $i--) {
                    if ($nach[$i] -lt 0) { $nach[$i] = if ($i + 1 -lt $nach.Count) { $nach[$i + 1] } else { $t } }
                }
                $dl = for ($i = 0; $i -lt $nach.Count; $i++) {
                    $kon = if ($i + 1 -lt $nach.Count) { $nach[$i + 1] } else { $t }
                    ([math]::Max(0.0, $kon - $nach[$i])).ToString('0.0', $inv)
                }
                $nov = '{0}{1}{2}{1}{3}' -f $S.klyuch, "`t", ($t - $S.nachalo).ToString('0.0', $inv), (@($dl) -join ';')
                $stroki = @()
                if (Test-Path -LiteralPath $S.ist) { $stroki = @([IO.File]::ReadAllLines($S.ist, [Text.Encoding]::UTF8)) }
                $chuzhie = @($stroki | Where-Object { $_ -and ($_ -split "`t")[0] -ne $S.klyuch })
                $svoi = @(@($stroki | Where-Object { $_ -and ($_ -split "`t")[0] -eq $S.klyuch }) + $nov | Select-Object -Last 5)
                [IO.File]::WriteAllLines($S.ist, [string[]]@($chuzhie + $svoi), (New-Object Text.UTF8Encoding($false)))
            } catch { }
        }
    } catch { }
}
# ---------- конец помощников хода ----------
if ($Install) {
    Hod-Nachat "Ставлю настройку обхода zapret: $Install" @('Ставлю службу zapret с этой настройкой') 'zapret-ustanovka' @(15)
} else {
    Hod-Nachat 'Подбор настройки обхода zapret' @('Снимаю службу zapret', 'Проверяю настройки обхода по очереди', 'Ставлю лучшую настройку') 'zapret-avto' @(10, 540, 15)
}
function Get-Zagruzki {
    # папка «Загрузки» — её можно перенести на другой диск, поэтому спрашиваем у Windows
    $p = $null
    try { $p = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path } catch { }
    if (-not $p -or -not (Test-Path -LiteralPath $p)) { $p = Join-Path $env:USERPROFILE 'Downloads' }
    return $p
}
# самая новая версия zapret в Загрузках (папка с service.bat)
$Z = (Get-ChildItem (Get-Zagruzki) -Directory -Filter 'zapret-discord-youtube-*' | Where-Object { Test-Path (Join-Path $_.FullName 'service.bat') } |
    Sort-Object { [version](($_.Name -replace '^zapret-discord-youtube-', '') -replace '[^0-9.]', '') } | Select-Object -Last 1).FullName
$LOG = Join-Path $PSScriptRoot 'zapret-avto.log'
if (-not $Z) {
    "[{0}] zapret не найден: в «Загрузках» нет папки zapret-discord-youtube-…. Нажмите в окне «Скачать и поставить zapret»." -f (Get-Date -Format 'HH:mm:ss') | Out-File $LOG -Append -Encoding utf8
    Hod-Konec $false 'zapret не найден в «Загрузках» — нажмите «Скачать и поставить zapret»'
    exit 2
}
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
    if ($idx -lt 1) { L "не нашёл настройку обхода '$name' в списке"; return $false }
    L "ставлю службу: $name (пункт $idx)"
    Menu 'service_install' "$idx"
    Start-Sleep 3
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    $reg = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -ErrorAction SilentlyContinue).'zapret-discord-youtube'
    $img = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -ErrorAction SilentlyContinue).ImagePath
    L ("служба: {0}, настройка обхода: {1}, папка новой версии: {2}" -f $svc.Status, $reg, ($img -like "*$(Split-Path $Z -Leaf)*"))
    return ($svc.Status -eq 'Running' -and $reg -eq $name)
}

if ($Install) {
    L "=== только установка: $Install"
    Make-AutoBat
    $ok = Install $Install
    if (-not $ok) { L 'не встала — возвращаю прежнюю'; Hod-Tekst "Не встала — возвращаю прежнюю настройку ($FALLBACK)"; [void](Install $FALLBACK) }
    Remove-Item "$Z\service_auto.bat" -ErrorAction SilentlyContinue
    L '=== готово'
    if ($ok) { Hod-Konec $true "zapret работает с настройкой обхода $Install" }
    else { Hod-Konec $false "Настройка $Install не встала — вернул прежнюю ($FALLBACK)" }
    exit
}

L '=== старт'
$upd = Join-Path $Z 'utils\check_updates.enabled'
$updOff = $false
if (Test-Path $upd) { Rename-Item $upd 'check_updates.enabled.off'; $updOff = $true }
$hodOk = $false; $hodItog = ''   # итог для окна (ход)

try {
    # 1. снять службу
    Make-AutoBat
    L 'снимаю службу zapret'
    Menu 'service_remove'
    Start-Sleep 2
    L ("служба после снятия: " + [bool](Get-Service zapret -ErrorAction SilentlyContinue))

    # 2. тест: копия скрипта с автоответами и без ожидания клавиши
    Hod-Shag 1
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
    L 'проверяю все настройки обхода по очереди'
    # вывод теста идёт в журнал как раньше; по строкам «[7/19] general (ALT7).bat» — счёт для окна
    & $auto *>&1 | ForEach-Object {
        if ("$_" -match '^\s*\[(\d+)/(\d+)\]\s+(.+?)(\.bat)?\s*$') {
            Hod-Schet ([int]$Matches[1] - 1) ([int]$Matches[2]) ('Проверяю настройку {0} из {1}: {2}' -f $Matches[1], $Matches[2], $Matches[3])
        }
        $_
    } | Out-File (Join-Path $PSScriptRoot 'zapret-test-output.log') -Encoding utf8
    Remove-Item $auto -ErrorAction SilentlyContinue

    # 3. лучшая стратегия
    Hod-Shag 2
    $res = Get-ChildItem (Join-Path $Z 'utils\test results') -Filter 'test_results_*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $before } | Sort-Object LastWriteTime | Select-Object -Last 1
    $best = $null
    if ($res) {
        $line = Select-String -Path $res.FullName -Pattern '^Best strategy:\s*(.+?)(\.bat)?\s*$' | Select-Object -Last 1
        if ($line) { $best = $line.Matches[0].Groups[1].Value.Trim() }
        Get-Content $res.FullName | Select-String -Pattern '^general.*: OK:' | ForEach-Object { L ("  " + $_.Line) }
    }
    if ($best) {
        L "лучшая настройка обхода: $best"
        Hod-Tekst "Ставлю лучшую настройку: $best"
        if (Install $best) {
            $hodOk = $true; $hodItog = "Поставлена настройка обхода $best — служба zapret работает"
        } else {
            L 'лучшая не встала — возвращаю прежнюю'
            Hod-Tekst "Не встала — возвращаю прежнюю настройку ($FALLBACK)"
            [void](Install $FALLBACK)
            $hodItog = "Лучшая настройка $best не встала — вернул прежнюю ($FALLBACK)"
        }
    } else {
        L 'тест не дал результата — возвращаю прежнюю настройку обхода'
        Hod-Tekst "Проверка не дала результата — возвращаю прежнюю настройку ($FALLBACK)"
        [void](Install $FALLBACK)
        $hodItog = "Подбор не дал результата — вернул прежнюю настройку ($FALLBACK)"
    }
} catch {
    L ("ошибка: " + $_)
    if (-not (Get-Service zapret -ErrorAction SilentlyContinue)) { [void](Install $FALLBACK) }
    $hodItog = 'Подбор прервался из-за ошибки — подробности в zapret-avto.log'
} finally {
    Remove-Item "$Z\service_auto.bat" -ErrorAction SilentlyContinue
    if ($updOff) { Rename-Item (Join-Path $Z 'utils\check_updates.enabled.off') 'check_updates.enabled' -ErrorAction SilentlyContinue }
    L '=== готово'
    Hod-Konec $hodOk $(if ($hodItog) { $hodItog } else { 'Подбор оборвался — подробности в zapret-avto.log' })
}

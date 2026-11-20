# Тест стратегий zapret на скорость видео YouTube напрямую (без VPN). Запуск с правами администратора.
# Ставит самую быструю из надёжных (прошли полный тест 36/36), если она заметно быстрее ALT13; иначе — ALT13.
# С параметром -Hod "<путь>" пишет ход для окна (шаги, «проверяю настройку 7 из 19», итог); без него — как раньше.
param([string]$Hod)
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
Hod-Nachat 'Подбор настройки zapret по скорости YouTube' @('Узнаю ссылку на пробное видео YouTube',
    'Снимаю службу zapret и меряю скорость без неё', 'Проверяю настройки обхода по очереди',
    'Ставлю самую быструю надёжную настройку', 'Проверяю скорость YouTube') 'zapret-youtube' @(15, 15, 200, 20, 15)
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
$HERE = $PSScriptRoot
$LOG = Join-Path $HERE 'zapret-youtube.log'
if (-not $Z) {
    "[{0}] zapret не найден: в «Загрузках» нет папки zapret-discord-youtube-…. Нажмите в окне «Скачать и поставить zapret»." -f (Get-Date -Format 'HH:mm:ss') | Out-File $LOG -Append -Encoding utf8
    Hod-Konec $false 'zapret не найден в «Загрузках» — нажмите «Скачать и поставить zapret»'
    exit 2
}
# ссылка на видеопоток живёт несколько часов и привязана к IP — берём свежую напрямую, пока zapret ещё стоит
$YTDLP = (Get-Command yt-dlp.exe -ErrorAction SilentlyContinue).Source; if (-not $YTDLP) { $YTDLP = 'yt-dlp.exe' }
$URL = ''
try { $URL = (& $YTDLP --proxy '' -q -f 'bv*[height<=1080][ext=mp4]/bv*' -g 'https://www.youtube.com/watch?v=aqz-KE-bpKQ' 2>$null | Select-Object -First 1) } catch {}
if (-not $URL -and (Test-Path (Join-Path $HERE 'yt-url.txt'))) { $URL = (Get-Content (Join-Path $HERE 'yt-url.txt') -Raw).Trim() }
$SAFE = @('general (ALT13)', 'general (EXP)', 'general (ALT12)', 'general (ALT9)', 'general (ALT)', 'general (ALT11)')
$CURRENT = 'general (ALT13)'
function L($m) { "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m | Out-File $LOG -Append -Encoding utf8 }

function Measure-Yt {
    $o = & curl.exe -s --noproxy '*' -r 0-24999999 -m 12 -o NUL -w '%{http_code} %{size_download} %{speed_download}' $URL
    $p = "$o".Split(' ')
    if ($p.Count -eq 3 -and $p[0] -in '200', '206') { return [math]::Round([double]$p[2] * 8 / 1e6, 1) }
    return 0
}
function Check-Discord {
    $o = & curl.exe -s --noproxy '*' -m 6 -o NUL -w '%{http_code}' 'https://discord.com/api/v9/gateway'
    return ("$o" -eq '200')
}
function Stop-Winws { Get-Process winws -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1 }

L '=== старт: какая настройка обхода быстрее грузит YouTube'
if (-not $URL) {
    L 'не смог получить ссылку на видео YouTube (нужна программа yt-dlp, её нет или она не ответила) — тест не начинаю, zapret не трогаю'; L '=== готово'
    Hod-Konec $false 'Нет ссылки на видео YouTube: нужна программа yt-dlp — zapret не трогал'
    exit
}
$hodOk = $false; $hodItog = ''   # итог для окна (ход)
try {
    Hod-Shag 1
    L 'снимаю службу zapret'
    & sc.exe stop zapret | Out-Null; Start-Sleep 2; & sc.exe delete zapret | Out-Null
    Stop-Winws
    $base = Measure-Yt
    L "без zapret вообще: YouTube $base Мбит/с, Discord $(Check-Discord)"

    $bats = Get-ChildItem -LiteralPath $Z -Filter '*.bat' | Where-Object { $_.Name -notlike 'service*' } |
        Sort-Object { [Regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) }
    $res = @{}
    Hod-Shag 2
    $hodN = 0
    foreach ($b in $bats) {
        Hod-Schet $hodN (@($bats).Count) ('Проверяю настройку {0} из {1}: {2}' -f ($hodN + 1), @($bats).Count, $b.BaseName)
        $hodN++
        Start-Process cmd.exe -ArgumentList "/c `"`"$($b.FullName)`"`"" -WorkingDirectory $Z -WindowStyle Hidden
        for ($i = 0; $i -lt 20 -and -not (Get-Process winws -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 300 }
        Start-Sleep 2
        $yt = Measure-Yt
        $dc = Check-Discord
        $res[$b.BaseName] = @($yt, $dc)
        L ("{0,-34} YouTube {1,6} Мбит/с   Discord {2}" -f $b.BaseName, $yt, $(if ($dc) { 'да' } else { 'НЕТ' }))
        Stop-Winws
    }

    $cur = $res[$CURRENT][0]
    $best = $SAFE | Where-Object { $res.ContainsKey($_) -and $res[$_][1] } | Sort-Object { -$res[$_][0] } | Select-Object -First 1
    $pick = $CURRENT
    if ($best -and $res[$best][0] -ge [math]::Max(40, 3 * $cur)) { $pick = $best }
    L "нынешняя ${CURRENT}: $cur Мбит/с; лучшая надёжная: $best ($($res[$best][0]) Мбит/с) → ставлю $pick"
    Hod-Shag 3 "Ставлю настройку обхода $pick"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $pick
    Start-Sleep 3
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne 'Running') {
        L 'служба не запустилась — возвращаю ALT13'
        Hod-Tekst "Служба не запустилась — возвращаю $CURRENT"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $CURRENT
        $hodItog = "Служба zapret с настройкой $pick не запустилась — вернул $CURRENT"
    }
    Start-Sleep 3
    Hod-Shag 4
    $hodSl = (Get-Service zapret).Status
    $hodSt = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret').'zapret-discord-youtube'
    $hodYt = Measure-Yt
    L ("итог: служба {0}, настройка обхода {1}, YouTube напрямую сейчас {2} Мбит/с" -f $hodSl, $hodSt, $hodYt)
    if (-not $hodItog) {
        if ("$hodSl" -eq 'Running') { $hodOk = $true; $hodItog = "Поставлена настройка обхода $hodSt — YouTube напрямую $hodYt Мбит/с" }
        else { $hodItog = "Служба zapret не работает — YouTube напрямую $hodYt Мбит/с" }
    }
} catch {
    L ("ошибка: " + $_)
    if (-not (Get-Service zapret -ErrorAction SilentlyContinue)) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $CURRENT
    }
    $hodItog = 'Подбор прервался из-за ошибки — подробности в zapret-youtube.log'
} finally {
    L '=== готово'
    Hod-Konec $hodOk $(if ($hodItog) { $hodItog } else { 'Подбор оборвался — подробности в zapret-youtube.log' })
}

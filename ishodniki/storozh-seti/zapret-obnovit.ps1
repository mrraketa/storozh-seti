# Обновление zapret (Flowseal/zapret-discord-youtube) с GitHub: новая версия — рядом со старой в «Загрузках»,
# списки *-user.txt переносятся, служба ставится с той же стратегией. Старая папка остаётся (откат — поставить её стратегию).
# Запуск с правами администратора (из окна «Сторож сети», кнопка «Обновить zapret»). Журнал: zapret-obnovit.log рядом.
# С параметром -Hod "<путь>" пишет ход для окна (шаги и итог); без него — как раньше.
param([string]$Hod)
$ErrorActionPreference = 'Stop'
$LOG = Join-Path $PSScriptRoot 'zapret-obnovit.log'
function L($m) { "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m | Out-File $LOG -Append -Encoding utf8 }
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
Hod-Nachat 'Обновление zapret' @('Узнаю новую версию на GitHub', 'Качаю zapret', 'Распаковываю и переношу ваши списки',
    'Ставлю службу новой версии') 'zapret-obnovit' @(4, 15, 5, 15)
$hodOk = $false; $hodItog = ''   # итог для окна (ход)
function Get-Zagruzki {
    # папка «Загрузки» — её можно перенести на другой диск, поэтому спрашиваем у Windows
    $p = $null
    try { $p = (New-Object -ComObject Shell.Application).NameSpace('shell:Downloads').Self.Path } catch { }
    if (-not $p -or -not (Test-Path -LiteralPath $p)) { $p = Join-Path $env:USERPROFILE 'Downloads' }
    return $p
}
$ZAG = Get-Zagruzki
New-Item -ItemType Directory -Force $ZAG | Out-Null
L '=== проверяю новую версию zapret'
try {
    $tek = Get-ChildItem $ZAG -Directory -Filter 'zapret-discord-youtube-*' | Where-Object { Test-Path (Join-Path $_.FullName 'service.bat') } |
        Sort-Object { [version](($_.Name -replace '^zapret-discord-youtube-', '') -replace '[^0-9.]', '') } | Select-Object -Last 1
    $tekVer = if ($tek) { [version](($tek.Name -replace '^zapret-discord-youtube-', '') -replace '[^0-9.]', '') } else { [version]'0.0' }
    L "сейчас стоит: $(if ($tek) { $tek.Name } else { 'нет' })"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $wc = New-Object Net.WebClient
    $wc.Headers.Add('User-Agent', 'storozh-seti')
    $wc.Proxy = [Net.WebRequest]::GetSystemWebProxy()   # GitHub из РФ — через Karing (системный прокси)
    $rel = $wc.DownloadString('https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest') | ConvertFrom-Json
    $novVer = [version](($rel.tag_name -replace '[^0-9.]', ''))
    L "на GitHub: $($rel.tag_name)"
    if ($novVer -le $tekVer) {
        L 'новее нет — ничего не делаю'; L '=== готово'
        Hod-Konec $true "Новее нет — у вас последняя версия zapret ($($rel.tag_name))"
        exit 0
    }
    $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) {
        L 'в выпуске нет zip-архива — не обновляю'; L '=== готово'
        Hod-Konec $false 'В выпуске zapret на GitHub нет архива — не обновлял'
        exit 1
    }
    $zip = Join-Path $env:TEMP $asset.name
    L "качаю $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) МБ)"
    Hod-Shag 1 "Качаю zapret $($rel.tag_name) ($([math]::Round($asset.size / 1MB, 1)) МБ)"
    $wc.DownloadFile($asset.browser_download_url, $zip)
    Hod-Shag 2
    $cel = Join-Path $ZAG ("zapret-discord-youtube-" + ($rel.tag_name -replace '^v', ''))
    if (Test-Path $cel) { $cel = $cel + '-' + (Get-Date -Format 'MMdd-HHmm') }
    $tmp = Join-Path $env:TEMP ('zapret-new-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $koren = Get-ChildItem $tmp -Recurse -Filter 'service.bat' | Select-Object -First 1
    if (-not $koren) {
        L 'в архиве нет service.bat — не обновляю'; L '=== готово'
        Hod-Konec $false 'В скачанном архиве zapret нет нужных файлов — не обновлял'
        exit 1
    }
    Move-Item $koren.DirectoryName $cel
    L "распаковал в $cel"
    if ($tek) {
        foreach ($f in Get-ChildItem $tek.FullName -Recurse -Filter '*-user.txt') {
            $rel2 = $f.FullName.Substring($tek.FullName.Length).TrimStart('\')
            $kuda = Join-Path $cel $rel2
            New-Item -ItemType Directory (Split-Path $kuda) -Force | Out-Null
            Copy-Item $f.FullName $kuda -Force
            L "перенёс список: $rel2"
        }
    }
    $strat = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret' -ErrorAction SilentlyContinue).'zapret-discord-youtube'
    if (-not $strat) { $strat = 'general (ALT13)' }
    if (-not (Test-Path (Join-Path $cel "$strat.bat"))) { $strat = (Get-ChildItem $cel -Filter 'general*.bat' | Select-Object -First 1).BaseName; L "такой настройки обхода в новой версии нет — беру $strat" }
    L "ставлю службу новой версии с настройкой обхода: $strat"
    Hod-Shag 3 "Ставлю службу новой версии с настройкой обхода $strat"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'zapret-avto.ps1') -Install $strat
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    L ("служба: {0}" -f $(if ($svc) { $svc.Status } else { 'нет' }))
    Remove-Item $zip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
    if ($svc -and "$($svc.Status)" -eq 'Running') { $hodOk = $true; $hodItog = "zapret обновлён до $($rel.tag_name), служба работает" }
    else { $hodItog = "zapret обновлён до $($rel.tag_name), но служба не запустилась — подберите настройку обхода" }
} catch {
    L ("ошибка: " + $_.Exception.Message)
    $hodItog = 'Не вышло обновить zapret — нет связи с GitHub или файл занят (подробности в zapret-obnovit.log)'
}
L '=== готово'
Hod-Konec $hodOk $(if ($hodItog) { $hodItog } else { 'Обновление оборвалось — подробности в zapret-obnovit.log' })

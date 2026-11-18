# Тест стратегий zapret на скорость видео YouTube напрямую (без VPN). Запуск с правами администратора.
# Ставит самую быструю из надёжных (прошли полный тест 36/36), если она заметно быстрее ALT13; иначе — ALT13.
$ErrorActionPreference = 'Continue'
# самая новая версия zapret в Загрузках (папка с service.bat)
$Z = (Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads') -Directory -Filter 'zapret-discord-youtube-*' | Where-Object { Test-Path (Join-Path $_.FullName 'service.bat') } |
    Sort-Object { [version](($_.Name -replace '^zapret-discord-youtube-', '') -replace '[^0-9.]', '') } | Select-Object -Last 1).FullName
$HERE = $PSScriptRoot
$LOG = Join-Path $HERE 'zapret-youtube.log'
if (-not $Z) {
    "[{0}] zapret не найден: в «Загрузках» нет папки zapret-discord-youtube-…. Нажмите в окне «Скачать и поставить zapret»." -f (Get-Date -Format 'HH:mm:ss') | Out-File $LOG -Append -Encoding utf8
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

L '=== старт теста стратегий на скорость YouTube'
if (-not $URL) { L 'не смог получить ссылку на видео YouTube (yt-dlp) — тест не начинаю, zapret не трогаю'; L '=== готово'; exit }
try {
    L 'снимаю службу zapret'
    & sc.exe stop zapret | Out-Null; Start-Sleep 2; & sc.exe delete zapret | Out-Null
    Stop-Winws
    $base = Measure-Yt
    L "без zapret вообще: YouTube $base Мбит/с, Discord $(Check-Discord)"

    $bats = Get-ChildItem -LiteralPath $Z -Filter '*.bat' | Where-Object { $_.Name -notlike 'service*' } |
        Sort-Object { [Regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) }
    $res = @{}
    foreach ($b in $bats) {
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
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $pick
    Start-Sleep 3
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    if (-not $svc -or $svc.Status -ne 'Running') {
        L 'служба не запустилась — возвращаю ALT13'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $CURRENT
    }
    Start-Sleep 3
    L ("итог: служба {0}, стратегия {1}, YouTube напрямую сейчас {2} Мбит/с" -f (Get-Service zapret).Status,
        (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret').'zapret-discord-youtube', (Measure-Yt))
} catch {
    L ("ошибка: " + $_)
    if (-not (Get-Service zapret -ErrorAction SilentlyContinue)) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $HERE 'zapret-avto.ps1') -Install $CURRENT
    }
} finally {
    L '=== готово'
}

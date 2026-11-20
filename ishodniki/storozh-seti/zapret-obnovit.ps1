# Обновление zapret (Flowseal/zapret-discord-youtube) с GitHub: новая версия — рядом со старой в «Загрузках»,
# списки *-user.txt переносятся, служба ставится с той же стратегией. Старая папка остаётся (откат — поставить её стратегию).
# Запуск с правами администратора (из окна «Сторож сети», кнопка «Обновить zapret»). Журнал: zapret-obnovit.log рядом.
$ErrorActionPreference = 'Stop'
$LOG = Join-Path $PSScriptRoot 'zapret-obnovit.log'
function L($m) { "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m | Out-File $LOG -Append -Encoding utf8 }
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
    if ($novVer -le $tekVer) { L 'новее нет — ничего не делаю'; L '=== готово'; exit 0 }
    $asset = $rel.assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) { L 'в выпуске нет zip-архива — не обновляю'; L '=== готово'; exit 1 }
    $zip = Join-Path $env:TEMP $asset.name
    L "качаю $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) МБ)"
    $wc.DownloadFile($asset.browser_download_url, $zip)
    $cel = Join-Path $ZAG ("zapret-discord-youtube-" + ($rel.tag_name -replace '^v', ''))
    if (Test-Path $cel) { $cel = $cel + '-' + (Get-Date -Format 'MMdd-HHmm') }
    $tmp = Join-Path $env:TEMP ('zapret-new-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $koren = Get-ChildItem $tmp -Recurse -Filter 'service.bat' | Select-Object -First 1
    if (-not $koren) { L 'в архиве нет service.bat — не обновляю'; L '=== готово'; exit 1 }
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
    if (-not (Test-Path (Join-Path $cel "$strat.bat"))) { $strat = (Get-ChildItem $cel -Filter 'general*.bat' | Select-Object -First 1).BaseName; L "такой стратегии в новой версии нет — беру $strat" }
    L "ставлю службу новой версии со стратегией: $strat"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'zapret-avto.ps1') -Install $strat
    $svc = Get-Service zapret -ErrorAction SilentlyContinue
    L ("служба: {0}" -f $(if ($svc) { $svc.Status } else { 'нет' }))
    Remove-Item $zip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    L ("ошибка: " + $_.Exception.Message)
}
L '=== готово'

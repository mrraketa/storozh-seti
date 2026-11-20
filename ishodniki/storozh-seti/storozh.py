"""Сторож сети: раз в 10 минут проверяет, что всё открывается, и при надобности
добавляет сайты в VPN-группу Karing.

Правила, которые он соблюдает:
- Claude всегда через VPN (проверяется каждый запуск);
- Karing перезапускается только когда не запущена игра и не чаще раза в час;
- после перезапуска проверка, при неудаче откат;
- всё, что сделано, пишется в otchet.txt простым языком.
"""
import csv, io, json, os, re, shutil, socket, subprocess, sys, time
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
STATE = os.path.join(HERE, "state.json")
REPORT = os.path.join(HERE, "otchet.txt")
BACKUP = os.path.join(HERE, "backup")
LOCK = os.path.join(HERE, "run.lock")

if HERE not in sys.path:   # встроенный Python не кладёт папку скрипта в sys.path — иначе hod.py не найдётся
    sys.path.append(HERE)
try:
    import hod   # ход долгих действий для окна (договор storozh-okno\DOGOVOR-hod.md); пишет, только если окно дало STOROZH_HOD
except Exception:   # выпуск без hod.py — ход просто не пишется, остальное работает как раньше
    import types
    hod = types.SimpleNamespace(nachat=lambda *a, **k: False, shag=lambda *a, **k: None, tekst=lambda *a, **k: None,
                                schet=lambda *a, **k: None, konec=lambda *a, **k: None, idet=lambda: False)

KD = os.path.join(os.environ.get("APPDATA") or os.path.expanduser(r"~\AppData\Roaming"), "karing", "karing")
KEXE = os.path.join(os.environ.get("ProgramW6432") or os.environ.get("ProgramFiles") or r"C:\Program Files",
                    "Karing", "karing.exe")   # обычное место; где Karing стоит на самом деле — karing_exe()
CORE = os.path.join(KD, "service_core.json")
KLOG = os.path.join(KD, "service_core.log")
KFILES = ["karing_subscribe_use.json", "karing_routing_group.json", "karing_setting.json"]


def _steam_papka():
    """Папка Steam: из реестра (у друга Steam может стоять на другом диске), иначе обычное место."""
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
            p = os.path.normpath(winreg.QueryValueEx(k, "SteamPath")[0])
        if os.path.isdir(p):
            return p
    except OSError:
        pass
    return r"C:\Program Files (x86)\Steam"


STEAM = _steam_papka()
STEAM_LOG = os.path.join(STEAM, "logs", "connection_log.txt")


def _porty_karing():
    """Порты Karing — из его же настроек: рабочий конфиг (mixed_in_rule / mixed_in_proxy), иначе karing_setting.json;
    у всех по умолчанию 3067 / 3066."""
    pr = pv = None
    try:
        with open(CORE, encoding="utf-8") as f:
            vh = {i.get("tag"): i.get("listen_port") for i in json.load(f).get("inbounds", [])}
        pr, pv = vh.get("mixed_in_rule"), vh.get("mixed_in_proxy")
    except Exception:
        pass
    if not (pr and pv):
        try:
            with open(os.path.join(KD, "karing_setting.json"), encoding="utf-8") as f:
                px = json.load(f).get("proxy") or {}
            pr, pv = pr or px.get("mixed_port"), pv or px.get("mixed_forword_port")
        except Exception:
            pass
    try:
        return int(pr or 3067), int(pv or 3066)
    except (TypeError, ValueError):
        return 3067, 3066


_PR, _PV = _porty_karing()
RULES = f"http://127.0.0.1:{_PR}"   # как ходят программы (по правилам)
VPN = f"http://127.0.0.1:{_PV}"     # принудительно через VPN
AUTO_GROUP = "🤖 Авто VPN"
DIRECT_GROUP = "↩️ Напрямую: сайты против VPN"  # сайты, которые отказывают адресам VPN (DDoS-Guard и т.п.), а напрямую работают
REFUSE = {"403", "429", "500", "502", "503", "521", "522"}
NOWIN = 0x08000000                # не показывать чёрные окна консоли
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0"

# Серверы установленных программ. Проверяются раз в час.
PROBES = [
    ("Discord", "https://discord.com/api/v9/gateway"),
    ("Telegram", "https://api.telegram.org/"),
    ("YouTube", "https://www.youtube.com/generate_204"),  # сама страница из РФ всегда тянется секундами, даже когда видео идёт на 300 Мбит/с
    ("Google", "https://accounts.google.com/"),
    ("Claude", "https://claude.ai/"),
    ("Steam вход", "https://cmp1-sto2.steamserver.net/"),
    ("Steam CDN", "https://cache1-fra1.steamcontent.com/"),
    ("Steam магазин", "https://store.steampowered.com/"),
    ("Steam сообщество", "https://steamcommunity.com/"),
    ("LM Studio", "https://huggingface.co/api/models?limit=1"),
    ("LM Studio", "https://lmstudio.ai/"),
    ("Perplexity", "https://www.perplexity.ai/"),
    ("NVIDIA", "https://www.nvidia.com/"),
    ("Adobe вход", "https://ims-na1.adobelogin.com/"),
    ("Ubisoft", "https://public-ubiservices.ubi.com/"),
    ("Microsoft", "https://login.live.com/"),
    ("Microsoft Store", "https://displaycatalog.mp.microsoft.com/"),
    ("Obsidian", "https://obsidian.md/"),
    ("GitHub", "https://github.com/"),
    ("npm", "https://registry.npmjs.org/"),
    ("pip", "https://pypi.org/simple/"),
    ("Vortex", "https://www.nexusmods.com/"),
    ("TLauncher", "https://tlauncher.org/"),
    ("Minecraft", "https://sessionserver.mojang.com/"),
    ("DaVinci", "https://www.blackmagicdesign.com/"),
    ("Tailscale", "https://controlplane.tailscale.com/"),
    ("Яндекс", "https://ya.ru/"),
    ("Госуслуги", "https://www.gosuslugi.ru/"),
]
# Домены, которые целиком в VPN не переносим — только конкретный адрес.
WIDE = {"microsoft.com", "windows.com", "windowsupdate.com", "google.com", "amazonaws.com",
        "cloudfront.net", "akamaihd.net", "akamaized.net", "azureedge.net", "apple.com",
        "googleapis.com", "gstatic.com", "live.com", "msn.com", "office.com", "yandex.net"}
# Эти адреса не ошибки, а служебный шум.
NOISE = re.compile(r"msftconnecttest|disabled\.invalid|\.local$|localhost|NXDOMAIN|empty result")
GAMES = {"dota2.exe", "cs2.exe", "javaw.exe", "cyberpunk2077.exe"}
NOT_GAMES = {"wallpaper_engine", "steamworks shared", "steamvr", "steam controller configs",
             "steamlinuxruntime", "proton - experimental"}


# ---------- вспомогательное ----------

def now():
    return datetime.now().strftime("%d.%m.%Y %H:%M")


def say(msg):
    try:
        with open(REPORT, "a", encoding="utf-8") as f:
            f.write(f"[{now()}] {msg}\n")
    except OSError:
        pass


def _python(okno=False):
    """python.exe (okno=False) или pythonw.exe (okno=True) рядом с тем Python, что запущен сейчас:
    у друга это встроенный Python в папке программы, а не системный."""
    p = os.path.join(os.path.dirname(sys.executable), "pythonw.exe" if okno else "python.exe")
    return p if os.path.exists(p) else sys.executable


def run(args, timeout=30):
    try:
        return subprocess.run(args, capture_output=True, text=True, timeout=timeout,
                              creationflags=NOWIN, encoding="utf-8", errors="replace",
                              env={**os.environ, "PYTHONIOENCODING": "utf-8"})
    except subprocess.TimeoutExpired:
        return None
    except OSError:   # нет такой программы (например, curl.exe на Windows старше 10 версии 1803)
        return None


def curl_net():
    """curl.exe есть во всех Windows 10 (с 1803) и 11; на более старых его нет — тогда сторож не может проверять сайты."""
    return shutil.which("curl.exe") is None


def curl(url, proxy, t=10):
    r = run(["curl.exe", "-s", "-m", str(t), "-A", UA, "-o", "NUL", "-w", "%{http_code} %{time_total}",
             "-x", proxy, url], timeout=t + 5)
    p = (r.stdout.split() if r else [])
    return (p[0], float(p[1])) if len(p) == 2 else ("000", float(t))


def direct(url, t=10):
    r = run(["curl.exe", "-s", "-m", str(t), "-A", UA, "-o", "NUL", "-w", "%{http_code} %{time_total}",
             "--noproxy", "*", url], timeout=t + 5)
    p = (r.stdout.split() if r else [])
    return (p[0], float(p[1])) if len(p) == 2 else ("000", float(t))


def ok(code):
    return code not in ("000", "")


NASTR_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "nastroyki.json")
NASTR_DEFAULT = {
    "avto_server": True,      # ночью мерить серверы и ставить самый быстрый
    "avto_sayty": True,       # не открылся сайт напрямую — сам добавить в VPN
    "avto_spiski": True,      # раз в сутки обновлять списки блокировок
    "zapret_slezhka": True,   # следить за службой zapret
    "ne_v_igre": True,        # не перезапускать Karing во время игр
    "zvonok_min": 20,         # звонок в Discord держит правки не дольше N минут (0 — не ждать)
    "marshruty": {},          # программа.exe -> "vpn" | "direct" | "block" | имя сервера
    "claude_strana": "",      # закрепить Claude за сервером (имя из Karing); пусто — Claude идёт как всё остальное
    "zashchita_claude": False,  # следить за правилами брандмауэра «Claude только через Karing»
}


def opt(key):
    d = dict(NASTR_DEFAULT)
    try:
        d.update(json.load(open(NASTR_FILE, encoding="utf-8")))
    except (OSError, ValueError):
        pass
    return d.get(key)


def load_json(path, default):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def save_json(path, obj):
    tmp = f"{path}.{os.getpid()}.tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    for i in range(20):  # файл может быть занят другим процессом сторожа — подождать и повторить
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            time.sleep(0.25 * (i + 1))
    os.replace(tmp, path)


def port_up(port=None):
    port = port or _PR
    s = socket.socket(); s.settimeout(1)
    try:
        s.connect(("127.0.0.1", port)); return True
    except OSError:
        return False
    finally:
        s.close()


def procs():
    r = run(["tasklist", "/FO", "CSV", "/NH"])
    if not r:
        return set()
    return {row[0].lower() for row in csv.reader(io.StringIO(r.stdout)) if row}


def steam_game_exes(state):
    """Имена exe всех игр из библиотек Steam (кешируется на сутки)."""
    cache = state.get("game_exes")
    if cache and time.time() - state.get("game_exes_ts", 0) < 86400:
        return set(cache)
    libs = [os.path.join(STEAM, "steamapps")]
    vdf = os.path.join(STEAM, "steamapps", "libraryfolders.vdf")
    try:
        for m in re.finditer(r'"path"\s+"([^"]+)"', open(vdf, encoding="utf-8", errors="replace").read()):
            libs.append(os.path.join(m.group(1).replace("\\\\", "\\"), "steamapps"))
    except OSError:
        pass
    names = set()
    for lib in set(libs):
        common = os.path.join(lib, "common")
        for root, dirs, files in os.walk(common):
            if root == common:  # всегда работающие программы — не игры
                dirs[:] = [d for d in dirs if d.lower() not in NOT_GAMES]
            if root.count(os.sep) - common.count(os.sep) > 5:
                dirs[:] = []
                continue
            names.update(f.lower() for f in files if f.lower().endswith(".exe"))
    names -= {"unitycrashhandler64.exe", "unitycrashhandler32.exe", "crashreporter.exe"}
    state["game_exes"], state["game_exes_ts"] = sorted(names), time.time()
    return names


def in_discord_call():
    """Он сейчас в голосовом канале Discord: в журнале Discord свежий heartbeat голосового сервера."""
    log = os.path.join(os.path.expanduser("~"), "AppData", "Roaming", "discord", "logs", "renderer_js.log")
    try:
        with open(log, "rb") as f:
            f.seek(0, 2)
            f.seek(max(0, f.tell() - 60000))
            tail = f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return False
    for line in reversed(tail):
        if "RTCControlSocket(default)] Heartbeat ACK" in line:
            try:
                ts = datetime.strptime(line[1:20], "%Y-%m-%d %H:%M:%S")
            except ValueError:
                return False
            return (datetime.now() - ts).total_seconds() < 120
        if "RTCControlSocket(default)] CLOSE" in line or "VOICE_DISCONNECT" in line:
            return False
    return False


def game_running(state):
    """Игры — и звонок в Discord: в это время Karing не перезапускаем без крайней нужды."""
    running = procs()
    # служебные exe, которые лежат в папках игр Steam, но игрой не являются: python.exe — это сам сторож
    # и ретранслятор NightCoop (25.10: из-за него сторож сутки «ждал конца игры» и не применял правки)
    not_games = {"python.exe", "pythonw.exe", "crashpad_handler.exe", "cefsharp.browsersubprocess.exe",
                 "redengineerrorreporter.exe", "driverversionchecker.exe", "layerschecker.exe",
                 "windowsdesktop-runtime-6.0.11-win-x64.exe"}
    if not opt("ne_v_igre"):
        return []
    hit = sorted((running & (GAMES | steam_game_exes(state))) - not_games)
    if in_discord_call() and call_still_waits():
        hit.append("звонок в Discord")
    return hit


CALL_WAIT = os.path.join(HERE, "zvonok_zhdu.txt")
CALL_MAX_WAIT = 20 * 60


def call_still_waits():
    """18.11: он сидит в Discord круглые сутки — звонок держит правки не дольше 20 минут подряд,
    потом Karing перезапускается (звонок моргнёт секунд на 10). Отметка начала ожидания — в файле."""
    try:
        since = float(open(CALL_WAIT).read())
    except (OSError, ValueError):
        since = time.time()
        try:
            open(CALL_WAIT, "w").write(str(since))
        except OSError:
            pass
    if time.time() - since < (opt("zvonok_min") or 0) * 60:
        return True
    try:
        os.remove(CALL_WAIT)   # отождали — следующая правка снова подождёт 20 минут
    except OSError:
        pass
    return False


def sovet(dlya_nego, dlya_drugih):
    """Подсказка в окошке: у него (Claude закреплён) — «напишите Claude …», у друга — где нажать в окне сторожа."""
    return dlya_nego if CLAUDE_PIN else dlya_drugih


def zapret_ustanovlen(zq=None):
    """Служба zapret вообще есть (работает или нет). sc отвечает кодом 1060, если службы нет."""
    zq = zq if zq is not None else run(["sc", "query", "zapret"])
    return bool(zq) and zq.returncode != 1060 and "1060" not in (zq.stdout or "")


def suffix(host):
    parts = host.lower().strip(".").split(".")
    base = ".".join(parts[-2:])
    return host.lower() if base in WIDE else base


# ---------- Karing ----------

def karing_kill():
    for p in ("karing.exe", "karingService.exe"):
        run(["taskkill", "/F", "/IM", p])
    time.sleep(2)


class KaringNet(Exception):
    """Понятная человеку причина, почему с Karing сейчас ничего не сделать."""


def karing_exe():
    """Где стоит karing.exe: из service.json самого Karing (work_dir = <папка Karing>\\data), иначе обычные места."""
    sj = load_json(os.path.join(KD, "service.json"), {}) or {}
    mesta = []
    if sj.get("work_dir"):
        mesta.append(os.path.join(os.path.dirname(os.path.normpath(sj["work_dir"])), "karing.exe"))
    mesta += [KEXE, os.path.join(os.environ.get("ProgramFiles(x86)") or r"C:\Program Files (x86)", "Karing", "karing.exe"),
              os.path.join(os.environ.get("LOCALAPPDATA") or "", "Programs", "Karing", "karing.exe")]
    return next((p for p in mesta if p and os.path.isfile(p)), None)


def karing_beda():
    """Почему Karing сейчас не работает — словами для человека (None — работает)."""
    if not os.path.isdir(KD) and not karing_exe():
        return "Karing не установлен — установите Karing и добавьте в него подписку"
    if not os.path.exists(CORE):
        return "Karing ещё ни разу не подключался — откройте Karing и включите в нём подключение"
    if "karing.exe" not in procs():
        return "Karing не запущен — запустите его и включите подключение"
    if not port_up():
        return "Karing открыт, но подключение в нём выключено — включите его"
    return None


def perezapusk_nelzya():
    """Почему Karing сейчас нельзя перезапускать (None — можно). Без этой проверки у друга после перезапуска VPN
    мог бы остаться выключенным: Karing не найден или в нём выключено автоподключение после запуска."""
    if not karing_exe():
        return "не нашёл программу Karing (karing.exe) — перезапустить её не смогу"
    st = load_json(os.path.join(KD, "karing_setting.json"), {}) or {}
    if st.get("auto_connect_after_launch") is False:
        return ("в Karing выключено автоматическое подключение после запуска — после перезапуска VPN остался бы "
                "выключенным. Включите его в настройках Karing и повторите")
    return None


def karing_start():
    """Через Проводник, чтобы Karing не зависел от сторожа. False — karing.exe не найден."""
    exe = karing_exe()
    if not exe:
        return False
    subprocess.Popen(["explorer.exe", exe], creationflags=NOWIN)
    return True


def karing_wait(t0):
    for _ in range(60):
        try:
            fresh = os.path.getmtime(CORE) > t0
        except OSError:
            fresh = False
        if fresh and port_up():
            time.sleep(4)
            return True
        time.sleep(1)
    return False


CLAUDE_PROVERKA = os.path.join(HERE, "claude-proverka.json")   # последний результат проверки Claude — для значка в окне


def claude_ok():
    code, _ = curl("https://api.anthropic.com/v1/messages", RULES)
    otvet = code not in ("000", "403")
    _zapisat_tiho(CLAUDE_PROVERKA, {"ok": otvet, "kogda": time.time()})
    return otvet


def _zapisat_tiho(path, obj):
    """Записать маленький файл атомарно и без ожиданий; не вышло — не беда (это только для окна)."""
    tmp = f"{path}.{os.getpid()}.tmp"
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False)
        os.replace(tmp, path)
    except OSError:
        try:
            os.remove(tmp)
        except OSError:
            pass


def claude_vedu():
    """Сторож следит за Claude, только если об этом просили: Claude закреплён за сервером (настройка «claude_strana»)
    или в Karing есть своя группа «Claude». У друга по умолчанию нет ни того, ни другого — и проверять Claude
    там бессмысленно: напрямую из РФ он отвечает 403, и любая правка откатывалась бы."""
    if CLAUDE_PIN:
        return True
    use = load_json(os.path.join(KD, "karing_subscribe_use.json"), {}) or {}
    return any(g.get("diversion_name") == "Claude" for g in use.get("diversion_group", []) or [])


def vpn_ok():
    """Сам VPN работает: через порт «всё через VPN» открывается проверочная страница Google."""
    code, _ = curl("https://www.gstatic.com/generate_204", VPN)
    return ok(code)


def svyaz_ok():
    """Главная проверка после перезапуска: у него — Claude через Англию, у остальных — сам VPN."""
    return claude_ok() if claude_vedu() else vpn_ok()


def verify_after_restart():
    checks = {}
    if claude_vedu():
        checks["Claude"] = claude_ok()
    else:
        checks["VPN"] = vpn_ok()
    if os.path.isdir(STEAM):
        checks["Steam вход"] = ok(curl("https://cmp1-sto2.steamserver.net/", RULES)[0])
    checks["Яндекс"] = ok(curl("https://ya.ru/", RULES)[0])
    return all(checks.values()), checks


def _zapomnit():
    """Копия файлов Karing перед правкой: в папку backup и в память. Файла не было — запомнить и это,
    чтобы при откате удалить созданный (у свежего Karing части файлов может не быть)."""
    os.makedirs(BACKUP, exist_ok=True)
    saved = {}
    for n in KFILES:
        p = os.path.join(KD, n)
        try:
            with open(p, "rb") as f:
                saved[n] = f.read()
            shutil.copy2(p, os.path.join(BACKUP, n))
        except FileNotFoundError:
            saved[n] = None
    return saved


def _vernut(saved):
    for n, b in saved.items():
        p = os.path.join(KD, n)
        try:
            if b is None:
                if os.path.exists(p):
                    os.remove(p)
            else:
                with open(p, "wb") as f:
                    f.write(b)
        except OSError:
            pass


def gruppy(rg):
    """Список групп «Самостоятельная настройка» из karing_routing_group.json. У свежего Karing его может не быть
    (items пустой) — тогда создаётся такой же, какой делает сам Karing."""
    items = rg.setdefault("items", [])
    it = next((x for x in items if x.get("groupid") == "custom"), None)
    if it is None:
        it = {"groupid": "custom", "urlOrPath": "", "remark": "Самостоятельная настройка", "editAble": True, "groups": []}
        items.insert(0, it)
    return it.setdefault("groups", [])


def set_group(groups, use, name, suffixes):
    """Создать/заменить группу доменов и назначить её на VPN."""
    groups[:] = [g for g in groups if g["name"] != name]
    if suffixes:
        groups.append({"groupid": "custom", "name": name, "type": "", "or": True,
                       "domain_suffix": sorted(suffixes)})
    use["diversion_group"] = [g for g in use.get("diversion_group", []) if g.get("diversion_name") != name]
    if suffixes:
        tgt = target_server()
        use["diversion_group"].append({"diversion_groupid": "custom", "diversion_name": name,
                                       "server_groupid": sub_id(use, tgt), "server_name": tgt,
                                       "dns_servers": []})


ROUTE_PREFIX = "🧭 "


def set_routes(groups, use, routes):
    """Маршруты по программам: группа «🧭 имя.exe» с process_name_windows, сразу после Claude."""
    groups[:] = [g for g in groups if not g.get("name", "").startswith(ROUTE_PREFIX)]
    use["diversion_group"][:] = [g for g in use["diversion_group"] if not g.get("diversion_name", "").startswith(ROUTE_PREFIX)]
    pos_g = next((i + 1 for i, g in enumerate(groups) if g.get("name") == "Claude"), 0)
    pos_u = next((i + 1 for i, g in enumerate(use["diversion_group"]) if g.get("diversion_name") == "Claude"), 0)
    for exe, tgt in sorted(routes.items(), reverse=True):
        name = ROUTE_PREFIX + exe
        groups.insert(pos_g, {"groupid": "custom", "name": name, "type": "", "or": True,
                              "process_name_windows": sorted({exe, exe.lower()})})
        if tgt == "direct":
            sg, sn = "direct", "direct_out"
        elif tgt == "block":
            sg, sn = "block", "block_out"
        else:
            sn = role_server("fast") if tgt == "vpn" else tgt
            sg = sub_id(use, sn)
        use["diversion_group"].insert(pos_u, {"diversion_groupid": "custom", "diversion_name": name,
                                              "server_groupid": sg, "server_name": sn, "dns_servers": []})


# Шаги любой правки Karing — пульт ставит их в список шагов своих действий (ход для окна), а apply_changes
# и switch_roles отмечают их по имени. Нет такого шага в списке действия — меняется только подпись.
KARING_SHAGI = ["Записываю настройки Karing", "Перезапускаю Karing", "Жду, пока Karing подключится", "Проверяю, что всё открывается"]
KARING_OTKAT = "Не заработало — возвращаю прежние настройки Karing"


def apply_changes(add_suffixes, fix_invariants, list_groups=None, add_direct=None, routes=None, remove=None):
    """Правит файлы Karing и перезапускает его. Возвращает (успех, описание).
    list_groups: {имя группы: множество доменов} — группы из списков блокировок, заменяются целиком."""
    if not os.path.exists(CORE):
        return False, karing_beda() or "Karing не найден"
    if curl_net():
        return False, "нет программы curl.exe (она есть в Windows 10 и 11) — без неё не проверить, что после правки всё работает"
    nelzya = perezapusk_nelzya()   # Karing закрыт, но найден — запустим его сами, как раньше
    if nelzya:
        return False, nelzya
    vedu_claude = claude_vedu()
    hod.shag(KARING_SHAGI[0])
    saved = _zapomnit()
    karing_kill()
    try:
        rg = load_json(os.path.join(KD, "karing_routing_group.json"), None) or {"items": [], "rule_set_items": []}
        use = load_json(os.path.join(KD, "karing_subscribe_use.json"), None) or {}
        st = load_json(os.path.join(KD, "karing_setting.json"), None) or {}
        use.setdefault("diversion_group", [])
        groups = gruppy(rg)
        if routes is not None:
            set_routes(groups, use, routes)
        if remove:  # «Вернуть» из окна: убрать сайт из групп, куда его добавил сторож
            for g in groups:
                if g.get("name") in (AUTO_GROUP, DIRECT_GROUP):
                    g["domain_suffix"] = [x for x in g.get("domain_suffix", []) if x not in set(remove)]
        for name, doms in (list_groups or {}).items():
            set_group(groups, use, name, doms)
        if add_direct:
            dg = next((g for g in groups if g.get("name") == DIRECT_GROUP), None)
            if not dg:
                dg = {"groupid": "custom", "name": DIRECT_GROUP, "type": "", "or": True, "domain_suffix": []}
                pos = next((i + 1 for i, g in enumerate(groups) if g.get("name", "").startswith("▶️")), 1)
                groups.insert(pos, dg)
                use["diversion_group"].insert(1, {"diversion_groupid": "custom", "diversion_name": DIRECT_GROUP,
                                                  "server_groupid": "direct", "server_name": "direct_out",
                                                  "dns_servers": []})
            dg["domain_suffix"] = sorted(set(dg.get("domain_suffix", [])) | set(add_direct))
        for g in groups:  # Claude — только через VPN: убрать его домены из всех групп «напрямую»
            if g.get("name") == DIRECT_GROUP or g.get("name", "").startswith("▶️"):
                g["domain_suffix"] = [x for x in g.get("domain_suffix", []) if not NEVER_DIRECT.search(x)]
        auto = next((g for g in groups if g.get("name") == AUTO_GROUP), None)
        if add_suffixes:
            if not auto:
                auto = {"groupid": "custom", "name": AUTO_GROUP, "type": "", "or": True, "domain_suffix": []}
                groups.append(auto)
            auto["domain_suffix"] = sorted(set(auto.get("domain_suffix", [])) | set(add_suffixes))
            for g in groups:  # сайт перенесён в VPN — убрать его из «напрямую», иначе то правило стоит выше и VPN не срабатывает
                if g.get("name") == DIRECT_GROUP:
                    g["domain_suffix"] = [x for x in g.get("domain_suffix", []) if x not in set(add_suffixes)]
        if auto and not any(g.get("diversion_name") == AUTO_GROUP for g in use["diversion_group"]):
            tgt = target_server()
            use["diversion_group"].append({"diversion_groupid": "custom", "diversion_name": AUTO_GROUP,
                                           "server_groupid": sub_id(use, tgt), "server_name": tgt,
                                           "dns_servers": []})
        if fix_invariants:
            st["proxy_all"] = False
            dns = st.setdefault("dns", {})
            dns["enable_inbound_domain_resolve"] = True
            dns["ttl"] = DNS_TTL
            if vedu_claude:   # у друга Claude не ведём — его группы не трогаем и не создаём
                cs = role_server("claude")
                for g in use["diversion_group"]:
                    if g.get("diversion_name") == "Claude":
                        g["server_groupid"], g["server_name"] = sub_id(use, cs), cs
                for g in groups:
                    if g.get("name") == "Claude":
                        g["domain_suffix"] = sorted(set(g.get("domain_suffix", [])) | CLAUDE_SUFFIXES)
                # Claude — первым правилом, иначе claude.ai перехватывают GFW/Cloudflare
                groups.sort(key=lambda x: 0 if x.get("name") == "Claude" else 1)
                use["diversion_group"].sort(key=lambda x: 0 if x.get("diversion_name") == "Claude" else 1)
        save_json(os.path.join(KD, "karing_routing_group.json"), rg)
        save_json(os.path.join(KD, "karing_subscribe_use.json"), use)
        save_json(os.path.join(KD, "karing_setting.json"), st)
    except Exception as e:
        hod.tekst("Не вышло записать — возвращаю прежние настройки Karing")
        _vernut(saved)
        karing_start()
        prichina = "файл занят другой программой или только для чтения" if isinstance(e, OSError) else "настройки Karing в непривычном виде"
        return False, f"не смог записать настройки Karing ({prichina}) — вернул как было"
    t0 = time.time()
    hod.shag(KARING_SHAGI[1])
    karing_start()
    hod.shag(KARING_SHAGI[2])
    good = karing_wait(t0)
    hod.shag(KARING_SHAGI[3])
    passed, checks = verify_after_restart() if good else (False, {"Karing запустился": False})
    if passed:
        return True, "проверка после перезапуска: " + ", ".join(f"{k} — да" for k in checks)
    hod.tekst(KARING_OTKAT)
    karing_kill()
    _vernut(saved)
    t1 = time.time()
    karing_start()
    karing_wait(t1)
    bad = ", ".join(k for k, v in checks.items() if not v)
    return False, f"после перезапуска не заработало: {bad}. Вернул прежние настройки"


def idle_minutes():
    """Сколько минут он не трогал мышь и клавиатуру."""
    import ctypes
    class LII(ctypes.Structure):
        _fields_ = [("cbSize", ctypes.c_uint), ("dwTime", ctypes.c_uint)]
    li = LII(); li.cbSize = ctypes.sizeof(LII)
    if not ctypes.windll.user32.GetLastInputInfo(ctypes.byref(li)):
        return 0
    return (ctypes.windll.kernel32.GetTickCount() - li.dwTime) / 60000


def popup(text):
    """Окошко-напоминание отдельным процессом — сторож не ждёт, пока его закроют."""
    code = f"import ctypes; ctypes.windll.user32.MessageBoxW(0, {text!r}, 'Сторож сети', 0x40 | 0x40000)"
    subprocess.Popen([_python(okno=True), "-c", code], creationflags=NOWIN)


def manual_global():
    """Он сам переключил Karing на «Глобально» (например, для входа в аккаунт) — ничего не трогать."""
    if load_json(os.path.join(KD, "karing_setting.json"), {}).get("proxy_all") is True:
        return True
    # второй признак — рабочий конфиг: наших правил нет, а всё уходит на сервер, а не напрямую
    route = load_json(CORE, {}).get("route", {})
    ours = any("[Самостоятельная настройка]" in r.get("name", "") for r in route.get("rules", []))
    return bool(route) and not ours and route.get("final") not in (None, "direct_out")


def global_not_britain():
    if not CLAUDE_PIN:
        return
    """В «Глобально» Claude идёт через тот сервер, что выбран в Karing. Не Англия — тревога (его слово 19.10:
    «если Claude не через Британию — пусть у него вообще не будет интернета»). Сам режим не трогаем — только окошко."""
    if not manual_global():
        return
    srv = load_json(CORE, {}).get("route", {}).get("final")
    if not srv or srv == CLAUDE_PIN:
        return
    st = load_json(STATE, {})
    if time.time() - st.get("global_pin_popup", 0) > 600:
        st["global_pin_popup"] = time.time()
        save_json(STATE, st)
        say(f"Karing в «Глобально» через {srv}, а не через {CLAUDE_PIN} — Claude сейчас идёт не через Британию.")
        popup(f"Karing в «Глобально» через {srv}.\nClaude сейчас идёт НЕ через Британию. Выберите в Karing сервер {CLAUDE_PIN}.")


FW_GROUP = "Claude только через Karing"
FW_BAT = os.path.join(HERE, "claude-tolko-cherez-karing.bat")
DNS_TTL = 600  # 21.10: было 43200 (12 ч) — Karing держал мёртвый узел Akamai, магазин Steam не грузился
FW_TASK = "Claude tolko cherez Karing"


def firewall_gaps():
    if not opt("zashchita_claude"):
        return
    """Раз в час: все ли программы Claude закрыты правилом «только через Karing». После обновления Claude
    появляется новый claude.exe без правила — сам правило ставить не могу (это настройки защиты Windows),
    поэтому окошко: запустить claude-tolko-cherez-karing.bat."""
    st = load_json(STATE, {})
    if time.time() - st.get("fw_check_ts", 0) < 3600:
        return
    st["fw_check_ts"] = time.time()
    _LA = (os.environ.get("LOCALAPPDATA") or os.path.expanduser(r"~\AppData\Local")).replace("'", "''")
    ps = ("$r=@(Get-NetFirewallRule -Group '" + FW_GROUP + "' -ErrorAction SilentlyContinue | "
          "Get-NetFirewallApplicationFilter | ForEach-Object Program);"
          "if(-not $r){'NORULES'};$r;'---';"
          "foreach($p in Get-AppxPackage -Name 'Claude*'){Get-ChildItem $p.InstallLocation -Recurse "
          "-Include 'Claude.exe','cowork-svc.exe' -File -EA SilentlyContinue|ForEach-Object FullName};"
          f"foreach($cc in '{_LA}\\Claude-Data',"
          f"'{_LA}\\Packages\\Claude_pzs8sxrjxfjjc\\LocalCache\\Roaming\\Claude\\claude-code'){{"
          "if(Test-Path $cc){Get-ChildItem $cc -Recurse -Filter claude.exe -File|ForEach-Object FullName}}")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True,
                             encoding="utf-8", errors="replace", timeout=60, creationflags=NOWIN).stdout
    except Exception:
        save_json(STATE, st)
        return
    rules_part, _, exe_part = out.partition("---")
    # задание работает от системы и обычному пользователю не видно — живость по его отметке (раз в 5 мин)
    alive = os.path.join(HERE, "zashchita.alive")
    no_task = not os.path.exists(alive) or time.time() - os.path.getmtime(alive) > 1200
    covered = {l.strip().lower() for l in rules_part.splitlines() if l.strip() and l.strip() not in ("NORULES", "NOTASK")}
    exes = {l.strip() for l in exe_part.splitlines() if l.strip()}
    gaps = sorted(p for p in exes if p.lower() not in covered)
    # с 21.10 правила подстраивает задание Windows (раз в 5 мин) — окошко только если задания нет
    # или дыра держится дольше 20 минут (задание сломалось)
    since = st.get("fw_gap_since") or time.time() if gaps else None
    st["fw_gaps"], st["fw_gap_since"] = gaps, since
    broken = no_task or (gaps and time.time() - since > 1200)
    if broken and time.time() - st.get("fw_popup", 0) > 6 * 3600:
        st["fw_popup"] = time.time()
        why = "задание Windows не отмечалось больше 20 мин" if no_task else f"{len(gaps)} программ(ы) без правила дольше 20 мин"
        say(f"Защита «Claude только через Karing» не работает: {why}. " + "; ".join(gaps))
        popup("Защита Claude не работает — новая версия может выйти в интернет мимо Британии.\n"
              f"Запустите один раз {FW_BAT}\n(Windows спросит разрешение — нажмите «Да»).")
    save_json(STATE, st)


PROXY_FILE = os.path.join(HERE, "proksi.json")


def uk_proxy_watch():
    """Раз в 10 минут: где сейчас личный прокси PROXY6 (proksi.json). Ждём замену бразильского адреса на британский.
    Окошко — когда прокси стал британским (можно подключать) или перестал отвечать (заменили — новые данные в кабинете)."""
    cfg = load_json(PROXY_FILE, None)
    if not cfg or cfg.get("connected") or cfg.get("off"):
        return
    st = load_json(STATE, {})
    if time.time() - st.get("uk_proxy_ts", 0) < 600:
        return
    st["uk_proxy_ts"] = time.time()
    px = f"http://{cfg['user']}:{cfg['password']}@{cfg['host']}:{cfg['port']}"
    try:
        out = subprocess.run(["curl", "-s", "-m", "20", "-x", px, "https://www.cloudflare.com/cdn-cgi/trace"],
                             capture_output=True, text=True, timeout=30, creationflags=NOWIN).stdout
    except Exception:
        out = ""
    loc = next((l[4:] for l in out.splitlines() if l.startswith("loc=")), None)
    prev = st.get("uk_proxy_loc")
    st["uk_proxy_loc"] = loc or "нет ответа"
    if st["uk_proxy_loc"] != prev:
        if loc == "GB":
            say(f"Личный прокси {cfg['host']} теперь британский (Cloudflare loc=GB) — можно подключать Claude.")
            popup("PROXY6 заменили прокси — теперь он британский.\nНапишите Claude «прокси британский, подключай».")
        elif loc is None and prev not in (None, "нет ответа"):
            say(f"Личный прокси {cfg['host']} перестал отвечать — видимо, PROXY6 выдали новый адрес.")
            popup("Прокси PROXY6 перестал отвечать — видимо, его заменили.\nНапишите Claude «проверь новый прокси».")
        elif prev is not None:
            say(f"Личный прокси {cfg['host']}: страна по Cloudflare {loc or '—'} (было {prev}).")
    save_json(STATE, st)


def invariants_broken():
    st = load_json(os.path.join(KD, "karing_setting.json"), {}) or {}
    use = load_json(os.path.join(KD, "karing_subscribe_use.json"), {}) or {}
    bad = []
    dns = st.get("dns") or {}
    if (dns.get("ttl") or 0) > DNS_TTL:
        srok = f"{dns['ttl'] // 3600} ч" if dns["ttl"] >= 3600 else f"{dns['ttl'] // 60} мин"
        bad.append(f"Karing слишком долго помнит адреса сайтов ({srok}) — магазин Steam и другие сайты могут не грузиться")
    if not dns.get("enable_inbound_domain_resolve"):
        bad.append("в Karing выключено определение адресов сайтов — часть правил не срабатывает")
    if not claude_vedu():
        return bad   # у друга Claude не закреплён и своей группы нет — правила про Claude не проверяем
    cl = [g for g in use.get("diversion_group", []) if g.get("diversion_name") == "Claude"]
    if not cl or cl[0].get("server_groupid") == "direct":
        bad.append("Claude назначен напрямую, а не через VPN")
    elif cl[0].get("server_name") != role_server("claude"):
        bad.append(f"Claude назначен на {cl[0].get('server_name')}, а должен на {role_server('claude')}")
    # по рабочему конфигу: первое VPN-правило из наших групп должно быть Claude
    rules = [r for r in load_json(CORE, {}).get("route", {}).get("rules", [])
             if "[Самостоятельная настройка]" in r.get("name", "")]
    if rules and not rules[0].get("name", "").startswith("Claude"):
        bad.append("правило Claude стоит не первым — claude.ai может уйти не через Англию")
    rg = load_json(os.path.join(KD, "karing_routing_group.json"), {})
    cg = [g for g in (rg.get("items") or [{}])[0].get("groups", []) if g.get("name") == "Claude"]
    miss = CLAUDE_SUFFIXES - set(cg[0].get("domain_suffix", [])) if cg else set()
    if miss:
        bad.append(f"в группе Claude не хватает адресов: {', '.join(sorted(miss))}")
    for g in (rg.get("items") or [{}])[0].get("groups", []):
        if g.get("name") == DIRECT_GROUP or g.get("name", "").startswith("▶️"):
            hit = [x for x in g.get("domain_suffix", []) if NEVER_DIRECT.search(x)]
            if hit:
                bad.append(f"домены Claude в группе «напрямую»: {', '.join(hit)}")
    return bad


# ---------- поиск проблем ----------

def log_failures(state):
    """Новые ошибки прямых подключений из журнала Karing с прошлого запуска."""
    try:
        size = os.path.getsize(KLOG)
    except OSError:
        return {}
    pos = state.get("log_pos", size)
    if pos > size:
        pos = 0
    fails = {}
    with open(KLOG, "rb") as f:
        f.seek(pos)
        data = f.read()
    state["log_pos"] = pos + len(data)
    for line in data.decode("utf-8", "replace").splitlines():
        m = re.search(r"open connection to (\S+?):(\d+) using outbound/direct\[direct_out\]: (.*)", line)
        if not m or NOISE.search(line):
            continue
        host = m.group(1)
        if re.fullmatch(r"[\d.]+|\[?[0-9a-f:]+\]?", host):
            continue  # голый IP — домен не узнать
        fails[host] = fails.get(host, 0) + 1
    return fails


def needs_vpn(host):
    """По текущим правилам не работает или тормозит, а через VPN работает.
    Если адрес уже идёт через VPN, правила и VPN совпадут — ничего не добавится."""
    url = f"https://{host}/"
    if YT_DIRECT.search(host):  # 30.10: страница YouTube медленная и при рабочем zapret — проверяем пустой ответ, как плеер
        url = f"https://{host}/generate_204"
    r1, rt1 = curl(url, RULES)
    v, vt = curl(url, VPN)
    if not ok(v):
        return False, (f"{host}: не открывается и через VPN — дело не в маршруте" if not ok(r1) else "")
    if not ok(r1):
        time.sleep(2)
        r2, _ = curl(url, RULES)  # второй шанс, чтобы не ловить случайность
        if not ok(r2):
            return True, f"{host}: напрямую не открывается, через VPN открывается"
    if rt1 > 3 and vt < 1.5:
        time.sleep(2)
        r2, rt2 = curl(url, RULES)
        if rt2 > 3:
            return True, f"{host}: напрямую тормозит ({rt2:.1f} с), через VPN {vt:.1f} с"
    return False, ""


# ---------- списки блокировок РФ ----------

LISTS_DIR = os.path.join(HERE, "spiski")
SERVICES_GROUP = "🚫 РФ: сервисы"
REGISTRY_GROUP = "🚫 РФ: реестр РКН"
SOURCES = {
    "itdog-inside.lst": "https://raw.githubusercontent.com/itdoginfo/allow-domains/main/Russia/inside-raw.lst",
    "refilter-community.lst": "https://raw.githubusercontent.com/1andrevich/Re-filter-lists/main/community.lst",
    "antifilter-community.lst": "https://community.antifilter.download/list/domains.lst",
    "refilter-domains.lst": "https://raw.githubusercontent.com/1andrevich/Re-filter-lists/main/domains_all.lst",
}
SERVICE_FILES = ["itdog-inside.lst", "refilter-community.lst", "antifilter-community.lst"]
REGISTRY_FILES = ["refilter-domains.lst"]
# Это должно идти напрямую, даже если попало в список.
KEEP_DIRECT = re.compile(r"(^|\.)(steamcontent\.com|steampowered\.com|steamstatic\.com|vk\.com|vk\.ru|"
                         r"yandex\.[a-z]+|ya\.ru|gosuslugi\.ru|sberbank\.ru|tbank\.ru|tinkoff\.ru|"
                         r"ozon\.ru|wildberries\.ru|funpay\.com|windowsupdate\.com)$")
DOMAIN_RE = re.compile(r"^[a-z0-9][a-z0-9.-]*[a-z0-9]$|^[a-z]{2,}$")


def read_list(files):
    out = set()
    for fn in files:
        try:
            for line in open(os.path.join(LISTS_DIR, fn), encoding="utf-8", errors="replace"):
                d = line.strip().lower().lstrip("*.").lstrip(".")
                if d and not d.startswith("#") and DOMAIN_RE.match(d) and not KEEP_DIRECT.search(d):
                    out.add(d)
        except OSError:
            pass
    return out


def refresh_lists():
    """Скачать свежие списки через VPN. Возвращает True, если что-то изменилось."""
    os.makedirs(LISTS_DIR, exist_ok=True)
    changed = False
    for fn, url in SOURCES.items():
        path = os.path.join(LISTS_DIR, fn)
        tmp = path + ".new"
        r = run(["curl.exe", "-sL", "-m", "120", "-x", VPN, "-o", tmp, "-w", "%{http_code}", url], timeout=140)
        if not r or r.stdout.strip() != "200" or not os.path.exists(tmp) or os.path.getsize(tmp) < 1000:
            say(f"не смог обновить список {fn} — оставил прежний")
            if os.path.exists(tmp):
                os.remove(tmp)
            continue
        old = open(path, "rb").read() if os.path.exists(path) else b""
        new = open(tmp, "rb").read()
        if new != old:
            os.replace(tmp, path)
            changed = True
        else:
            os.remove(tmp)
    return changed


def wanted_list_groups():
    services = read_list(SERVICE_FILES)
    registry = read_list(REGISTRY_FILES) - services
    return {SERVICES_GROUP: services, REGISTRY_GROUP: registry}


def sample_check(state, n=15):
    """Проверить несколько случайных адресов из списков по правилам Karing."""
    import random
    groups = wanted_list_groups()
    pool = sorted(groups[SERVICES_GROUP]) + random.sample(sorted(groups[REGISTRY_GROUP]),
                                                          min(200, len(groups[REGISTRY_GROUP])))
    pool = [d for d in pool if "." in d]
    st = state.setdefault("sample", {"checked": 0, "ok": 0, "dead": 0, "fixed": [], "day": ""})
    today = datetime.now().strftime("%d.%m.%Y")
    if st["day"] != today:
        if st["checked"]:
            say(f"Списки блокировок за {st['day']}: проверено {st['checked']} адресов — открываются через "
                f"Karing {st['ok']}, мёртвые сайты (не открываются нигде) {st['dead']}"
                + (f", пришлось добавить в VPN: {', '.join(st['fixed'])}" if st["fixed"] else "") + ".")
        st.update(checked=0, ok=0, dead=0, fixed=[], to_direct=[], day=today)
    need = set()
    vybor = random.sample(pool, min(n, len(pool)))
    for i, d in enumerate(vybor):
        hod.schet(i, len(vybor), f"Проверяю адреса из списков блокировок: {i + 1} из {len(vybor)}")
        code, _ = curl(f"https://{d}/", RULES, t=8)
        st["checked"] += 1
        if code in REFUSE and not covered_by_direct(d) and not NEVER_DIRECT.search(d):
            dc, dt = direct(f"https://{d}/", t=8)
            if dc.startswith(("2", "3")) and dt < 3:  # медленно напрямую (тормозят Cloudflare) — не переносим
                time.sleep(2)
                dc2, dt2 = direct(f"https://{d}/", t=8)
                if curl(f"https://{d}/", RULES, t=8)[0] in REFUSE and dc2.startswith(("2", "3")) and dt2 < 3:
                    state.setdefault("pending_direct", [])
                    if d not in state["pending_direct"]:
                        state["pending_direct"].append(d)
                    st.setdefault("to_direct", []).append(d)
        if ok(code):
            st["ok"] += 1
        else:
            v, _ = curl(f"https://{d}/", VPN, t=8)
            if ok(v):
                time.sleep(2)
                if ok(curl(f"https://{d}/", RULES, t=8)[0]) or covered_by_vpn(d):
                    st["ok"] += 1  # со второй попытки открылся или и так идёт через VPN — это мигание, не блокировка
                else:
                    need.add(d)  # по правилам не открывается, а через VPN открывается — правило не сработало
                    st["fixed"].append(d)
            else:
                st["dead"] += 1
        time.sleep(0.5)
    return need


def covered_by_direct(host):
    host = host.lower().strip(".")
    rg = load_json(os.path.join(KD, "karing_routing_group.json"), {})
    for item in rg.get("items", []):
        for g in item.get("groups", []):
            if g.get("name") == DIRECT_GROUP:
                return any(host == x or host.endswith("." + x) for x in (g.get("domain_suffix") or []))
    return False


def covered_by_vpn(host):
    """Адрес уже попадает в одну из наших групп, назначенных на VPN (тогда добавлять его бессмысленно)."""
    host = host.lower().strip(".")
    rg = load_json(os.path.join(KD, "karing_routing_group.json"), {})
    use = load_json(os.path.join(KD, "karing_subscribe_use.json"), {})
    vpn_groups = {g.get("diversion_name") for g in use.get("diversion_group", [])
                  if g.get("server_groupid") not in ("direct", None)}
    for item in rg.get("items", []):
        for g in item.get("groups", []):  # по порядку, как Karing: решает первая подходящая группа
            for suf in g.get("domain_suffix", []) or []:
                suf = suf.lower().strip(".")
                if host == suf or host.endswith("." + suf):
                    return g.get("name") in vpn_groups
    return False


def steam_status():
    try:
        with open(STEAM_LOG, encoding="utf-8", errors="replace") as f:
            tail = f.readlines()[-60:]
    except OSError:
        return None
    for line in reversed(tail):
        if "[Logged On" in line:
            return "вошёл"
        if "Logged Off" in line or "[Connecting" in line or "ConnectionDisconnected" in line:
            return "не вошёл: " + line[:19].strip("[] ")
    return None


# ---------- запасной сервер при падении VPN ----------

PREFERRED = "Финляндия🇫🇮🔃"                          # 01.10: Claude обычно через Финляндию (Нидерланды мигали десятки раз в день)
LOGIN_SERVER = "Англия🇬🇧🔃"                          # 15.10: аккаунт Claude теперь английский — и вход тоже через Англию (было Нидерланды)
CLAUDE_PIN = opt("claude_strana") or ""   # 13.10 его слово: у него Claude ИСКЛЮЧИТЕЛЬНО через Англию (nastroyki.json); у друга по умолчанию не закреплён
NEVER_DIRECT = re.compile(r"(^|\.)(claude\.ai|claude\.com|claudeusercontent\.com|anthropic\.com|clau\.de|openai\.com|chatgpt\.com|oaistatic\.com|oaiusercontent\.com)$")  # 01.11: OpenAI из РФ не пускает — напрямую нельзя
CLAUDE_SUFFIXES = {"anthropic.com", "claude.ai", "claude.com", "claudeusercontent.com", "clau.de"}  # 15.10: все адреса Claude — через Англию  # Claude — только VPN
YT_DIRECT = re.compile(r"(^|\.)(youtube\.com|googlevideo\.com|ytimg\.com|ggpht\.com|youtu\.be|youtube-nocookie\.com)$")
GOOD_SERVER = re.compile(r"🔃|^Польша🇵🇱$")          # основные зарубежные
BAD_SERVER = re.compile(r"Россия|Russia|Moscow|Москва|🇷🇺|LTE|YouTube", re.I)   # российские (у друга имена по-английски), мобильные, спецсерверы


def clash():
    api = load_json(CORE, {}).get("experimental", {}).get("clash_api", {})
    return api.get("external_controller"), api.get("secret", "")


def delay(tag, timeout_ms=4000):
    import urllib.request, urllib.parse
    ctl, sec = clash()
    url = (f"http://{ctl}/proxies/{urllib.parse.quote(tag)}/delay?timeout={timeout_ms}&url="
           + urllib.parse.quote("https://www.gstatic.com/generate_204"))
    try:
        op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        r = op.open(urllib.request.Request(url, headers={"Authorization": "Bearer " + sec}),
                    timeout=timeout_ms / 1000 + 4)
        return json.load(r).get("delay")
    except Exception:
        return None


def current_vpn_server():
    """Через какой сервер сейчас идёт Claude (по рабочему конфигу Karing)."""
    for r in load_json(CORE, {}).get("route", {}).get("rules", []):
        if r.get("name", "").startswith("Claude"):
            return r.get("outbound")
    return None


PROXY_TYPES = {"vless", "vmess", "trojan", "shadowsocks", "hysteria", "hysteria2", "tuic", "wireguard", "socks", "http", "shadowtls", "anytls"}


def vse_servery():
    """Все VPN-серверы, что есть в Karing (без «напрямую», «блок» и автовыбора)."""
    return [o["tag"] for o in (load_json(CORE, {}) or {}).get("outbounds", []) if o.get("type") in PROXY_TYPES and o.get("tag")]


def candidates():
    """Серверы подписки. У него — основные с «🔃» в имени; у друга имена другие — тогда все серверы, кроме российских и спецсерверов."""
    vse = vse_servery()
    svoi = [t for t in vse if GOOD_SERVER.search(t) and not BAD_SERVER.search(t)]
    return svoi or [t for t in vse if not BAD_SERVER.search(t)] or vse


def sub_id(use, tag=None):
    """groupid подписки для группы маршрутов: та подписка, где есть сервер tag (так Karing находит сервер).
    Без tag или если не нашёлся — подписка «недавнего» сервера, иначе первая подписка с серверами.
    У свежего Karing «недавних» может не быть — раньше тогда получался None, и Karing выбрасывал правило."""
    subs = [it for it in ((load_json(os.path.join(KD, "karing_subscribe.json"), {}) or {}).get("items") or [])
            if it.get("groupid") and it.get("servers")]
    if tag:
        for it in subs:
            if any(x.get("tag") == tag for x in it.get("servers") or []):
                return it["groupid"]
    return (next((x.get("groupid") for x in (use or {}).get("recent", []) or [] if x.get("groupid")), None)
            or next((it["groupid"] for it in subs if it["groupid"] != "custom"), None)
            or next((it["groupid"] for it in subs), None))


ROLES = {
    # роль: (домашний сервер, ключ аварийной замены в state)
    "claude": ("CLAUDE_HOME", "failover_claude"),   # Claude — всегда Англия (его слово 13.10)
    "fast": ("FAST_HOME", "failover_fast"),         # всё остальное через VPN — самый быстрый по замеру
}


def home(role):
    """Домашний сервер роли. Если его убрали из подписки — запасной (иначе Karing выкинет правило целиком)."""
    st = load_json(STATE, {})
    if role == "claude":
        if not CLAUDE_PIN:
            return home("fast")
        return CLAUDE_PIN  # даже если Англии нет в подписке: пусть лучше не работает, чем идёт не через Англию
    tag = st.get("fast_server") or PREFERRED
    live = candidates()
    if live and tag not in live:
        sel = (load_json(os.path.join(KD, "karing_subscribe_use.json"), {}) or {}).get("select_default")
        for alt in (PREFERRED, "Франция🇫🇷🔃", "Англия🇬🇧🔃", "Германия🇩🇪🔃", sel):   # sel — выбранный в Karing (у друга)
            if alt in live:
                return alt
        return live[0]
    return tag


STATS_FILE = os.path.join(HERE, "server_stats.json")


def server_scores(hours=24):
    """По статистике наблюдателя за последние сутки: {сервер: (обрывов, минут без ответа, средний пинг, замеров)}."""
    st = load_json(STATS_FILE, {})
    agg = {}
    for h in sorted(st)[-hours:]:
        for tag, (ok, fail, ev, ms) in st[h].items():
            a = agg.setdefault(tag, [0, 0, 0, 0])
            a[0] += ok; a[1] += fail; a[2] += ev; a[3] += ms
    return {t: (a[2], a[1], round(a[3] / a[0]) if a[0] else 9999, a[0] + a[1]) for t, a in agg.items()}


def is_stable(tag, scores=None):
    """Сервер можно ставить на «остальное»: за сутки не отвечал не больше 3% времени и обрывался не чаще 10 раз.
    Если статистики мало — считаем стабильным (решает скорость)."""
    v = (scores or server_scores()).get(tag)
    if not v or v[3] < 300:
        return True
    return v[1] / v[3] <= 0.03 and v[0] <= 10


def pick_claude_server():
    """Ночью: Claude — на сервер, который за сутки обрывался меньше всех (с постоянным адресом выхода).
    Возвращает (новый сервер или None, текст для отчёта)."""
    scores = server_scores()
    rotating = {r[0] for r in (load_json(STATE, {}).get("speed_test", {}).get("results") or [])
                if len(r) >= 4 and (r[3] or 0) > 1}
    ok = {t: v for t, v in scores.items() if v[3] >= 600 and t not in rotating and t in candidates()
          and v[1] / v[3] <= 0.02 and v[2] < 2000}  # не отвечал >2% времени — мёртвый/плохой, «0 обрывов» у него ложные
    ok = {t: v for t, v in ok.items() if delay(t, 3000) is not None}  # и жив прямо сейчас
    if not ok:
        return None, "Выбор сервера Claude: нет сервера с достаточной статистикой и стабильностью — оставляю как есть."
    rank = sorted(ok.items(), key=lambda kv: (kv[1][0], kv[1][1], kv[1][2]))
    best, cur = rank[0][0], home("claude")
    top = "; ".join(f"{t} — обрывов {v[0]}, минут без ответа {v[1]}, пинг {v[2]} мс" for t, v in rank[:3])
    cur_v = scores.get(cur)
    if best != cur and (cur_v is None or cur not in ok or rank[0][1][0] < cur_v[0]):
        return best, f"Выбор сервера Claude за сутки: {top}. Было {cur} (обрывов {cur_v[0] if cur_v else '?'}) — перевожу Claude на {best}."
    return None, f"Выбор сервера Claude за сутки: {top}. Оставляю {cur}."


def login_active(st=None):
    st = st if st is not None else load_json(STATE, {})
    return st.get("login_until", 0) > time.time()


def role_server(role):
    """Куда сейчас должна идти роль: режим входа и Claude — Англия; при аварии — запасной; иначе — домашний."""
    if role == "claude":
        return CLAUDE_PIN or role_server("fast")
    st = load_json(STATE, {})
    return st.get(ROLES[role][1]) or home(role)


def target_server():
    """Для новых групп (списки, авто-VPN) — сервер «скорость»."""
    return role_server("fast")


def vybrat_tekushchiy(use, tag):
    """Сделать tag текущим сервером Karing — как выбор сервера в его окне: select_default и первым в «недавних»."""
    if not tag or tag in ("direct_out", "block_out"):
        return
    use["select_default"] = tag
    rec = use.get("recent") or []
    i = next((k for k, x in enumerate(rec) if x.get("tag") == tag), None)
    if i:
        rec.insert(0, rec.pop(i))


def assign_vpn_groups(server=None):
    """Claude -> сервер роли claude, остальные VPN-группы -> сервер роли fast.
    Группы «текущий сервер Karing» (currentSelected) и блок не трогаем. Если Claude не закреплён (у друга),
    «остальное» — это и есть текущий сервер Karing: его и ставим (иначе замер у друга ничего бы не менял)."""
    p = os.path.join(KD, "karing_subscribe_use.json")
    use = load_json(p, None)
    if not use:
        return
    for g in use.get("diversion_group", []) or []:
        if g.get("diversion_groupid") == "final" or g.get("server_groupid") in ("direct", "currentSelected"):
            continue
        if g.get("diversion_name", "").startswith(("🧪", "🧭")):
            continue
        if g.get("diversion_name", "").startswith("⛔") or g.get("server_groupid") == "block":
            g["server_groupid"], g["server_name"] = "block", "block_out"
            continue
        tag = role_server("claude" if g.get("diversion_name") == "Claude" else "fast")
        g["server_groupid"], g["server_name"] = sub_id(use, tag), tag
    if not CLAUDE_PIN:
        vybrat_tekushchiy(use, role_server("fast"))
    save_json(p, use)


def fast_current():
    """Через какой сервер сейчас идёт «всё остальное»: у него — по правилу заблокированного в РФ (GFW);
    у друга такой группы может не быть или она «напрямую» — тогда текущий сервер Karing."""
    route = (load_json(CORE, {}) or {}).get("route", {}) or {}
    for r in route.get("rules", []):
        if "GFW" in r.get("name", "") and r.get("outbound") not in (None, "direct_out", "block_out"):
            return r.get("outbound")
    for r in route.get("rules", []):
        if r.get("name") == "proxy[all]" and r.get("outbound"):
            return r.get("outbound")
    sel = (load_json(os.path.join(KD, "karing_subscribe_use.json"), {}) or {}).get("select_default")
    return sel or (route.get("final") if route.get("final") not in (None, "direct_out", "block_out") else None)


def switch_roles(why, revert=None, claude_moves=True):
    """Переназначить группы по текущему state и перезапустить Karing, с проверкой и откатом.
    revert — какие поля state вернуть, если не вышло (иначе в памяти остаётся плохой сервер и всё идёт по кругу).
    claude_moves=False — меняется только «остальное»: если Claude не отвечает, виноват его сервер (Англия мигает),
    а не переключение — тогда достаточно, чтобы ответил новый сервер «остального» (19.10: так откатывалось весь день)."""
    nelzya = perezapusk_nelzya() if os.path.exists(CORE) else (karing_beda() or "Karing не найден")
    if nelzya:
        if revert:
            st = load_json(STATE, {})
            st.update(revert)
            save_json(STATE, st)
        say(f"{why} Не переключаю: {nelzya}.")
        return False
    vedu_claude = claude_vedu()
    hod.shag(KARING_SHAGI[0])
    saved = _zapomnit()
    karing_kill()
    assign_vpn_groups()
    t0 = time.time()
    hod.shag(KARING_SHAGI[1])
    karing_start()
    hod.shag(KARING_SHAGI[2])
    up = karing_wait(t0)
    hod.shag(KARING_SHAGI[3])
    c_ok = up and svyaz_ok()
    fc = fast_current() if up else None
    good = c_ok or (up and not claude_moves and fc is not None and delay(fc, 3000) is not None)
    if good:
        if vedu_claude:
            tail = "Claude отвечает." if c_ok else f"Claude пока не отвечает — мигает его сервер {CLAUDE_PIN}, переключение тут ни при чём."
            say(f"{why} Сейчас: Claude — {current_vpn_server()}, остальное — {fast_current()}. {tail}")
        else:
            say(f"{why} Сейчас через VPN — {fc}. " + ("VPN работает." if c_ok else "Сервер отвечает."))
        return True
    hod.tekst(KARING_OTKAT)
    karing_kill()
    _vernut(saved)
    t1 = time.time()
    karing_start()
    karing_wait(t1)
    if revert:
        st = load_json(STATE, {})
        st.update(revert)
        save_json(STATE, st)
    say(f"{why} Переключение не удалось — " + ("Claude не ответил" if vedu_claude else "VPN через новый сервер не заработал")
        + ", вернул как было.")
    return False


def switch_server(server, why):
    """Совместимость: всё на один сервер (обе роли)."""
    st = load_json(STATE, {})
    for role in ROLES:
        st[ROLES[role][1]] = None if server in (None, home(role)) else server
    save_json(STATE, st)
    return switch_roles(why)


def karing_api_alive():
    """Ядро Karing отвечает на служебный запрос — значит, не зависло (даже если серверы VPN лежат)."""
    import urllib.request
    ctl, sec = clash()
    try:
        op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        op.open(urllib.request.Request(f"http://{ctl}/version", headers={"Authorization": "Bearer " + sec}), timeout=4)
        return True
    except Exception:
        return False


def with_lock(fn):
    """Перезапуск Karing — под общей блокировкой сторожа, чтобы две задачи не делали это одновременно."""
    if os.path.exists(LOCK) and time.time() - os.path.getmtime(LOCK) < 1800:
        return False
    open(LOCK, "w").close()
    try:
        return fn()
    finally:
        try:
            os.remove(LOCK)
        except OSError:
            pass


def _tcp(host, port, timeout=2.0):
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def vpn_check():
    """Раз в 2 минуты: живы ли серверы обеих ролей. Переключает только при падении."""
    if manual_global():
        global_not_britain()
        return
    if os.path.exists(LOCK) and time.time() - os.path.getmtime(LOCK) < 1800:
        return  # идёт другой проход сторожа — Karing не трогаем вдвоём
    if not clash()[0]:
        return  # Karing не установлен или без служебного входа (clash API): ни зависание, ни серверы не проверить
    state = load_json(STATE, {})
    direct_ok = any(_tcp(h, 443) for h in ("77.88.55.242", "87.250.250.242"))
    if "karing.exe" in procs() and direct_ok:
        alive_core = karing_api_alive()
        any_server = alive_core and any(delay(t, 3000) for t in candidates())
        if not alive_core:
            # Karing правда завис: ядро не отвечает. Две проверки подряд → перезапуск, но не больше 2 раз в час
            state["karing_hang"] = state.get("karing_hang", 0) + 1
            recent = [x for x in state.get("hang_restarts", []) if time.time() - x < 3600]
            if state["karing_hang"] >= 2:
                state["karing_hang"] = 0
                if len(recent) >= 2:
                    if time.time() - state.get("hang_popup", 0) > 3600:
                        state["hang_popup"] = time.time()
                        say("Karing не отвечает, два перезапуска за час не помогли — больше не перезапускаю"
                            + (", нужен Claude." if CLAUDE_PIN else "."))
                        popup("Karing не работает, и перезапуски не помогают. "
                              + sovet("Напишите Claude «проверь что не так».", "Перезапустите Karing вручную; не поможет — переустановите его."))
                    state["hang_restarts"] = recent
                    save_json(STATE, state)
                    return
                state["hang_restarts"] = recent + [time.time()]
                save_json(STATE, state)
                with_lock(lambda: switch_roles("Karing завис и перестал отвечать — перезапустил."))
                return
            save_json(STATE, state)
            return
        if not any_server:
            # ядро живо, но не отвечает НИ ОДИН сервер — сбой VPN-сервиса, перезапуск не поможет
            state["karing_hang"] = 0
            if time.time() - state.get("vpn_down_popup", 0) > 3600:
                state["vpn_down_popup"] = time.time()
                say("Ни один VPN-сервер не отвечает (Karing при этом жив) — похоже, сбой у VPN-сервиса. Ничего не перезапускаю.")
            save_json(STATE, state)
            return
    state["karing_hang"] = 0
    # режим входа закончился — вернуть Claude на обычный сервер
    if state.get("login_applied") and not login_active(state):
        state["login_applied"] = False
        save_json(STATE, state)
        switch_roles("Режим входа закончился — Claude снова через " + home("claude") + ".")
        return
    # отложенная смена серверов (ждала конца игры)
    if state.get("roles_pending") and not game_running(state):
        state["roles_pending"] = False
        save_json(STATE, state)
        switch_roles("Применяю отложенную смену серверов.")
        return
    if not port_up():
        save_json(STATE, state)
        return
    changed, whys = False, []
    revert_keys = {k: state.get(k) for k in ("failover_claude", "failover_fast")}
    live = {"claude": current_vpn_server(), "fast": fast_current()}
    vse = set(vse_servery())
    for role, cur in live.items():
        if not cur or cur not in vse:
            continue   # нет сервера или это автовыбор Karing (urltest) — его не переключаем
        if role == "claude" and not CLAUDE_PIN:
            continue  # Claude не закреплён — идёт вместе с «остальным»
        if role == "claude" and login_active(state):
            continue  # вход в аккаунт — только Англия, даже если мигает
        fkey = ROLES[role][1]
        failk, okk = f"vpn_fail_{role}", f"{fkey}_ok_since"
        if role == "claude":
            if cur != CLAUDE_PIN:
                state["roles_pending"] = True  # Claude не через Англию — вернуть при первой возможности
                continue
            alive = delay(cur) is not None or (time.sleep(5) or delay(cur) is not None)
            state[failk] = 0 if alive else state.get(failk, 0) + 1
            if state[failk] >= 3 and time.time() - state.get("pin_popup", 0) > 3600:
                state["pin_popup"] = time.time()
                say(f"Сервер {CLAUDE_PIN} (Claude) не отвечает уже {state[failk] * 2} мин. Claude закреплён на Англии — на другой не перевожу.")
                popup("VPN-сервер Англия не отвечает — Claude сейчас может не работать. Сторож его не переключает (Claude только через Англию).")
            continue
        alive = delay(cur) is not None or (time.sleep(5) or delay(cur) is not None)
        if not alive:
            state[failk] = state.get(failk, 0) + 1
            if state[failk] >= 2:
                others = [t for t in candidates() if t != cur]
                scores = [(d, t) for t in others for d in [delay(t, 3000)] if d]
                if scores:
                    best = min(scores)[1]
                    if role == "fast" and not state.get("fast_server"):
                        state["fast_server"] = cur   # у друга «домашнего» ещё нет — домашним считаем тот, что упал
                    state[fkey], state[okk], state[failk] = best, 0, 0
                    changed = True
                    whys.append(f"сервер {cur} ({'Claude' if role == 'claude' else 'скорость'}) перестал отвечать, временно {best}.")
                else:
                    say(f"VPN-сервер {cur} не отвечает, и живых запасных нет — похоже, пропал интернет.")
                    state[failk] = 0
        else:
            state[failk] = 0
            if state.get(fkey):  # авария прошла: домашний сервер жив 30 минут — вернуть (не в игре)
                h = home(role)
                if delay(h) is not None:
                    since = state.get(okk) or time.time()
                    state[okk] = since
                    if time.time() - since > 1800 and not game_running(state):
                        state[fkey], state[okk] = None, 0
                        changed = True
                        whys.append(f"домашний сервер {h} снова стабилен, возвращаю.")
                else:
                    state[okk] = 0
    save_json(STATE, state)
    if changed:
        # здесь меняется только «остальное»: Claude закреплён на Англии и выше всегда continue
        with_lock(lambda: switch_roles(" ".join(whys), revert=revert_keys, claude_moves=False))


# ---------- главный проход ----------

# Шаги прохода для окна (кнопка «Проверить сейчас» = pult.py proverka). Плановый проход хода не пишет.
PROVERKA_SHAGI = ["Проверяю, что Karing работает", "Проверяю настройки Karing", "Проверяю сайты и программы",
                  "Проверяю списки блокировок", "Исправляю Karing, если нужно", "Записываю итог"]


def main():
    if os.path.exists(LOCK) and time.time() - os.path.getmtime(LOCK) < 1800:
        return
    open(LOCK, "w").close()
    try:
        hod.shag(PROVERKA_SHAGI[0])
        for fn, imya in ((firewall_gaps, "защиты «Claude только через Karing»"), (uk_proxy_watch, "личного прокси")):
            try:   # до загрузки state: пишут в него сами, иначе main затрёт
                fn()
            except Exception as e:
                say(f"Проверка {imya} упала: {e}")
        state = load_json(STATE, {})
        pending = set(state.get("pending", []))
        notes = []

        # 1. Karing вообще работает?
        if "karing.exe" not in procs():  # процесс есть, но порт молчит — это «завис», им занимается проверка VPN
            if not os.path.isdir(KD) and not karing_exe():
                if not state.get("karing_net"):
                    say("Karing не установлен — сторожу нечего делать. Установите Karing и добавьте подписку.")
                    state["karing_net"] = True
                save_json(STATE, state)
                return
            state["karing_net"] = False
            if state.get("karing_down"):
                if karing_start():
                    ready = karing_wait(time.time() - 1)
                    say("Karing не работал — запустил заново. " + ("Поднялся." if ready else "НЕ поднялся!"))
                elif not state.get("karing_exe_net"):
                    say("Karing не работает, а запустить его не смог — не нашёл karing.exe.")
                    state["karing_exe_net"] = True
                state["karing_down"] = False
            else:
                state["karing_down"] = True  # подождём одну проверку: вдруг его закрыли на минуту
            save_json(STATE, state)
            return
        state["karing_down"] = False
        state["karing_exe_net"] = False
        if curl_net():   # без curl.exe все проверки «не открывается» — ничего не трогаем, только сообщаем
            if not state.get("curl_net_noted"):
                say("Нет программы curl.exe (она есть в Windows 10 и 11) — сторож не может проверять сайты и ничего не меняет.")
                state["curl_net_noted"] = True
            save_json(STATE, state)
            return
        state["curl_net_noted"] = False

        # Он сам переключил Karing на «Глобально» (например, для входа в аккаунт) — ничего не трогать
        if manual_global():
            if not state.get("global_noted"):
                say("Karing переключён на «Глобально» вручную — сторож ничего не меняет, пока не вернётся «Правила».")
                state["global_noted"] = True
                state["global_since"] = time.time()
            since = time.time() - state.get("global_since", time.time())
            # у него «Глобально» — на время входа, напоминать каждые 6 ч; у друга это может быть обычный режим — один раз
            if since > 3600 and (time.time() - state.get("global_popup", 0) > 6 * 3600 if CLAUDE_PIN else not state.get("global_popup")):
                state["global_popup"] = time.time()
                popup(f"Karing всё ещё в режиме «Глобально» — уже {since / 3600:.0f} ч.\n\n"
                      "Весь интернет идёт через один VPN-сервер, российские сайты тоже. "
                      "Если вход в аккаунт закончен — нажмите в Karing «Правила».")
            save_json(STATE, state)
            return
        if state.get("global_noted"):
            say("Karing снова в режиме «Правила» — сторож продолжает работу.")
            state["global_noted"] = False
            state["global_popup"] = 0

        # 2. Главные условия: Claude через VPN и режим «по правилам»
        hod.shag(PROVERKA_SHAGI[1])
        broken = invariants_broken()
        if claude_vedu() and not claude_ok():
            broken.append("Claude не открывается через Karing")
        # поломку, которую 6 раз подряд не удалось починить, дальше чиним не чаще раза в 2 часа (а не каждые 10 минут);
        # удачная починка обнуляет счёт — то, что лечится, лечится как раньше
        povt = {k: [t for t in v if time.time() - t < 86400] for k, v in (state.get("inv_fix") or {}).items()}
        povt = {k: v for k, v in povt.items() if v}
        uporno = [b for b in broken if len(povt.get(b, [])) >= 6 and time.time() - povt[b][-1] < 2 * 3600]
        if uporno and state.get("inv_fix_noted") != uporno:
            say("6 раз подряд не вышло починить перезапуском — пробую теперь раз в 2 часа: " + "; ".join(uporno))
        state["inv_fix_noted"] = uporno
        broken = [b for b in broken if b not in uporno]

        # 3. Новые ошибки из журнала + раз в час проверка всех программ
        # zapret нужен YouTube (идёт напрямую) и голосу Discord
        zq = run(["sc", "query", "zapret"])
        zapret_stoit = zapret_ustanovlen(zq)   # не установлен вовсе (у друга) — не надоедаем окошками
        if opt("zapret_slezhka") and zapret_stoit and "RUNNING" not in zq.stdout and time.time() - state.get("zapret_popup", 0) > 6 * 3600:
            state["zapret_popup"] = time.time()
            say("Служба zapret не работает — YouTube и голос Discord напрямую не пойдут.")
            popup("Служба zapret не работает — YouTube и голос в Discord сейчас не пойдут. "
                  + sovet("Напишите Claude «проверь zapret».", "Откройте «Сторож сети», раздел zapret, и подберите настройку обхода."))

        hod.shag(PROVERKA_SHAGI[2])
        candidates = {h for h, n in log_failures(state).items() if n >= 2}
        if time.time() - state.get("probe_ts", 0) > 3600:
            state["probe_ts"] = time.time()
            for i, (name, url) in enumerate(PROBES):
                hod.schet(i, len(PROBES), f"Проверяю программы: {i + 1} из {len(PROBES)} ({name})")
                code, t = curl(url, RULES)
                host = url.split("/")[2]
                if not ok(code) or t > 3:
                    candidates.add(host)
                time.sleep(1)
        known = set(state.get("checked_hosts", {}).keys())
        for host in sorted(candidates):
            last = state.get("checked_hosts", {}).get(host, 0)
            if host in known and time.time() - last < 6 * 3600:
                continue
            state.setdefault("checked_hosts", {})[host] = time.time()
            hod.tekst(f"Проверяю, открывается ли {host}")
            need, why = needs_vpn(host)
            if why:
                notes.append(why)
            if need and YT_DIRECT.search(host) and zapret_stoit:
                # YouTube идёт напрямую через zapret — в VPN его не заворачиваем, а зовём перетестировать zapret
                # (если zapret не установлен вовсе — YouTube идёт как любой сайт: в VPN, ниже)
                if time.time() - state.get("yt_popup", 0) > 6 * 3600:
                    state["yt_popup"] = time.time()
                    say(f"YouTube напрямую не открывается ({host}) — похоже, настройка обхода zapret перестала работать.")
                    popup("YouTube перестал открываться напрямую — похоже, Роскомнадзор сломал настройку обхода zapret. "
                          + sovet("Напишите Claude «перетестируй zapret».", "Откройте «Сторож сети», раздел zapret, и подберите настройку обхода заново."))
            elif need and not covered_by_vpn(host) and opt("avto_sayty"):
                pending.add(suffix(host))
            time.sleep(1)

        # 4. Steam
        s = steam_status()
        if s and s != "вошёл" and "steam.exe" in procs():
            if s != state.get("steam_last"):
                code, _ = curl("https://cmp1-sto2.steamserver.net/", RULES)
                notes.append(f"Steam {s}; сервер входа через Karing: {'отвечает' if ok(code) else 'НЕ отвечает'}")
        state["steam_last"] = s

        # 5. Списки блокировок РФ: раз в сутки обновить, каждый запуск проверить несколько адресов
        hod.shag(PROVERKA_SHAGI[3])
        if opt("avto_spiski") and time.time() - state.get("lists_ts", 0) > 86400:
            state["lists_ts"] = time.time()
            hod.tekst("Обновляю списки заблокированного в РФ (раз в сутки)")
            if refresh_lists():
                state["lists_pending"] = True
                notes.append("Списки заблокированного в РФ обновились — внесу в Karing")
        for d in (sample_check(state) if opt("avto_spiski") else []):
            pending.add(suffix(d))

        # 6. Раз в сутки ночью (3–6 ч) перемерить скорость VPN-серверов для «остального» (Claude не трогаем)
        hour = datetime.now().hour
        due = time.time() - state.get("speed_ts", 0) > 20 * 3600
        nightly = due and (3 <= hour < 6 or idle_minutes() >= 10)  # компьютер ночью часто спит — берём первую тихую минуту
        if ((nightly and opt("avto_server")) or state.get("speed_now")) and not game_running(state):
            state["speed_ts"] = time.time()
            state["speed_now"] = False
            save_json(STATE, state)
            hod.tekst("Меряю скорость серверов (раз в сутки) — это займёт несколько минут")
            os.remove(LOCK)  # замер сам берёт блокировку
            try:
                r = run([_python(okno=False), os.path.join(HERE, "skorost_serverov.py"), "--apply"], timeout=1200)
                with open(os.path.join(HERE, "skorost.log"), "a", encoding="utf-8") as f:
                    f.write(f"=== {now()}\n" + ((r.stdout + r.stderr) if r else "не завершился за 20 минут\n"))
            finally:
                open(LOCK, "w").close()
            state = {**state, **{k: v for k, v in load_json(STATE, {}).items()
                                 if k in ("fast_server", "speed_test", "failover_fast", "failover_claude")}}
            new_claude = None  # 13.10: Claude закреплён на Англии, ночной выбор для него выключен
            if new_claude:
                prev = {"claude_server": state.get("claude_server"), "failover_claude": state.get("failover_claude")}
                state["claude_server"] = new_claude
                state["failover_claude"] = None
                save_json(STATE, state)
                if not switch_roles("Ночной выбор: Claude на самый стабильный сервер.", revert=prev):
                    state.update(prev)  # иначе в памяти останется сервер, через который Claude не работает

        for n in notes:
            say(n)

        # 5. Применить, если есть что и можно
        # то, что и так уже идёт через VPN, не повод перезапускать Karing
        hod.shag(PROVERKA_SHAGI[4])
        pending = {p for p in pending if not covered_by_vpn(p)}
        lists_pending = state.get("lists_pending", False)
        pending_direct = sorted({suffix(d) for d in state.get("pending_direct", [])
                                 if not covered_by_direct(d) and not NEVER_DIRECT.search(d)})
        if pending or broken or lists_pending or pending_direct:
            games = game_running(state)
            since = time.time() - state.get("restart_ts", 0)
            too_soon = since < (1800 if broken else 3 * 3600)  # несрочное копим и применяем пачкой, не чаще раза в 3 ч
            if games:
                if state.get("wait_note") != ",".join(games):
                    say("Нужно поправить Karing (" + ", ".join(sorted(pending) + broken) +
                        "), но запущена игра: " + ", ".join(games) + ". Жду, пока выйдете.")
                    state["wait_note"] = ",".join(games)
            elif too_soon:
                pass
            else:
                what = []
                if pending:
                    what.append("в VPN: " + ", ".join(sorted(pending)))
                if broken:
                    what.append("чиню: " + "; ".join(broken))
                lg = None
                if lists_pending:
                    lg = wanted_list_groups()
                    if state.get("registry_off"):
                        lg[REGISTRY_GROUP] = set()
                    what.append("списки РФ: " + ", ".join(f"{k} — {len(v)} доменов" for k, v in lg.items()))
                if pending_direct:
                    what.append("напрямую (через VPN сайт отказывает): " + ", ".join(pending_direct))
                success, info = apply_changes(sorted(pending), bool(broken), lg, pending_direct)
                for b in broken:
                    if success:
                        povt.pop(b, None)
                    else:
                        povt.setdefault(b, []).append(time.time())
                if success:
                    state["pending_direct"] = []
                state["restart_ts"] = time.time()
                state["wait_note"] = ""
                say(("ИСПРАВЛЕНО. " if success else "НЕ ВЫШЛО. ") + " | ".join(what) + ". " + info)
                if success:
                    pending = set()
                    state["lists_pending"] = False
                elif lists_pending and not state.get("registry_off"):
                    state["registry_off"] = True  # большой реестр не потянулся — дальше без него
                    say("Большой реестр РКН отключаю, в следующий раз внесу только списки сервисов.")
        hod.shag(PROVERKA_SHAGI[5])
        state["inv_fix"] = povt
        state["pending"] = sorted(pending)
        state["last_run"] = now()
        disk = load_json(STATE, {})  # данные о переключении сервера ведёт проверка VPN — не затирать
        for k in ("failover_claude", "failover_fast", "failover_claude_ok_since", "failover_fast_ok_since",
                  "vpn_fail_claude", "vpn_fail_fast", "fast_server", "speed_test", "speed_ts", "speed_now", "karing_hang",
                  "login_until", "login_applied", "roles_pending", "claude_server",
                  "global_since", "global_popup", "yt_popup", "zapret_popup", "pending_direct",
                  "hang_restarts", "hang_popup", "vpn_down_popup"):
            if k in disk:
                state[k] = disk[k]
        save_json(STATE, state)
    finally:
        try:
            os.remove(LOCK)
        except OSError:
            pass


if __name__ == "__main__":
    if "--once-report" in sys.argv:
        print(open(REPORT, encoding="utf-8").read() if os.path.exists(REPORT) else "отчёта пока нет")
    elif "--vpn" in sys.argv:
        if os.path.exists(LOCK) and time.time() - os.path.getmtime(LOCK) < 1800:
            sys.exit(0)  # идёт большой проход — не мешаем
        try:
            vpn_check()
        except Exception as e:
            say(f"проверка VPN упала: {e!r}")
    else:
        try:
            main()
        except Exception as e:
            say(f"сторож упал: {e!r}")

"""Пульт сторожа сети: команды для окна «Сторож сети». Всё отвечает одной строкой JSON.

  pult.py sostoyanie            — что сейчас: сервер, Claude, zapret, мигания, отчёт, настройки
  pult.py programmy             — какие программы сейчас в сети и куда идут
  pult.py servery               — серверы и последний замер
  pult.py nastroyki-zadat '{json}'  — сохранить настройки (выключатели, маршруты)
  pult.py primenit-marshruty    — внести маршруты программ в Karing (перезапуск Karing)
  pult.py sayt DOMEN vpn|direct — сайт через VPN или напрямую (перезапуск Karing)
  pult.py server AUTO|ИМЯ       — сервер для «остального» (перезапуск Karing)
  pult.py zamer                 — замерить все серверы и поставить лучший
  pult.py proverka              — проход сторожа сейчас
Кнопку нажал человек, поэтому команды не ждут конца игры/звонка.
"""
import json, os, re, sys, time, subprocess, traceback
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
os.chdir(HERE)
import storozh as s

ZHURNAL = os.path.join(HERE, "pult.log")


def log(m):
    try:
        with open(ZHURNAL, "a", encoding="utf-8") as f:
            f.write(f"[{datetime.now():%d.%m %H:%M:%S}] {m}\n")
    except OSError:
        pass


def out(obj):
    sys.stdout.buffer.write(json.dumps(obj, ensure_ascii=False).encode("utf-8"))


def nastroyki():
    d = dict(s.NASTR_DEFAULT)
    d.update(s.load_json(s.NASTR_FILE, {}))
    return d


def clash(path):
    """Служебный вход Karing (clash API). Не работает — s.KaringNet с понятной причиной."""
    import urllib.request
    ctl, sec = s.clash()
    if not ctl:
        raise s.KaringNet(s.karing_beda() or "в Karing не включён служебный вход (clash API) — обновите Karing")
    req = urllib.request.Request("http://" + ctl + path, headers={"Authorization": "Bearer " + sec})
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        return json.load(op.open(req, timeout=5))
    except (OSError, ValueError):
        raise s.KaringNet(s.karing_beda() or "Karing не отвечает на служебные запросы — перезапустите его")


def zagruzki():
    """Папка «Загрузки» — её можно перенести на другой диск, поэтому берём из реестра."""
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders") as k:
            p = os.path.expandvars(winreg.QueryValueEx(k, "{374DE290-123F-4565-9164-39C4925E467B}")[0])
        if os.path.isdir(p):
            return p
    except OSError:
        pass
    return os.path.join(os.path.expanduser("~"), "Downloads")


def miganiya_za_sutki():
    n = 0
    gran = datetime.now() - timedelta(days=1)
    try:
        for x in open(os.path.join(HERE, "mignaniya.log"), encoding="utf-8"):
            m = re.match(r"\[(\d\d)\.(\d\d) (\d\d):(\d\d)", x)
            if m and "сбой" in x:
                d = datetime(datetime.now().year, int(m[2]), int(m[1]), int(m[3]), int(m[4]))
                if d > datetime.now():
                    d = d.replace(year=d.year - 1)
                n += d >= gran
    except OSError:
        pass
    return n


def tail(path, n):
    try:
        return open(path, encoding="utf-8").read().splitlines()[-n:]
    except OSError:
        return []


def sostoyanie():
    st = s.load_json(s.STATE, {})
    karing = os.path.exists(s.CORE) and s.port_up()
    zq = s.run(["sc", "query", "zapret"])
    zapret = bool(zq and "RUNNING" in zq.stdout)
    zapret_est = s.zapret_ustanovlen(zq)
    strat = ""
    try:
        import winreg
        k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Services\zapret")
        strat = winreg.QueryValueEx(k, "zapret-discord-youtube")[0]
    except OSError:
        pass
    fast = s.fast_current() if karing else None
    skor = {r[0]: r[1] for r in (st.get("speed_test") or {}).get("results", [])}
    games = s.game_running(st)
    bedy = []
    if not karing:
        bedy.append(s.karing_beda() or "Karing не работает")
    if not zapret:
        # строка начинается с «zapret» — по ней окно ставит кнопку (подобрать стратегию / поставить zapret);
        # поставлен ли он вообще — в ключе zapret_est
        bedy.append("zapret не работает" if zapret_est else
                    "zapret не установлен — без него YouTube и голос Discord напрямую не работают")
    if s.curl_net():
        bedy.append("Нет программы curl.exe (она есть в Windows 10 и 11) — без неё сторож не проверяет сайты и не меряет скорость")
    if not zadaniya_est():
        bedy.append("Сторож не запускается сам — включите проверку каждые 10 минут")
    if st.get("pending"):
        bedy.append("Пустить через VPN: " + ", ".join(st["pending"]) + " — напрямую не открывается или тормозит")
    out({
        "karing": karing, "zapret": zapret, "strategiya": strat, "zapret_est": zapret_est,
        "karing_est": os.path.isdir(s.KD) or bool(s.karing_exe()),
        "server": fast, "skorost": skor.get(fast), "claude": s.CLAUDE_PIN or "как всё остальное",
        "posledniy": st.get("last_run"), "zamer": (st.get("speed_test") or {}).get("when"),
        "miganiya": miganiya_za_sutki(), "igra": games, "bedy": bedy,
        "otchet": tail(s.REPORT, 12), "mig_log": tail(os.path.join(HERE, "mignaniya.log"), 40),
        "pult_log": tail(ZHURNAL, 40), "nastroyki": nastroyki(),
        "stat": statistika(st),
    })


def statistika(st):
    """Для графиков: обрывы VPN по дням, надёжность серверов за сутки, история замеров."""
    dni = {}
    segodnya = datetime.now().date()
    for i in range(6, -1, -1):
        d = segodnya - timedelta(days=i)
        dni[d.strftime("%d.%m")] = [0, 0]
    try:
        for x in open(os.path.join(HERE, "mignaniya.log"), encoding="utf-8"):
            m = re.match(r"\[(\d\d\.\d\d) [\d:]+\] сбой (\d+) с", x)
            if m and m[1] in dni:
                dni[m[1]][0] += 1
                dni[m[1]][1] += int(m[2])
    except OSError:
        pass
    try:
        sc = s.server_scores(24)
    except Exception:
        sc = {}
    # обрывы по часам за последние 24 ч
    chasy = [[0, 0] for _ in range(24)]
    gran = datetime.now() - timedelta(hours=24)
    try:
        for x in open(os.path.join(HERE, "mignaniya.log"), encoding="utf-8"):
            m = re.match(r"\[(\d\d)\.(\d\d) (\d\d):(\d\d):\d\d\] сбой (\d+) с", x)
            if m:
                d = datetime(datetime.now().year, int(m[2]), int(m[1]), int(m[3]), int(m[4]))
                if gran <= d <= datetime.now():
                    chasy[d.hour][0] += 1
                    chasy[d.hour][1] += int(m[5])
    except OSError:
        pass
    # история замеров: когда, какой выбран, его скорость
    ist = []
    try:
        kogda, skor = None, {}
        for x in open(os.path.join(HERE, "skorost.log"), encoding="utf-8"):
            if x.startswith("=== "):
                kogda, skor = x[4:20].strip(), {}
            m = re.match(r"\s+(\S+)\s+\S+\s+(\d+) Мбит/с", x)
            if m:
                skor[m[1]] = int(m[2])
            m = re.match(r"ВЫБРАН: (\S+)", x)
            if m and kogda:
                ist.append([kogda[:11].strip(), m[1], skor.get(m[1], 0)])
    except OSError:
        pass
    return {"dni": [[k, v[0], v[1]] for k, v in dni.items()],
            "chasy": chasy,
            "nadezhnost": {k: [v[0], v[1], v[2]] for k, v in sc.items()},
            "vybory": ist[-12:], **monitor_svodka()}


def programmy():
    nas = nastroyki()["marshruty"]
    agg = {}
    info = ""
    try:
        for c in clash("/connections").get("connections", []):
            exe = (c["metadata"].get("processPath") or "").split("\\")[-1]
            if not exe:
                continue
            a = agg.setdefault(exe, {"exe": exe, "kuda": set(), "kb": 0, "soed": 0})
            a["kuda"].add((c.get("chains") or ["?"])[0])
            a["kb"] += (c.get("download", 0) + c.get("upload", 0)) // 1024
            a["soed"] += 1
    except Exception as e:
        info = str(e) if isinstance(e, s.KaringNet) else ""
        log(f"программы: {e!r}")
    cherez = set(agg)
    try:
        vse = programmy_vse()
    except Exception as e:
        vse = {}; log(f"netstat: {e!r}")
    for exe in vse:
        agg.setdefault(exe, {"exe": exe, "kuda": set(), "kb": 0, "soed": 0})
    for exe in nas:
        agg.setdefault(exe, {"exe": exe, "kuda": set(), "kb": 0, "soed": 0})
    spisok = []
    for a in sorted(agg.values(), key=lambda x: (-x["kb"], x["exe"].lower())):
        kuda = sorted(a["kuda"])
        e = a["exe"]
        spisok.append({"exe": e, "seychas": ", ".join("напрямую" if k == "direct_out" else k for k in kuda) if kuda else
                       ("мимо Karing" if e in vse else "сейчас не в сети"),
                       "kb": a["kb"], "marshrut": nas.get(e, "avto"), "pravilo": e in nas,
                       "karing": e in cherez, "v_seti": e in vse or e in cherez})
    out({"programmy": spisok, "servery": serv_list(), **({"info": info} if info else {})})


def serv_list():
    try:
        return s.candidates()
    except Exception:
        return []


def servery():
    st = s.load_json(s.STATE, {})
    out({"zamer": st.get("speed_test") or {}, "servery": serv_list(), "seychas": s.fast_current(),
         "vybor": st.get("fast_server"), "claude": s.CLAUDE_PIN or "как всё остальное", "podrobno": servery_podrobno()})


def nastroyki_zadat(js):
    d = nastroyki()
    try:
        nov = json.loads(js)
        if not isinstance(nov, dict):
            raise ValueError
    except ValueError:
        out({"ok": False, "info": "настройки не сохранены: окно прислало их в непонятном виде"}); return
    d.update(nov)
    s.save_json(s.NASTR_FILE, d)
    log("настройки сохранены: " + json.dumps({k: v for k, v in d.items() if k != "marshruty"}, ensure_ascii=False))
    out({"ok": True})


def s_zamkom(fn):
    for _ in range(120):
        if not os.path.exists(s.LOCK) or time.time() - os.path.getmtime(s.LOCK) > 1800:
            break
        time.sleep(3)
    open(s.LOCK, "w").close()
    try:
        return fn()
    finally:
        try:
            os.remove(s.LOCK)
        except OSError:
            pass


def primenit_marshruty():
    r = nastroyki()["marshruty"]
    ok, info = s_zamkom(lambda: s.apply_changes([], False, routes=r))
    s.say(("ИСПРАВЛЕНО. " if ok else "НЕ ВЫШЛО. ") + f"маршруты программ ({len(r)}). " + info)
    log(f"маршруты {r}: {ok} {info}")
    out({"ok": ok, "info": info})


def sayt(domen, kuda):
    d = re.sub(r"^https?://", "", domen.strip().lower()).split("/")[0]
    d = s.suffix(d) if d.count(".") > 1 else d
    if kuda == "vpn":
        ok, info = s_zamkom(lambda: s.apply_changes([d], False))
    else:
        if s.NEVER_DIRECT.search(d):
            out({"ok": False, "info": f"{d} — только через VPN"}); return
        ok, info = s_zamkom(lambda: s.apply_changes([], False, add_direct=[d]))
    s.say(("ИСПРАВЛЕНО. " if ok else "НЕ ВЫШЛО. ") + f"{'в VPN' if kuda == 'vpn' else 'напрямую'}: {d} (вручную). " + info)
    log(f"сайт {d} {kuda}: {ok} {info}")
    out({"ok": ok, "info": info, "domen": d})


def server(imya):
    if imya != "AUTO":
        beda = s.karing_beda() or s.perezapusk_nelzya()
        if beda:
            out({"ok": False, "info": beda, "seychas": s.fast_current()}); return
        if imya not in s.vse_servery():
            out({"ok": False, "info": f"сервера «{imya}» нет в Karing — обновите подписку в Karing", "seychas": s.fast_current()}); return
    st = s.load_json(s.STATE, {})
    prev = {"fast_server": st.get("fast_server"), "failover_fast": st.get("failover_fast")}
    if imya == "AUTO":
        s_zamkom(lambda: None)
        zamer(); return
    st["fast_server"] = imya
    st["failover_fast"] = None
    s.save_json(s.STATE, st)
    ok = s_zamkom(lambda: s.switch_roles(f"Вручную: «остальное» на {imya}.", revert=prev, claude_moves=False))
    if not ok:
        st = s.load_json(s.STATE, {}); st.update(prev); s.save_json(s.STATE, st)
    log(f"сервер {imya}: {ok}")
    out({"ok": bool(ok), "seychas": s.fast_current(),
         **({} if ok else {"info": "переключение не удалось — через новый сервер связи нет, вернул как было"})})


def zamer():
    import skorost_serverov as k
    k.s.game_running = lambda state: []
    sys.argv = ["skorost_serverov.py", "--apply"]
    import io, contextlib
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        k.main()
    txt = buf.getvalue()
    with open(os.path.join(HERE, "skorost.log"), "a", encoding="utf-8") as f:
        f.write(f"=== {s.now()} (кнопка)\n" + txt)
    log("замер: " + txt.strip().splitlines()[-2:].__repr__())
    oshibka = next((x.split("ОШИБКА:", 1)[1].strip() for x in txt.splitlines() if "ОШИБКА:" in x), "")
    oshibka = re.sub(r"\s*— (возвращаю настройки как были|Karing не трогал)\s*$", "", oshibka)
    ok = "ВЫБРАН" in txt and not oshibka
    res = {"ok": ok, "vyvod": txt.splitlines()[-20:], "seychas": s.fast_current()}
    if not ok:
        res["info"] = oshibka or "замер не дошёл до выбора сервера — подробности в журнале замеров (skorost.log)"
    out(res)


def proverka():
    beda = s.karing_beda() or ("нет программы curl.exe (она есть в Windows 10 и 11) — сторож не может проверять сайты" if s.curl_net() else None)
    if beda:
        out({"ok": False, "info": beda, "otchet": tail(s.REPORT, 8)}); return
    s.game_running = lambda state: []
    st = s.load_json(s.STATE, {})
    st["probe_ts"] = 0   # проверить все программы сейчас, а не раз в час
    s.save_json(s.STATE, st)
    s.main()
    log("проверка по кнопке")
    out({"ok": True, "otchet": tail(s.REPORT, 8)})


def _oem(args):
    """Вывод консольных утилит Windows — в кодировке OEM (cp866), а не UTF-8."""
    try:
        return subprocess.run(args, capture_output=True, timeout=30, creationflags=0x08000000).stdout.decode("cp866", "replace")
    except Exception:
        return ""


class _R:
    def __init__(self, t): self.stdout = t


def _netstat_bayty():
    r = _R(_oem(["netstat", "-e"]))
    for x in (r.stdout.splitlines() if r else []):
        ch = re.findall(r"\d+", x)
        if len(ch) == 2 and re.match(r"\s*(Bytes|Байт)", x):
            return int(ch[0]), int(ch[1])
    return None


def set_seychas():
    """Сколько качает весь компьютер сейчас (сетевая карта, 2 с) и сколько из этого идёт через VPN (Karing)."""
    def vpn_snap():
        v = 0
        try:
            for c in clash("/connections").get("connections", []):
                if (c.get("chains") or ["direct_out"])[0] not in ("direct_out", "block_out"):
                    v += c.get("download", 0) + c.get("upload", 0)
        except Exception:
            pass
        return v
    a, va = _netstat_bayty(), vpn_snap()
    time.sleep(2)
    b, vb = _netstat_bayty(), vpn_snap()
    if not a or not b:
        out({"ok": False, "info": "netstat не ответил"}); return
    vniz = max(0, (b[0] - a[0]) % (1 << 32)) / 2 / 1e6
    vverh = max(0, (b[1] - a[1]) % (1 << 32)) / 2 / 1e6
    out({"ok": True, "vniz": round(vniz, 2), "vverh": round(vverh, 2), "vpn": round(max(0, vb - va) / 2 / 1e6, 2)})


def test_seti():
    """Тест скорости: 25 МБ с Cloudflare через VPN и напрямую, плюс задержка. Кнопку нажимает человек."""
    if s.curl_net():
        out({"ok": False, "info": "нет программы curl.exe (она есть в Windows 10 и 11) — мерить скорость нечем"}); return
    url = "https://speed.cloudflare.com/__down?bytes=25000000"
    def zamer(prox):
        a = ["curl.exe", "-s", "-o", "NUL", "-m", "25", "-w", "%{http_code} %{speed_download} %{time_connect}"]
        a += (["-x", prox] if prox else ["--noproxy", "*"]) + [url]
        r = s.run(a, timeout=35)
        p = (r.stdout.split() if r else [])
        if len(p) == 3 and p[0] == "200":
            return {"mbit": round(float(p[1]) * 8 / 1e6, 1), "ping": round(float(p[2]) * 1000)}
        return {"mbit": 0, "ping": None}
    vpn = zamer(s.VPN) if s.port_up(s._PV) else {"mbit": 0, "ping": None}
    pryamo = zamer(None)
    st = s.load_json(s.STATE, {})
    ist = st.get("test_seti", [])[-19:]
    ist.append([datetime.now().strftime("%d.%m %H:%M"), vpn["mbit"], pryamo["mbit"]])
    st["test_seti"] = ist
    s.save_json(s.STATE, st)
    log(f"тест сети: VPN {vpn}, напрямую {pryamo}")
    res = {"ok": True, "vpn": vpn, "pryamo": pryamo, "server": s.fast_current(), "istoriya": ist}
    if not vpn["mbit"]:
        res["info"] = s.karing_beda() or "через VPN ничего не скачалось — сервер не отвечает"
    out(res)


def programmy_vse():
    """Все программы с открытыми соединениями (netstat), а не только те, что идут через Karing."""
    r = _R(_oem(["netstat", "-ano", "-p", "tcp"]))
    pidy = {}
    for x in (r.stdout.splitlines() if r else []):
        p = x.split()
        if len(p) == 5 and p[3] in ("ESTABLISHED", "УСТАНОВЛЕНО") and not p[2].startswith(("127.", "[::1]")):
            pidy[p[4]] = pidy.get(p[4], 0) + 1
    t = _R(_oem(["tasklist", "/fo", "csv", "/nh"]))
    imena = {}
    for x in (t.stdout.splitlines() if t else []):
        m = re.match(r'"([^"]+)","(\d+)"', x)
        if m:
            imena[m[2]] = m[1]
    vse = {}
    for pid, n in pidy.items():
        exe = imena.get(pid)
        if exe and exe.lower() not in ("system", "svchost.exe", "karing.exe", "karingservice.exe", "lsass.exe", "services.exe"):
            vse[exe] = vse.get(exe, 0) + n
    return vse


def servery_podrobno():
    """Для каждого сервера — вердикт простыми словами: работает / не работает / нестабильный / адрес меняется / медленный."""
    st = s.load_json(s.STATE, {})
    res = (st.get("speed_test") or {}).get("results", [])
    try:
        sc = s.server_scores(24)
    except Exception:
        sc = {}
    zhivye = set(s.candidates() or [])
    itog = {}
    for r in res:
        imya, sk, ping, adr = r[0], r[1], r[2], r[3]
        obr, minut = (sc.get(imya) or (0, 0, 0))[:2]
        prichiny = []
        if sk < 5 or (zhivye and imya not in zhivye):
            vid, slovo = "plohoy", "Не работает"
            prichiny.append("скорость почти ноль" if sk < 5 else "сейчас не отвечает")
        else:
            vid, slovo = "horoshiy", "Хороший"
            if adr > 1:
                vid, slovo = "nado_znat", "Адрес меняется"
                prichiny.append(f"за замер сменил {adr} адреса — сайты могут выкидывать из аккаунтов")
            if obr >= 20 or minut >= 20:
                vid, slovo = "plohoy" if obr >= 40 else "nado_znat", "Нестабильный"
                prichiny.append(f"за сутки обрывался {obr} раз, без ответа {minut} мин")
            if sk < 50:
                if vid == "horoshiy":
                    vid, slovo = "nado_znat", "Медленный"
                prichiny.append(f"скорость {sk} Мбит/с — ниже 50")
            if ping and ping > 150:
                prichiny.append(f"пинг {ping} мс — для игр и звонков много")
        if not prichiny:
            prichiny.append(f"{sk} Мбит/с, пинг {ping} мс, обрывов за сутки {obr}")
        itog[imya] = {"vid": vid, "slovo": slovo, "prichiny": prichiny, "obryvov": obr, "minut": minut}
    return itog


ZADANIYA = [("Storozh seti", "storozh.py", 10), ("Storozh VPN", "storozh.py --vpn", 2), ("Nablyudatel migany", "nablyudatel.py", 15)]


def zadaniya_est():
    r = s.run(["schtasks", "/query", "/tn", "Storozh seti"])
    return bool(r and r.returncode == 0)


def ustanovit_zadaniya():
    """Задания Планировщика от текущего пользователя (без прав администратора): сторож каждые 10 мин и при входе.
    Запускает pythonw.exe, что лежит рядом с текущим Python (у друга — встроенный в папке программы)."""
    pyw = s._python(okno=True)
    itog = []
    for imya, skript, minut in ZADANIYA:
        parts = skript.split(" ", 1)
        komanda = f'"{pyw}" "{os.path.join(HERE, parts[0])}"' + (" " + parts[1] if len(parts) > 1 else "")
        if len(komanda) > 261:   # больше Планировщик не принимает
            out({"ok": False, "info": "путь к папке сторожа слишком длинный для Планировщика Windows — перенесите папку "
                                      "программы поближе к корню диска (например, C:\\StorozhSeti) и нажмите ещё раз"})
            return
        r = s.run(["schtasks", "/create", "/f", "/tn", imya, "/sc", "minute", "/mo", str(minut), "/tr", komanda])
        itog.append(f"{imya}: {'да' if r and r.returncode == 0 else 'нет'}")
    log("задания: " + ", ".join(itog))
    ok = zadaniya_est()
    out({"ok": ok, "info": ", ".join(itog)})


# ---------- для «Живого монитора» ----------

SNIMOK = os.path.join(os.environ.get("TEMP", HERE), "storozh-seti-snimok.json")


def skorosti():
    """Нагрузка каждой программы сейчас, МБ/с. Счётчики байтов Karing — разница с прошлым опросом
    (окно спрашивает раз в 5 с; прошлый снимок лежит в TEMP). Первый раз — два снимка с паузой 2 с."""
    def snap():
        d = {}
        for c in clash("/connections").get("connections", []):
            exe = (c["metadata"].get("processPath") or "").split("\\")[-1]
            if exe:
                d[c["id"]] = [exe, c.get("download", 0) + c.get("upload", 0), (c.get("chains") or ["?"])[0]]
        return d
    sejchas = time.time()
    proshl = s.load_json(SNIMOK, None)
    try:
        if not proshl or sejchas - proshl.get("t", 0) > 30:
            proshl = {"t": time.time(), "s": snap()}
            time.sleep(2)
            sejchas = time.time()
        b = snap()
    except s.KaringNet as e:
        out({"skorosti": {}, "ok": False, "info": str(e)}); return
    s.save_json(SNIMOK, {"t": sejchas, "s": b})
    dt = max(0.5, sejchas - proshl["t"])
    a = proshl["s"]
    sk = {}
    for k, (exe, v, kuda) in b.items():
        x = sk.setdefault(exe, {"bait": 0, "kuda": set()})
        bylo = a.get(k)
        x["bait"] += max(0, v - (bylo[1] if bylo else 0))   # новое соединение — всё, что оно успело скачать
        x["kuda"].add(kuda)
    out({"skorosti": {e: {"mbayt": round(x["bait"] / dt / 1e6, 3),
                          "kuda": ", ".join(sorted("напрямую" if k == "direct_out" else k for k in x["kuda"]))} for e, x in sk.items()}})


def proverit_sayt(domen):
    d = re.sub(r"^https?://", "", domen.strip().lower()).split("/")[0]
    if s.curl_net():
        out({"ok": False, "info": "нет программы curl.exe (она есть в Windows 10 и 11) — проверить сайт нечем"}); return
    url = "https://" + d + "/"
    kp, tp = s.direct(url, 12)
    kv, tv = s.curl(url, s.VPN, 12)
    def ocenka(k, t):
        if not s.ok(k):
            return "не открывается"
        return "быстро" if t < 1.5 else ("открывается, но тормозит" if t < 8 else "очень медленно")
    sovet, deystvie = "", ""
    pr_ok, vpn_ok = s.ok(kp), s.ok(kv)
    if vpn_ok and (not pr_ok or tp > max(2.0, tv * 2.5)):
        sovet = "пустить через VPN" + (f" — в {tp / max(tv, 0.05):.0f} раз быстрее" if pr_ok else " — напрямую не открывается"); deystvie = "vpn"
    elif pr_ok and (not vpn_ok or tv > max(2.0, tp * 2.5)):
        sovet = "пустить напрямую" + (" — через VPN сайт отказывает" if not vpn_ok else " — так быстрее"); deystvie = "direct"
    elif pr_ok and vpn_ok:
        sovet = "ничего не менять — открывается и так"
    else:
        sovet = "не открывается нигде — похоже, сайт сам не работает"
    log(f"проверка сайта {d}: напрямую {kp} {tp:.1f} с, VPN {kv} {tv:.1f} с")
    out({"ok": True, "domen": d, "napryamuyu": [kp, round(tp, 2), ocenka(kp, tp)], "vpn": [kv, round(tv, 2), ocenka(kv, tv)],
         "server": s.fast_current(), "sovet": sovet, "deystvie": deystvie, "uzhe_vpn": s.covered_by_vpn(d), "uzhe_napr": s.covered_by_direct(d)})


def sayty_istoriya():
    """Что сторож делал с сайтами: из отчёта, свежие сверху."""
    sp = []
    for x in reversed(tail(s.REPORT, 400)):
        m = re.match(r"\[([\d.]+ [\d:]+)\] (?:ИСПРАВЛЕНО\. )(.*)", x)
        if not m:
            continue
        for chast in m[2].split(" | "):
            for vid, pref in (("vpn", "в VPN: "), ("direct", "напрямую (через VPN сайт отказывает): "), ("vpn", "в VPN: ")):
                if chast.startswith(pref):
                    for d in re.split(r",\s*", chast[len(pref):].split(".")[0] if False else chast[len(pref):].rstrip(".")):
                        d = d.split(" ")[0].strip().rstrip(".")
                        if d and "." in d and not any(z["domen"] == d for z in sp):
                            sp.append({"domen": d, "vid": vid, "kogda": m[1]})
        if len(sp) >= 30:
            break
    out({"sayty": sp})


def vernut(domen):
    ok, info = s_zamkom(lambda: s.apply_changes([], False, remove=[domen]))
    s.say(("ИСПРАВЛЕНО. " if ok else "НЕ ВЫШЛО. ") + f"вернул как было: {domen} (вручную). " + info)
    log(f"вернуть {domen}: {ok} {info}")
    out({"ok": ok, "info": info})


def zapret_sost():
    kod, t = s.direct("https://www.youtube.com/generate_204", 8)
    golos = discord_golos()
    ver = ""
    try:
        ver = os.path.basename(next(iter(sorted(
            [p for p in os.listdir(zagruzki()) if p.startswith("zapret-discord-youtube-")],
            key=lambda n: [int(x) for x in re.findall(r"\d+", n)])[-1:]), ""))
    except OSError:
        pass
    posl = ""
    try:
        import urllib.request
        rq = urllib.request.Request("https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest", headers={"User-Agent": "storozh-seti"})
        posl = json.load(urllib.request.urlopen(rq, timeout=10)).get("tag_name", "").lstrip("v")
    except Exception as e:
        log(f"zapret: версия на GitHub не узнал: {e!r}")
    out({"youtube": [kod, round(t, 2), s.ok(kod)], "golos": golos, "versiya": ver.replace("zapret-discord-youtube-", ""), "posledn": posl})


def discord_golos():
    """Голос Discord за последние 2 минуты: пинги до голосового сервера — сколько дошло и с какой задержкой."""
    p = os.path.join(os.environ.get("APPDATA", ""), "discord", "logs", "discord-webrtc_0")
    try:
        with open(p, "rb") as f:
            f.seek(0, 2); f.seek(max(0, f.tell() - 400000))
            stroki = f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return {"est": False}
    gran = datetime.now() - timedelta(minutes=2)
    otpr, prish, rtt = set(), set(), []
    for x in stroki:
        m = re.match(r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)", x)
        if not m or datetime.strptime(m[1], "%Y-%m-%d %H:%M:%S") < gran:
            continue
        a = re.search(r"Sending ping to .*seq: (\d+)", x)
        if a: otpr.add(a[1])
        b = re.search(r"Ping received from .*seq: (\d+), rtt: (\d+)", x)
        if b: prish.add(b[1]); rtt.append(int(b[2]))
    if not otpr:
        return {"est": False}
    poter = round(100 * len(otpr - prish) / len(otpr), 1)
    return {"est": True, "poteri": poter, "ping": round(sum(rtt) / len(rtt)) if rtt else None, "zamerov": len(otpr)}


def lenta():
    """Журнал одной лентой: отчёт сторожа, обрывы, действия в окне."""
    sob = []
    for x in tail(s.REPORT, 200):
        m = re.match(r"\[(\d\d)\.(\d\d)\.\d{4} (\d\d:\d\d)\] (.*)", x)
        if m:
            t = m[4]
            vid = "ispr" if t.startswith("ИСПРАВЛЕНО") else "zamer" if t.startswith("Замер") else "info"
            sob.append([f"{m[1]}.{m[2]} {m[3]}", vid, t.replace("ИСПРАВЛЕНО. ", "Исправлено: ")])
    for x in tail(os.path.join(HERE, "mignaniya.log"), 200):
        m = re.match(r"\[(\d\d\.\d\d) (\d\d:\d\d):\d\d\] сбой (\d+) с — (.*)", x)
        if m:
            sob.append([f"{m[1]} {m[2]}", "obryv", f"VPN не отвечал {m[3]} с ({m[4].replace('VPN-сервер: ', '')})"])
    # журнал окна: у него — storozh-okno\storozh-okno.log рядом, в выпуске для друга — рядом с StorozhSeti.exe
    okno = next((p for p in (os.path.join(os.path.dirname(HERE), "storozh-okno", "storozh-okno.log"),
                             os.path.join(os.path.dirname(HERE), "storozh-okno.log")) if os.path.exists(p)), "")
    for x in tail(okno, 300):
        m = re.match(r"\[(\d\d\.\d\d) (\d\d:\d\d):\d\d\] (Готово: |Не вышло: |настройка |скрипт: )(.*)", x)
        if m:
            sob.append([f"{m[1]} {m[2]}", "moi", {"Готово: ": "Готово — ", "Не вышло: ": "Не вышло — ",
                                                  "настройка ": "Настройка: ", "скрипт: ": "Запущен "}[m[3]] + m[4]])
    def klyuch(e):
        d, t = e[0].split(" ")
        return (d[3:5], d[0:2], t)
    sob.sort(key=klyuch, reverse=True)
    out({"lenta": sob[:150]})


def monitor_svodka():
    """Длиннейший обрыв и время без VPN за сутки."""
    gran = datetime.now() - timedelta(days=1)
    dl, vsego = 0, 0
    for x in tail(os.path.join(HERE, "mignaniya.log"), 400):
        m = re.match(r"\[(\d\d)\.(\d\d) (\d\d):(\d\d):\d\d\] сбой (\d+) с", x)
        if m:
            d = datetime(datetime.now().year, int(m[2]), int(m[1]), int(m[3]), int(m[4]))
            if d >= gran:
                dl = max(dl, int(m[5])); vsego += int(m[5])
    return {"samyy_dlinnyy": dl, "bez_vpn": vsego}


def ponyatno(e):
    """Исключение — фразой для человека. Неожиданные ошибки остаются видны как «внутренняя ошибка» (их надо чинить)."""
    import urllib.error
    if isinstance(e, s.KaringNet):
        return str(e)
    if isinstance(e, (ConnectionError, TimeoutError, urllib.error.URLError)):
        return s.karing_beda() or "Karing не отвечает — перезапустите его и повторите"
    if isinstance(e, PermissionError):
        return "нет доступа к файлу — папка сторожа только для чтения или файл занят другой программой (подробности в pult.log)"
    return f"внутренняя ошибка сторожа: {e!r} (подробности в pult.log)"


KOMANDY = {  # команда: (функция, сколько параметров нужно)
    "sostoyanie": (sostoyanie, 0), "programmy": (programmy, 0), "servery": (servery, 0),
    "nastroyki-zadat": (nastroyki_zadat, 1), "primenit-marshruty": (primenit_marshruty, 0),
    "sayt": (sayt, 2), "server": (server, 1), "zamer": (zamer, 0), "proverka": (proverka, 0),
    "skorosti": (skorosti, 0), "proverit-sayt": (proverit_sayt, 1), "sayty-istoriya": (sayty_istoriya, 0),
    "vernut": (vernut, 1), "zapret": (zapret_sost, 0), "lenta": (lenta, 0), "ustanovit-zadaniya": (ustanovit_zadaniya, 0),
    "set-seychas": (set_seychas, 0), "test-seti": (test_seti, 0)}


if __name__ == "__main__":
    a = sys.argv[1:]
    try:
        if not a or a[0] not in KOMANDY:
            out({"ok": False, "info": f"неизвестная команда «{a[0] if a else ''}»"})
        elif len(a) - 1 < KOMANDY[a[0]][1]:
            out({"ok": False, "info": f"команде «{a[0]}» не хватает параметров"})
        else:
            fn, n = KOMANDY[a[0]]
            fn(*a[1:1 + n])
    except Exception as e:
        log(f"ошибка {a}: {e!r}\n{traceback.format_exc()}")
        out({"ok": False, "info": ponyatno(e)})

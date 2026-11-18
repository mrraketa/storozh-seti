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
import json, os, re, sys, time, subprocess
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
    import urllib.request
    core = s.load_json(s.CORE, {})
    api = core.get("experimental", {}).get("clash_api", {})
    req = urllib.request.Request("http://" + api["external_controller"] + path,
                                 headers={"Authorization": "Bearer " + api.get("secret", "")})
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    return json.load(op.open(req, timeout=5))


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
    karing = s.port_up()
    zq = s.run(["sc", "query", "zapret"])
    zapret = bool(zq and "RUNNING" in zq.stdout)
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
        bedy.append("Karing не работает")
    if not zapret:
        bedy.append("zapret не работает")
    if not zadaniya_est():
        bedy.append("Сторож не запускается сам — включите проверку каждые 10 минут")
    if st.get("pending"):
        bedy.append("Пустить через VPN: " + ", ".join(st["pending"]) + " — напрямую не открывается или тормозит")
    out({
        "karing": karing, "zapret": zapret, "strategiya": strat,
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
        log(f"программы: {e!r}")
    for exe in nas:
        agg.setdefault(exe, {"exe": exe, "kuda": set(), "kb": 0, "soed": 0})
    spisok = []
    for a in sorted(agg.values(), key=lambda x: -x["kb"]):
        kuda = sorted(a["kuda"])
        spisok.append({"exe": a["exe"], "seychas": ", ".join("напрямую" if k == "direct_out" else k for k in kuda) or "—",
                       "kb": a["kb"], "marshrut": nas.get(a["exe"], "avto")})
    out({"programmy": spisok, "servery": serv_list()})


def serv_list():
    try:
        return s.candidates()
    except Exception:
        return []


def servery():
    st = s.load_json(s.STATE, {})
    out({"zamer": st.get("speed_test") or {}, "servery": serv_list(), "seychas": s.fast_current(),
         "vybor": st.get("fast_server"), "claude": s.CLAUDE_PIN or "как всё остальное"})


def nastroyki_zadat(js):
    d = nastroyki()
    d.update(json.loads(js))
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
    out({"ok": bool(ok), "seychas": s.fast_current()})


def zamer():
    import skorost_serverov as k
    k.s.game_running = lambda state: []
    sys.argv = ["skorost_serverov.py", "--apply"]
    import io, contextlib
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        k.main()
    with open(os.path.join(HERE, "skorost.log"), "a", encoding="utf-8") as f:
        f.write(f"=== {s.now()} (кнопка)\n" + buf.getvalue())
    log("замер: " + buf.getvalue().strip().splitlines()[-2:].__repr__())
    out({"ok": "ВЫБРАН" in buf.getvalue(), "vyvod": buf.getvalue().splitlines()[-20:], "seychas": s.fast_current()})


def proverka():
    s.game_running = lambda state: []
    st = s.load_json(s.STATE, {})
    st["probe_ts"] = 0   # проверить все программы сейчас, а не раз в час
    s.save_json(s.STATE, st)
    s.main()
    log("проверка по кнопке")
    out({"ok": True, "otchet": tail(s.REPORT, 8)})


ZADANIYA = [("Storozh seti", "storozh.py", 10), ("Storozh VPN", "storozh.py --vpn", 2), ("Nablyudatel migany", "nablyudatel.py", 15)]


def zadaniya_est():
    r = s.run(["schtasks", "/query", "/tn", "Storozh seti"])
    return bool(r and r.returncode == 0)


def ustanovit_zadaniya():
    """Задания Планировщика от текущего пользователя (без прав администратора): сторож каждые 10 мин и при входе."""
    pyw = sys.executable.replace("python.exe", "pythonw.exe")
    itog = []
    for imya, skript, minut in ZADANIYA:
        parts = skript.split(" ", 1)
        komanda = f'"{pyw}" "{os.path.join(HERE, parts[0])}"' + (" " + parts[1] if len(parts) > 1 else "")
        r = s.run(["schtasks", "/create", "/f", "/tn", imya, "/sc", "minute", "/mo", str(minut), "/tr", komanda])
        itog.append(f"{imya}: {'да' if r and r.returncode == 0 else 'нет'}")
    log("задания: " + ", ".join(itog))
    ok = zadaniya_est()
    out({"ok": ok, "info": ", ".join(itog)})


# ---------- для «Живого монитора» ----------

def skorosti():
    """Скорость каждой программы сейчас: два снимка соединений Karing с паузой 2 с."""
    def snap():
        d = {}
        for c in clash("/connections").get("connections", []):
            exe = (c["metadata"].get("processPath") or "").split("\\")[-1]
            if exe:
                d[c["id"]] = (exe, c.get("download", 0) + c.get("upload", 0), (c.get("chains") or ["?"])[0])
        return d
    a = snap(); time.sleep(2); b = snap()
    sk = {}
    for k, (exe, v, kuda) in b.items():
        x = sk.setdefault(exe, {"bait": 0, "kuda": set()})
        x["bait"] += max(0, v - a.get(k, (exe, v, kuda))[1])
        x["kuda"].add(kuda)
    out({"skorosti": {e: {"mbit": round(x["bait"] * 8 / 2 / 1e6, 2),
                          "kuda": ", ".join(sorted("напрямую" if k == "direct_out" else k for k in x["kuda"]))} for e, x in sk.items()}})


def proverit_sayt(domen):
    d = re.sub(r"^https?://", "", domen.strip().lower()).split("/")[0]
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
            [p for p in os.listdir(os.path.join(os.path.expanduser("~"), "Downloads")) if p.startswith("zapret-discord-youtube-")],
            key=lambda n: [int(x) for x in re.findall(r"\d+", n)])[-1:]), ""))
    except OSError:
        pass
    out({"youtube": [kod, round(t, 2), s.ok(kod)], "golos": golos, "versiya": ver.replace("zapret-discord-youtube-", "")})


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
    okno = os.path.join(os.path.dirname(HERE), "storozh-okno", "storozh-okno.log")
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


if __name__ == "__main__":
    a = sys.argv[1:]
    try:
        {"sostoyanie": sostoyanie, "programmy": programmy, "servery": servery,
         "nastroyki-zadat": lambda: nastroyki_zadat(a[1]), "primenit-marshruty": primenit_marshruty,
         "sayt": lambda: sayt(a[1], a[2]), "server": lambda: server(a[1]),
         "zamer": zamer, "proverka": proverka, "skorosti": skorosti, "proverit-sayt": lambda: proverit_sayt(a[1]),
         "sayty-istoriya": sayty_istoriya, "vernut": lambda: vernut(a[1]), "zapret": zapret_sost, "lenta": lenta, "ustanovit-zadaniya": ustanovit_zadaniya}[a[0]]()
    except Exception as e:
        log(f"ошибка {a}: {e!r}")
        out({"ok": False, "info": f"ошибка: {e}"})

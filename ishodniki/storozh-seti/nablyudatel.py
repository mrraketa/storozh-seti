"""Наблюдатель за миганиями: каждые 5 секунд отдельно проверяет роутер, прямой интернет,
Karing и оба VPN-сервера (Claude и «скорость»). Пишет только сбои: когда начался, сколько длился,
что именно пропало — и по этому понятно, кто виноват: линия провайдера или VPN.
Ничего не меняет. Журнал: mignaniya.log рядом; итог за сутки — строкой в otchet.txt."""
import os, re, socket, sys, time
from datetime import datetime
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # встроенный Python не кладёт папку скрипта в sys.path
import storozh as s

LOG = os.path.join(s.HERE, "mignaniya.log")
PERIOD = 3
DIRECT = [("77.88.55.242", 443), ("87.250.250.242", 443)]   # Яндекс, напрямую мимо Karing
ROUTER = None   # (адрес, порт) роутера; None — проверить нельзя, считаем, что он есть


def nayti_router():
    """Роутер у всех свой (192.168.0.1, 192.168.1.1, 10.0.0.1 …) — берём основной шлюз Windows и порт, на котором
    он отвечает. Не отвечает ни на 80, ни на 443 — роутер не проверяем (иначе каждые 5 с «сбой роутера»)."""
    r = s.run(["route", "print", "-4", "0.0.0.0"])
    shlyuzy = []   # (метрика, адрес): маршрутов по умолчанию бывает несколько (виртуальные сети) — берём главный
    for x in (r.stdout.splitlines() if r else []):
        p = x.split()
        if len(p) == 5 and p[0] == "0.0.0.0" and p[1] == "0.0.0.0" and re.fullmatch(r"\d+\.\d+\.\d+\.\d+", p[2]) and p[4].isdigit():
            shlyuzy.append((int(p[4]), p[2]))
    if shlyuzy:
        gw = min(shlyuzy)[1]
        for port in (80, 443):
            if tcp(gw, port, 1.5):
                return (gw, port)
        return None
    return ("192.168.1.1", 80) if tcp("192.168.1.1", 80, 1.5) else None


def tcp(host, port, t=2.0):
    try:
        with socket.create_connection((host, port), timeout=t):
            return True
    except OSError:
        return False


def probe():
    """Все проверки одновременно, чтобы шаг был короче самих миганий."""
    from concurrent.futures import ThreadPoolExecutor
    tags = list(dict.fromkeys(s.role_server(r) for r in ("claude", "fast")))
    with ThreadPoolExecutor(8) as ex:
        f_router = ex.submit(tcp, ROUTER[0], ROUTER[1], 1.5) if ROUTER else None
        f_direct = [ex.submit(tcp, h, p) for h, p in DIRECT]
        f_karing = ex.submit(s.port_up)
        f_srv = {t: ex.submit(s.delay, t, 2500) for t in tags}
        karing = f_karing.result()
        return {"router": f_router.result() if f_router else True, "direct": any(f.result() for f in f_direct), "karing": karing,
                "servers": {t: (f.result() is not None) for t, f in f_srv.items()} if karing else {}}


def verdict(bad):
    if not bad["router"]:
        return "роутер или кабель до него"
    if not bad["direct"]:
        return "линия провайдера (пропал и прямой интернет)"
    if not bad["karing"]:
        return "Karing не работал (перезапуск)"
    dead = [t for t, ok in bad["servers"].items() if not ok]
    if dead:
        return "VPN-сервер: " + ", ".join(dead) + " (прямой интернет при этом был)"
    return "непонятно"


def raz(n):
    """«1 раз», «2 раза», «5 раз»."""
    return f"{n} раза" if n % 10 in (2, 3, 4) and n % 100 not in (12, 13, 14) else f"{n} раз"


def prichina_slovami(v):
    """Причина обрыва для человека, без скобок: «VPN-сервер: Англия (прямой интернет при этом был)» → «сервер Англия»."""
    if v.startswith("VPN-сервер: "):
        return "сервер " + v[len("VPN-сервер: "):].split(" (")[0]
    if v.startswith("Karing не работал"):
        return "Karing не работал"
    if v.startswith("линия провайдера"):
        return "пропал интернет у провайдера"
    return v


def day_summary(day):
    """Сколько сбоев каждого вида записано в журнал за этот день."""
    tag, out = day.strftime("[%d.%m "), {}
    try:
        for line in open(LOG, encoding="utf-8"):
            if line.startswith(tag) and " сбой " in line:
                v = re.sub(r"\s*\(.*\)\s*$", "", line.split(" — ", 1)[-1].strip())
                out[v] = out.get(v, 0) + 1
    except OSError:
        pass
    return out


def write(line):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


# ---------- статистика по всем серверам (для выбора сервера Claude) ----------

STATS = os.path.join(s.HERE, "server_stats.json")
SCAN_EVERY = 60          # раз в минуту — все серверы
KEEP_HOURS = 48


def full_scan_loop():
    """Раз в минуту пингует все основные серверы и копит почасовую статистику:
    [ответил, не ответил, обрывов (переходов «жив → нет»), сумма пинга мс]."""
    from concurrent.futures import ThreadPoolExecutor
    last_ok = {}
    while True:
        t0 = time.time()
        try:
            if s.port_up() and not s.manual_global():
                tags = s.candidates()
                with ThreadPoolExecutor(8) as ex:
                    res = dict(zip(tags, ex.map(lambda t: s.delay(t, 2500), tags)))
                hour = datetime.now().strftime("%Y-%m-%d %H")
                st = s.load_json(STATS, {})
                b = st.setdefault(hour, {})
                for t, d in res.items():
                    row = b.setdefault(t, [0, 0, 0, 0])
                    if d is None:
                        row[1] += 1
                        if last_ok.get(t, True):
                            row[2] += 1
                    else:
                        row[0] += 1
                        row[3] += int(d)
                    last_ok[t] = d is not None
                for h in sorted(st)[:-KEEP_HOURS]:
                    del st[h]
                s.save_json(STATS, st)
        except Exception as e:
            write(f"[{s.now()}] обход серверов: ошибка {e!r}")
        time.sleep(max(5, SCAN_EVERY - (time.time() - t0)))


def main():
    import threading
    global ROUTER
    ROUTER = nayti_router()
    threading.Thread(target=full_scan_loop, daemon=True).start()
    write(f"[{s.now()}] наблюдатель запущен")
    start, worst, day, counts, nfail = None, None, datetime.now().date(), {}, 0
    while True:
        p = probe()
        ok = p["router"] and p["direct"] and p["karing"] and all(p["servers"].values())
        now = datetime.now()
        if not ok:
            nfail += 1
            if start is None:
                start, worst = now, p
            else:  # запоминаем самое «глубокое» состояние сбоя
                for k in ("router", "direct", "karing"):
                    worst[k] = worst[k] and p[k]
                for t, v in p["servers"].items():
                    worst["servers"][t] = worst["servers"].get(t, True) and v
        elif start is not None:
            if nfail >= 2:  # одиночная неудача — шум, не сбой
                dur = (now - start).total_seconds()
                v = verdict(worst)
                write(f"[{start:%d.%m %H:%M:%S}] сбой {dur:.0f} с — {v}")
                counts[v] = counts.get(v, 0) + 1
            start = worst = None
            nfail = 0
        if now.date() != day:
            daily = day_summary(day)  # по журналу на диске — перезапуск наблюдателя счётчик не обнуляет
            if daily:
                s.say("Обрывы VPN за " + day.strftime("%d.%m") + ": " +
                      "; ".join(f"{prichina_slovami(k)} — {raz(n)}" for k, n in sorted(daily.items(), key=lambda x: -x[1])) + ".")
            else:
                s.say(f"Обрывы VPN за {day:%d.%m}: ни одного.")
            day, counts = now.date(), {}
        time.sleep(PERIOD)


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        write(f"[{s.now()}] наблюдатель упал: {e!r}")

"""Замер скорости VPN-серверов Karing одним перезапуском: временные правила по исходящему порту
(порт 40001 -> сервер 1, 40002 -> сервер 2 ...), скачивание через каждый, затем правила убираются.
Запуск: python skorost_serverov.py [--apply]  (--apply — назначить самый быстрый на всё, кроме Claude)"""
import json, os, re, subprocess, sys, time
import storozh as s

for _st in (sys.stdout, sys.stderr):  # из Планировщика вывод в cp1251 — флажки 🇫🇮 роняли замер
    try:
        _st.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

TEST_PREFIX = "🧪 скорость "
HOSTS = [  # (название, схема, настоящее имя, IP, путь) — качаем по <схема>://k<N>-<ip>.nip.io с подменой Host
    ("OVH, Франция", "https", "proof.ovh.net", "141.95.207.211", "/files/100Mb.dat"),
]


IPECHO = ("ifconfig.me", "https", "ifconfig.me", None, "/ip")   # отдаёт адрес выхода; по HTTPS — чтобы маршрут шёл по имени-двойнику, а не по заголовку Host
IP_TRIES = 6


def alias(i, ip):
    return f"k{i}-{ip.replace('.', '-')}.nip.io"
BYTES = 50_000_000


def set_test_groups(servers):
    rg = s.load_json(os.path.join(s.KD, "karing_routing_group.json"), None)
    use = s.load_json(os.path.join(s.KD, "karing_subscribe_use.json"), None)
    groups = rg["items"][0]["groups"]
    groups[:] = [g for g in groups if not g["name"].startswith(TEST_PREFIX)]
    use["diversion_group"] = [g for g in use["diversion_group"]
                              if not g.get("diversion_name", "").startswith(TEST_PREFIX)]
    sub = s.sub_id(use)
    # тестовые правила — В НАЧАЛО списка: иначе правила по IP (Cloudflare, Google) перехватят их раньше
    test_g, test_d = [], []
    for i, tag in enumerate(servers, 1):
        name = f"{TEST_PREFIX}{i}"
        test_g.append({"groupid": "custom", "name": name, "type": "", "or": True,
                       "domain_suffix": [alias(i, h[3]) for h in HOSTS + [IPECHO]]})
        test_d.append({"diversion_groupid": "custom", "diversion_name": name,
                       "server_groupid": sub, "server_name": tag, "dns_servers": []})
    groups[:0] = test_g
    use["diversion_group"][:0] = test_d
    s.save_json(os.path.join(s.KD, "karing_routing_group.json"), rg)
    s.save_json(os.path.join(s.KD, "karing_subscribe_use.json"), use)


def restart():
    s.karing_kill()
    t0 = time.time()
    s.karing_start()
    return s.karing_wait(t0)


def resolve_hosts():
    """Узнать IP площадок (через Cloudflare DNS), чтобы все имена-двойники вели в одну точку."""
    out = []
    for name, scheme, real, ip, path in HOSTS:
        if not ip:
            r = s.run(["powershell", "-NoProfile", "-Command",
                       f"(Resolve-DnsName {real} -Type A -Server 1.1.1.1 -DnsOnly | Where-Object IPAddress | Select-Object -First 1).IPAddress"])
            ip = (r.stdout.strip() if r else "") or None
        if ip:
            out.append((name, scheme, real, ip, path))
    return out


def exit_ips(i):
    """Несколько отдельных соединений подряд: сколько разных адресов выхода у сервера.
    Если адрес прыгает, Google/YouTube показывают «подозрительный трафик»."""
    _, scheme, real, ip, path = IPECHO
    seen = set()
    for _ in range(IP_TRIES):
        r = s.run(["curl.exe", "-s", "-k", "-m", "8", "-x", s.RULES, "-H", f"Host: {real}", "-A", "curl/8",
                   f"{scheme}://{alias(i, ip)}{path}"], timeout=15)
        out = (r.stdout.strip() if r else "")
        if re.fullmatch(r"[0-9.]{7,15}", out):
            seen.add(out)
    return seen


def resolve_hosts_list(lst):
    global HOSTS
    saved, HOSTS = HOSTS, lst
    try:
        return resolve_hosts()
    finally:
        HOSTS = saved


def speed(i, host):
    _, scheme, real, ip, path = host
    args = ["curl.exe", "-s", "-m", "25", "-x", s.RULES, "-H", f"Host: {real}", "-r", f"0-{BYTES - 1}",
            "-o", "NUL", "-w", "%{http_code} %{size_download} %{speed_download}"]
    if scheme == "https":
        args.append("-k")  # имя-двойник не совпадает с сертификатом; файл тестовый, данных нет
    r = s.run(args + [f"{scheme}://{alias(i, ip)}{path}"], timeout=40)
    p = r.stdout.split() if r else []
    if len(p) == 3 and p[0] in ("200", "206") and int(float(p[1])) > 1_000_000:
        return float(p[2]) * 8 / 1e6
    return 0.0


def route_of(i):
    """Проверка, что правило сработало: через какой сервер ушло соединение к имени-двойнику."""
    import socket, urllib.request
    ctl, sec = s.clash()
    host = alias(i, HOSTS[0][3])
    sock = socket.create_connection(("127.0.0.1", 3067))
    sock.sendall(f"CONNECT {host}:80 HTTP/1.1\r\nHost: {host}:80\r\n\r\n".encode())
    sock.recv(100)
    time.sleep(0.5)
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    d = json.load(op.open(urllib.request.Request(f"http://{ctl}/connections",
                                                 headers={"Authorization": "Bearer " + sec}), timeout=5))
    sock.close()
    for c in d.get("connections") or []:
        if c["metadata"].get("host") == host:
            return (c.get("chains") or ["?"])[0]
    return None


def main():
    apply = "--apply" in sys.argv
    if s.game_running(s.load_json(s.STATE, {})):
        print("запущена игра — не трогаю"); return
    for _ in range(120):  # подождать, если сторож сейчас работает
        if not os.path.exists(s.LOCK):
            break
        time.sleep(5)
    open(s.LOCK, "w").close()
    backup = {n: open(os.path.join(s.KD, n), "rb").read() for n in s.KFILES}
    try:
        global HOSTS, IPECHO
        HOSTS = resolve_hosts()
        IPECHO = resolve_hosts_list([IPECHO])[0]
        print("площадки:", [(h[0], h[3]) for h in HOSTS])
        servers = [t for t in s.candidates() if s.delay(t, 3000)]
        print("живых серверов:", len(servers))
        if not servers:
            raise RuntimeError("в подписке Karing нет отвечающих серверов — проверьте, что подписка обновлена и Karing включён")
        set_test_groups(servers)
        if not restart():
            raise RuntimeError("Karing не поднялся после добавления тестовых правил")
        chk = route_of(1)
        print(f"проверка правила: имя-двойник №1 ушло через {chk}, ожидался {servers[0]}")
        if chk != servers[0]:
            raise RuntimeError("правило по имени не сработало")
        res = []
        for i, tag in enumerate(servers, 1):
            sp = [speed(i, h) for h in HOSTS]
            ping = s.delay(tag, 3000)
            ips = exit_ips(i)
            res.append((sum(sp) / len(sp), sp, ping, tag, sorted(ips)))
            print(f"  {tag:18} OVH {sp[0]:5.0f} Мбит/с  пинг {ping} мс  адресов выхода: {len(ips)} {sorted(ips)}", flush=True)
        res.sort(key=lambda r: r[0], reverse=True)
        sc = s.server_scores()
        stable = [r for r in res if len(r[4]) == 1 and r[0] > 0 and s.is_stable(r[3], sc)]  # быстрый, с постоянным адресом И без частых обрывов
        pick = stable[0] if stable else res[0]
        best = pick[3]
        print("ВЫБРАН:", best, "(самый быстрый с постоянным адресом)" if stable else "(стабильных нет — самый быстрый)")
        state = s.load_json(s.STATE, {})
        state["speed_test"] = {"when": s.now(), "results": [(t, round(m), p, len(ip)) for m, _, p, t, ip in res]}
        s.save_json(s.STATE, state)
        # убрать тестовые правила и назначить серверы
        set_test_groups([])
        if apply:
            state = s.load_json(s.STATE, {})
            state["fast_server"] = best
            s.save_json(s.STATE, state)
            s.assign_vpn_groups()
        ok = restart() and s.claude_ok()
        print("после перезапуска: Claude", "отвечает" if ok else "НЕ отвечает", "| Claude через", s.current_vpn_server())
        if not ok:
            raise RuntimeError("после применения Claude не отвечает")
        s.say(f"Замер VPN-серверов: выбран {best} ({pick[0]:.0f} Мбит/с, адрес выхода {'постоянный' if stable else 'ПРЫГАЕТ — стабильных нет'}); "
              f"стабильных серверов {len(stable)} из {len(res)}."
              + (" Назначен на всё, кроме Claude (Claude — " + s.home("claude") + ")." if apply else ""))
    except Exception as e:
        print("ОШИБКА:", e, "— возвращаю настройки как были")
        s.karing_kill()
        for n, b in backup.items():
            open(os.path.join(s.KD, n), "wb").write(b)
        t0 = time.time(); s.karing_start(); s.karing_wait(t0)
        print("возвращено, Claude:", s.claude_ok())
    finally:
        os.remove(s.LOCK)


if __name__ == "__main__":
    main()

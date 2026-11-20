"""Замер скорости VPN-серверов Karing одним перезапуском: временные правила по имени-двойнику
(k1-….nip.io -> сервер 1, k2-… -> сервер 2 ...), скачивание через каждый, затем правила убираются.
Запуск: python skorost_serverov.py [--apply]  (--apply — назначить самый быстрый на всё, кроме Claude;
у друга, где Claude не закреплён, — сделать его текущим сервером Karing)
Работает и на свежем Karing: сам создаёт нужные группы, в режиме «Глобально» на время замера включает
«Правила» и возвращает как было; если до правок видно, что замер невозможен, — Karing не трогает вовсе."""
import json, os, re, socket, subprocess, sys, time
from urllib.parse import urlparse
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # встроенный Python не кладёт папку скрипта в sys.path
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
MAX_SERVEROV = 20   # у друга в подписке может быть сотня серверов — меряем 20 самых отзывчивых, иначе замер на час


def alias(i, ip):
    return f"k{i}-{ip.replace('.', '-')}.nip.io"
BYTES = 50_000_000


class NeMenyal(Exception):
    """Замер невозможен, а Karing ещё не тронут — откатывать и перезапускать нечего."""


def set_test_groups(servers):
    p_rg, p_use = os.path.join(s.KD, "karing_routing_group.json"), os.path.join(s.KD, "karing_subscribe_use.json")
    rg = s.load_json(p_rg, None) or {"items": [], "rule_set_items": []}
    use = s.load_json(p_use, None) or {}
    groups = s.gruppy(rg)   # у свежего Karing групп «Самостоятельная настройка» может не быть — создаются
    groups[:] = [g for g in groups if not g.get("name", "").startswith(TEST_PREFIX)]
    div = use.setdefault("diversion_group", [])
    div[:] = [g for g in div if not g.get("diversion_name", "").startswith(TEST_PREFIX)]
    # тестовые правила — В НАЧАЛО списка: иначе правила по IP (Cloudflare, Google) перехватят их раньше
    test_g, test_d = [], []
    for i, tag in enumerate(servers, 1):
        name = f"{TEST_PREFIX}{i}"
        test_g.append({"groupid": "custom", "name": name, "type": "", "or": True,
                       "domain_suffix": [alias(i, h[3]) for h in HOSTS + ([IPECHO] if IPECHO and IPECHO[3] else [])]})
        test_d.append({"diversion_groupid": "custom", "diversion_name": name,
                       "server_groupid": s.sub_id(use, tag), "server_name": tag, "dns_servers": []})
    groups[:0] = test_g
    div[:0] = test_d
    s.save_json(p_rg, rg)
    s.save_json(p_use, use)


def rezhim_pravila(vklyuchit, bylo=None):
    """В «Глобально» Karing не применяет группы — на время замера включить «Правила», потом вернуть как было.
    Возвращает прежнее значение proxy_all."""
    p = os.path.join(s.KD, "karing_setting.json")
    st = s.load_json(p, None)
    if st is None:
        return None
    if vklyuchit:
        was = st.get("proxy_all")
        if was:
            st["proxy_all"] = False
            s.save_json(p, st)
        return was
    if bylo and not st.get("proxy_all"):
        st["proxy_all"] = bylo
        s.save_json(p, st)
    return bylo


def restart():
    s.karing_kill()
    t0 = time.time()
    if not s.karing_start():
        return False
    return s.karing_wait(t0)


def resolve_hosts():
    """Узнать IP площадок (через Cloudflare DNS, иначе обычный DNS), чтобы все имена-двойники вели в одну точку."""
    out = []
    for name, scheme, real, ip, path in HOSTS:
        if not ip:
            r = s.run(["powershell", "-NoProfile", "-Command",
                       f"(Resolve-DnsName {real} -Type A -Server 1.1.1.1 -DnsOnly | Where-Object IPAddress | Select-Object -First 1).IPAddress"])
            ip = (r.stdout.strip() if r else "") or None
            if not ip or not re.fullmatch(r"[0-9.]{7,15}", ip):
                try:
                    ip = socket.gethostbyname(real)
                except OSError:
                    ip = None
        if ip:
            out.append((name, scheme, real, ip, path))
    return out


def exit_ips(i):
    """Несколько отдельных соединений подряд: сколько разных адресов выхода у сервера.
    Если адрес прыгает, Google/YouTube показывают «подозрительный трафик». None — проверить не удалось."""
    if not IPECHO or not IPECHO[3]:
        return None
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


def pravilo_v_konfige(i, tag):
    """Karing собрал правило «🧪 скорость i» на нужный сервер — по его рабочему конфигу (надёжнее живой проверки)."""
    for r in (s.load_json(s.CORE, {}) or {}).get("route", {}).get("rules", []):
        if str(r.get("name", "")).startswith(f"{TEST_PREFIX}{i}["):
            return r.get("outbound") == tag
    return False


def route_of(i):
    """Живая проверка, что правило сработало: через какой сервер ушло соединение к имени-двойнику."""
    import urllib.request
    ctl, sec = s.clash()
    host = alias(i, HOSTS[0][3])
    u = urlparse(s.RULES)
    try:
        sock = socket.create_connection((u.hostname, u.port), timeout=10)
    except OSError:
        return None
    try:
        sock.sendall(f"CONNECT {host}:80 HTTP/1.1\r\nHost: {host}:80\r\n\r\n".encode())
        try:
            sock.recv(100)
        except OSError:
            pass
        time.sleep(0.5)
        op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        d = json.load(op.open(urllib.request.Request(f"http://{ctl}/connections",
                                                     headers={"Authorization": "Bearer " + sec}), timeout=5))
    except Exception:
        return None
    finally:
        sock.close()
    for c in d.get("connections") or []:
        if (c.get("metadata") or {}).get("host") == host:
            return (c.get("chains") or ["?"])[0]
    return None


def predproverka():
    """Почему замер сейчас невозможен (None — можно). Здесь Karing ещё не тронут."""
    beda = s.karing_beda()
    if beda:
        return beda
    if s.curl_net():
        return "нет программы curl.exe (она есть в Windows 10 и 11) — мерить скорость нечем"
    if not s.clash()[0] or not s.karing_api_alive():
        return "Karing не отвечает на служебные запросы — перезапустите Karing и повторите"
    if not s.candidates():
        return "в Karing нет серверов — добавьте подписку и обновите её"
    nelzya = s.perezapusk_nelzya()
    if nelzya:
        return "замеру нужно перезапустить Karing, а " + nelzya
    return None


def zhivye():
    """Отвечающие серверы (параллельно), не больше MAX_SERVEROV самых быстрых по пингу; порядок — как в подписке."""
    from concurrent.futures import ThreadPoolExecutor
    tags = s.candidates()
    with ThreadPoolExecutor(8) as ex:
        d = dict(zip(tags, ex.map(lambda t: s.delay(t, 3000), tags)))
    zh = [t for t in tags if d.get(t)]
    if len(zh) > MAX_SERVEROV:
        luchshie = set(sorted(zh, key=lambda t: d[t])[:MAX_SERVEROV])
        zh = [t for t in zh if t in luchshie]
    return zh


def main():
    global HOSTS, IPECHO
    apply = "--apply" in sys.argv
    if s.game_running(s.load_json(s.STATE, {})):
        print("запущена игра — не трогаю"); return
    beda = predproverka()
    if beda:
        print("ОШИБКА:", beda)
        return
    for _ in range(120):  # подождать, если сторож сейчас работает (блокировку старше 30 мин считаем брошенной)
        if not os.path.exists(s.LOCK) or time.time() - os.path.getmtime(s.LOCK) > 1800:
            break
        time.sleep(5)
    open(s.LOCK, "w").close()
    izmenil, backup, bylo_global = False, None, None
    try:
        backup = s._zapomnit()
        HOSTS = resolve_hosts()
        ip = resolve_hosts_list([IPECHO])
        IPECHO = ip[0] if ip else None
        print("площадки:", [(h[0], h[3]) for h in HOSTS], "" if IPECHO else "(адрес выхода проверить не смогу)")
        if not HOSTS:
            raise NeMenyal("не узнал адрес площадки для замера")
        servers = zhivye()
        print("живых серверов:", len(servers))
        if not servers:
            raise NeMenyal("ни один сервер Karing сейчас не отвечает — проверьте, что подписка обновлена и VPN в Karing включён")
        izmenil = True
        set_test_groups(servers)
        bylo_global = rezhim_pravila(True)
        if bylo_global:
            print("Karing в режиме «Глобально» — на время замера включаю «Правила», потом верну")
        if not restart():
            raise RuntimeError("Karing не поднялся после добавления тестовых правил")
        v_konfige = pravilo_v_konfige(1, servers[0])
        chk = route_of(1)
        print(f"проверка правила: в конфиге Karing {'есть' if v_konfige else 'НЕТ'}; имя-двойник №1 ушло через {chk}, ожидался {servers[0]}")
        if not v_konfige and chk != servers[0]:
            raise RuntimeError("Karing не применил тестовые правила — замер невозможен")
        res = []
        for i, tag in enumerate(servers, 1):
            sp = [speed(i, h) for h in HOSTS]
            ping = s.delay(tag, 3000)
            ips = exit_ips(i)
            res.append((sum(sp) / len(sp), sp, ping, tag, sorted(ips) if ips is not None else None))
            print(f"  {tag:18} OVH {sp[0]:5.0f} Мбит/с  пинг {ping} мс  адресов выхода: "
                  + (f"{len(ips)} {sorted(ips)}" if ips is not None else "не проверял"), flush=True)
        res.sort(key=lambda r: r[0], reverse=True)
        sc = s.server_scores()
        # быстрый, с постоянным адресом (или адрес не проверить) И без частых обрывов
        stable = [r for r in res if (r[4] is None or len(r[4]) == 1) and r[0] > 0 and s.is_stable(r[3], sc)]
        pick = stable[0] if stable else res[0]
        best = pick[3]
        if pick[0] <= 0:
            raise RuntimeError("ни через один сервер не удалось ничего скачать — замер не удался")
        print("ВЫБРАН:", best, "(самый быстрый с постоянным адресом)" if stable else "(стабильных нет — самый быстрый)")
        state = s.load_json(s.STATE, {})
        state["speed_test"] = {"when": s.now(), "results": [(t, round(m), p, len(ip) if ip is not None else 0) for m, _, p, t, ip in res]}
        s.save_json(s.STATE, state)
        # убрать тестовые правила, вернуть режим и назначить серверы
        set_test_groups([])
        rezhim_pravila(False, bylo_global)
        if apply:
            state = s.load_json(s.STATE, {})
            state["fast_server"] = best
            s.save_json(s.STATE, state)
            s.assign_vpn_groups()
        ok = restart() and s.svyaz_ok()
        if s.claude_vedu():
            print("после перезапуска: Claude", "отвечает" if ok else "НЕ отвечает", "| Claude через", s.current_vpn_server())
        else:
            print("после перезапуска: VPN", "работает" if ok else "НЕ работает", "| сейчас через", s.fast_current())
        if not ok:
            raise RuntimeError("после применения " + ("Claude не отвечает" if s.claude_vedu() else "VPN не работает"))
        if apply and not s.CLAUDE_PIN and s.fast_current() != best:
            print(f"внимание: Karing после перезапуска идёт через {s.fast_current()}, а не через {best}")
        if s.claude_vedu():
            dop = " Назначен на всё, кроме Claude (Claude — " + s.home("claude") + ")."
        else:
            dop = " Назначен текущим сервером Karing."
        s.say(f"Замер VPN-серверов: выбран {best} ({pick[0]:.0f} Мбит/с, адрес выхода {'постоянный' if stable else 'ПРЫГАЕТ — стабильных нет'}); "
              f"стабильных серверов {len(stable)} из {len(res)}." + (dop if apply else ""))
    except Exception as e:
        print("ОШИБКА:", e, "— возвращаю настройки как были" if izmenil else "— Karing не трогал")
        if izmenil:
            s.karing_kill()
            s._vernut(backup)
            t0 = time.time()
            s.karing_start()
            s.karing_wait(t0)
            print("возвращено, связь:", "есть" if s.svyaz_ok() else "НЕТ")
    finally:
        try:
            os.remove(s.LOCK)
        except OSError:
            pass


if __name__ == "__main__":
    main()

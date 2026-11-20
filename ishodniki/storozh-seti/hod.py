"""Ход долгого действия для окна «Сторож сети» (договор: storozh-okno\\DOGOVOR-hod.md).

Окно даёт путь к файлу хода в переменной окружения STOROZH_HOD. Нет её — модуль молчит: не пишет ни ход,
ни историю длительностей (плановые запуски из Планировщика работают как раньше). Любая ошибка записи
проглатывается: действие важнее его хода.

    hod.nachat("Замер серверов", ["Нахожу серверы", "Меряю скорость", ...], "zamer", [10, 120, ...])
    hod.shag(1)                         # номер шага с нуля или его имя из списка; назад не ходит
    hod.schet(7, 14, "Меряю скорость: 7 из 14")
    hod.tekst("Жду, пока Karing подключится")
    hod.konec(True, "Выбран Germany 1, 260 Мбит/с")   # первый konec побеждает, дальше файл не меняется

vesa — примерные секунды на каждый шаг: по ним идёт процент, пока нет истории прошлых запусков.
С историей (hod-vremya.json рядом, последние удачные запуски того же действия) процент и «осталось» — по ней.
"""
import atexit, json, os, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
ISTORIYA = os.path.join(HERE, "hod-vremya.json")
SKOLKO_ISTORII = 5      # сколько прошлых удачных запусков помнить на каждое действие
SERDCE = 1.5            # договор: писать не реже раза в 2 с

# Путь берём один раз и убираем из окружения: дочерние процессы (ночной замер из прохода сторожа и т.п.)
# не должны переписывать ход того действия, которое запустило окно.
PUT = os.environ.pop("STOROZH_HOD", "") or None

_zamok = threading.RLock()
_d = None               # то, что пишется в файл (ключи договора)
_v = {}                 # внутреннее: веса, начала шагов, счёт, «стоп» сердцебиения этого действия
_pauza = threading.Event()   # никогда не ставится — только для коротких пауз (time.sleep в проверке подменён)


def _seychas():
    return time.time()


def _mediana(x):
    x = sorted(x)
    n = len(x)
    return (x[n // 2] if n % 2 else (x[n // 2 - 1] + x[n // 2]) / 2) if n else 0.0


def _istoriya_chitat(klyuch, n):
    """Длительности шагов по прошлым удачным запускам (медиана) или None."""
    try:
        with open(ISTORIYA, encoding="utf-8") as f:
            zap = [z for z in (json.load(f).get(klyuch) or []) if len(z.get("shagi") or []) == n]
        if not zap:
            return None
        return [max(0.2, _mediana([float(z["shagi"][i]) for z in zap])) for i in range(n)]
    except Exception:
        return None


def _istoriya_zapisat(klyuch, vsego, shagi):
    try:
        try:
            with open(ISTORIYA, encoding="utf-8") as f:
                ist = json.load(f)
            if not isinstance(ist, dict):
                ist = {}
        except Exception:
            ist = {}
        zap = [z for z in (ist.get(klyuch) or []) if isinstance(z, dict)]
        zap.append({"vsego": round(vsego, 1), "shagi": [round(x, 1) for x in shagi], "kogda": round(_seychas())})
        ist[klyuch] = zap[-SKOLKO_ISTORII:]
        _zamenit(ISTORIYA, json.dumps(ist, ensure_ascii=False, indent=1), popytok=5)
    except Exception:
        pass


def _zamenit(put, tekst, popytok=3):
    """Атомарно: во временный файл рядом, потом замена. Окно может как раз читать файл — тогда повторить."""
    tmp = f"{put}.{os.getpid()}.tmp"
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            f.write(tekst)
        for i in range(popytok):
            try:
                os.replace(tmp, put)
                return True
            except PermissionError:
                _pauza.wait(0.05 * (i + 1))
    except Exception:
        pass
    try:
        os.remove(tmp)
    except OSError:
        pass
    return False


def _vesa():
    return _v["ist"] or _v["vesa"]


def _poschitat(t):
    """procent (не убывает) и ostalos по весам шагов, счёту внутри шага и прошедшему времени."""
    d, v = _d, _v
    n, k = len(d["shagi"]), d["shag"]
    w = _vesa()
    vsego = sum(w) or 1.0
    v_shage = t - v["nach"][k]
    dolya = 0.0
    if v["iz"] > 0:
        dolya = v["n"] / v["iz"]
        if v["n"] > 0:
            na_odin = (v["t_scheta"] - v["nach"][k]) / v["n"]
            if na_odin > 0:
                dolya += min(0.9, (t - v["t_scheta"]) / na_odin) / v["iz"]
    elif w[k] > 0:
        dolya = min(0.9, v_shage / w[k])
    dolya = min(1.0, dolya)
    p = int(100 * (sum(w[:k]) + w[k] * dolya) / vsego)
    p = max(d["procent"], min(99, p))
    ost = None
    if v["ist"]:
        if v["iz"] > 0 and v["n"] > 0:
            ost_shag = (v["t_scheta"] - v["nach"][k]) / v["n"] * (v["iz"] - v["n"]) - (t - v["t_scheta"])
        else:
            ost_shag = w[k] - v_shage
        ost = max(0.0, ost_shag) + sum(w[k + 1:])
    else:
        proshlo = t - d["nachalo"]
        if p >= 3 and proshlo >= 3:
            ost = proshlo * (100 - p) / p
    return p, (None if ost is None else max(0, int(round(ost))))


def _zapisat(sila=False):
    """Записать файл хода. Без sila — не чаще 4 раз в секунду (счёт из потоков бывает частым)."""
    if _d is None or not PUT:
        return
    t = _seychas()
    if not sila and t - _v.get("t_zapisi", 0) < 0.25:
        _v["nado"] = True
        return
    if not _d["konec"]:
        _d["procent"], _d["ostalos"] = _poschitat(t)
    _d["obnovleno"] = round(t, 3)
    _v["t_zapisi"], _v["nado"] = t, False
    _zamenit(PUT, json.dumps(_d, ensure_ascii=False), popytok=20 if _d["konec"] else 3)


def _serdce(stop):
    while not stop.wait(SERDCE):
        try:
            with _zamok:
                if not stop.is_set() and _d is not None and not _d["konec"]:
                    _zapisat(sila=True)
        except Exception:
            pass


def idet():
    """Действие начато в этом процессе и ещё не кончилось."""
    return _d is not None and not _d["konec"]


def nachat(chto, shagi, klyuch=None, vesa=None):
    """Начать ход. False — хода нет (нет STOROZH_HOD) или уже идёт внешнее действие (вложенный вызов ничего не меняет)."""
    global _d, _v
    if not PUT:
        return False
    try:
        with _zamok:
            if idet():
                return False
            shagi = [str(x) for x in shagi] or [str(chto)]
            n = len(shagi)
            klyuch = klyuch or chto
            vesa = [float(x) for x in vesa] if vesa and len(vesa) == n else [1.0] * n
            t = _seychas()
            _v = {"klyuch": klyuch, "vesa": [max(0.1, x) for x in vesa], "ist": _istoriya_chitat(klyuch, n),
                  "nach": [t] + [None] * (n - 1), "n": 0, "iz": 0, "t_scheta": t, "t_zapisi": 0, "nado": False,
                  "stop": threading.Event()}
            _d = {"chto": str(chto), "shagi": shagi, "shag": 0, "tekst": shagi[0], "procent": 0, "ostalos": None,
                  "nachalo": round(t, 3), "obnovleno": round(t, 3), "konec": False, "ok": None, "itog": ""}
            _zapisat(sila=True)
            threading.Thread(target=_serdce, args=(_v["stop"],), daemon=True, name="hod-serdce").start()
        return True
    except Exception:
        return False


def shag(nomer, tekst=None):
    """Перейти к шагу (номер с нуля или имя из списка). Шага с таким именем нет — меняется только текст;
    назад ход не идёт (откат после неудачи — это текст внутри текущего шага)."""
    if not idet():
        return
    try:
        with _zamok:
            d, v = _d, _v
            if isinstance(nomer, str):
                if nomer not in d["shagi"]:
                    d["tekst"] = tekst or nomer
                    _zapisat()
                    return
                nomer = d["shagi"].index(nomer)
            nomer = max(0, min(int(nomer), len(d["shagi"]) - 1))
            if nomer > d["shag"]:
                t = _seychas()
                for i in range(d["shag"] + 1, nomer + 1):
                    v["nach"][i] = t
                d["shag"] = nomer
                v["n"] = v["iz"] = 0
                v["t_scheta"] = t
                d["tekst"] = tekst or d["shagi"][nomer]
                _zapisat(sila=True)
            else:
                d["tekst"] = tekst or d["tekst"]
                _zapisat()
    except Exception:
        pass


def tekst(t):
    """Сменить подпись внутри текущего шага."""
    if not idet() or not t:
        return
    try:
        with _zamok:
            _d["tekst"] = str(t)
            _zapisat()
    except Exception:
        pass


def schet(n, iz, tekst=None):
    """Счёт внутри шага: «7 из 14». Процент шага растёт по счёту, «осталось» — по скорости счёта."""
    if not idet():
        return
    try:
        with _zamok:
            d, v = _d, _v
            n, iz = max(0, int(n)), max(0, int(iz))
            if iz and (iz != v["iz"] or n >= v["n"]):
                if n != v["n"]:
                    v["t_scheta"] = _seychas()
                v["n"], v["iz"] = min(n, iz), iz
            d["tekst"] = tekst or f"{d['shagi'][d['shag']]}: {n} из {iz}"
            _zapisat(sila=n >= iz)
    except Exception:
        pass


def konec(ok, itog):
    """Конец действия: konec true, ok, итог одной фразой. Повторный вызов ничего не меняет."""
    if not idet():
        return
    try:
        with _zamok:
            d, v = _d, _v
            t = _seychas()
            d.update(konec=True, ok=bool(ok), itog=str(itog or ("Готово" if ok else "Не вышло")).strip(),
                     procent=100, ostalos=0)
            if ok:
                d["shag"] = len(d["shagi"]) - 1
            v["stop"].set()
            _zapisat(sila=True)
            if ok:   # в историю — только удачные: неудачи обычно короткие и сбили бы оценку
                nach = list(v["nach"])
                for i in range(len(nach) - 1, -1, -1):   # не пройденные шаги — нулевой длины
                    if nach[i] is None:
                        nach[i] = nach[i + 1] if i + 1 < len(nach) else t
                kon = nach[1:] + [t]
                _istoriya_zapisat(v["klyuch"], t - d["nachalo"], [max(0.0, b - a) for a, b in zip(nach, kon)])
    except Exception:
        pass


@atexit.register
def _obryv():
    """Процесс выходит, а итога нет (упал мимо всех обработчиков) — сказать окну честно, а не молчать до «нет вестей»."""
    if idet():
        konec(False, "Действие оборвалось на полпути — подробности в журнале сторожа")

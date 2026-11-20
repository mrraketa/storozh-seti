// Разделы окна «Сторож сети» в виде «Живой монитор» (концепт 3, его выбор 18.11).
// Каждый раздел переживает любой ответ пульта: пустой, {"ok": false}, без ключей, с null — показывает, что случилось,
// а не падает и не остаётся пустым (проверка: --samoproverka, наборы «как у друга», «всё сломано», «кривые данные»).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

partial class Okno
{
    // ---------- общее ----------
    int Shir { get { return Math.Max(600, soderzh.ClientSize.Width - soderzh.Padding.Horizontal - 34); } }
    static List<object> Spisok(object o) { return o is IEnumerable && !(o is string) && !(o is IDictionary) ? ((IEnumerable)o).Cast<object>().ToList() : new List<object>(); }
    static List<List<object>> Spiski(Dictionary<string, object> d, string k) { return Pult.L(d, k).Where(o => o is IEnumerable && !(o is string) && !(o is IDictionary)).Select(Spisok).ToList(); }
    static string Vremya(int sek) { sek = Math.Max(0, sek); return sek >= 3600 ? (sek / 3600) + " ч " + (sek % 3600 / 60) + " мин" : sek >= 60 ? (sek / 60) + " мин " + (sek % 60) + " с" : sek + " с"; }
    Nadpis Zg(string t, int x, int y, float r = 11f) { return new Nadpis { Text = t, Font = new Font("Segoe UI Semibold", r), Location = new Point(x, y) }; }
    Nadpis Np(string t, int x, int y, bool vt = false, int maxW = 0) { var n = new Nadpis { Text = t, Location = new Point(x, y), Tag = vt ? "vt" : null }; if (maxW > 0) n.MaximumSize = new Size(maxW, 0); return n; }
    Nadpis Zk(string z, int x, int y, float r = 12f, object cvet = null) { return new Nadpis { Text = z, Font = Zn.Shrift(r), Location = new Point(x, y), Tag = cvet ?? "vt" }; }
    // одна строка: не влезла — многоточие, полный текст — в подсказке
    Nadpis Np1(string t, int x, int y, int w, bool vt = true, Font f = null, bool podskazka = true)
    {
        var n = new Nadpis { Text = t ?? "", Location = new Point(x, y), Tag = vt ? "vt" : null };
        if (f != null) n.Font = f;
        n.AutoSize = false; n.AutoEllipsis = true;
        n.Size = new Size(Math.Max(16, w), (f ?? Font).Height + 4);
        if (podskazka && !string.IsNullOrEmpty(t)) podskazki.SetToolTip(n, Pult.Chisto(t));
        return n;
    }
    int VysTeksta(string t, int w, Font f = null) { return TextRenderer.MeasureText(string.IsNullOrEmpty(t) ? " " : t, f ?? Font, new Size(Math.Max(10, w), 10000), TextFormatFlags.WordBreak).Height; }

    Panel Shapka(string zag, string pod, Knopa knopka)
    {
        int W = Shir;
        var p = new Panel { Width = W, Height = 44, Margin = new Padding(0, 0, 0, 8) };
        var fz = new Font("Segoe UI Semibold", 15f);
        p.Controls.Add(new Label { Text = zag, AutoSize = true, Font = fz, Location = new Point(0, 4) });
        int x = 10 + TextRenderer.MeasureText(zag, fz).Width;
        int prav = knopka != null ? W - knopka.Width - 10 : W;
        if (pod != "") p.Controls.Add(Np1(pod, x, 14, prav - x));
        if (knopka != null) { knopka.Location = new Point(W - knopka.Width, 2); knopka.Margin = Padding.Empty; p.Controls.Add(knopka); }
        return p;
    }

    // красная карточка: что сломалось и «Повторить»
    Kartochka KartaOshibki(int W, string zag, string tekst, Action povtor)
    {
        int tw = W - 32 - (povtor != null ? 176 : 0);
        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12), Ottenok = Ris.Smes(pal.Poverhnost, Kras, pal.Temnaya ? 0.16 : 0.10) };
        kar.Controls.Add(Zk(Zn.Preduprezhd, 16, 14, 12f, Kras));
        kar.Controls.Add(Np1(zag, 44, 12, tw - 28, false, new Font("Segoe UI Semibold", 11f)));
        kar.Controls.Add(Np(tekst, 16, 42, true, tw));
        kar.Height = Math.Max(64, 42 + VysTeksta(tekst, tw) + 16);
        if (povtor != null) { var kn = KnZ("Повторить", Zn.Obnovit, povtor, false, 150); kn.Location = new Point(W - 166, 13); kn.Margin = Padding.Empty; kar.Controls.Add(kn); }
        return kar;
    }

    Knopa KnZ(string t, string zn, Action d, bool glavnaya = false, int w = 200) { var k = Kn(t, d, glavnaya, w); k.Znachok = zn; return k; }

    bool Sprosit(string v) { if (suho) return true; return MessageBox.Show(this, v, "Сторож сети", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

    // Karing выключен — замер и смена сервера не пройдут: сказать сразу, а не через минуту ожидания
    bool KaringVyklyuchen() { return sost != null && !Pult.Sboy(sost) && Pult.G(sost, "karing") is bool && !Pult.B(sost, "karing"); }

    // ---------- живое: скорость программ раз в 5 с ----------
    Dictionary<string, object> skorData;
    readonly Dictionary<string, Tuple<Nadpis, Uroven, Nadpis>> zhivye = new Dictionary<string, Tuple<Nadpis, Uroven, Nadpis>>();
    bool skorIdet;

    Dictionary<string, object> setData, testData;
    Nadpis setVniz, setVverh, setVpn;
    bool setIdet, testIdet;

    void ObnovitSkorosti()
    {
        if (zanyat || (tekushchaya != "glavnaya" && tekushchaya != "programmy")) return;
        if (!skorIdet)
        {
            skorIdet = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var d = Pult.Vyzov("skorosti");
                try { BeginInvoke((Action)(() => { skorIdet = false; skorData = d; PokazatSkorosti(); })); } catch { skorIdet = false; }
            });
        }
        if (!setIdet && tekushchaya == "glavnaya")
        {
            setIdet = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var d = Pult.Vyzov("set-seychas");
                try { BeginInvoke((Action)(() => { setIdet = false; setData = d; PokazatSet(); })); } catch { setIdet = false; }
            });
        }
    }

    void PokazatSet()
    {
        if (setVniz == null || setVniz.IsDisposed || setData == null) return;
        if (Pult.Sboy(setData)) { setVniz.Text = setVverh.Text = setVpn.Text = "—"; return; }
        setVniz.Text = Pult.Ch(Pult.G(setData, "vniz")).ToString("0.00");
        setVverh.Text = Pult.Ch(Pult.G(setData, "vverh")).ToString("0.00");
        setVpn.Text = Pult.Ch(Pult.G(setData, "vpn")).ToString("0.00");
    }

    void PokazatSkorosti()
    {
        var sk = Pult.D(Pult.G(skorData, "skorosti"));
        double max = Math.Max(0.5, sk.Values.Select(v => Pult.Ch(Pult.G(Pult.D(v), "mbayt"))).DefaultIfEmpty(0).Max());
        foreach (var kv in zhivye)
        {
            if (kv.Value.Item1.IsDisposed) continue;
            object v; double mb = 0; string kuda = null;
            bool est = sk.TryGetValue(kv.Key, out v);
            if (est) { mb = Pult.Ch(Pult.G(Pult.D(v), "mbayt")); kuda = Pult.S(Pult.D(v), "kuda"); }
            kv.Value.Item1.Text = est ? MBs(mb) : skorData == null ? "…" : "не видно";
            kv.Value.Item2.Dolya = mb / max; kv.Value.Item2.Invalidate();
            if (!string.IsNullOrEmpty(kuda) && kv.Value.Item3 != null) kv.Value.Item3.Text = Pult.Chisto(kuda);
        }
    }

    static string Znachok(string exe)
    {
        var e = (exe ?? "").ToLowerInvariant();
        if (e.Contains("zen") || e.Contains("edge") || e.Contains("chrome") || e.Contains("firefox") || e.Contains("browser")) return Zn.Globus;
        if (e.Contains("steam")) return "";       // игра
        if (e.Contains("discord") || e.Contains("telegram")) return "";   // сообщение
        if (e.Contains("claude")) return "";
        if (e.Contains("python") || e.Contains("curl")) return "";       // командная строка
        return Zn.Prilozh;
    }

    static string Imya(string exe)
    {
        string n;
        try { n = Path.GetFileNameWithoutExtension(exe ?? ""); } catch { n = exe ?? ""; }
        var izv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "zen", "Zen" }, { "msedge", "Edge" }, { "claude", "Claude" }, { "Discord", "Discord" }, { "Telegram", "Telegram" },
            { "steam", "Steam" }, { "steamwebhelper", "Steam (окно)" }, { "RobloxPlayerBeta", "Roblox" }, { "pythonw", "Сторож (Python)" }, { "python", "Python" } };
        string v; return izv.TryGetValue(n, out v) ? v : n;
    }

    // ---------- Монитор (главная) ----------
    Panel StrGlavnaya()
    {
        var k = Kolonka(); int W = Shir;
        bool est = sost != null;
        if (est && Pult.Sboy(sost))
        {
            bool netPy = Pult.B(sost, "net_python");
            k.Controls.Add(KartaOshibki(W, netPy ? "Сторож не может работать без Python" : "Сторож не ответил", Pult.Info(sost),
                () => { sost = null; Perestroit(); Obnovit(); }));
            if (!netPy) k.Controls.Add(Tx("Окно спрашивает сторожа раз в минуту само. Если ошибка не уходит — подробности в журнале окна (файл storozh-okno.log рядом с программой).", true));
            return k;
        }
        var bedy = Pult.L(sost, "bedy").OfType<string>().Where(b => b.Trim() != "").ToList();
        var st = Pult.D(Pult.G(sost, "stat"));
        bool karing = Pult.B(sost, "karing");

        // состояние одной строкой + кнопки (узкое окно — кнопки под текстом)
        Color cv = !est ? pal.Liniya : bedy.Count == 0 ? Zel : (karing ? Zhelt : Kras);
        string claude = Pult.Chisto(Pult.S(sost, "claude"));
        string kogda = Kogda(Pult.S(sost, "posledniy"));
        string podp = est ? (claude == "" || claude == "как всё остальное" ? "Claude: как всё остальное" : "Claude: " + claude) + "  ·  zapret " + (Pult.B(sost, "zapret") ? "включён" : "не работает") +
                            "  ·  " + (kogda != "" ? "проверено " + kogda : "сторож ещё не проверял сеть") : "";
        bool shirokoVerh = W >= 820;
        int tekstW = shirokoVerh ? W - 64 - 16 - 350 : W - 64 - 16;
        int podpH = est ? VysTeksta(podp, tekstW) : 0;
        int knY = shirokoVerh ? 13 : 40 + podpH + 10;
        var top = new Kartochka { Width = W, Height = shirokoVerh ? Math.Max(64, 40 + podpH + 12) : knY + 38 + 13, Margin = new Padding(0, 0, 0, 12), Ottenok = Ris.Smes(pal.Poverhnost, cv, pal.Temnaya ? 0.16 : 0.10) };
        top.Controls.Add(new Krug { Size = new Size(36, 36), Location = new Point(14, 14), Cvet = cv, Galka = est && bedy.Count == 0 });
        top.Controls.Add(Np1(!est ? "Смотрю…" : bedy.Count == 0 ? "Всё работает" : "Есть что поправить", 62, 10, tekstW, false, new Font("Segoe UI Semibold", 13f), false));
        if (est) top.Controls.Add(Np(podp, 64, 40, true, tekstW));
        var k1 = KnZ("Проверить", Zn.Obnovit, () => Deystvie("проверка сети", new[] { "proverka" }, null), true, 150);
        var k2 = KnZ("Лучший сервер", Zn.Skorost, NaytiLuchshiy, false, 180);
        k1.Location = shirokoVerh ? new Point(W - 346, knY) : new Point(64, knY);
        k2.Location = shirokoVerh ? new Point(W - 192, knY) : new Point(64 + 160, knY);
        k1.Margin = k2.Margin = Padding.Empty;
        top.Controls.Add(k1); top.Controls.Add(k2);
        k.Controls.Add(top);

        // что сделать сейчас — только если есть
        foreach (var b in bedy)
        {
            Knopa kn = null; string pod = null;
            if (b.StartsWith("zapret"))
            {
                // службы нет и стратегии в реестре нет — zapret не поставлен: подбирать нечего, сначала поставить
                if (!Pult.B(sost, "zapret") && Pult.S(sost, "strategiya") == "")
                {
                    pod = "Похоже, zapret не установлен. Без него YouTube и голос Discord идут только через VPN.";
                    kn = Kn("Поставить zapret", () => Otkryt("zapret"), true, 210);
                }
                else kn = Kn("Подобрать стратегию", PodobratZapret, true, 210);
            }
            else if (b.StartsWith("Пустить через VPN")) kn = Kn("Поправить сейчас", () => Deystvie("правка Karing", new[] { "proverka" }, null), true, 210);
            else if (b.StartsWith("Сторож не запускается")) kn = Kn("Включить", () => Deystvie("включение сторожа по расписанию", new[] { "ustanovit-zadaniya" }, null), true, 210);
            else if (b.StartsWith("Karing"))
            {
                pod = "Запустите Karing и включите в нём подключение (режим «Правила»). Без Karing VPN не работает.";
                kn = Kn("Проверить снова", () => { zanyatoStroka.Text = "Проверяю…"; Obnovit(() => zanyatoStroka.Text = ""); }, false, 210);
            }
            int tw = W - 44 - 240;
            int h1 = VysTeksta(b, tw), h2 = pod != null ? VysTeksta(pod, tw) + 4 : 0;
            var r = new Kartochka { Width = W, Height = Math.Max(52, h1 + h2 + 32), Margin = new Padding(0, 0, 0, 8) };
            r.Controls.Add(Zk(Zn.Preduprezhd, 16, 16, 12f, Zhelt));
            r.Controls.Add(Np(b, 44, 16, false, tw));
            if (pod != null) r.Controls.Add(Np(pod, 44, 16 + h1 + 4, true, tw));
            if (kn != null) { kn.Location = new Point(W - 224, 7); kn.Margin = Padding.Empty; r.Controls.Add(kn); }
            k.Controls.Add(r);
        }
        if (!est) return k;

        // спидометр + сутки
        bool uzko = W < 820;
        int suW = uzko ? W : W - 242;
        var ryad = new Panel { Width = W, Height = uzko ? 452 : 220, Margin = new Padding(0, 0, 0, 12) };
        var sp = new Kartochka { Width = uzko ? W : 230, Height = 220, Location = new Point(0, 0) };
        double skor = Pult.Ch(Pult.G(sost, "skorost"));
        var vyb = Spiski(st, "vybory");
        var proshlye = vyb.Take(Math.Max(0, vyb.Count - 1)).Select(v => Pult.Ch(Pult.El(v, 2))).Where(z => z > 0).ToList();
        double obychno = proshlye.Count > 0 ? proshlye.Average() : 0;
        sp.Controls.Add(Np("Скорость VPN", 16, 12, true));
        sp.Controls.Add(new Spidometr { Zn = Math.Round(skor / 8, 1), Max = Math.Max(40, skor / 8), Ed = "МБ/с", Location = new Point(15, 34), Size = new Size(200, 120) });
        string server = Pult.Chisto(Pult.S(sost, "server"));
        sp.Controls.Add(Np1(!karing ? "Karing выключен" : server == "" ? "сервер не выбран" : server + (skor > 0 ? " · по замеру" : " · ещё не замерялся"), 16, 158, 200, false));
        if (obychno > 0) sp.Controls.Add(Np1("обычно ~" + (obychno / 8).ToString("0") + " МБ/с", 16, 182, 200));
        ryad.Controls.Add(sp);

        var su = new Kartochka { Width = suW, Height = 220, Location = uzko ? new Point(0, 232) : new Point(242, 0) };
        su.Controls.Add(Zg("Последние сутки", 16, 12));
        su.Controls.Add(Np1("цвет — как работал VPN в этот час, наведите — подробности", 150, 15, suW - 166));
        var ps = new PolosaSutok { Location = new Point(16, 46), Size = new Size(suW - 32, 54) };
        var chasy = Pult.L(st, "chasy").ToList();
        for (int h = 0; h < 24 && h < chasy.Count; h++) { var c = Spisok(chasy[h]); ps.Obryvy[h] = Pult.Cel(Pult.El(c, 0)); ps.Sekund[h] = Pult.Cel(Pult.El(c, 1)); }
        su.Controls.Add(ps);
        int lx = 16;
        foreach (var l in new[] { Tuple.Create(PolosaSutok.Zel, "без обрывов"), Tuple.Create(PolosaSutok.Zhel, "1–2 обрыва"), Tuple.Create(PolosaSutok.Kras, "3 и больше или дольше минуты") })
        {
            int tw = TextRenderer.MeasureText(l.Item2, Font).Width + 6;
            if (lx + 18 + tw > suW - 16) break;
            su.Controls.Add(new Nadpis { Text = "●", ForeColor = l.Item1, Tag = l.Item1, Location = new Point(lx, 108) });
            su.Controls.Add(Np1(l.Item2, lx + 18, 108, tw, true, null, false));
            lx += 18 + tw + 16;
        }
        int x = 16, shag = (suW - 32) / 3;
        foreach (var c in new[] { Tuple.Create("обрывов", Pult.Cel(Pult.G(sost, "miganiya")).ToString()), Tuple.Create("без VPN всего", Vremya(Pult.Cel(Pult.G(st, "bez_vpn")))),
                                  Tuple.Create("самый длинный", Vremya(Pult.Cel(Pult.G(st, "samyy_dlinnyy")))) })
        {
            su.Controls.Add(Np1(c.Item1, x, 142, shag - 8));
            su.Controls.Add(Np1(c.Item2, x - 2, 162, shag - 6, false, new Font("Segoe UI Semibold", 15f)));
            x += shag;
        }
        ryad.Controls.Add(su);
        k.Controls.Add(ryad);

        // сеть сейчас + тест скорости (узкое окно — кнопка теста под цифрами)
        bool shirokoSet = W >= 840;
        int nizSet = shirokoSet ? 120 : 168;
        var set = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        set.Controls.Add(Zg("Сеть сейчас", 16, 12));
        set.Controls.Add(Np1("весь компьютер, обновляется каждые 5 с", 130, 15, Math.Min(320, W - 146)));
        int sx = 16;
        foreach (var c in new[] { Tuple.Create("", "Скачивание", "МБ/с"), Tuple.Create("", "Отдача", "МБ/с"), Tuple.Create(Zn.Shit, "Из них через VPN", "МБ/с") })
        {
            set.Controls.Add(Zk(c.Item1, sx, 46, 11f));
            set.Controls.Add(Np1(c.Item2, sx + 22, 44, 160));
            var ch = new Nadpis { Text = "…", Font = new Font("Segoe UI Semibold", 17f), Location = new Point(sx - 2, 66) };
            set.Controls.Add(ch);
            set.Controls.Add(Np(c.Item3, sx + 86, 78, true));
            if (c.Item2 == "Скачивание") setVniz = ch; else if (c.Item2 == "Отдача") setVverh = ch; else setVpn = ch;
            sx += 196;
        }
        var kt = KnZ(testIdet ? "Идёт тест…" : "Тест скорости", Zn.Skorost, () =>
        {
            if (testIdet) return;
            if (zanyat) { zanyatoStroka.Text = "Тест скорости — после того, как закончится: " + deystvieSeychas; return; }
            if (suho) { Zapis("[тест] тест скорости"); return; }
            testIdet = true; zanyatoStroka.Text = "Идёт тест скорости: 25 МБ через VPN и 25 МБ напрямую, около 30 секунд…";
            Zapis("тест скорости");
            Perestroit();
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("test-seti"); try { BeginInvoke((Action)(() => { testIdet = false; testData = d; zanyatoStroka.Text = Pult.Sboy(d) ? "Не вышло: тест скорости — " + Pult.Info(d) : "Готово: тест скорости"; Zapis(zanyatoStroka.Text); Perestroit(); })); } catch { testIdet = false; } });
        }, true, 200);
        kt.Location = shirokoSet ? new Point(W - 216, 52) : new Point(16, 116); kt.Margin = Padding.Empty; set.Controls.Add(kt);
        int setH = nizSet;
        if (testData != null && Pult.Sboy(testData))
        {
            string t = "Тест скорости не прошёл: " + Pult.Info(testData);
            set.Controls.Add(Np(t, 16, nizSet, true, W - 32));
            setH = nizSet + VysTeksta(t, W - 32) + 14;
        }
        else if (testData != null)
        {
            var v = Pult.D(Pult.G(testData, "vpn")); var pr = Pult.D(Pult.G(testData, "pryamo"));
            double mv = Pult.Ch(Pult.G(v, "mbit")), mp = Pult.Ch(Pult.G(pr, "mbit")), mx = Math.Max(1, Math.Max(mv, mp));
            int y0 = nizSet;
            set.Controls.Add(Np("Тест скорости", 16, y0, true));
            string srv = Pult.Chisto(Pult.S(testData, "server"));
            int rezX = W - 270;
            foreach (var t in new[] { Tuple.Create(srv != "" ? "Через VPN (" + srv + ")" : "Через VPN", mv, Pult.S(v, "ping")), Tuple.Create("Напрямую", mp, Pult.S(pr, "ping")) })
            {
                y0 += 30;
                set.Controls.Add(Np1(t.Item1, 16, y0, 216, false));
                set.Controls.Add(new Uroven { Location = new Point(240, y0 + 7), Size = new Size(Math.Max(40, rezX - 250), 9), Dolya = t.Item2 / mx });
                var rez = Np1(t.Item2 > 0 ? MBsIzMbit(t.Item2) + (t.Item3 != "" ? " · отклик " + t.Item3 + " мс" : "") : "не прошло", rezX, y0, 254, false);
                rez.Tag = t.Item2 >= 50 ? Zel : t.Item2 > 0 ? Zhelt : Kras;
                set.Controls.Add(rez);
            }
            string vv = mv <= 0 ? "Через VPN тест не прошёл — Karing выключен или сервер не отвечает." :
                        mv >= 50 && mp >= 50 ? "Обе дороги быстрые — всё в порядке." : mv < 50 && mp >= 50 ? "VPN медленнее 6 МБ/с — попробуйте «Лучший сервер»." :
                        mp < 50 && mv >= 50 ? "Напрямую медленно — похоже на провайдера или zapret." : "Медленно везде — похоже на провайдера или Wi-Fi.";
            set.Controls.Add(Np(vv, 16, y0 + 32, true, W - 32));
            setH = y0 + 32 + VysTeksta(vv, W - 32) + 14;
        }
        set.Height = setH;
        k.Controls.Add(set);
        PokazatSet();

        // сейчас в интернете
        k.Controls.Add(KartochkaProgramm(W));

        var igra = Pult.L(sost, "igra").OfType<string>().ToList();
        if (igra.Count > 0)
        {
            int zv = Pult.Cel(Pult.G(Pult.D(Pult.G(sost, "nastroyki")), "zvonok_min"));
            k.Controls.Add(Tx("Сейчас идёт: " + string.Join(", ", igra) + ". Karing перезапускаю только после этого" + (zv > 0 ? " (звонок — не дольше " + zv + " мин)." : "."), true));
        }
        return k;
    }

    // скорость: всё в мегабайтах в секунду (1 МБ/с = 8 Мбит/с)
    static string MBs(double mbayt) { return mbayt <= 0 ? "0 МБ/с" : mbayt < 0.01 ? "< 0,01 МБ/с" : mbayt < 10 ? mbayt.ToString("0.00") + " МБ/с" : mbayt.ToString("0.0") + " МБ/с"; }
    static string MBsIzMbit(double mbit) { return MBs(mbit / 8); }

    // «20.11.2026 21:50» → «в 21:50» сегодня, иначе «18.11 в 21:50»
    static string Kogda(string dt)
    {
        DateTime d;
        if (DateTime.TryParseExact(dt ?? "", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d.Date == DateTime.Today ? "в " + d.ToString("HH:mm") : d.ToString("dd.MM") + " в " + d.ToString("HH:mm");
        return (dt ?? "").Length >= 5 ? "в " + dt.Substring(dt.Length - 5) : "";
    }

    // «19.11.2026 07:19» → «19.11 07:19»
    static string Korotko(string dt)
    {
        DateTime d;
        return DateTime.TryParseExact(dt ?? "", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d.ToString("dd.MM HH:mm") : dt;
    }

    string deystvieSeychas = "";   // что сейчас идёт (для «сначала дождитесь»)

    void NaytiLuchshiy()
    {
        if (KaringVyklyuchen()) { zanyatoStroka.Text = "Сначала запустите Karing: без него серверы не замерить."; return; }
        if (Sprosit("Замерю все VPN-серверы и поставлю самый быстрый с постоянным адресом. Займёт 2–5 минут, интернет пару раз моргнёт. Начать?"))
            Deystvie("замер серверов", new[] { "zamer" }, d => servData = null);
    }

    // монитор: кто сейчас в интернете и куда идёт
    bool progIdet;
    Kartochka KartochkaProgramm(int W)
    {
        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(Zg("Сейчас в интернете", 16, 12));
        kar.Controls.Add(Np1("скорость каждой программы, обновляется каждые 5 с", 180, 15, W - 196));
        int y = 44;
        if (progData == null) { kar.Controls.Add(Np("Смотрю, какие программы в сети…", 16, 48, true)); kar.Height = 84; ZagruzitProgrammy(); return kar; }
        if (Pult.Sboy(progData))
        {
            string t = "Не смог узнать программы: " + Pult.Info(progData);
            kar.Controls.Add(Np(t, 16, 48, true, W - 32));
            kar.Height = 48 + VysTeksta(t, W - 32) + 14;
            return kar;
        }
        var spisok = Pult.Slovari(progData, "programmy").Where(p => Pult.B(p, "v_seti") && Pult.S(p, "exe") != "").Take(7).ToList();
        foreach (var p in spisok) y += StrokaProgrammy(kar, p, W, y, false);
        if (spisok.Count == 0) { kar.Controls.Add(Np("Сейчас ни одна программа не в сети.", 16, 48, true)); y = 80; }
        kar.Height = y + 8;
        PokazatSkorosti();
        return kar;
    }

    Nadpis ImyaProgrammy(string exe, int x, int y, int w)
    {
        var n = Np1(Imya(exe), x, y, w, false, null, false);
        podskazki.SetToolTip(n, exe);
        return n;
    }

    // Одна строка программы; возвращает её высоту.
    // Монитор: значок | имя | куда | полоса | скорость. «Программы»: имя и куда двумя строками, справа выбор
    // (у правил ещё «Убрать»); узкое окно — выбор под именем.
    int StrokaProgrammy(Kartochka kar, Dictionary<string, object> p, int W, int y, bool sVyborom, bool pravilo = false)
    {
        string exe = Pult.S(p, "exe");
        bool cherezKaring = Pult.B(p, "karing"), vSeti = Pult.B(p, "v_seti");
        string kudaT = Pult.Chisto(Pult.S(p, "seychas"));
        if (!sVyborom)
        {
            int cKuda = 190, cPolosa = 430, shirPolosy = Math.Max(60, W - 140 - cPolosa), cSkor = cPolosa + shirPolosy + 10;
            kar.Controls.Add(Zk(Znachok(exe), 16, y + 9, 12f));
            kar.Controls.Add(ImyaProgrammy(exe, 46, y + 10, cKuda - 46 - 8));
            var kuda = Np1(kudaT, cKuda, y + 10, cPolosa - cKuda - 10);
            kar.Controls.Add(kuda);
            var u = new Uroven { Location = new Point(cPolosa, y + 17), Size = new Size(shirPolosy, 7), Visible = cherezKaring };   // мимо Karing скорость не видна — полосы нет
            kar.Controls.Add(u);
            var mb = Np1(cherezKaring ? "…" : "не видно", cSkor, y + 10, W - 16 - cSkor, true, null, false);
            kar.Controls.Add(mb);
            if (cherezKaring) zhivye[exe] = Tuple.Create(mb, u, kuda);
            else podskazki.SetToolTip(mb, "Программа ходит мимо Karing — её скорость отдельно не видна. Общая нагрузка — в «Сеть сейчас».");
            return 40;
        }

        int pravW = 300 + (pravilo ? 104 : 0);
        bool dve = W < 860;
        int rowH = dve ? 96 : 54;
        int xPrav = dve ? 46 : W - 16 - pravW, yPrav = dve ? y + 56 : y + 11;
        int levW = dve ? W - 46 - 16 - 110 : Math.Min(260, xPrav - 46 - 160);
        kar.Controls.Add(Zk(Znachok(exe), 16, dve ? y + 18 : y + 16, 12f));
        kar.Controls.Add(ImyaProgrammy(exe, 46, y + 6, levW));
        var kudaN = Np1(kudaT, 46, y + 28, levW);
        kar.Controls.Add(kudaN);
        Uroven ur; Nadpis skor;
        string bezSkor = vSeti ? "не видно" : "—";
        if (dve)
        {
            skor = Np1(cherezKaring ? "…" : bezSkor, W - 16 - 94, y + 6, 94, true, null, false);
            ur = new Uroven { Location = new Point(W - 16 - 94, y + 36), Size = new Size(94, 6) };
        }
        else
        {
            int xPol = 46 + levW + 12, skW = 84, polW = xPrav - 12 - skW - 8 - xPol;
            ur = new Uroven { Location = new Point(xPol, y + 24), Size = new Size(Math.Max(16, polW), 7), Visible = polW >= 40 };
            skor = Np1(cherezKaring ? "…" : bezSkor, xPol + (polW >= 40 ? polW + 8 : 0), y + 17, skW, true, null, false);
        }
        if (!cherezKaring) ur.Visible = false;
        kar.Controls.Add(ur); kar.Controls.Add(skor);
        if (cherezKaring) zhivye[exe] = Tuple.Create(skor, ur, kudaN);
        else podskazki.SetToolTip(skor, vSeti ? "Программа ходит мимо Karing — её скорость отдельно не видна." : "Программа сейчас не в сети.");

        bool claude = exe.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) && Pult.S(sost, "claude") != "" && Pult.S(sost, "claude") != "как всё остальное";
        if (claude)
        {
            var z = new Kartochka { Location = new Point(xPrav, yPrav), Size = new Size(300, 32), Ottenok = Ris.Smes(pal.Poverhnost, pal.Akcent, 0.18) };
            z.Controls.Add(Np1("закреплён: " + Pult.Chisto(Pult.S(sost, "claude")), 12, 6, 276, false));
            kar.Controls.Add(z);
            return rowH;
        }
        if (vSeti && !cherezKaring && !pravilo)
        {
            var z = new Kartochka { Location = new Point(xPrav, yPrav), Size = new Size(300, 32) };
            z.Controls.Add(Np1(KaringVyklyuchen() ? "Karing выключен — правила не действуют" : "мимо Karing — правило не подействует", 12, 6, 276));
            kar.Controls.Add(z);
            return rowH;
        }
        // выбор есть и у правил программ, которых сейчас нет в сети или которые идут мимо Karing: их правило можно сменить и убрать
        string m = izmeneniya.ContainsKey(exe) ? izmeneniya[exe] : Pult.S(p, "marshrut");
        if (m == "") m = "avto";
        var seg = new Segmenty { Punkty = new[] { "Авто", m != "avto" && m != "vpn" && m != "direct" && m != "block" ? "VPN: " + Pult.Chisto(m) : "VPN", "Напрямую", "Без сети" },
            Location = new Point(xPrav, yPrav), Size = new Size(300, 32) };
        seg.Vybran = m == "avto" ? 0 : m == "direct" ? 2 : m == "block" ? 3 : 1;
        string tek = m;
        seg.Smena += i => { izmeneniya[exe] = i == 0 ? "avto" : i == 1 ? (tek != "avto" && tek != "direct" && tek != "block" ? tek : "vpn") : i == 2 ? "direct" : "block"; ZaplanirovatMarshruty(); };
        kar.Controls.Add(seg);
        if (pravilo)
        {
            var ub = Kn("Убрать", () => { izmeneniya[exe] = "ubrat"; ZaplanirovatMarshruty(); Perestroit(); }, false, 96);
            ub.Height = 32; ub.Location = new Point(xPrav + 304, yPrav); ub.Margin = Padding.Empty;
            kar.Controls.Add(ub);
        }
        return rowH;
    }

    void ZagruzitProgrammy()
    {
        if (progIdet) return;
        progIdet = true;
        ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("programmy"); try { BeginInvoke((Action)(() => { progIdet = false; progData = d; if (tekushchaya == "programmy" || tekushchaya == "glavnaya") Perestroit(); ObnovitSkorosti(); })); } catch { progIdet = false; } });
    }

    // ---------- Программы ----------
    Dictionary<string, object> progData;
    readonly Dictionary<string, string> izmeneniya = new Dictionary<string, string>();
    System.Windows.Forms.Timer marshrutTaymer;

    Panel StrProgrammy()
    {
        var k = Kolonka(); int W = Shir;
        var dob = KnZ("Добавить по имени", "", DobavitProgrammu, false, 200);
        k.Controls.Add(Shapka("Программы", "", dob));
        if (progData == null)
        {
            k.Controls.Add(Tx("Смотрю, какие программы в сети…", true));
            ZagruzitProgrammy();
            return k;
        }
        if (Pult.Sboy(progData))
        {
            k.Controls.Add(KartaOshibki(W, "Не смог узнать программы", Pult.Info(progData), () => { progData = null; Perestroit(); }));
            return k;
        }
        var vse = Pult.Slovari(progData, "programmy").Where(p => Pult.S(p, "exe") != "").ToList();
        Func<Dictionary<string, object>, string> ex = p => Pult.S(p, "exe");
        var pravila = vse.Where(p => (Pult.B(p, "pravilo") || izmeneniya.ContainsKey(ex(p))) && !(izmeneniya.ContainsKey(ex(p)) && izmeneniya[ex(p)] == "ubrat")).ToList();
        var ostalnye = vse.Where(p => !pravila.Contains(p) && Pult.B(p, "v_seti")).ToList();

        var kp = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        kp.Controls.Add(Zg("Ваши правила", 16, 12));
        kp.Controls.Add(Np1("эти программы идут так, как вы выбрали. «Убрать» — снова решает сторож.", 128, 15, W - 144));
        int y = 44;
        if (pravila.Count == 0) { kp.Controls.Add(Np("Правил пока нет. Выберите способ у программы ниже — она переедет сюда.", 16, 48, true, W - 32)); y = 80; }
        foreach (var p in pravila) y += StrokaProgrammy(kp, p, W, y, true, true);
        kp.Height = y + 12;
        k.Controls.Add(kp);

        var ko = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        ko.Controls.Add(Zg("Сейчас в сети", 16, 12));
        ko.Controls.Add(Np1("все программы с соединениями, скорость каждые 5 с", 130, 15, W - 146));
        y = 44;
        foreach (var p in ostalnye) y += StrokaProgrammy(ko, p, W, y, true);
        if (ostalnye.Count == 0) { ko.Controls.Add(Np("Других программ в сети сейчас нет.", 16, 48, true)); y = 80; }
        ko.Height = y + 8;
        k.Controls.Add(ko);
        k.Controls.Add(Tx("Авто — заблокированное в России идёт через VPN, остальное напрямую. «Без сети» — программа не выйдет в интернет. «Мимо Karing» — программа (часто игры и Steam-загрузки) не пользуется прокси, и правило на неё не подействует. Изменение применяется само через пару секунд, интернет моргнёт один раз.", true));
        PokazatSkorosti();
        return k;
    }

    void DobavitProgrammu()
    {
        string exe = Vvod("Программы нет в списке, потому что она сейчас не в сети. Впишите её имя — как в Диспетчере задач на вкладке «Подробности», например RobloxPlayerBeta.exe:");
        if (string.IsNullOrWhiteSpace(exe)) return;
        exe = exe.Trim().Trim('"');
        try { exe = Path.GetFileName(exe); } catch { exe = ""; }   // вставили путь целиком — берём имя файла
        if (exe == "" || exe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { zanyatoStroka.Text = "Такого имени программы не бывает — впишите, например, RobloxPlayerBeta.exe"; return; }
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        izmeneniya[exe] = "vpn";
        if (progData == null || Pult.Sboy(progData)) progData = new Dictionary<string, object> { { "programmy", new List<object>() } };
        var sp = Pult.L(progData, "programmy").ToList();
        if (!sp.Any(o => Pult.S(Pult.D(o), "exe").Equals(exe, StringComparison.OrdinalIgnoreCase)))
            sp.Add(new Dictionary<string, object> { { "exe", exe }, { "seychas", "сейчас не в сети" }, { "marshrut", "vpn" }, { "pravilo", true }, { "karing", false }, { "v_seti", false } });
        progData["programmy"] = sp;
        ZaplanirovatMarshruty();
        Perestroit();
    }

    string Vvod(string vopros)
    {
        if (suho) { Zapis("[тест] окно ввода"); return null; }
        using (var f = new Form { Text = "Сторож сети", Width = 520, Height = 190, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = pal.Fon, ForeColor = pal.Tekst, Font = Font })
        {
            var l = new Label { Text = vopros, Location = new Point(14, 12), Size = new Size(480, 40) };
            var t = new TextBox { Location = new Point(14, 56), Width = 476, BackColor = pal.Poverhnost, ForeColor = pal.Tekst };
            var ok = new Button { Text = "Добавить", DialogResult = DialogResult.OK, Location = new Point(390, 92), Width = 100, Height = 30, FlatStyle = FlatStyle.Flat };
            var otm = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(282, 92), Width = 100, Height = 30, FlatStyle = FlatStyle.Flat };
            f.Controls.AddRange(new Control[] { l, t, ok, otm }); f.AcceptButton = ok; f.CancelButton = otm;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }
    }

    void ZaplanirovatMarshruty()
    {
        if (suho) { Zapis("[тест] маршруты: " + string.Join(", ", izmeneniya.Select(kv => kv.Key + "=" + kv.Value))); return; }
        if (marshrutTaymer == null)
        {
            marshrutTaymer = new System.Windows.Forms.Timer { Interval = 2500 };
            taymery.Add(marshrutTaymer);
            marshrutTaymer.Tick += (s, e) =>
            {
                if (zanyat) return;   // дождаться конца текущего действия
                marshrutTaymer.Stop();
                // Правила пишутся целиком: без нынешнего списка сторожа (он не ответил) записали бы только
                // сегодняшние изменения и стёрли остальные правила — поэтому не пишем вовсе.
                var nas = Pult.G(sost, "nastroyki") as Dictionary<string, object>;
                if (nas == null)
                {
                    zanyatoStroka.Text = "Не сохранил выбор: сторож не ответил, какие правила уже есть. Выберите ещё раз через минуту.";
                    Zapis(zanyatoStroka.Text);
                    izmeneniya.Clear(); progData = null; Obnovit();
                    return;
                }
                var mar = new Dictionary<string, object>(Pult.D(Pult.G(nas, "marshruty")));
                foreach (var kv in izmeneniya) { if (kv.Value == "avto" || kv.Value == "ubrat") mar.Remove(kv.Key); else mar[kv.Key] = kv.Value; }
                var opis = string.Join(", ", izmeneniya.Select(kv => Imya(kv.Key) + " → " + (kv.Value == "avto" ? "авто" : kv.Value == "ubrat" ? "правило убрано" : kv.Value == "vpn" ? "VPN" : kv.Value == "direct" ? "напрямую" : kv.Value == "block" ? "запрет" : Pult.Chisto(kv.Value))));
                Deystvie("маршруты: " + opis, new[] { "nastroyki-zadat", Pult.Json(new Dictionary<string, object> { { "marshruty", mar } }) }, d =>
                {
                    izmeneniya.Clear();
                    if (Pult.Sboy(d) || !Pult.B(d, "ok")) { progData = null; return; }   // не сохранилось — показать, как есть на деле
                    Deystvie("перезапуск Karing с маршрутами", new[] { "primenit-marshruty" }, d2 => { progData = null; if (tekushchaya == "programmy") Perestroit(); });
                });
            };
        }
        marshrutTaymer.Stop(); marshrutTaymer.Start();
    }

    // ---------- Сайты ----------
    Dictionary<string, object> saytProverka, saytyIst;
    string saytPole = "";
    bool saytIdet;

    void ProveritSayt()
    {
        string d = (saytPole ?? "").Trim();
        if (d == "") { zanyatoStroka.Text = "Впишите адрес сайта, например youtube.com"; return; }
        if (saytIdet) return;
        saytIdet = true;
        zanyatoStroka.Text = "Проверяю " + d + " напрямую и через VPN, до 25 секунд…";
        Zapis("проверка сайта " + d);
        if (tekushchaya == "sayty") Perestroit();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var r = Pult.Vyzov("proverit-sayt", d);
            try { BeginInvoke((Action)(() => { saytIdet = false; saytProverka = r; zanyatoStroka.Text = Pult.Sboy(r) ? "Не вышло: проверка сайта — " + Pult.Info(r) : ""; if (tekushchaya == "sayty") Perestroit(); })); }
            catch { saytIdet = false; }
        });
    }

    Panel StrSayty()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Сайты", "", null));
        string obT = "Заблокированные в России — через VPN (списки обновляются сами), остальные — напрямую. Сайт не открывается или тормозит? Впишите его ниже: сторож проверит обе дороги и подскажет.";
        var obyas = new Kartochka { Width = W, Height = 40 + VysTeksta(obT, W - 70) + 14, Margin = new Padding(0, 0, 0, 12) };
        obyas.Controls.Add(Zk(Zn.Globus, 16, 16, 13f));
        obyas.Controls.Add(new Nadpis { Text = "Как идут сайты", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(46, 12) });
        obyas.Controls.Add(Np(obT, 46, 40, true, W - 70));
        k.Controls.Add(obyas);

        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(new Nadpis { Text = "Проверить сайт", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(16, 12) });
        var pole = new TextBox { Name = "pole-sayt", Location = new Point(16, 44), Width = W - 200, Text = saytPole, Font = new Font("Segoe UI", 11f), BorderStyle = BorderStyle.FixedSingle };
        pole.TextChanged += (s, e) => saytPole = pole.Text;
        var kn = KnZ(saytIdet ? "Проверяю…" : "Проверить", "", ProveritSayt, true, 160);
        kn.Location = new Point(W - 176, 41); kn.Margin = Padding.Empty;
        pole.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; kn.PerformClick(); } };
        kar.Controls.Add(pole); kar.Controls.Add(kn);
        int y = 92;
        if (saytProverka != null && Pult.Sboy(saytProverka))
        {
            string t = "Не смог проверить сайт: " + Pult.Info(saytProverka);
            kar.Controls.Add(Np(t, 16, y, true, W - 32));
            y += VysTeksta(t, W - 32) + 12;
        }
        else if (saytProverka != null)
        {
            var dve = new[] { Spisok(Pult.G(saytProverka, "napryamuyu")), Spisok(Pult.G(saytProverka, "vpn")) };
            int bw = (W - 44) / 2;
            string srv = Pult.Chisto(Pult.S(saytProverka, "server"));
            for (int i = 0; i < 2; i++)
            {
                string oc = Convert.ToString(Pult.El(dve[i], 2) ?? "");
                if (oc == "") oc = "нет ответа";
                bool ok = oc == "быстро", plokho = oc == "не открывается" || oc == "очень медленно" || oc == "нет ответа";
                Color c = ok ? Zel : plokho ? Kras : Zhelt;
                var blok = new Kartochka { Width = bw, Height = 92, Location = new Point(16 + i * (bw + 12), y), Ottenok = Ris.Smes(pal.Poverhnost, c, pal.Temnaya ? 0.2 : 0.12) };
                blok.Controls.Add(Np1(i == 0 ? "Напрямую" : "Через VPN" + (srv != "" ? " (" + srv + ")" : ""), 12, 7, bw - 24));
                double t = Pult.Ch(Pult.El(dve[i], 1));
                var bol = Np1(oc == "не открывается" || oc == "нет ответа" ? "нет" : t.ToString("0.0") + " с", 10, 29, bw - 20, false, new Font("Segoe UI Semibold", 16f), false);
                bol.Tag = c;
                blok.Controls.Add(bol);
                blok.Controls.Add(Np1(oc, 12, 64, bw - 24));
                kar.Controls.Add(blok);
            }
            y += 92 + 12;
            string dey = Pult.S(saytProverka, "deystvie"), dom = Pult.S(saytProverka, "domen"), sovet = Pult.S(saytProverka, "sovet");
            string seych = Pult.B(saytProverka, "uzhe_vpn") ? "через VPN" : Pult.B(saytProverka, "uzhe_napr") ? "напрямую" : "по спискам (сам выберет)";
            string tekst = (dom != "" ? dom + " сейчас идёт: " : "Сейчас идёт: ") + seych + "." + (sovet != "" ? "  Совет: " + sovet + "." : "");
            bool estDey = (dey == "vpn" || dey == "direct") && dom != "";
            bool ryadom = estDey && W >= 900;
            int tw = ryadom ? W - 32 - 350 : W - 32;
            int th = VysTeksta(tekst, tw);
            kar.Controls.Add(Np(tekst, 16, y + 6, false, tw));
            if (estDey)
            {
                int ky = ryadom ? y : y + th + 14;
                var kd = Kn(dey == "vpn" ? "Пустить через VPN" : "Пустить напрямую", () => Deystvie("сайт " + dom + (dey == "vpn" ? " в VPN" : " напрямую"), new[] { "sayt", dom, dey }, d => { saytyIst = null; saytProverka = null; }), true, 200);
                var ko = Kn("Оставить", () => { saytProverka = null; Perestroit(); }, false, 110);
                kd.Location = new Point(ryadom ? W - 336 : 16, ky); ko.Location = new Point(ryadom ? W - 126 : 226, ky);
                kd.Margin = ko.Margin = Padding.Empty;
                kar.Controls.Add(kd); kar.Controls.Add(ko);
                y = ryadom ? y + Math.Max(38, th + 12) : ky + 38;
            }
            else y += th + 6;
            y += 12;
        }
        kar.Height = Math.Max(96, y + 4);
        k.Controls.Add(kar);

        // история
        if (saytyIst == null)
        {
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("sayty-istoriya"); try { BeginInvoke((Action)(() => { saytyIst = d; if (tekushchaya == "sayty") Perestroit(); })); } catch { } });
            return k;
        }
        if (Pult.Sboy(saytyIst))
        {
            k.Controls.Add(KartaOshibki(W, "Не смог прочитать, что сторож поменял сам", Pult.Info(saytyIst), () => { saytyIst = null; Perestroit(); }));
            return k;
        }
        var ist = Pult.Slovari(saytyIst, "sayty").Where(s => Pult.S(s, "domen") != "").Take(12).ToList();
        var ki = new Kartochka { Width = W, Height = 48 + Math.Max(1, ist.Count) * 42, Margin = new Padding(0, 0, 0, 12) };
        ki.Controls.Add(Zg("Что сторож поменял сам", 16, 12));
        ki.Controls.Add(Np1("«Отменить» — сайт снова пойдёт по спискам, как до правки", 220, 15, W - 236));
        y = 44;
        foreach (var s in ist)
        {
            bool vpn = Pult.S(s, "vid") == "vpn";
            string dom = Pult.S(s, "domen");
            var pill = new Kartochka { Location = new Point(16, y + 7), Size = new Size(86, 24), Ottenok = Ris.Smes(pal.Poverhnost, vpn ? Zel : Zhelt, 0.25) };
            pill.Controls.Add(new Nadpis { Text = vpn ? "в VPN" : "напрямую", Location = new Point(10, 3) });
            ki.Controls.Add(pill);
            ki.Controls.Add(Np1(dom, 114, y + 10, 176, false));
            ki.Controls.Add(Np1((vpn ? "пущен через VPN — напрямую не открывался" : "пущен напрямую — через VPN отказывал") + (Pult.S(s, "kogda") != "" ? " · " + Korotko(Pult.S(s, "kogda")) : ""), 300, y + 10, W - 430));
            var kv = Kn("Отменить", () => { if (Sprosit("Отменить правку для " + dom + "? Сайт снова пойдёт по спискам, как до неё. Karing перезапустится.")) Deystvie("отмена правки " + dom, new[] { "vernut", dom }, d => saytyIst = null); }, false, 100);
            kv.Height = 30; kv.Location = new Point(W - 116, y + 4); kv.Margin = Padding.Empty; ki.Controls.Add(kv);
            y += 42;
        }
        if (ist.Count == 0) ki.Controls.Add(Np("Пока ничего.", 16, 46, true));
        k.Controls.Add(ki);
        return k;
    }

    // ---------- Серверы ----------
    Dictionary<string, object> servData;
    bool servIdet;

    Panel StrServery()
    {
        var k = Kolonka(); int W = Shir;
        var zamerKn = KnZ("Замерить и выбрать лучший", Zn.Skorost, NaytiLuchshiy, true, 270);
        if (servData == null)
        {
            k.Controls.Add(Shapka("Серверы", "", null));
            k.Controls.Add(Tx("Загружаю…", true));
            if (!servIdet)
            {
                servIdet = true;
                ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("servery"); try { BeginInvoke((Action)(() => { servIdet = false; servData = d; if (tekushchaya == "servery") Perestroit(); })); } catch { servIdet = false; } });
            }
            return k;
        }
        if (Pult.Sboy(servData))
        {
            k.Controls.Add(Shapka("Серверы", "", null));
            k.Controls.Add(KartaOshibki(W, "Не смог получить серверы", Pult.Info(servData), () => { servData = null; Perestroit(); }));
            return k;
        }
        var zamer = Pult.D(Pult.G(servData, "zamer"));
        var rez = Spiski(zamer, "results").Where(r => Pult.El(r, 0) is string && (string)r[0] != "").ToList();
        string kogda = Pult.S(zamer, "when");
        k.Controls.Add(Shapka("Серверы", kogda != "" ? "замер " + kogda : "", zamerKn));
        string seychas = Pult.S(servData, "seychas"), claude = Pult.S(servData, "claude");
        bool claudeZakr = claude != "" && claude != "как всё остальное";
        if (rez.Count == 0)
        {
            var imena = Pult.L(servData, "servery").OfType<string>().Where(x => x != "").Select(Pult.Chisto).ToList();
            string t = imena.Count > 0
                ? "Серверы ещё не замерялись. Нажмите «Замерить и выбрать лучший» — сторож проверит каждый и поставит самый быстрый (2–5 минут). В подписке: " + string.Join(", ", imena) + "."
                : KaringVyklyuchen() ? "Серверов не видно: Karing выключен. Запустите Karing — и здесь появятся серверы вашей подписки."
                : "Серверов пока не видно. Проверьте, что в Karing добавлена подписка VPN, и нажмите «Замерить и выбрать лучший».";
            var kar = new Kartochka { Width = W, Height = 34 + VysTeksta(t, W - 70), Margin = new Padding(0, 0, 0, 12) };
            kar.Controls.Add(Zk(Zn.Server, 16, 16, 13f));
            kar.Controls.Add(Np(t, 46, 16, false, W - 70));
            k.Controls.Add(kar);
            return k;
        }
        var nad = Pult.D(Pult.G(Pult.D(Pult.G(sost, "stat")), "nadezhnost"));
        var podrobno = Pult.D(Pult.G(servData, "podrobno"));
        var setka = new FlowLayoutPanel { Width = W + 12, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        int kol = W < 820 ? 2 : 3, cw = (W - 12 * (kol - 1)) / kol;
        var fZag = new Font("Segoe UI Semibold", 11f);
        foreach (var r in rez)
        {
            string imya = (string)r[0];
            int sk = Pult.Cel(Pult.El(r, 1)), ping = Pult.Cel(Pult.El(r, 2));
            var ndL = Spisok(Pult.G(nad, imya));
            int? obr = ndL.Count > 0 ? Pult.Cel(ndL[0]) : (int?)null;
            bool tek = imya == seychas, kl = imya == claude, mertv = sk < 5;
            var pod = Pult.D(Pult.G(podrobno, imya));
            string vid = Pult.S(pod, "vid"), slovo = Pult.S(pod, "slovo");
            Color vc = vid == "plohoy" ? Kras : vid == "nado_znat" ? Zhelt : Zel;
            var prich = Pult.L(pod, "prichiny").OfType<string>().ToList();
            var kar = new Kartochka { Width = cw, Height = 186, Margin = new Padding(0, 0, 12, 12), Ottenok = tek ? Ris.Smes(pal.Poverhnost, pal.Akcent, 0.18) : (Color?)null };
            string metka = tek ? "сейчас" : kl ? "Claude" : "";
            int metkaW = metka != "" ? TextRenderer.MeasureText(metka, Font).Width + 4 : 0;
            kar.Controls.Add(Np1(Pult.Chisto(imya), 14, 12, cw - 28 - metkaW - 8, false, fZag));
            if (metka != "") kar.Controls.Add(Np1(metka, cw - 14 - metkaW, 14, metkaW, true, null, false));
            if (slovo != "")
            {
                var pl = new Kartochka { Location = new Point(14, 118), Size = new Size(Math.Min(cw - 160, TextRenderer.MeasureText(slovo, Font).Width + 22), 24), Ottenok = Ris.Smes(pal.Poverhnost, vc, 0.3) };
                pl.Controls.Add(Np1(slovo, 10, 2, pl.Width - 14, false));
                kar.Controls.Add(pl);
            }
            if (prich.Count > 0)
            {
                var pr = Np(string.Join("; ", prich), 14, 146, true, cw - 28); pr.AutoSize = false; pr.Size = new Size(cw - 28, 36); pr.AutoEllipsis = true;
                kar.Controls.Add(pr);
                podskazki.SetToolTip(pr, string.Join(Environment.NewLine, prich));
            }
            string skT = (sk / 8.0).ToString(sk < 80 ? "0.0" : "0");
            var fSk = new Font("Segoe UI Semibold", 20f);
            int skW = TextRenderer.MeasureText(skT, fSk).Width;
            kar.Controls.Add(Np1(skT, 12, 36, skW + 6, false, fSk, false));
            kar.Controls.Add(Np1("МБ/с", 20 + skW, 52, 60, true, null, false));
            kar.Controls.Add(Np1("пинг " + ping + " мс" + (obr != null ? " · обрывов за сутки " + obr : ""), 14, 78, cw - 28));
            if (obr != null)
            {
                // полоса: доля часов без обрывов
                int bad = Math.Max(0, Math.Min(24, obr.Value));
                kar.Controls.Add(new Uroven { Location = new Point(14, 104), Size = new Size(cw - 28, 8), Dolya = 1 - bad / 24.0 });
            }
            if (!tek && !mertv)
            {
                var kp = Kn("Поставить", () =>
                {
                    if (KaringVyklyuchen()) { zanyatoStroka.Text = "Сначала запустите Karing: без него сервер не поставить."; return; }
                    if (Sprosit("Пустить сайты и программы через " + Pult.Chisto(imya) + "? Karing перезапустится, интернет моргнёт.")) Deystvie("переключение на " + Pult.Chisto(imya), new[] { "server", imya }, d => servData = null);
                }, false, 120);
                kp.Height = 28; kp.Location = new Point(cw - 134, 116); kp.Margin = Padding.Empty; kar.Controls.Add(kp);
            }
            setka.Controls.Add(kar);
        }
        k.Controls.Add(setka);
        k.Controls.Add(Tx("Полоса под цифрами — сколько часов за сутки сервер проработал без обрывов. «Адрес меняется» — сайты каждый раз видят новый адрес и могут выкидывать из аккаунтов, сторож такие сам не выбирает." +
                          (claudeZakr ? " Claude закреплён за сервером «" + Pult.Chisto(claude) + "» — здесь это не меняется." : ""), true));
        return k;
    }

    // ---------- zapret ----------
    Dictionary<string, object> zapData;
    string hodFayl; long hodStart; TextBox hodPole;
    bool zapIdet;

    Panel StrZapret()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("zapret", "открывает YouTube и голос Discord без VPN", null));
        bool rab = Pult.B(sost, "zapret");
        string strat = Pult.S(sost, "strategiya");
        bool zapOk = zapData != null && !Pult.Sboy(zapData) && Pult.G(zapData, "youtube") != null;
        string ver = Pult.S(zapData, "versiya"), posl = Pult.S(zapData, "posledn");
        // не установлен: папки нет в «Загрузках», служба не работает и стратегии в реестре нет
        bool netZ = zapOk && ver == "" && !rab && strat == "";
        bool estNovee = zapOk && posl != "" && ver != "" && Sravnit(posl, ver) > 0;

        var top = new Kartochka { Width = W, Height = 64, Margin = new Padding(0, 0, 0, 12) };
        top.Controls.Add(new Krug { Size = new Size(34, 34), Location = new Point(14, 15), Cvet = rab ? Zel : Kras, Galka = rab });
        int tw = W - 166 - 60 - 10;
        top.Controls.Add(Np1(rab ? "Работает" + (strat != "" ? " · стратегия " + strat : "") : netZ ? "zapret не установлен" : "Не работает", 60, 10, tw, false, new Font("Segoe UI Semibold", 12f)));
        string vText = zapData == null ? (suho ? "" : "проверяю…") : !zapOk ? "не смог проверить: " + Pult.Info(zapData) :
            netZ ? "его нет в «Загрузках» — нажмите «Скачать и поставить zapret» ниже" :
            ver == "" ? "версию не узнал: папки zapret-discord-youtube нет в «Загрузках»" :
            "версия " + ver + (posl == "" ? " · последнюю на GitHub узнать не удалось" : estNovee ? " · есть новее: " + posl : " · последняя, обновлять не нужно");
        top.Controls.Add(Np1(vText, 62, 36, tw));
        var kp = KnZ("Проверить", Zn.Obnovit, () => { zapData = null; Perestroit(); }, false, 150);
        kp.Location = new Point(W - 166, 14); kp.Margin = Padding.Empty; top.Controls.Add(kp);
        k.Controls.Add(top);

        if (zapData == null && !suho && !zapIdet)
        {
            zapIdet = true;
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("zapret"); try { BeginInvoke((Action)(() => { zapIdet = false; zapData = d; if (tekushchaya == "zapret") Perestroit(); })); } catch { zapIdet = false; } });
        }
        else if (zapOk)
        {
            int cw2 = (W - 12) / 2;
            var dva = new Panel { Width = W, Height = 124, Margin = new Padding(0, 0, 0, 12) };
            var yt = Spisok(Pult.G(zapData, "youtube"));
            bool ytOk = Pult.El(yt, 2) is bool && (bool)Pult.El(yt, 2);
            var a = new Kartochka { Size = new Size(cw2, 124), Location = new Point(0, 0), Ottenok = Ris.Smes(pal.Poverhnost, ytOk ? Zel : Kras, pal.Temnaya ? 0.14 : 0.08) };
            a.Controls.Add(Np1("YouTube напрямую", 14, 12, cw2 - 28));
            a.Controls.Add(Np1(ytOk ? "открывается · " + Pult.Ch(Pult.El(yt, 1)).ToString("0.00") + " с" : "не открывается", 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f)));
            a.Controls.Add(Np(ytOk ? "zapret справляется" : rab ? "нужно подобрать стратегию" : netZ ? "нужен zapret — или VPN" : "zapret не работает", 14, 70, true, cw2 - 28));
            dva.Controls.Add(a);
            var g = Pult.D(Pult.G(zapData, "golos"));
            var b = new Kartochka { Size = new Size(cw2, 124), Location = new Point(cw2 + 12, 0) };
            b.Controls.Add(Np1("Голос Discord за 2 минуты", 14, 12, cw2 - 28));
            if (Pult.B(g, "est"))
            {
                double pot = Pult.Ch(Pult.G(g, "poteri"));
                Color c = pot < 2 ? Zel : pot < 8 ? Zhelt : Kras;
                var n = Np1("потерь " + pot.ToString("0.#") + " %" + (Pult.S(g, "ping") != "" ? " · пинг " + Pult.S(g, "ping") + " мс" : ""), 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f));
                n.Tag = c; b.Controls.Add(n);
                b.Controls.Add(Np(pot < 2 ? "голос идёт чисто" : pot < 8 ? "возможны редкие заикания" : "голос будет прерываться — попробуйте подобрать стратегию", 14, 70, true, cw2 - 28));
            }
            else
            {
                b.Controls.Add(Np1("вы не в звонке", 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f)));
                b.Controls.Add(Np("зайдите в голосовой канал и нажмите «Проверить»", 14, 70, true, cw2 - 28));
            }
            dva.Controls.Add(b);
            k.Controls.Add(dva);
        }

        var dey = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        var b1 = KnZ("Подобрать лучшую стратегию", Zn.Molniya, PodobratZapret, true, 290);
        var b2 = KnZ("Быстрее YouTube", "", () => ZapustitSkript("zapret: быстрее YouTube", "zapret-youtube.ps1", "zapret-youtube.log", "Идёт несколько минут. YouTube и сайты без VPN будут прерываться. Начать?"), false, 200);
        var b3 = KnZ(netZ ? "Скачать и поставить zapret" : estNovee ? "Обновить до " + posl : "Обновить zapret", "", ObnovitZapret, estNovee || netZ, netZ ? 290 : 220);
        var knopki = new List<Knopa>();
        if (!netZ) { knopki.Add(b1); knopki.Add(b2); }
        if (estNovee || netZ || (zapOk && posl == "" && ver != "")) knopki.Add(b3);
        var knopkiZ = new FlowLayoutPanel { Location = new Point(14, 14), Width = W - 28, WrapContents = true, BackColor = Color.Transparent };
        foreach (var bb in knopki) { bb.Margin = new Padding(0, 0, 10, 8); knopkiZ.Controls.Add(bb); }
        dey.Controls.Add(knopkiZ);
        int vysKn = knopki.Count == 0 ? 0 : knopkiZ.GetPreferredSize(new Size(W - 28, 0)).Height;
        knopkiZ.Height = Math.Max(1, vysKn);
        int y = 14 + vysKn + 2;
        string t = netZ ? "Скачаю zapret с GitHub (Flowseal/zapret-discord-youtube) и поставлю службу. Windows спросит разрешение." : "Подбор идёт около 10 минут, всё это время интернет без VPN рвётся. Ход — здесь же, строками.";
        dey.Controls.Add(Np(t, 16, y, true, W - 32));
        y += VysTeksta(t, W - 32) + 10;
        if (hodFayl != null)
        {
            hodPole = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Location = new Point(16, y), Size = new Size(W - 32, 184), Font = new Font("Consolas", 9.5f) };
            dey.Controls.Add(hodPole);
            ObnovitHod();
            y += 184 + 10;
        }
        dey.Height = y + 6;
        k.Controls.Add(dey);
        return k;
    }

    static int Sravnit(string a, string b)
    {
        Version x, y;
        if (!Version.TryParse(new string((a ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray()), out x)) return 0;
        if (!Version.TryParse(new string((b ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray()), out y)) return 0;
        return x.CompareTo(y);
    }

    void PodobratZapret() { ZapustitSkript("подбор стратегии zapret", "zapret-avto.ps1", "zapret-avto.log", "Подбор идёт около 10 минут. Всё это время интернет без VPN будет рвать. Начать?"); }

    void ObnovitZapret()
    {
        ZapustitSkript("обновление zapret", "zapret-obnovit.ps1", "zapret-obnovit.log",
            zapData != null && !Pult.Sboy(zapData) && Pult.S(zapData, "versiya") == ""
            ? "Скачаю zapret с GitHub (Flowseal/zapret-discord-youtube, архив ~4 МБ) в «Загрузки» и поставлю службу со стратегией general (ALT13). Windows спросит разрешение. Начать?"
            : "Проверю новую версию zapret на GitHub (Flowseal/zapret-discord-youtube). Если она есть — скачаю архив (~4 МБ) в «Загрузки», перенесу ваши списки и поставлю ту же стратегию. Старая версия останется на месте. Начать?");
    }

    void ZapustitSkript(string chto, string fayl, string log, string vopros)
    {
        if (suho) { Zapis("[тест] скрипт: " + fayl); return; }
        if (zanyat) { zanyatoStroka.Text = "Сначала дождитесь конца: " + deystvieSeychas; return; }
        string put = Path.Combine(Pult.Papka, fayl);
        if (!File.Exists(put)) { zanyatoStroka.Text = "Не нашёл " + fayl + " в папке storozh-seti — распакуйте архив целиком."; return; }
        if (!Sprosit(vopros)) return;
        var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + put + "\"") { WorkingDirectory = Pult.Papka };
        if (Admin()) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
        else { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
        hodFayl = Path.Combine(Pult.Papka, log);
        try { hodStart = File.Exists(hodFayl) ? new FileInfo(hodFayl).Length : 0; } catch { hodStart = 0; }
        Process p;
        try { p = Process.Start(psi); }
        catch { zanyatoStroka.Text = "Не запустил: права администратора не дали."; hodFayl = null; return; }
        if (p == null) { zanyatoStroka.Text = "Не запустил " + fayl + "."; hodFayl = null; return; }
        zanyat = true; deystvieSeychas = chto;
        zanyatoStroka.Text = "Идёт: " + chto + "…";
        Zapis("скрипт: " + fayl);
        if (tekushchaya == "zapret") Perestroit();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { p.WaitForExit(); } catch { }
            try { BeginInvoke((Action)(() => { zanyat = false; zanyatoStroka.Text = "Готово: " + chto; Zapis("Готово: " + chto); ObnovitHod(); zapData = null; Obnovit(); })); } catch { }
        });
    }

    void ObnovitHod()
    {
        if (hodFayl == null || hodPole == null || hodPole.IsDisposed) return;
        try
        {
            using (var f = new FileStream(hodFayl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                f.Seek(Math.Min(hodStart, f.Length), SeekOrigin.Begin);
                using (var r = new StreamReader(f, Encoding.UTF8, true))
                {
                    var t = r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
                    hodPole.Text = t == "" ? "Запускается…" : t;
                    hodPole.SelectionStart = hodPole.TextLength; hodPole.ScrollToCaret();
                }
            }
        }
        catch { if (hodPole.Text == "") hodPole.Text = "Запускается…"; }
    }

    // ---------- Журнал ----------
    Dictionary<string, object> lentaData;
    string lentaFiltr = "";

    Panel StrZhurnal()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Журнал", "", null));
        if (lentaData == null)
        {
            k.Controls.Add(Tx("Загружаю…", true));
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("lenta"); try { BeginInvoke((Action)(() => { lentaData = d; if (tekushchaya == "zhurnal") Perestroit(); })); } catch { } });
            return k;
        }
        if (Pult.Sboy(lentaData))
        {
            k.Controls.Add(KartaOshibki(W, "Не смог прочитать журнал сторожа", Pult.Info(lentaData), () => { lentaData = null; Perestroit(); }));
            return k;
        }
        var vse = Spiski(lentaData, "lenta").Where(e => e.Count >= 3).ToList();
        var filtry = Ryad(); filtry.Margin = new Padding(0, 0, 0, 10); filtry.MaximumSize = new Size(W, 0);
        foreach (var f in new[] { new[] { "", "Всё", "" }, new[] { "ispr", "Исправлено", "" }, new[] { "obryv", "Обрывы", Zn.Preduprezhd }, new[] { "zamer", "Замеры", Zn.Skorost }, new[] { "moi", "Мои действия", "" } })
        {
            string id = f[0];
            int n = id == "" ? vse.Count : vse.Count(e => Convert.ToString(e[1]) == id);
            string t = f[1] + " · " + n;
            var kn = Kn(t, () => { lentaFiltr = id; Perestroit(); }, lentaFiltr == id, TextRenderer.MeasureText(t, Font).Width + 30);
            filtry.Controls.Add(kn);
        }
        k.Controls.Add(filtry);
        var spis = vse.Where(e => lentaFiltr == "" || Convert.ToString(e[1]) == lentaFiltr).Take(60).ToList();
        var kar = new Kartochka { Width = W, Height = 16 + Math.Max(1, spis.Count) * 36, Margin = new Padding(0, 0, 0, 12) };
        int y = 8;
        foreach (var e in spis)
        {
            string vid = Convert.ToString(e[1]);
            Color c = vid == "ispr" ? Zel : vid == "obryv" ? Zhelt : vid == "zamer" ? pal.Akcent : pal.Vtorichnyy;
            string zn = vid == "ispr" ? "" : vid == "obryv" ? Zn.Preduprezhd : vid == "zamer" ? Zn.Skorost : vid == "moi" ? "" : "";
            kar.Controls.Add(Np1(Convert.ToString(e[0]), 16, y + 8, 98, true, null, false));
            kar.Controls.Add(Zk(zn, 120, y + 7, 11f, c));
            kar.Controls.Add(Np1(Pult.Chisto(Convert.ToString(e[2])), 148, y + 8, W - 170, false));
            y += 36;
        }
        if (spis.Count == 0) kar.Controls.Add(Np(vse.Count == 0 ? "Пусто: сторож ещё ничего не делал. Записи появятся после первой проверки сети." : "В этом разделе пусто.", 16, 12, true, W - 32));
        k.Controls.Add(kar);
        return k;
    }

    // ---------- Настройки ----------
    Panel StrNastroyki()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Настройки", "", null));
        if (sost == null) { k.Controls.Add(Tx("Загружаю…", true)); return k; }
        var nas = Pult.G(sost, "nastroyki") as Dictionary<string, object>;
        if (nas == null)
        {
            // без нынешних значений переключатели показали бы неправду — лучше честно сказать
            k.Controls.Add(KartaOshibki(W, "Не смог прочитать настройки", Pult.Sboy(sost) ? Pult.Info(sost) : "сторож не прислал настройки", () => { sost = null; Perestroit(); Obnovit(); }));
            return k;
        }
        k.Controls.Add(GruppaNastroek(W, "Сторож сам", nas, new[] {
            new[] { "avto_server", "Выбирать самый быстрый сервер", "ночью мерит все и ставит лучший с постоянным адресом" },
            new[] { "avto_sayty", "Пускать через VPN сайты, которые не открываются", "проверяет раз в час, Claude и YouTube не трогает" },
            new[] { "avto_spiski", "Обновлять списки блокировок", "раз в сутки, около 83 тысяч адресов" } }));
        var ne = GruppaNastroek(W, "Не мешать", nas, new[] { new[] { "ne_v_igre", "Не перезапускать Karing во время игр", "правки ждут, пока игра закроется" } });
        int yz = ne.Height - 4;
        bool ryadom = W >= 780;
        int tw = ryadom ? W - 336 - 16 - 12 : W - 32;
        ne.Controls.Add(Np1("Звонок в Discord задерживает правки не дольше", 16, yz, tw, false));
        ne.Controls.Add(Np1("потом звонок моргнёт секунд на 10", 16, yz + 22, tw));
        int tek; if (!int.TryParse(Pult.S(nas, "zvonok_min"), out tek)) tek = 20;
        var vars = new[] { 0, 10, 20, 60 };
        var seg = new Segmenty { Punkty = vars.Select(v => v == 0 ? "не ждать" : v + " мин").ToArray(), Location = ryadom ? new Point(W - 336, yz + 2) : new Point(16, yz + 50), Size = new Size(320, 32) };
        int idx = Array.IndexOf(vars, tek);
        seg.Vybran = idx >= 0 ? idx : Array.IndexOf(vars, vars.OrderBy(v => Math.Abs(v - tek)).First());
        seg.Smena += i => Sohranit("zvonok_min", vars[i]);
        ne.Controls.Add(seg);
        ne.Height = ryadom ? yz + 58 : yz + 50 + 32 + 16;
        k.Controls.Add(ne);
        k.Controls.Add(GruppaNastroek(W, "Уведомления", nas, new[] { new[] { "zapret_slezhka", "Окошко, если zapret перестал работать", "и запись в журнал" } }));
        string claude = Pult.Chisto(Pult.S(sost, "claude"));
        k.Controls.Add(Tx(claude != "" && claude != "как всё остальное"
            ? "Claude закреплён за сервером «" + claude + "» — здесь это не меняется (claude_strana в storozh-seti\\nastroyki.json)."
            : "Claude идёт как всё остальное. Закрепить его за одним сервером можно в storozh-seti\\nastroyki.json (claude_strana — имя сервера, как в Karing).", true));
        return k;
    }

    Kartochka GruppaNastroek(int W, string zag, Dictionary<string, object> nas, string[][] punkty)
    {
        var kar = new Kartochka { Width = W, Height = 38 + punkty.Length * 58 + 6, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(Np1(zag, 16, 12, W - 32));
        int y = 38;
        foreach (var p in punkty)
        {
            string key = p[0];
            kar.Controls.Add(Np1(p[1], 16, y + 4, W - 16 - 76, false));
            kar.Controls.Add(Np1(p[2], 16, y + 26, W - 16 - 76));
            var t = new Tumbler { Location = new Point(W - 60, y + 12), Vkl = Pult.B(nas, key) };
            t.Smena += v => Sohranit(key, v);
            kar.Controls.Add(t);
            y += 58;
        }
        return kar;
    }
}

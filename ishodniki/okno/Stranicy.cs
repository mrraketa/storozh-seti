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
    internal static string Vremya(int sek) { sek = Math.Max(0, sek); return sek >= 3600 ? (sek / 3600) + " ч " + (sek % 3600 / 60) + " мин" : sek >= 60 ? (sek / 60) + " мин " + (sek % 60) + " с" : sek + " с"; }
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
            if (!netPy) k.Controls.Add(Tx("Окно спрашивает сторожа раз в минуту само. Если ошибка не уходит — подробности в журнале окна: файл storozh-okno.log рядом с программой.", true));
            return k;
        }
        var bedy = Pult.L(sost, "bedy").OfType<string>().Where(b => b.Trim() != "").ToList();
        var st = Pult.D(Pult.G(sost, "stat"));
        bool karing = Pult.B(sost, "karing");
        // список первичной настройки (DOGOVOR-hod.md §2): виден, пока есть несделанный обязательный пункт и он не скрыт
        var nastr = Pult.Slovari(sost, "nastroyka");
        bool pokazNastr = est && nastr.Count > 0 && !Pult.B(sost, "nastroyka_skryta") && nastr.Any(p => !Pult.B(p, "gotovo") && !Pult.B(p, "neobyaz"));

        // состояние одной строкой + кнопки (узкое окно — кнопки под текстом); Claude — не в подписи, а значком справа
        Color cv = !est ? pal.Liniya : bedy.Count == 0 ? Zel : (karing ? Zhelt : Kras);
        string kogda = Kogda(Pult.S(sost, "posledniy"));
        bool zapUst = Pult.B(sost, "zapret") || Pult.S(sost, "strategiya") != "";
        string podp = est ? (Pult.B(sost, "zapret") ? "zapret работает" : zapUst ? "zapret не работает" : "zapret не установлен") +
                            "  ·  " + (kogda != "" ? "сторож проверял сеть " + kogda : "сторож ещё не проверял сеть") : "";
        const string CHTO_BUDET = "«Проверить» — сторож сразу проверит сеть и починит, что сломано (интернет может моргнуть). «Лучший сервер» — 2–5 минут, интернет пару раз моргнёт.";
        var cz = Pult.D(Pult.G(sost, "claude_znachok"));
        var znCl = est && Pult.B(cz, "pokazat") ? ZnachokClaude(cz) : null;
        int znW = znCl != null ? znCl.Width + 12 : 0;
        bool shirokoVerh = W >= 820;
        int tekstW = shirokoVerh ? W - 64 - 16 - 350 - znW : W - 64 - 16;
        int zagW = shirokoVerh ? tekstW : W - 62 - 16 - znW;
        int podpH = est ? VysTeksta(podp, tekstW) : 0;
        int knY = shirokoVerh ? 13 : 40 + podpH + 10;
        int budX = shirokoVerh ? W - 346 : 64, budW = shirokoVerh ? 330 : W - 64 - 16;
        int budH = est ? VysTeksta(CHTO_BUDET, budW) : 0;
        int vys = shirokoVerh ? Math.Max(Math.Max(64, 40 + podpH + 12), est ? knY + 38 + 6 + budH + 10 : 64) : knY + 38 + (est ? 6 + budH : 0) + 12;
        var top = new Kartochka { Name = "glavnaya-sostoyanie", Width = W, Height = vys, Margin = new Padding(0, 0, 0, 12), Ottenok = Ris.Smes(pal.Poverhnost, cv, pal.Temnaya ? 0.16 : 0.10) };
        top.Controls.Add(new Krug { Size = new Size(36, 36), Location = new Point(14, 14), Cvet = cv, Galka = est && bedy.Count == 0 });
        top.Controls.Add(Np1(!est ? "Смотрю…" : bedy.Count == 0 ? "Всё работает" : "Есть что поправить", 62, 10, zagW, false, new Font("Segoe UI Semibold", 13f), false));
        if (est) top.Controls.Add(Np(podp, 64, 40, true, tekstW));
        var k1 = KnZ("Проверить", Zn.Obnovit, () => Deystvie("Проверка сети", new[] { "proverka" }, null), true, 150);
        var k2 = KnZ("Лучший сервер", Zn.Skorost, NaytiLuchshiy, false, 180);
        podskazki.SetToolTip(k1, "Сторож сразу проверит VPN, сайты и программы и починит, что сломано.\nЕсли чинить нужно — Karing перезапустится, интернет моргнёт на пару секунд.");
        podskazki.SetToolTip(k2, "Замерит скорость всех серверов VPN и поставит самый быстрый.\n2–5 минут, интернет пару раз моргнёт.");
        k1.Location = shirokoVerh ? new Point(W - 346, knY) : new Point(64, knY);
        k2.Location = shirokoVerh ? new Point(W - 192, knY) : new Point(64 + 160, knY);
        k1.Margin = k2.Margin = Padding.Empty;
        top.Controls.Add(k1); top.Controls.Add(k2);
        if (est) top.Controls.Add(Np(CHTO_BUDET, budX, knY + 38 + 6, true, budW));
        if (znCl != null)
        {
            // правый угол: на узком окне — над кнопками, на широком — левее кнопок, по их середине
            znCl.Location = shirokoVerh ? new Point(W - 346 - 12 - znCl.Width, knY + 6) : new Point(W - 14 - znCl.Width, 12);
            top.Controls.Add(znCl);
        }
        k.Controls.Add(top);
        if (pokazNastr) k.Controls.Add(KartaNastroyki(W, nastr));

        // что сделать сейчас — только если есть (то, что уже стоит в списке «Настройка», второй раз не показываем)
        Func<string, bool> vSpiske = id => pokazNastr && nastr.Any(p => Pult.S(p, "id") == id && !Pult.B(p, "gotovo"));
        foreach (var b in bedy)
        {
            if ((b.StartsWith("Karing") && vSpiske("karing")) || (b.StartsWith("Сторож не запускается") && vSpiske("zadaniya")) || (b.StartsWith("zapret") && vSpiske("zapret"))) continue;
            Knopa kn = null; string pod = null;
            if (b.StartsWith("zapret"))
            {
                // службы нет и настройки обхода в реестре нет — zapret не поставлен: подбирать нечего, сначала поставить
                if (!zapUst)
                {
                    pod = "Похоже, zapret не установлен. Без него YouTube и голос Discord идут только через VPN.";
                    kn = Kn("Поставить zapret", () => Otkryt("zapret"), true, 210);
                }
                else
                {
                    pod = "«Подобрать настройку» переберёт настройки обхода в zapret и поставит самую надёжную. Около 10 минут — всё это время интернет без VPN рвётся.";
                    kn = Kn("Подобрать настройку", PodobratZapret, true, 210);
                }
            }
            else if (b.StartsWith("Пустить через VPN"))
            {
                pod = "Сторож пустит эти сайты через VPN. Karing перезапустится — интернет моргнёт на пару секунд.";
                kn = Kn("Поправить сейчас", () => Deystvie("Проверка сети и правка", new[] { "proverka" }, null), true, 210);
            }
            else if (b.StartsWith("Сторож не запускается"))
            {
                pod = "Сторож будет сам проверять сеть каждые 10 минут, даже когда это окно закрыто. Прав администратора не нужно.";
                kn = Kn("Включить", VklyuchitZadaniya, true, 210);
            }
            else if (b.StartsWith("Karing"))
            {
                pod = "Запустите Karing и включите в нём подключение (режим «Правила»). Без Karing VPN не работает.";
                kn = Kn("Проверить снова", ProveritSnova, false, 210);
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
        sp.Controls.Add(Np1(!karing ? "Karing выключен" : server == "" ? "сервер не выбран" : server + (skor > 0 ? " · последний замер" : " · ещё не замерялся"), 16, 158, 200, false));
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
        foreach (var c in new[] { Tuple.Create("обрывов", Pult.Cel(Pult.G(sost, "miganiya")).ToString()), Tuple.Create("VPN не отвечал всего", Vremya(Pult.Cel(Pult.G(st, "bez_vpn")))),
                                  Tuple.Create("самый долгий обрыв", Vremya(Pult.Cel(Pult.G(st, "samyy_dlinnyy")))) })
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
        foreach (var c in new[] { Tuple.Create("", "Скачивание", "МБ/с"), Tuple.Create("", "Отправка", "МБ/с"), Tuple.Create(Zn.Shit, "Из них через VPN", "МБ/с") })
        {
            set.Controls.Add(Zk(c.Item1, sx, 46, 11f));
            set.Controls.Add(Np1(c.Item2, sx + 22, 44, 160));
            var ch = new Nadpis { Text = "…", Font = new Font("Segoe UI Semibold", 17f), Location = new Point(sx - 2, 66) };
            set.Controls.Add(ch);
            set.Controls.Add(Np(c.Item3, sx + 86, 78, true));
            if (c.Item2 == "Скачивание") setVniz = ch; else if (c.Item2 == "Отправка") setVverh = ch; else setVpn = ch;
            sx += 196;
        }
        var kt = KnZ(testIdet ? "Идёт тест…" : "Тест скорости", Zn.Skorost, () =>
        {
            if (testIdet) return;
            testIdet = true;
            if (!Deystvie("Тест скорости", new[] { "test-seti" }, d => { testIdet = false; testData = d; })) testIdet = false;
        }, true, 200);
        kt.Location = shirokoSet ? new Point(W - 216, 46) : new Point(16, 116); kt.Margin = Padding.Empty; set.Controls.Add(kt);
        const string TEST_CHTO = "около 30 с: скачает по 25 МБ через VPN и напрямую";
        podskazki.SetToolTip(kt, "Скачает 25 МБ через VPN и 25 МБ напрямую и покажет обе скорости. Около 30 секунд, интернет не рвётся.");
        set.Controls.Add(shirokoSet ? Np1(TEST_CHTO, W - 216, 88, 200) : Np1(TEST_CHTO, 226, 125, W - 242));
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
                        mv >= 50 && mp >= 50 ? "И через VPN, и напрямую быстро — всё в порядке." : mv < 50 && mp >= 50 ? "VPN медленнее 6 МБ/с — попробуйте «Лучший сервер» (вверху)." :
                        mp < 50 && mv >= 50 ? "Напрямую медленно — похоже, тормозит провайдер или zapret." : "Медленно везде — похоже на провайдера или Wi-Fi.";
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
            k.Controls.Add(Tx("Сейчас: " + string.Join(", ", igra) + ". Пока это идёт, сторож не перезапускает Karing, чтобы не оборвать" + (zv > 0 ? " (звонок ждёт не дольше " + zv + " мин)." : "."), true));
        }
        return k;
    }

    void VklyuchitZadaniya() { Deystvie("Включение проверки каждые 10 минут", new[] { "ustanovit-zadaniya" }, null); }
    void ProveritSnova() { zanyatoStroka.Text = "Проверяю…"; Obnovit(() => zanyatoStroka.Text = ""); }

    // Значок-«пилюля» Claude в правом углу главной карточки (его выбор 21.11): «● Claude · Англия»,
    // точка зелёная — отвечает, красная — нет, серая — не проверяли; при наведении — когда проверено.
    Kartochka ZnachokClaude(Dictionary<string, object> cz)
    {
        var okV = Pult.G(cz, "ok");
        Color c = okV is bool ? ((bool)okV ? Zel : Kras) : Sery;
        string srv = Pult.Chisto(Pult.G(cz, "server") as string ?? "").Trim();
        string t = "Claude" + (srv != "" ? " · " + srv : "");
        int tw = Math.Min(170, TextRenderer.MeasureText(t, Font).Width + 4);
        var z = new Kartochka { Name = "claude-znachok", Size = new Size(26 + tw + 10, 26) };
        var tochka = new Nadpis { Text = "●", Tag = c, ForeColor = c, Location = new Point(8, 3) };
        var n = Np1(t, 26, 3, tw, false, null, false);
        z.Controls.Add(tochka); z.Controls.Add(n);
        string kogda = Pult.Chisto(Convert.ToString(Pult.G(cz, "proveren") ?? "", CultureInfo.CurrentCulture)).Trim();
        string pod = (okV is bool ? ((bool)okV ? "Claude отвечает" : "Claude не отвечает") + (kogda != "" ? " — проверено " + kogda : "") : "Claude ещё не проверяли") +
                     (srv != "" ? "\nСторож пускает Claude через сервер «" + srv + "»." : "");
        foreach (Control x in new Control[] { z, tochka, n }) podskazki.SetToolTip(x, pod);
        return z;
    }

    // Список первичной настройки на Мониторе (его выбор 21.11): «Настройка — 2 из 4», справа «Скрыть»;
    // пункты с галочкой или пустым кружком, у несделанного — кнопка и короткий совет.
    Kartochka KartaNastroyki(int W, List<Dictionary<string, object>> nastr)
    {
        var kar = new Kartochka { Name = "karta-nastroyki", Width = W, Margin = new Padding(0, 0, 0, 12) };
        string zag = "Настройка — " + nastr.Count(p => Pult.B(p, "gotovo")) + " из " + nastr.Count;
        var fz = new Font("Segoe UI Semibold", 11f);
        kar.Controls.Add(Zg(zag, 16, 12));
        int zx = 16 + TextRenderer.MeasureText(zag, fz).Width + 10;
        var skr = Kn("Скрыть", () =>
        {
            if (sost != null) sost["nastroyka_skryta"] = true;   // сразу, не дожидаясь сторожа
            Perestroit();
            Sohranit("nastroyka_skryta", true);
        }, false, 100);
        skr.Height = 30; skr.Location = new Point(W - 16 - skr.Width, 9); skr.Margin = Padding.Empty;
        podskazki.SetToolTip(skr, "Спрятать этот список. Вернуть его можно в «Настройках», раздел «Монитор».");
        kar.Controls.Add(skr);
        kar.Controls.Add(Np1("что осталось сделать, чтобы сторож работал сам", zx, 15, W - 16 - skr.Width - 12 - zx));
        int y = 50;
        foreach (var p in nastr)
        {
            string id = Pult.S(p, "id");
            bool g = Pult.B(p, "gotovo"), neob = Pult.B(p, "neobyaz");
            string tekst = Pult.Chisto(Pult.G(p, "tekst") as string ?? "").Trim();
            if (tekst == "") tekst = id != "" ? id : "Пункт настройки";
            string sovet = g ? "" : Pult.Chisto(Pult.G(p, "sovet") as string ?? "").Trim();
            var knopki = new List<Knopa>();
            if (!g)
            {
                if (id == "karing") knopki.Add(Kn("Проверить снова", ProveritSnova, false, 160));
                else if (id == "zadaniya") knopki.Add(Kn("Включить", VklyuchitZadaniya, true, 130));
                else if (id == "server") knopki.Add(Kn("Замерить", NaytiLuchshiy, true, 130));
                else if (id == "zapret")
                {
                    knopki.Add(Kn("Поставить", () => Otkryt("zapret"), false, 120));
                    if (neob)
                        knopki.Add(Kn("Не нужно", () =>
                        {
                            p["gotovo"] = true;   // сразу убрать из несделанных, сторож запомнит
                            Perestroit();
                            Sohranit("zapret_ne_nuzhen", true);
                        }, false, 110));
                }
            }
            if (id == "zadaniya" && knopki.Count > 0) podskazki.SetToolTip(knopki[0], "Сторож будет сам проверять сеть каждые 10 минут, даже когда окно закрыто. Прав администратора не нужно.");
            if (id == "server" && knopki.Count > 0) podskazki.SetToolTip(knopki[0], "Замерю все серверы VPN и поставлю самый быстрый. 2–5 минут, интернет пару раз моргнёт.");
            if (id == "zapret" && knopki.Count > 0) podskazki.SetToolTip(knopki[0], "Откроет раздел zapret: там кнопка «Скачать и поставить zapret».");
            int knW = knopki.Sum(x => x.Width) + Math.Max(0, knopki.Count - 1) * 8;
            int tx = 46, tw = W - tx - 16 - (knW > 0 ? knW + 16 : 0);
            int h1 = VysTeksta(tekst, tw), h2 = sovet != "" ? VysTeksta(sovet, tw) : 0;
            int rowH = Math.Max(knopki.Count > 0 ? 34 : 24, h1 + (h2 > 0 ? h2 + 2 : 0));
            kar.Controls.Add(Zk(g ? Zn.GalkaKrug : Zn.Krug0, 16, y + 1, 12f, g ? (object)Zel : "vt"));
            kar.Controls.Add(Np(tekst, tx, y + 2, g, tw));
            if (h2 > 0) kar.Controls.Add(Np(sovet, tx, y + 4 + h1, true, tw));
            int kx = W - 16 - knW;
            foreach (var kb in knopki) { kb.Height = 32; kb.Location = new Point(kx, y); kb.Margin = Padding.Empty; kar.Controls.Add(kb); kx += kb.Width + 8; }
            y += rowH + 10;
        }
        kar.Height = y + 4;
        return kar;
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
        if (Sprosit("Замерю скорость всех серверов VPN и поставлю самый быстрый из надёжных. Займёт 2–5 минут, интернет пару раз моргнёт. Начать?"))
            Deystvie("Замер серверов", new[] { "zamer" }, d => servData = null);
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
            else podskazki.SetToolTip(mb, "Программа выходит в интернет не через Karing — её скорость отдельно не видна. Общая нагрузка — в «Сеть сейчас».");
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
        else podskazki.SetToolTip(skor, vSeti ? "Программа выходит в интернет не через Karing — её скорость отдельно не видна." : "Программа сейчас не в сети.");

        bool claude = exe.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) && Pult.S(sost, "claude") != "" && Pult.S(sost, "claude") != "как всё остальное";
        if (claude)
        {
            var z = new Kartochka { Location = new Point(xPrav, yPrav), Size = new Size(300, 32), Ottenok = Ris.Smes(pal.Poverhnost, pal.Akcent, 0.18) };
            z.Controls.Add(Np1("всегда через «" + Pult.Chisto(Pult.S(sost, "claude")) + "»", 12, 6, 276, false));
            kar.Controls.Add(z);
            return rowH;
        }
        if (vSeti && !cherezKaring && !pravilo)
        {
            var z = new Kartochka { Location = new Point(xPrav, yPrav), Size = new Size(300, 32) };
            z.Controls.Add(Np1(KaringVyklyuchen() ? "Karing выключен — выбор не действует" : "идёт не через Karing — выбор не подействует", 12, 6, 276));
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
        podskazki.SetToolTip(dob, "Программы нет в списке, потому что она сейчас не в сети? Впишите её имя — и выберите, как ей ходить.");
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
        ko.Controls.Add(Np1("все программы, которые сейчас пользуются интернетом", 130, 15, W - 146));
        y = 44;
        foreach (var p in ostalnye) y += StrokaProgrammy(ko, p, W, y, true);
        if (ostalnye.Count == 0) { ko.Controls.Add(Np("Других программ в сети сейчас нет.", 16, 48, true)); y = 80; }
        ko.Height = y + 8;
        k.Controls.Add(ko);
        k.Controls.Add(Tx("Авто — заблокированное в России идёт через VPN, остальное напрямую. «Без сети» — программа не выйдет в интернет. «Мимо Karing» — программа (часто игры и загрузки Steam) выходит в интернет сама, в обход Karing, и выбор на неё не подействует. Выбор применяется сам через пару секунд: Karing перезапустится, интернет моргнёт один раз.", true));
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
                var opis = string.Join(", ", izmeneniya.Select(kv => Imya(kv.Key) + " → " + (kv.Value == "avto" ? "авто" : kv.Value == "ubrat" ? "правило убрано" : kv.Value == "vpn" ? "VPN" : kv.Value == "direct" ? "напрямую" : kv.Value == "block" ? "без сети" : Pult.Chisto(kv.Value))));
                Deystvie("Сохраняю выбор: " + opis, new[] { "nastroyki-zadat", Pult.Json(new Dictionary<string, object> { { "marshruty", mar } }) }, d =>
                {
                    izmeneniya.Clear();
                    if (Pult.Sboy(d) || !Pult.B(d, "ok")) { progData = null; return; }   // не сохранилось — показать, как есть на деле
                    Deystvie("Применяю выбор для программ (Karing перезапустится)", new[] { "primenit-marshruty" }, d2 => { progData = null; if (tekushchaya == "programmy") Perestroit(); });
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
        if (!Deystvie("Проверка сайта " + d, new[] { "proverit-sayt", d }, r => { saytIdet = false; saytProverka = r; })) saytIdet = false;
    }

    Panel StrSayty()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Сайты", "", null));
        string obT = "Заблокированные в России — через VPN (списки обновляются сами), остальные — напрямую. Сайт не открывается или тормозит? Впишите его ниже: сторож проверит, открывается ли он напрямую и через VPN, и подскажет, как лучше.";
        var obyas = new Kartochka { Width = W, Height = 40 + VysTeksta(obT, W - 70) + 14, Margin = new Padding(0, 0, 0, 12) };
        obyas.Controls.Add(Zk(Zn.Globus, 16, 16, 13f));
        obyas.Controls.Add(new Nadpis { Text = "Как идут сайты", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(46, 12) });
        obyas.Controls.Add(Np(obT, 46, 40, true, W - 70));
        k.Controls.Add(obyas);

        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(new Nadpis { Text = "Проверить сайт", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(16, 12) });
        kar.Controls.Add(Np1("до 25 секунд: открою сайт напрямую и через VPN, ничего не меняю", 150, 15, W - 166));
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
            string seych = Pult.B(saytProverka, "uzhe_vpn") ? "через VPN" : Pult.B(saytProverka, "uzhe_napr") ? "напрямую" : "как решит сторож (если сайт в списке заблокированных — через VPN)";
            string tekst = (dom != "" ? dom + " сейчас идёт: " : "Сейчас идёт: ") + seych + "." + (sovet != "" ? "  Совет: " + sovet + "." : "");
            bool estDey = (dey == "vpn" || dey == "direct") && dom != "";
            if (estDey) tekst += "  Кнопка ниже: Karing перезапустится, интернет моргнёт на пару секунд.";
            bool ryadom = estDey && W >= 900;
            int tw = ryadom ? W - 32 - 350 : W - 32;
            int th = VysTeksta(tekst, tw);
            kar.Controls.Add(Np(tekst, 16, y + 6, false, tw));
            if (estDey)
            {
                int ky = ryadom ? y : y + th + 14;
                var kd = Kn(dey == "vpn" ? "Пустить через VPN" : "Пустить напрямую", () => Deystvie((dey == "vpn" ? "Пускаю через VPN: " : "Пускаю напрямую: ") + dom, new[] { "sayt", dom, dey }, d => { saytyIst = null; saytProverka = null; }), true, 200);
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
        ki.Controls.Add(Np1("«Отменить» — сайт снова пойдёт как до правки; интернет моргнёт", 220, 15, W - 236));
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
            var kv = Kn("Отменить", () => { if (Sprosit("Отменить правку для " + dom + "? Сайт снова пойдёт так, как шёл до неё. Karing перезапустится — интернет моргнёт на пару секунд.")) Deystvie("Отмена правки " + dom, new[] { "vernut", dom }, d => saytyIst = null); }, false, 100);
            kv.Height = 30; kv.Location = new Point(W - 116, y + 4); kv.Margin = Padding.Empty; ki.Controls.Add(kv);
            y += 42;
        }
        if (ist.Count == 0) ki.Controls.Add(Np("Пока сторож ничего не менял.", 16, 46, true));
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
        podskazki.SetToolTip(zamerKn, "Замерю скорость всех серверов VPN и поставлю самый быстрый. 2–5 минут, интернет пару раз моргнёт.");
        k.Controls.Add(Shapka("Серверы", (kogda != "" ? "последний замер " + Korotko(kogda) + " · " : "") + "новый займёт 2–5 мин", zamerKn));
        string seychas = Pult.S(servData, "seychas"), claude = Pult.S(servData, "claude");
        bool claudeZakr = claude != "" && claude != "как всё остальное";
        if (rez.Count == 0)
        {
            var imena = Pult.L(servData, "servery").OfType<string>().Where(x => x != "").Select(Pult.Chisto).ToList();
            string t = imena.Count > 0
                ? "Серверы ещё не замерялись. Нажмите «Замерить и выбрать лучший» — сторож проверит каждый и поставит самый быстрый (2–5 минут, интернет пару раз моргнёт). В подписке: " + string.Join(", ", imena) + "."
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
            string metka = tek ? "сейчас" : kl ? "для Claude" : "";
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
            var otklik = Np1("отклик " + ping + " мс" + (obr != null ? " · обрывов за сутки " + obr : ""), 14, 78, cw - 28);
            kar.Controls.Add(otklik);
            podskazki.SetToolTip(otklik, "Отклик — через сколько миллисекунд сервер отвечает. Меньше — лучше: в играх и звонках меньше задержка.");
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
                    if (Sprosit("Пустить сайты и программы через сервер «" + Pult.Chisto(imya) + "»? Karing перезапустится, интернет моргнёт на пару секунд.")) Deystvie("Переключение на «" + Pult.Chisto(imya) + "»", new[] { "server", imya }, d => servData = null);
                }, false, 120);
                kp.Height = 28; kp.Location = new Point(cw - 134, 116); kp.Margin = Padding.Empty; kar.Controls.Add(kp);
                podskazki.SetToolTip(kp, "Пустить сайты и программы через этот сервер. Karing перезапустится — интернет моргнёт на пару секунд.");
            }
            setka.Controls.Add(kar);
        }
        k.Controls.Add(setka);
        k.Controls.Add(Tx("«Поставить» — сайты и программы пойдут через этот сервер: Karing перезапустится, интернет моргнёт на пару секунд. Полоса под цифрами — сколько часов за сутки сервер проработал без обрывов. «Адрес меняется» — сайты каждый раз видят новый адрес и могут выкидывать из аккаунтов, сторож такие сам не выбирает.", true));
        return k;
    }

    // ---------- zapret ----------
    Dictionary<string, object> zapData;
    string skriptLog; long skriptLogStart; TextBox skriptPole;   // журнал скрипта zapret (для старого сторожа, который не пишет ход)
    bool zapIdet;

    const string NASTROYKA_OBHODA = "Настройка обхода — набор приёмов, которыми zapret обходит блокировку. У каждой своё имя, например «general (ALT13)». Менять её вручную не нужно — для этого кнопки ниже.";

    Panel StrZapret()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("zapret", "программа, которая открывает YouTube и голос Discord без VPN", null));
        bool rab = Pult.B(sost, "zapret");
        string strat = Pult.Chisto(Pult.S(sost, "strategiya")).Trim();
        bool zapOk = zapData != null && !Pult.Sboy(zapData) && Pult.G(zapData, "youtube") != null;
        string ver = Pult.S(zapData, "versiya"), posl = Pult.S(zapData, "posledn");
        // не установлен: папки нет в «Загрузках», служба не работает и настройки обхода в реестре нет
        bool netZ = zapOk && ver == "" && !rab && strat == "";
        bool estNovee = zapOk && posl != "" && ver != "" && Sravnit(posl, ver) > 0;

        var top = new Kartochka { Width = W, Height = 64, Margin = new Padding(0, 0, 0, 12) };
        top.Controls.Add(new Krug { Size = new Size(34, 34), Location = new Point(14, 15), Cvet = rab ? Zel : Kras, Galka = rab });
        int tw = W - 166 - 60 - 10;
        top.Controls.Add(Np1(rab ? "zapret работает" : netZ ? "zapret не установлен" : "zapret не работает", 60, 10, tw, false, new Font("Segoe UI Semibold", 12f)));
        string vText = zapData == null ? (suho ? "" : "проверяю…") : !zapOk ? "не смог проверить: " + Pult.Info(zapData) :
            netZ ? "его нет в «Загрузках» — нажмите «Скачать и поставить zapret» ниже" :
            ver == "" ? "версию не узнал: папки zapret-discord-youtube нет в «Загрузках»" :
            "версия " + ver + (posl == "" ? " · новую на GitHub узнать не удалось" : estNovee ? " · есть новее: " + posl : " · последняя, обновлять не нужно");
        if (strat != "" && !netZ) vText += (vText != "" ? " · " : "") + "настройка обхода: " + strat;
        var vt = Np1(vText, 62, 36, tw);
        if (strat != "") podskazki.SetToolTip(vt, vText + "\n\n" + NASTROYKA_OBHODA);
        top.Controls.Add(vt);
        var kp = KnZ("Проверить", Zn.Obnovit, () => { zapData = null; Perestroit(); }, false, 150);
        podskazki.SetToolTip(kp, "Проверю, открывается ли YouTube без VPN и как идёт голос в Discord. Ничего не меняю.");
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
            a.Controls.Add(Np1("YouTube без VPN", 14, 12, cw2 - 28));
            a.Controls.Add(Np1(ytOk ? "открывается · " + Pult.Ch(Pult.El(yt, 1)).ToString("0.00") + " с" : "не открывается", 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f)));
            a.Controls.Add(Np(ytOk ? "zapret справляется" : rab ? "нужно подобрать настройку — кнопка ниже" : netZ ? "без zapret — только через VPN" : "zapret не работает", 14, 70, true, cw2 - 28));
            dva.Controls.Add(a);
            var g = Pult.D(Pult.G(zapData, "golos"));
            var b = new Kartochka { Size = new Size(cw2, 124), Location = new Point(cw2 + 12, 0) };
            b.Controls.Add(Np1("Голос в Discord за последние 2 минуты", 14, 12, cw2 - 28));
            if (Pult.B(g, "est"))
            {
                double pot = Pult.Ch(Pult.G(g, "poteri"));
                Color c = pot < 2 ? Zel : pot < 8 ? Zhelt : Kras;
                var n = Np1("теряется " + pot.ToString("0.#") + " %" + (Pult.S(g, "ping") != "" ? " · отклик " + Pult.S(g, "ping") + " мс" : ""), 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f));
                n.Tag = c; b.Controls.Add(n);
                b.Controls.Add(Np(pot < 2 ? "голос идёт чисто" : pot < 8 ? "возможны редкие заикания" : "голос будет прерываться — попробуйте «Подобрать надёжную настройку»", 14, 70, true, cw2 - 28));
            }
            else
            {
                b.Controls.Add(Np1("вы не в звонке", 12, 36, cw2 - 24, false, new Font("Segoe UI Semibold", 14f)));
                b.Controls.Add(Np("зайдите в голосовой канал и нажмите «Проверить»", 14, 70, true, cw2 - 28));
            }
            dva.Controls.Add(b);
            k.Controls.Add(dva);
        }

        // действия: у каждой кнопки — строка «что будет» (сколько идёт и рвётся ли интернет)
        var dey = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, 12) };
        var ryady = new List<Tuple<Knopa, string>>();
        if (!netZ)
        {
            ryady.Add(Tuple.Create(KnZ("Подобрать надёжную настройку", Zn.Molniya, PodobratZapret, true, 300),
                "Переберу все настройки обхода в zapret и поставлю самую надёжную. Около 10 минут — всё это время YouTube, Discord и сайты без VPN рвутся."));
            ryady.Add(Tuple.Create(KnZ("Ускорить YouTube", "", PodobratYoutube, false, 300),
                "Переберу настройки zapret и поставлю ту, при которой YouTube без VPN грузится быстрее всего (из тех, что работают надёжно). Несколько минут — видео и сайты без VPN в это время прерываются."));
        }
        if (estNovee || netZ || (zapOk && posl == "" && ver != ""))
            ryady.Add(Tuple.Create(KnZ(netZ ? "Скачать и поставить zapret" : estNovee ? "Обновить до " + posl : "Обновить zapret", "", ObnovitZapret, estNovee || netZ, 300),
                netZ ? "Скачаю zapret с GitHub (около 4 МБ) в «Загрузки» и включу — он будет работать сам и после перезагрузки. Windows спросит разрешение — нажмите «Да»."
                     : "Скачаю новую версию с GitHub (около 4 МБ) и включу с той же настройкой обхода, ваши списки сайтов перенесу. Пока ставится, YouTube без VPN может моргнуть; Windows спросит разрешение."));
        bool ryadom = W >= 300 + 16 + 260 + 32;
        int y = 14;
        foreach (var r in ryady)
        {
            var kn = r.Item1;
            kn.Location = new Point(16, y); kn.Margin = Padding.Empty;
            dey.Controls.Add(kn);
            int tx = ryadom ? 16 + kn.Width + 16 : 16, ty = ryadom ? y : y + 38 + 4, tw2 = W - tx - 16;
            int th = VysTeksta(r.Item2, tw2);
            dey.Controls.Add(Np(r.Item2, tx, ty + (ryadom && th < 38 ? (38 - th) / 2 : 0), true, tw2));
            y = ryadom ? y + Math.Max(38, th) + 14 : ty + th + 14;
        }
        if (strat != "" && !netZ)
        {
            const string NASTR_VNIZU = "Настройка обхода — набор приёмов, которыми zapret обходит блокировку; у каждой своё имя, например «general (ALT13)». Выбирать её руками не нужно — это делают две первые кнопки.";
            dey.Controls.Add(Np(NASTR_VNIZU, 16, y, true, W - 32));
            y += VysTeksta(NASTR_VNIZU, W - 32) + 10;
        }
        // старый сторож не пишет ход — тогда видно хотя бы журнал скрипта
        if (skriptLog != null && (hod == null || hod.Fayl == null))
        {
            dey.Controls.Add(Np1("Журнал скрипта:", 16, y, W - 32));
            y += 24;
            skriptPole = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Location = new Point(16, y), Size = new Size(W - 32, 184), Font = new Font("Consolas", 9.5f) };
            dey.Controls.Add(skriptPole);
            ObnovitZhurnalSkripta();
            y += 184 + 10;
        }
        dey.Height = y + 2;
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

    void PodobratZapret()
    {
        ZapustitSkript("Подбор надёжной настройки zapret", "zapret-avto.ps1", "zapret-avto.log",
            "Переберу все настройки обхода в zapret и поставлю самую надёжную. Это около 10 минут, и всё это время YouTube, Discord и другие сайты без VPN будут обрываться. Начать?");
    }

    void PodobratYoutube()
    {
        ZapustitSkript("Ускорение YouTube без VPN", "zapret-youtube.ps1", "zapret-youtube.log",
            "Переберу настройки zapret и поставлю ту, при которой YouTube без VPN грузится быстрее всего (из тех, что работают надёжно). Это несколько минут, и всё это время видео и сайты без VPN будут прерываться. Начать?");
    }

    void ObnovitZapret()
    {
        bool netZ = zapData != null && !Pult.Sboy(zapData) && Pult.S(zapData, "versiya") == "";
        ZapustitSkript(netZ ? "Установка zapret" : "Обновление zapret", "zapret-obnovit.ps1", "zapret-obnovit.log",
            netZ
            ? "Скачаю zapret с GitHub (архив около 4 МБ) в папку «Загрузки» и включу его с настройкой обхода, которая подходит большинству. Он будет работать сам и после перезагрузки. Windows спросит разрешение — нажмите «Да». Начать?"
            : "Проверю новую версию zapret на GitHub. Если она есть — скачаю (около 4 МБ) в «Загрузки», перенесу ваши списки сайтов и включу с той же настройкой обхода. Старая версия останется в «Загрузках» на всякий случай. Пока ставится, интернет без VPN может моргнуть. Windows спросит разрешение — нажмите «Да». Начать?");
    }

    // dop — свои параметры скрипта (например «-Install "general (ALT13)"»); -Hod добавляется к любому вызову, если скрипт его знает
    void ZapustitSkript(string chto, string fayl, string log, string vopros, string dop = "")
    {
        if (suho) { Zapis("[тест] скрипт: " + fayl); return; }
        if (zanyat) { NeSeychas(); return; }
        string put = Path.Combine(Pult.Papka, fayl);
        if (!File.Exists(put)) { zanyatoStroka.Text = "Не нашёл " + fayl + " в папке storozh-seti — распакуйте архив целиком."; return; }
        if (!Sprosit(vopros)) return;
        // Ход (DOGOVOR-hod.md): старый файл — удалить до запуска; путь — параметром -Hod (скрипт идёт с правами
        // администратора, окружение окна туда не доходит). Старая версия скрипта без -Hod упала бы на незнакомом
        // параметре — ей путь не даём, карточка тогда покажет «идёт…» и время.
        try { if (File.Exists(HOD_FAYL)) File.Delete(HOD_FAYL); } catch { }
        bool umeetHod = false;
        try { umeetHod = System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(put), @"\$Hod\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase); } catch { }
        var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + put + "\"" + (dop != "" ? " " + dop : "") + (umeetHod ? " -Hod \"" + HOD_FAYL + "\"" : "")) { WorkingDirectory = Pult.Papka };
        if (Admin()) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
        else { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
        skriptLog = Path.Combine(Pult.Papka, log);
        try { skriptLogStart = File.Exists(skriptLog) ? new FileInfo(skriptLog).Length : 0; } catch { skriptLogStart = 0; }
        Process p;
        try { p = Process.Start(psi); }
        catch { zanyatoStroka.Text = "Не запустил: Windows не дала разрешение (в её окошке нужно нажать «Да»)."; skriptLog = null; return; }
        if (p == null) { zanyatoStroka.Text = "Не запустил " + fayl + "."; skriptLog = null; return; }
        zanyat = true; deystvieSeychas = chto;
        var h = NachatHod(chto, false);
        Zapis("скрипт: " + fayl + (umeetHod ? " (ход в " + HOD_FAYL + ")" : " (старый, без хода)"));
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool? kodOk = null;
            try { p.WaitForExit(); kodOk = p.ExitCode == 0; } catch { }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    zanyat = false;
                    var f = ProchitatHod(h.Put);
                    if (f != null) h.Fayl = f;
                    // успех: что сказал сторож в файле хода; нет хода — код выхода скрипта (не узнали — считаем, что дошёл)
                    var otvet = h.KonecVFayle ? new Dictionary<string, object> { { "ok", Pult.B(h.Fayl, "ok") } }
                              : new Dictionary<string, object> { { "ok", kodOk != false }, { "info", kodOk == false ? "скрипт закончился с ошибкой — подробности в файле " + log + " в папке storozh-seti" : "" } };
                    ZakonchitHod(h, otvet);
                    ObnovitZhurnalSkripta(); zapData = null; Obnovit();
                }));
            }
            catch { }
        });
    }

    void ObnovitZhurnalSkripta()
    {
        if (skriptLog == null || skriptPole == null || skriptPole.IsDisposed) return;
        try
        {
            using (var f = new FileStream(skriptLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                f.Seek(Math.Min(skriptLogStart, f.Length), SeekOrigin.Begin);
                using (var r = new StreamReader(f, Encoding.UTF8, true))
                {
                    var t = r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
                    skriptPole.Text = t == "" ? "Запускается…" : t;
                    skriptPole.SelectionStart = skriptPole.TextLength; skriptPole.ScrollToCaret();
                }
            }
        }
        catch { if (skriptPole.Text == "") skriptPole.Text = "Запускается…"; }
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
            new[] { "avto_server", "Выбирать самый быстрый сервер", "ночью замеряет все серверы и ставит самый быстрый из надёжных" },
            new[] { "avto_sayty", "Пускать через VPN сайты, которые не открываются", "проверяет раз в час, Claude и YouTube не трогает" },
            new[] { "avto_spiski", "Обновлять списки заблокированных сайтов", "раз в сутки: по ним сторож решает, что пускать через VPN" } }));
        var ne = GruppaNastroek(W, "Не мешать", nas, new[] { new[] { "ne_v_igre", "Не перезапускать Karing во время игр", "правки ждут, пока игра закроется" } });
        int yz = ne.Height - 4;
        bool ryadom = W >= 780;
        int tw = ryadom ? W - 336 - 16 - 12 : W - 32;
        ne.Controls.Add(Np1("Во время звонка в Discord правки ждут не дольше", 16, yz, tw, false));
        ne.Controls.Add(Np1("потом звонок прервётся секунд на 10", 16, yz + 22, tw));
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
        // список «Настройка» на Мониторе: вернуть, если скрыли (только у сторожа, который его присылает)
        if (Pult.G(sost, "nastroyka") != null)
        {
            var mon = new Kartochka { Width = W, Height = 38 + 58 + 6, Margin = new Padding(0, 0, 0, 12) };
            mon.Controls.Add(Np1("Монитор", 16, 12, W - 32));
            mon.Controls.Add(Np1("Показывать список «Настройка»", 16, 42, W - 16 - 76, false));
            mon.Controls.Add(Np1("что осталось сделать, чтобы сторож работал сам; пропадает, когда всё готово", 16, 64, W - 16 - 76));
            var tm = new Tumbler { Location = new Point(W - 60, 50), Vkl = !Pult.B(sost, "nastroyka_skryta") };
            tm.Smena += v => { sost["nastroyka_skryta"] = !v; Sohranit("nastroyka_skryta", !v); };
            mon.Controls.Add(tm);
            k.Controls.Add(mon);
        }
        string claude = Pult.Chisto(Pult.S(sost, "claude"));
        if (claude != "" && claude != "как всё остальное") k.Controls.Add(Tx("Claude всегда идёт через сервер «" + claude + "».", true));
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

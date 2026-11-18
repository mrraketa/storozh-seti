// Разделы окна «Сторож сети» в виде «Живой монитор» (концепт 3, его выбор 18.11).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

partial class Okno
{
    // ---------- общее ----------
    int Shir { get { return Math.Max(600, soderzh.ClientSize.Width - soderzh.Padding.Horizontal - 34); } }
    static List<List<object>> Spiski(Dictionary<string, object> d, string k) { return Pult.L(d, k).Select(o => ((IEnumerable)o).Cast<object>().ToList()).ToList(); }
    static string Vremya(int sek) { return sek >= 3600 ? (sek / 3600) + " ч " + (sek % 3600 / 60) + " мин" : sek >= 60 ? (sek / 60) + " мин " + (sek % 60) + " с" : sek + " с"; }
    Nadpis Zg(string t, int x, int y, float r = 11f) { return new Nadpis { Text = t, Font = new Font("Segoe UI Semibold", r), Location = new Point(x, y) }; }
    Nadpis Np(string t, int x, int y, bool vt = false, int maxW = 0) { var n = new Nadpis { Text = t, Location = new Point(x, y), Tag = vt ? "vt" : null }; if (maxW > 0) n.MaximumSize = new Size(maxW, 0); return n; }
    Nadpis Zk(string z, int x, int y, float r = 12f, object cvet = null) { return new Nadpis { Text = z, Font = Zn.Shrift(r), Location = new Point(x, y), Tag = cvet ?? "vt" }; }

    Panel Shapka(string zag, string pod, Knopa knopka)
    {
        var p = new Panel { Width = Shir, Height = 44, Margin = new Padding(0, 0, 0, 8) };
        p.Controls.Add(new Label { Text = zag, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Location = new Point(0, 4) });
        if (pod != "") p.Controls.Add(new Label { Text = pod, AutoSize = true, Location = new Point(4 + TextRenderer.MeasureText(zag, new Font("Segoe UI Semibold", 15f)).Width, 14), Tag = "vt" });
        if (knopka != null) { knopka.Location = new Point(Shir - knopka.Width, 2); knopka.Margin = Padding.Empty; p.Controls.Add(knopka); }
        return p;
    }

    Knopa KnZ(string t, string zn, Action d, bool glavnaya = false, int w = 200) { var k = Kn(t, d, glavnaya, w); k.Znachok = zn; return k; }

    bool Sprosit(string v) { if (suho) return true; return MessageBox.Show(this, v, "Сторож сети", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

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
        if (setVniz == null || setVniz.IsDisposed || setData == null || !Pult.B(setData, "ok")) return;
        setVniz.Text = Convert.ToDouble(setData["vniz"]).ToString("0.0");
        setVverh.Text = Convert.ToDouble(setData["vverh"]).ToString("0.0");
        setVpn.Text = Convert.ToDouble(setData["vpn"]).ToString("0.0");
    }

    void PokazatSkorosti()
    {
        var sk = Pult.D(skorData != null && skorData.ContainsKey("skorosti") ? skorData["skorosti"] : null);
        double max = Math.Max(5, sk.Values.Select(v => Convert.ToDouble(Pult.D(v)["mbit"])).DefaultIfEmpty(0).Max());
        foreach (var kv in zhivye)
        {
            object v; double mb = 0; string kuda = null;
            if (sk.TryGetValue(kv.Key, out v)) { mb = Convert.ToDouble(Pult.D(v)["mbit"]); kuda = Pult.S(Pult.D(v), "kuda"); }
            kv.Value.Item1.Text = mb >= 0.05 ? mb.ToString("0.0") + " Мбит/с" : "—";
            kv.Value.Item2.Dolya = mb / max; kv.Value.Item2.Invalidate();
            if (kuda != null && kv.Value.Item3 != null) kv.Value.Item3.Text = Pult.Chisto(kuda);
        }
    }

    static string Znachok(string exe)
    {
        var e = exe.ToLowerInvariant();
        if (e.Contains("zen") || e.Contains("edge") || e.Contains("chrome") || e.Contains("firefox") || e.Contains("browser")) return Zn.Globus;
        if (e.Contains("steam")) return "\uE7FC";       // игра
        if (e.Contains("discord") || e.Contains("telegram")) return "\uE8BD";   // сообщение
        if (e.Contains("claude")) return "\uE945";
        if (e.Contains("python") || e.Contains("curl")) return "\uE756";       // командная строка
        return Zn.Prilozh;
    }

    static string Imya(string exe)
    {
        var n = Path.GetFileNameWithoutExtension(exe);
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
        var bedy = Pult.L(sost, "bedy").Select(Convert.ToString).ToList();
        var st = Pult.D(est && sost.ContainsKey("stat") ? sost["stat"] : null);

        // состояние одной строкой + кнопки
        Color cv = !est ? pal.Liniya : bedy.Count == 0 ? Zel : (Pult.B(sost, "karing") ? Zhelt : Kras);
        var top = new Kartochka { Width = W, Height = 64, Margin = new Padding(0, 0, 0, 12), Ottenok = Ris.Smes(pal.Poverhnost, cv, pal.Temnaya ? 0.16 : 0.10) };
        top.Controls.Add(new Krug { Size = new Size(36, 36), Location = new Point(14, 14), Cvet = cv, Galka = bedy.Count == 0 });
        top.Controls.Add(Zg(!est ? "Смотрю…" : bedy.Count == 0 ? "Всё работает" : "Есть что поправить", 62, 10, 13f));
        if (est) top.Controls.Add(Np("Claude через " + Pult.S(sost, "claude") + "  ·  zapret " + (Pult.B(sost, "zapret") ? "включён" : "НЕ работает") + "  ·  проверено в " + Hm(Pult.S(sost, "posledniy")), 64, 36, true, W - 420));
        var k1 = KnZ("Проверить", Zn.Obnovit, () => Deystvie("проверка сети", new[] { "proverka" }, null), true, 150);
        var k2 = KnZ("Лучший сервер", Zn.Skorost, NaytiLuchshiy, false, 180);
        k1.Location = new Point(W - 346, 13); k2.Location = new Point(W - 192, 13); k1.Margin = k2.Margin = Padding.Empty;
        top.Controls.Add(k1); top.Controls.Add(k2);
        k.Controls.Add(top);

        // что сделать сейчас — только если есть
        foreach (var b in bedy)
        {
            int vys = TextRenderer.MeasureText(b, Font, new Size(W - 280, 1000), TextFormatFlags.WordBreak).Height;
            var r = new Kartochka { Width = W, Height = Math.Max(52, vys + 32), Margin = new Padding(0, 0, 0, 8) };
            r.Controls.Add(Zk(Zn.Preduprezhd, 16, 16, 12f, Zhelt));
            r.Controls.Add(Np(b, 44, 16, false, W - 280));
            Knopa kn = null;
            if (b.StartsWith("zapret")) kn = Kn("Подобрать стратегию", PodobratZapret, true, 210);
            else if (b.StartsWith("Пустить через VPN")) kn = Kn("Поправить сейчас", () => Deystvie("правка Karing", new[] { "proverka" }, null), true, 210);
            else if (b.StartsWith("Сторож не запускается")) kn = Kn("Включить", () => Deystvie("включение сторожа по расписанию", new[] { "ustanovit-zadaniya" }, null), true, 210);
            if (kn != null) { kn.Location = new Point(W - 224, 7); kn.Margin = Padding.Empty; r.Controls.Add(kn); }
            k.Controls.Add(r);
        }
        if (!est) return k;

        // спидометр + сутки
        var ryad = new Panel { Width = W, Height = 220, Margin = new Padding(0, 0, 0, 12) };
        var sp = new Kartochka { Width = 230, Height = 220, Location = new Point(0, 0) };
        double skor = 0; double.TryParse(Pult.S(sost, "skorost"), out skor);
        var vyb = Spiski(st, "vybory");
        double obychno = vyb.Count > 1 ? vyb.Take(vyb.Count - 1).Select(v => Convert.ToDouble(v[2])).DefaultIfEmpty(0).Average() : 0;
        sp.Controls.Add(Np("Скорость VPN", 16, 12, true));
        sp.Controls.Add(new Spidometr { Zn = skor, Max = Math.Max(300, skor), Location = new Point(15, 34), Size = new Size(200, 120) });
        sp.Controls.Add(Np(Pult.S(sost, "server") + " · по замеру", 16, 158, false, 200));
        if (obychno > 0) sp.Controls.Add(Np("обычно у тебя ~" + obychno.ToString("0") + " Мбит/с", 16, 182, true, 200));
        ryad.Controls.Add(sp);

        var su = new Kartochka { Width = W - 242, Height = 220, Location = new Point(242, 0) };
        su.Controls.Add(Zg("Последние сутки", 16, 12));
        su.Controls.Add(Np("цвет — как работал VPN в этот час, наведите — подробности", 150, 15, true));
        var ps = new PolosaSutok { Location = new Point(16, 46), Size = new Size(W - 274, 54) };
        var chasy = Spiski(st, "chasy");
        for (int h = 0; h < 24 && h < chasy.Count; h++) { ps.Obryvy[h] = Convert.ToInt32(chasy[h][0]); ps.Sekund[h] = Convert.ToInt32(chasy[h][1]); }
        su.Controls.Add(ps);
        int lx = 16;
        foreach (var l in new[] { Tuple.Create(PolosaSutok.Zel, "без обрывов"), Tuple.Create(PolosaSutok.Zhel, "1–2 обрыва"), Tuple.Create(PolosaSutok.Kras, "3 и больше или дольше минуты") })
        {
            su.Controls.Add(new Nadpis { Text = "●", ForeColor = l.Item1, Tag = l.Item1, Location = new Point(lx, 108) });
            var t = Np(l.Item2, lx + 14, 108, true); su.Controls.Add(t);
            lx += 22 + TextRenderer.MeasureText(l.Item2, Font).Width + 14;
        }
        int x = 16;
        foreach (var c in new[] { Tuple.Create("обрывов", Pult.S(sost, "miganiya")), Tuple.Create("без VPN всего", Vremya(Convert.ToInt32(Pult.S(st, "bez_vpn") == "" ? "0" : Pult.S(st, "bez_vpn")))),
                                  Tuple.Create("самый длинный", Vremya(Convert.ToInt32(Pult.S(st, "samyy_dlinnyy") == "" ? "0" : Pult.S(st, "samyy_dlinnyy")))) })
        {
            su.Controls.Add(Np(c.Item1, x, 142, true));
            su.Controls.Add(new Nadpis { Text = c.Item2, Font = new Font("Segoe UI Semibold", 15f), Location = new Point(x - 2, 162) });
            x += 190;
        }
        ryad.Controls.Add(su);
        k.Controls.Add(ryad);

        // сеть сейчас + тест скорости
        var set = new Kartochka { Width = W, Height = testData == null ? 120 : 236, Margin = new Padding(0, 0, 0, 12) };
        set.Controls.Add(Zg("Сеть сейчас", 16, 12));
        set.Controls.Add(Np("весь компьютер, обновляется каждые 5 с", 130, 15, true));
        int sx = 16;
        foreach (var c in new[] { Tuple.Create("\uE896", "Скачивание", "Мбит/с"), Tuple.Create("\uE898", "Отдача", "Мбит/с"), Tuple.Create(Zn.Shit, "Из них через VPN", "Мбит/с") })
        {
            set.Controls.Add(Zk(c.Item1, sx, 46, 11f));
            set.Controls.Add(Np(c.Item2, sx + 22, 44, true));
            var ch = new Nadpis { Text = "…", Font = new Font("Segoe UI Semibold", 17f), Location = new Point(sx - 2, 66) };
            set.Controls.Add(ch);
            set.Controls.Add(Np(c.Item3, sx + 86, 78, true));
            if (c.Item2 == "Скачивание") setVniz = ch; else if (c.Item2 == "Отдача") setVverh = ch; else setVpn = ch;
            sx += 210;
        }
        var kt = KnZ(testIdet ? "Идёт тест…" : "Тест скорости", Zn.Skorost, () =>
        {
            if (testIdet || zanyat) return;
            if (suho) { Zapis("[тест] тест скорости"); return; }
            testIdet = true; zanyatoStroka.Text = "Идёт тест скорости: 25 МБ через VPN и 25 МБ напрямую, около 30 секунд…";
            Zapis("тест скорости");
            Perestroit();
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("test-seti"); try { BeginInvoke((Action)(() => { testIdet = false; testData = d; zanyatoStroka.Text = "Готово: тест скорости"; Zapis("Готово: тест скорости"); Perestroit(); })); } catch { testIdet = false; } });
        }, true, 200);
        kt.Location = new Point(W - 216, 52); kt.Margin = Padding.Empty; set.Controls.Add(kt);
        if (testData != null && Pult.B(testData, "ok"))
        {
            var v = Pult.D(testData["vpn"]); var pr = Pult.D(testData["pryamo"]);
            double mv = Convert.ToDouble(v["mbit"]), mp = Convert.ToDouble(pr["mbit"]), mx = Math.Max(1, Math.Max(mv, mp));
            int y0 = 120;
            set.Controls.Add(Np("Тест скорости", 16, y0, true));
            foreach (var t in new[] { Tuple.Create("Через VPN (" + Pult.Chisto(Pult.S(testData, "server")) + ")", mv, Pult.S(v, "ping")), Tuple.Create("Напрямую", mp, Pult.S(pr, "ping")) })
            {
                y0 += 30;
                set.Controls.Add(new Nadpis { Text = t.Item1, Location = new Point(16, y0) });
                var u = new Uroven { Location = new Point(240, y0 + 7), Size = new Size(W - 520, 9), Dolya = t.Item2 / mx };
                set.Controls.Add(u);
                set.Controls.Add(new Nadpis { Text = t.Item2 > 0 ? t.Item2.ToString("0") + " Мбит/с" + (t.Item3 != "" ? " · отклик " + t.Item3 + " мс" : "") : "не прошло", Location = new Point(W - 270, y0), Tag = t.Item2 >= 50 ? (object)Zel : t.Item2 > 0 ? (object)Zhelt : (object)Kras });
            }
            set.Controls.Add(Np(mv >= 50 && mp >= 50 ? "Обе дороги быстрые — всё в порядке." : mv < 50 && mp >= 50 ? "VPN медленнее 50 Мбит/с — попробуйте «Лучший сервер»." : mp < 50 && mv >= 50 ? "Напрямую медленно — похоже на провайдера или zapret." : "Медленно везде — похоже на провайдера или Wi-Fi.", 16, y0 + 32, true, W - 40));
        }
        k.Controls.Add(set);
        PokazatSet();

        // сейчас в интернете
        k.Controls.Add(KartochkaProgramm(W, false));

        var igra = Pult.L(sost, "igra").Select(Convert.ToString).ToList();
        if (igra.Count > 0) k.Controls.Add(Tx("Сейчас идёт: " + string.Join(", ", igra) + ". Karing перезапускаю только после этого (звонок — не дольше " + Pult.S(Pult.D(sost["nastroyki"]), "zvonok_min") + " мин).", true));
        return k;
    }

    static string Hm(string dt) { return dt.Length >= 5 ? dt.Substring(dt.Length - 5) : dt; }

    void NaytiLuchshiy()
    {
        if (Sprosit("Замерю все VPN-серверы и поставлю самый быстрый с постоянным адресом. Займёт 2–5 минут, интернет пару раз моргнёт. Начать?"))
            Deystvie("замер серверов", new[] { "zamer" }, d => servData = null);
    }

    // строки программ: на главной — без переключателей, в «Программах» — с ними
    // монитор: кто сейчас в интернете и куда идёт
    Kartochka KartochkaProgramm(int W, bool sPereklyuch)
    {
        zhivye.Clear();
        var spisok = Pult.L(progData, "programmy").Select(Pult.D).Where(p => Pult.B(p, "v_seti")).Take(7).ToList();
        var kar = new Kartochka { Width = W, Height = 48 + Math.Max(1, spisok.Count) * 40, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(Zg("Сейчас в интернете", 16, 12));
        kar.Controls.Add(Np("скорость каждой программы, обновляется каждые 5 с", 180, 15, true));
        if (progData == null) { kar.Controls.Add(Np("Смотрю, какие программы в сети…", 16, 48, true)); ZagruzitProgrammy(); return kar; }
        int y = 44;
        foreach (var p in spisok) { StrokaProgrammy(kar, p, W, y, false); y += 40; }
        PokazatSkorosti();
        return kar;
    }

    // одна строка программы: значок, имя, куда идёт, полоса скорости; в «Программах» ещё и выбор
    void StrokaProgrammy(Kartochka kar, Dictionary<string, object> p, int W, int y, bool sVyborom, bool pravilo = false)
    {
        string exe = Pult.S(p, "exe");
        bool cherezKaring = Pult.B(p, "karing");
        int rowH = sVyborom ? 54 : 40;
        kar.Controls.Add(Zk(Znachok(exe), 16, y + (rowH - 22) / 2, 12f));
        kar.Controls.Add(new Nadpis { Text = Imya(exe), Location = new Point(46, sVyborom ? y + 8 : y + 10) });
        string kudaT = Pult.Chisto(Pult.S(p, "seychas"));
        if (kudaT == "мимо Karing" && sVyborom) kudaT = "мимо Karing — напрямую, правило на неё не действует";
        var kuda = Np(kudaT, sVyborom ? 46 : 190, sVyborom ? y + 28 : y + 10, true, sVyborom ? 330 : 230);
        kar.Controls.Add(kuda);
        if (!sVyborom || cherezKaring)
        {
            int ux = sVyborom ? 390 : 430, uw = sVyborom ? W - ux - 470 : W - ux - 130;
            var u = new Uroven { Location = new Point(ux, y + rowH / 2 - 3), Size = new Size(Math.Max(40, uw), 7) };
            kar.Controls.Add(u);
            var mb = Np("—", ux + Math.Max(40, uw) + 8, y + rowH / 2 - 9, true);
            kar.Controls.Add(mb);
            zhivye[exe] = Tuple.Create(mb, u, kuda);
        }
        if (!sVyborom) return;
        bool claude = exe.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) && Pult.S(sost, "claude") != "как всё остальное";
        if (claude) { kar.Controls.Add(new Nadpis { Text = "закреплён: " + Pult.Chisto(Pult.S(sost, "claude")), Location = new Point(W - 330, y + 16), Tag = "vt" }); return; }
        string m = izmeneniya.ContainsKey(exe) ? izmeneniya[exe] : Pult.S(p, "marshrut");
        if (m == "") m = "avto";
        var seg = new Segmenty { Punkty = new[] { "Авто", m != "avto" && m != "vpn" && m != "direct" && m != "block" ? "VPN: " + Pult.Chisto(m) : "VPN", "Напрямую", "Без сети" },
            Location = new Point(W - (pravilo ? 420 : 316), y + 11), Size = new Size(300, 32) };
        seg.Vybran = m == "avto" ? 0 : m == "direct" ? 2 : m == "block" ? 3 : 1;
        string tek = m;
        seg.Smena += i => { izmeneniya[exe] = i == 0 ? "avto" : i == 1 ? (tek != "avto" && tek != "direct" && tek != "block" ? tek : "vpn") : i == 2 ? "direct" : "block"; ZaplanirovatMarshruty(); };
        kar.Controls.Add(seg);
        if (pravilo)
        {
            var ub = Kn("Убрать", () => { izmeneniya[exe] = "ubrat"; ZaplanirovatMarshruty(); Perestroit(); }, false, 96);
            ub.Height = 32; ub.Location = new Point(W - 112, y + 11); ub.Margin = Padding.Empty;
            kar.Controls.Add(ub);
        }
    }

    void ZagruzitProgrammy()
    {
        ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("programmy"); try { BeginInvoke((Action)(() => { progData = d; if (tekushchaya == "programmy" || tekushchaya == "glavnaya") Perestroit(); ObnovitSkorosti(); })); } catch { } });
    }

    // ---------- Программы ----------
    Dictionary<string, object> progData;
    readonly Dictionary<string, string> izmeneniya = new Dictionary<string, string>();
    System.Windows.Forms.Timer marshrutTaymer;

    Panel StrProgrammy()
    {
        var k = Kolonka(); int W = Shir;
        var dob = KnZ("Добавить по имени", "\uE710", DobavitProgrammu, false, 200);
        k.Controls.Add(Shapka("Программы", "", dob));
        if (progData == null)
        {
            k.Controls.Add(Tx("Смотрю, какие программы в сети…", true));
            ZagruzitProgrammy();
            return k;
        }
        var vse = Pult.L(progData, "programmy").Select(Pult.D).ToList();
        var pravila = vse.Where(p => (Pult.B(p, "pravilo") || izmeneniya.ContainsKey(Pult.S(p, "exe"))) && !(izmeneniya.ContainsKey(Pult.S(p, "exe")) && izmeneniya[Pult.S(p, "exe")] == "ubrat")).ToList();
        var ostalnye = vse.Where(p => !pravila.Contains(p) && Pult.B(p, "v_seti")).ToList();

        var kp = new Kartochka { Width = W, Height = 76 + Math.Max(1, pravila.Count) * 54, Margin = new Padding(0, 0, 0, 12) };
        kp.Controls.Add(Zg("Ваши правила", 16, 12));
        kp.Controls.Add(Np("эти программы идут так, как вы выбрали. «Убрать» — снова решает сторож.", 128, 15, true));
        int y = 44;
        if (pravila.Count == 0) { kp.Controls.Add(Np("Правил пока нет. Выберите способ у программы ниже — она переедет сюда.", 16, 48, true)); }
        foreach (var p in pravila) { StrokaProgrammy(kp, p, W, y, true, true); y += 54; }
        k.Controls.Add(kp);

        var ko = new Kartochka { Width = W, Height = 60 + Math.Max(1, ostalnye.Count) * 54, Margin = new Padding(0, 0, 0, 12) };
        ko.Controls.Add(Zg("Сейчас в сети", 16, 12));
        ko.Controls.Add(Np("все программы с соединениями, скорость каждые 5 с", 130, 15, true));
        y = 44;
        foreach (var p in ostalnye) { StrokaProgrammy(ko, p, W, y, Pult.B(p, "karing")); y += 54; }
        if (ostalnye.Count == 0) ko.Controls.Add(Np("Других программ в сети сейчас нет.", 16, 48, true));
        k.Controls.Add(ko);
        k.Controls.Add(Tx("Авто — заблокированное в России идёт через VPN, остальное напрямую. «Без сети» — программа не выйдет в интернет. «Мимо Karing» — программа (часто игры и Steam-загрузки) не пользуется прокси, и правило на неё не подействует. Изменение применяется само через пару секунд, интернет моргнёт один раз.", true));
        PokazatSkorosti();
        return k;
    }

    void DobavitProgrammu()
    {
        string exe = Vvod("Программы нет в списке, потому что она сейчас не в сети. Впишите её имя — как в Диспетчере задач на вкладке «Подробности», например RobloxPlayerBeta.exe:");
        if (string.IsNullOrWhiteSpace(exe)) return;
        exe = exe.Trim(); if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
        izmeneniya[exe] = "vpn";
        var sp = Pult.L(progData, "programmy").ToList();
        if (!sp.Any(o => Pult.S(Pult.D(o), "exe").Equals(exe, StringComparison.OrdinalIgnoreCase)))
        { sp.Add(new Dictionary<string, object> { { "exe", exe }, { "seychas", "сейчас не в сети" }, { "marshrut", "vpn" }, { "pravilo", true }, { "karing", true } }); if (progData != null) progData["programmy"] = sp; }
        ZaplanirovatMarshruty();
        Perestroit();
    }

    string Vvod(string vopros)
    {
        if (suho) { Zapis("[тест] окно ввода"); return null; }
        using (var f = new Form { Text = "Сторож сети", Width = 520, Height = 180, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = pal.Fon, ForeColor = pal.Tekst, Font = Font })
        {
            var l = new Label { Text = vopros, Location = new Point(14, 12), Size = new Size(480, 40) };
            var t = new TextBox { Location = new Point(14, 56), Width = 476, BackColor = pal.Poverhnost, ForeColor = pal.Tekst };
            var ok = new Button { Text = "Добавить", DialogResult = DialogResult.OK, Location = new Point(390, 90), Width = 100, FlatStyle = FlatStyle.Flat };
            f.Controls.AddRange(new Control[] { l, t, ok }); f.AcceptButton = ok;
            return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
        }
    }

    void ZaplanirovatMarshruty()
    {
        if (marshrutTaymer == null)
        {
            marshrutTaymer = new System.Windows.Forms.Timer { Interval = 2500 };
            marshrutTaymer.Tick += (s, e) =>
            {
                if (zanyat) return;   // дождаться конца текущего действия
                marshrutTaymer.Stop();
                var nas = Pult.D(Pult.D(sost)["nastroyki"]);
                var mar = new Dictionary<string, object>(Pult.D(nas.ContainsKey("marshruty") ? nas["marshruty"] : null));
                foreach (var kv in izmeneniya) { if (kv.Value == "avto" || kv.Value == "ubrat") mar.Remove(kv.Key); else mar[kv.Key] = kv.Value; }
                var opis = string.Join(", ", izmeneniya.Select(kv => Imya(kv.Key) + " → " + (kv.Value == "avto" ? "авто" : kv.Value == "ubrat" ? "правило убрано" : kv.Value == "vpn" ? "VPN" : kv.Value == "direct" ? "напрямую" : kv.Value == "block" ? "запрет" : Pult.Chisto(kv.Value))));
                Deystvie("маршруты: " + opis, new[] { "nastroyki-zadat", Pult.Json(new Dictionary<string, object> { { "marshruty", mar } }) }, d =>
                {
                    if (!Pult.B(d, "ok")) return;
                    izmeneniya.Clear();
                    Deystvie("перезапуск Karing с маршрутами", new[] { "primenit-marshruty" }, d2 => { progData = null; if (tekushchaya == "programmy") Perestroit(); });
                });
            };
        }
        marshrutTaymer.Stop(); marshrutTaymer.Start();
    }

    // ---------- Сайты ----------
    Dictionary<string, object> saytProverka, saytyIst;
    string saytPole = "";

    Panel StrSayty()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Сайты", "", null));
        var obyas = new Kartochka { Width = W, Height = 78, Margin = new Padding(0, 0, 0, 12) };
        obyas.Controls.Add(Zk(Zn.Globus, 16, 16, 13f));
        obyas.Controls.Add(new Nadpis { Text = "Как идут сайты", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(46, 12) });
        obyas.Controls.Add(Np("Заблокированные в России — через VPN (списки обновляются сами), остальные — напрямую. Сайт не открывается или тормозит? Впишите его ниже: сторож проверит обе дороги и подскажет.", 46, 34, true, W - 70));
        k.Controls.Add(obyas);
        var kar = new Kartochka { Width = W, Height = saytProverka == null ? 96 : 246, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(new Nadpis { Text = "Проверить сайт", Font = new Font("Segoe UI Semibold", 11f), Location = new Point(16, 12) });
        var pole = new TextBox { Location = new Point(16, 44), Width = W - 200, Text = saytPole, Font = new Font("Segoe UI", 11f), BorderStyle = BorderStyle.FixedSingle };
        pole.TextChanged += (s, e) => saytPole = pole.Text;
        var kn = KnZ("Проверить", "\uE721", () =>
        {
            if (saytPole.Trim() == "") return;
            zanyatoStroka.Text = "Проверяю " + saytPole.Trim() + "…";
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("proverit-sayt", saytPole.Trim()); try { BeginInvoke((Action)(() => { saytProverka = d; zanyatoStroka.Text = ""; Zapis("проверка сайта " + saytPole.Trim()); Perestroit(); })); } catch { } });
        }, true, 160);
        kn.Location = new Point(W - 176, 41); kn.Margin = Padding.Empty;
        pole.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; kn.PerformClick(); } };
        kar.Controls.Add(pole); kar.Controls.Add(kn);
        if (saytProverka != null)
        {
            var pr = Spiski(new Dictionary<string, object> { { "a", new[] { saytProverka["napryamuyu"], saytProverka["vpn"] } } }, "a");
            for (int i = 0; i < 2; i++)
            {
                bool ok = Convert.ToString(pr[i][2]) == "быстро";
                bool plokho = Convert.ToString(pr[i][2]) == "не открывается" || Convert.ToString(pr[i][2]) == "очень медленно";
                Color c = ok ? Zel : plokho ? Kras : Zhelt;
                var blok = new Kartochka { Width = (W - 44) / 2, Height = 84, Location = new Point(16 + i * ((W - 44) / 2 + 12), 88), Ottenok = Ris.Smes(pal.Poverhnost, c, pal.Temnaya ? 0.2 : 0.12) };
                blok.Controls.Add(Np(i == 0 ? "Напрямую" : "Через VPN (" + Pult.Chisto(Pult.S(saytProverka, "server")) + ")", 12, 10, true));
                double t = Convert.ToDouble(pr[i][1]);
                blok.Controls.Add(new Nadpis { Text = Convert.ToString(pr[i][2]) == "не открывается" ? "нет" : t.ToString("0.0") + " с", Font = new Font("Segoe UI Semibold", 16f), Location = new Point(10, 28), ForeColor = c, Tag = c });
                blok.Controls.Add(Np(Convert.ToString(pr[i][2]), 12, 58, true));
                kar.Controls.Add(blok);
            }
            string dey = Pult.S(saytProverka, "deystvie"), dom = Pult.S(saytProverka, "domen");
            string seych = Pult.B(saytProverka, "uzhe_vpn") ? "через VPN" : Pult.B(saytProverka, "uzhe_napr") ? "напрямую" : "по спискам (сам выберет)";
            kar.Controls.Add(Np("Сейчас идёт: " + seych + ".  Совет: " + Pult.S(saytProverka, "sovet") + ".", 16, 196, false, W - 400));
            if (dey != "")
            {
                var kd = Kn(dey == "vpn" ? "Пустить через VPN" : "Пустить напрямую", () => Deystvie("сайт " + dom + (dey == "vpn" ? " в VPN" : " напрямую"), new[] { "sayt", dom, dey }, d => { saytyIst = null; saytProverka = null; }), true, 200);
                kd.Location = new Point(W - 336, 190); kd.Margin = Padding.Empty; kar.Controls.Add(kd);
                var ko = Kn("Оставить", () => { saytProverka = null; Perestroit(); }, false, 110);
                ko.Location = new Point(W - 126, 190); ko.Margin = Padding.Empty; kar.Controls.Add(ko);
            }
        }
        k.Controls.Add(kar);

        // история
        if (saytyIst == null)
        {
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("sayty-istoriya"); try { BeginInvoke((Action)(() => { saytyIst = d; if (tekushchaya == "sayty") Perestroit(); })); } catch { } });
            return k;
        }
        var ist = Pult.L(saytyIst, "sayty").Select(Pult.D).Take(12).ToList();
        var ki = new Kartochka { Width = W, Height = 48 + Math.Max(1, ist.Count) * 42, Margin = new Padding(0, 0, 0, 12) };
        ki.Controls.Add(Zg("Что сторож поменял сам", 16, 12));
        ki.Controls.Add(Np("«Отменить» — сайт снова пойдёт по спискам, как до правки", 200, 15, true));
        int y = 44;
        foreach (var s in ist)
        {
            bool vpn = Pult.S(s, "vid") == "vpn";
            var pill = new Kartochka { Location = new Point(16, y + 7), Size = new Size(86, 24), Ottenok = Ris.Smes(pal.Poverhnost, vpn ? Zel : Zhelt, 0.25) };
            pill.Controls.Add(new Nadpis { Text = vpn ? "в VPN" : "напрямую", Location = new Point(10, 3) });
            ki.Controls.Add(pill);
            ki.Controls.Add(new Nadpis { Text = Pult.S(s, "domen"), Location = new Point(114, y + 10) });
            ki.Controls.Add(Np((vpn ? "пущен через VPN — напрямую не открывался" : "пущен напрямую — через VPN отказывал") + " · " + Pult.S(s, "kogda"), 300, y + 10, true, W - 430));
            string dom = Pult.S(s, "domen");
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

    Panel StrServery()
    {
        var k = Kolonka(); int W = Shir;
        if (servData == null)
        {
            k.Controls.Add(Shapka("Серверы", "", null));
            k.Controls.Add(Tx("Загружаю…", true));
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("servery"); try { BeginInvoke((Action)(() => { servData = d; if (tekushchaya == "servery") Perestroit(); })); } catch { } });
            return k;
        }
        if (!servData.ContainsKey("zamer")) { k.Controls.Add(Shapka("Серверы", "", null)); k.Controls.Add(Tx("Не смог получить серверы: " + Pult.S(servData, "info"), true)); servData = null; return k; }
        var zamer = Pult.D(servData["zamer"]);
        k.Controls.Add(Shapka("Серверы", "замер " + Pult.S(zamer, "when"), KnZ("Замерить и выбрать лучший", Zn.Skorost, NaytiLuchshiy, true, 270)));
        string seychas = Pult.S(servData, "seychas"), claude = Pult.S(servData, "claude");
        var nad = Pult.D(Pult.D(sost != null && sost.ContainsKey("stat") ? sost["stat"] : null).ContainsKey("nadezhnost") ? Pult.D(sost["stat"])["nadezhnost"] : null);
        var setka = new FlowLayoutPanel { Width = W + 12, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        int cw = (W - 24) / 3;
        foreach (var r in Spiski(zamer, "results"))
        {
            string imya = Convert.ToString(r[0]);
            int sk = Convert.ToInt32(r[1]), ping = Convert.ToInt32(r[2]); bool menyaet = Convert.ToInt32(r[3]) > 1;
            var nd = nad.ContainsKey(imya) ? ((IEnumerable)nad[imya]).Cast<object>().Select(Convert.ToInt32).ToList() : null;
            bool tek = imya == seychas, kl = imya == claude, mertv = sk < 5;
            var pod = Pult.D(Pult.D(servData.ContainsKey("podrobno") ? servData["podrobno"] : null).ContainsKey(imya) ? Pult.D(servData["podrobno"])[imya] : null);
            string vid = Pult.S(pod, "vid");
            Color vc = vid == "plohoy" ? Kras : vid == "nado_znat" ? Zhelt : Zel;
            var prich = Pult.L(pod, "prichiny").Select(Convert.ToString).ToList();
            var kar = new Kartochka { Width = cw, Height = 186, Margin = new Padding(0, 0, 12, 12), Ottenok = tek ? Ris.Smes(pal.Poverhnost, pal.Akcent, 0.18) : (Color?)null };
            kar.Controls.Add(Zg(Pult.Chisto(imya), 14, 12));
            string metka = tek ? "сейчас" : kl ? "Claude" : "";
            if (metka != "") kar.Controls.Add(new Nadpis { Text = metka, Location = new Point(cw - 14 - TextRenderer.MeasureText(metka, Font).Width, 14), Tag = "vt" });
            if (vid != "")
            {
                var pl = new Kartochka { Location = new Point(14, 118), Size = new Size(TextRenderer.MeasureText(Pult.S(pod, "slovo"), Font).Width + 22, 24), Ottenok = Ris.Smes(pal.Poverhnost, vc, 0.3) };
                pl.Controls.Add(new Nadpis { Text = Pult.S(pod, "slovo"), Location = new Point(10, 3) });
                kar.Controls.Add(pl);
                var pr = Np(string.Join("; ", prich), 14, 146, true, cw - 28); pr.AutoSize = false; pr.Size = new Size(cw - 28, 36); pr.AutoEllipsis = true;
                kar.Controls.Add(pr);
                new ToolTip().SetToolTip(pr, string.Join(Environment.NewLine, prich));
            }
            kar.Controls.Add(new Nadpis { Text = sk.ToString(), Font = new Font("Segoe UI Semibold", 20f), Location = new Point(12, 36) });
            kar.Controls.Add(Np("Мбит/с", 18 + TextRenderer.MeasureText(sk.ToString(), new Font("Segoe UI Semibold", 20f)).Width, 52, true));
            kar.Controls.Add(Np("пинг " + ping + (nd != null ? " · обрывов за сутки " + nd[0] : ""), 14, 78, true));
            if (nd != null)
            {
                // полоса: доля часов без обрывов / с обрывами
                int vsego = 24, bad = Math.Min(24, nd[0]); var u = new Uroven { Location = new Point(14, 104), Size = new Size(cw - 28, 8), Dolya = 1 - bad / (double)vsego };
                kar.Controls.Add(u);
            }
            if (!tek && !mertv)
            {
                var kp = Kn("Поставить", () => { if (Sprosit("Пустить сайты и программы через " + Pult.Chisto(imya) + "? Karing перезапустится, интернет моргнёт.")) Deystvie("переключение на " + Pult.Chisto(imya), new[] { "server", imya }, d => servData = null); }, false, 120);
                kp.Height = 28; kp.Location = new Point(cw - 134, 116); kp.Margin = Padding.Empty; kar.Controls.Add(kp);
            }
            setka.Controls.Add(kar);
        }
        k.Controls.Add(setka);
        k.Controls.Add(Tx("Полоса под цифрами — сколько часов за сутки сервер проработал без обрывов. «Адрес меняется» — сайты каждый раз видят новый адрес и могут выкидывать из аккаунтов, сторож такие сам не выбирает. Claude всегда идёт через " + Pult.Chisto(claude) + ", это закреплено.", true));
        return k;
    }

    // ---------- zapret ----------
    Dictionary<string, object> zapData;
    string hodFayl; long hodStart; TextBox hodPole;

    Panel StrZapret()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("zapret", "открывает YouTube и голос Discord без VPN", null));
        bool rab = Pult.B(sost, "zapret");
        var top = new Kartochka { Width = W, Height = 64, Margin = new Padding(0, 0, 0, 12) };
        top.Controls.Add(new Krug { Size = new Size(34, 34), Location = new Point(14, 15), Cvet = rab ? Zel : Kras, Galka = rab });
        top.Controls.Add(Zg(rab ? "Работает · стратегия " + Pult.S(sost, "strategiya") : "Не работает", 60, 10, 12f));
        top.Controls.Add(Np(zapData == null ? "проверяю…" : "версия " + Pult.S(zapData, "versiya"), 62, 36, true));
        var kp = KnZ("Проверить", Zn.Obnovit, () => { zapData = null; Perestroit(); }, false, 150);
        kp.Location = new Point(W - 166, 14); kp.Margin = Padding.Empty; top.Controls.Add(kp);
        k.Controls.Add(top);

        if (zapData != null && !zapData.ContainsKey("youtube")) { k.Controls.Add(Tx("Не смог проверить zapret: " + Pult.S(zapData, "info"), true)); zapData = null; }
        else if (zapData == null && suho) { }
        else if (zapData == null)
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("zapret"); try { BeginInvoke((Action)(() => { zapData = d; if (tekushchaya == "zapret") Perestroit(); })); } catch { } });
        else
        {
            var dva = new Panel { Width = W, Height = 124, Margin = new Padding(0, 0, 0, 12) };
            var yt = Spiski(new Dictionary<string, object> { { "a", new[] { zapData["youtube"] } } }, "a")[0];
            bool ytOk = Convert.ToBoolean(yt[2]);
            var a = new Kartochka { Size = new Size((W - 12) / 2, 124), Location = new Point(0, 0), Ottenok = Ris.Smes(pal.Poverhnost, ytOk ? Zel : Kras, pal.Temnaya ? 0.14 : 0.08) };
            a.Controls.Add(Np("YouTube напрямую", 14, 12, true));
            a.Controls.Add(new Nadpis { Text = ytOk ? "открывается · " + Convert.ToDouble(yt[1]).ToString("0.00") + " с" : "не открывается", Font = new Font("Segoe UI Semibold", 14f), Location = new Point(12, 36) });
            a.Controls.Add(Np(ytOk ? "zapret справляется" : "нужно подобрать стратегию", 14, 70, true));
            dva.Controls.Add(a);
            var g = Pult.D(zapData["golos"]);
            var b = new Kartochka { Size = new Size((W - 12) / 2, 124), Location = new Point((W - 12) / 2 + 12, 0) };
            b.Controls.Add(Np("Голос Discord за 2 минуты", 14, 12, true));
            if (Pult.B(g, "est"))
            {
                double pot = Convert.ToDouble(g["poteri"]);
                Color c = pot < 2 ? Zel : pot < 8 ? Zhelt : Kras;
                b.Controls.Add(new Nadpis { Text = "потерь " + pot.ToString("0.#") + " % · пинг " + Pult.S(g, "ping") + " мс", Font = new Font("Segoe UI Semibold", 14f), Location = new Point(12, 36), ForeColor = c, Tag = c });
                b.Controls.Add(Np(pot < 2 ? "голос идёт чисто" : pot < 8 ? "возможны редкие заикания" : "голос будет прерываться — попробуйте подобрать стратегию", 14, 70, true, (W - 12) / 2 - 28));
            }
            else
            {
                b.Controls.Add(new Nadpis { Text = "вы не в звонке", Font = new Font("Segoe UI Semibold", 14f), Location = new Point(12, 36) });
                b.Controls.Add(Np("зайдите в голосовой канал и нажмите «Проверить»", 14, 70, true));
            }
            dva.Controls.Add(b);
            k.Controls.Add(dva);
        }

        var dey = new Kartochka { Width = W, Height = hodFayl != null ? 290 : 110, Margin = new Padding(0, 0, 0, 12) };
        var b1 = KnZ("Подобрать лучшую стратегию", Zn.Molniya, PodobratZapret, true, 290);
        var b2 = KnZ("Быстрее YouTube", "\uE714", () => ZapustitSkript("zapret: быстрее YouTube", "zapret-youtube.ps1", "zapret-youtube.log", "Идёт несколько минут. YouTube и сайты без VPN будут прерываться. Начать?"), false, 200);
        var b3 = KnZ("Обновить zapret", "\uE896", ObnovitZapret, false, 200);
        b1.Location = new Point(14, 14); b2.Location = new Point(316, 14); b3.Location = new Point(528, 14);
        foreach (var bb in new[] { b1, b2, b3 }) { bb.Margin = Padding.Empty; dey.Controls.Add(bb); }
        dey.Controls.Add(Np("Подбор идёт около 10 минут, всё это время интернет без VPN рвётся. Ход — здесь же, строками.", 16, 64, true, W - 32));
        if (hodFayl != null)
        {
            hodPole = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Location = new Point(16, 92), Size = new Size(W - 32, 184), Font = new Font("Consolas", 9.5f) };
            dey.Controls.Add(hodPole);
            ObnovitHod();
        }
        k.Controls.Add(dey);
        return k;
    }

    void PodobratZapret() { ZapustitSkript("подбор стратегии zapret", "zapret-avto.ps1", "zapret-avto.log", "Подбор идёт около 10 минут. Всё это время интернет без VPN будет рвать. Начать?"); }

    void ObnovitZapret()
    {
        ZapustitSkript("обновление zapret", "zapret-obnovit.ps1", "zapret-obnovit.log",
            "Проверю новую версию zapret на GitHub (Flowseal/zapret-discord-youtube). Если она есть — скачаю архив (~4 МБ) в «Загрузки», перенесу ваши списки и поставлю ту же стратегию. Старая версия останется на месте. Начать?");
    }

    void ZapustitSkript(string chto, string fayl, string log, string vopros)
    {
        if (suho) { Zapis("[тест] скрипт: " + fayl); return; }
        if (zanyat || !Sprosit(vopros)) return;
        var psi = new ProcessStartInfo(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(Pult.Papka, fayl) + "\"") { WorkingDirectory = Pult.Papka };
        if (Admin()) { psi.UseShellExecute = false; psi.CreateNoWindow = true; }
        else { psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden; }
        hodFayl = Path.Combine(Pult.Papka, log);
        try { hodStart = File.Exists(hodFayl) ? new FileInfo(hodFayl).Length : 0; } catch { hodStart = 0; }
        Process p;
        try { p = Process.Start(psi); }
        catch { zanyatoStroka.Text = "Не запустил: права администратора не дали."; hodFayl = null; return; }
        zanyat = true;
        zanyatoStroka.Text = "Идёт: " + chto + "…";
        Zapis("скрипт: " + fayl);
        if (tekushchaya == "zapret") Perestroit();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            p.WaitForExit();
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
        catch { }
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
        var vse = Spiski(lentaData, "lenta");
        var filtry = Ryad(); filtry.Margin = new Padding(0, 0, 0, 10);
        foreach (var f in new[] { new[] { "", "Всё", "" }, new[] { "ispr", "Исправлено", "\uE90F" }, new[] { "obryv", "Обрывы", Zn.Preduprezhd }, new[] { "zamer", "Замеры", Zn.Skorost }, new[] { "moi", "Мои действия", "\uE7C9" } })
        {
            string id = f[0];
            int n = id == "" ? vse.Count : vse.Count(e => Convert.ToString(e[1]) == id);
            var kn = Kn(f[1] + " · " + n, () => { lentaFiltr = id; Perestroit(); }, lentaFiltr == id, 150);
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
            string zn = vid == "ispr" ? "\uE90F" : vid == "obryv" ? Zn.Preduprezhd : vid == "zamer" ? Zn.Skorost : vid == "moi" ? "\uE7C9" : "\uE946";
            kar.Controls.Add(Np(Convert.ToString(e[0]), 16, y + 8, true));
            kar.Controls.Add(Zk(zn, 120, y + 7, 11f, c));
            var t = Np(Pult.Chisto(Convert.ToString(e[2])), 148, y + 8, false, W - 170); t.AutoEllipsis = true; t.AutoSize = false; t.Size = new Size(W - 170, 22);
            kar.Controls.Add(t);
            y += 36;
        }
        if (spis.Count == 0) kar.Controls.Add(Np("Пусто.", 16, 12, true));
        k.Controls.Add(kar);
        return k;
    }

    // ---------- Настройки ----------
    Panel StrNastroyki()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Настройки", "", null));
        var nas = Pult.D(sost != null && sost.ContainsKey("nastroyki") ? sost["nastroyki"] : null);
        k.Controls.Add(GruppaNastroek(W, "Сторож сам", nas, new[] {
            new[] { "avto_server", "Выбирать самый быстрый сервер", "ночью мерит все и ставит лучший с постоянным адресом" },
            new[] { "avto_sayty", "Пускать через VPN сайты, которые не открываются", "проверяет раз в час, Claude и YouTube не трогает" },
            new[] { "avto_spiski", "Обновлять списки блокировок", "раз в сутки, около 83 тысяч адресов" } }));
        var ne = GruppaNastroek(W, "Не мешать", nas, new[] { new[] { "ne_v_igre", "Не перезапускать Karing во время игр", "правки ждут, пока игра закроется" } }, 1);
        ne.Controls.Add(new Nadpis { Text = "Звонок в Discord задерживает правки не дольше", Location = new Point(16, 102) });
        ne.Controls.Add(Np("потом звонок моргнёт секунд на 10", 16, 124, true));
        int tek = 20; int.TryParse(Pult.S(nas, "zvonok_min"), out tek);
        var vars = new[] { 0, 10, 20, 60 };
        var seg = new Segmenty { Punkty = vars.Select(v => v == 0 ? "не ждать" : v + " мин").ToArray(), Location = new Point(W - 336, 104), Size = new Size(320, 32) };
        seg.Vybran = Math.Max(0, Array.IndexOf(vars, tek));
        seg.Smena += i => Sohranit("zvonok_min", vars[i]);
        ne.Controls.Add(seg);
        k.Controls.Add(ne);
        k.Controls.Add(GruppaNastroek(W, "Уведомления", nas, new[] { new[] { "zapret_slezhka", "Окошко, если zapret перестал работать", "и запись в журнал" } }));
        k.Controls.Add(Tx("Claude всегда идёт через Англию — это закреплено и здесь не меняется.", true));
        return k;
    }

    Kartochka GruppaNastroek(int W, string zag, Dictionary<string, object> nas, string[][] punkty, int dop = 0)
    {
        var kar = new Kartochka { Width = W, Height = 44 + punkty.Length * 58 + dop * 50, Margin = new Padding(0, 0, 0, 12) };
        kar.Controls.Add(Np(zag, 16, 12, true));
        int y = 38;
        foreach (var p in punkty)
        {
            string key = p[0];
            kar.Controls.Add(new Nadpis { Text = p[1], Location = new Point(16, y + 4) });
            kar.Controls.Add(Np(p[2], 16, y + 26, true));
            var t = new Tumbler { Location = new Point(W - 60, y + 12), Vkl = Pult.B(nas, key) };
            t.Smena += v => Sohranit(key, v);
            kar.Controls.Add(t);
            y += 58;
        }
        return kar;
    }
}

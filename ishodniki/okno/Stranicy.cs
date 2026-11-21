// Разделы окна «Сторож сети». 21.11 — переделка по разбору DIZAYN-2111.md (его выбор: плитки по областям на Мониторе,
// меню без рамок) и по замечаниям критика (круг 2). Правила для всех экранов:
//  - одна шапка: заголовок, под ним одна строка подписи, справа не больше одной кнопки;
//  - одна синяя (главная) кнопка на экран — только для действия, нужного сейчас; при «всё хорошо» синих нет;
//  - карточка: заголовок, подпись ПОД ним, кнопки — в правом столбце одной ширины, отступы одинаковые (OTS);
//  - пояснения короткие и рядом; длинное — в подсказке у значка «?»; ничего не обрезается многоточием (кроме имён);
//  - цвет — только для состояния (зелёный/жёлтый/красный/серый — ZnakSost), маршруты — нейтральные метки;
//  - шрифты: 15 (шапка), 11 (заголовок карточки), 10 (текст), 9 (мелкие подписи) и 18 для крупных чисел;
//  - одна ширина содержимого на всех экранах (до 1240 px, по центру); скорость — в МБ/с везде;
//  - одно имя — одно действие; выключенный Karing везде одной фразой (KARING_NE).
// Каждый раздел переживает любой ответ пульта: пустой, {"ok": false}, без ключей, с null — показывает, что случилось,
// а не падает (проверка: --samoproverka, наборы «как у друга», «всё сломано», «кривые данные»).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

partial class Okno
{
    // ---------- сетка и шрифты (одно место на всё окно) ----------
    const int OTS = 16, PROM = 12, KN_W = 210, KN_H = 36, MAX_SHIR = 1240, ZHURNAL_SHIR = 860;
    internal static readonly Font F_STR = new Font("Segoe UI Semibold", 15f), F_ZAG = new Font("Segoe UI Semibold", 11f),
        F_TEKST = new Font("Segoe UI", 10f), F_ZHIR = new Font("Segoe UI Semibold", 10f), F_MELK = new Font("Segoe UI", 9f);
    static readonly Dictionary<float, Font> shriftyZn = new Dictionary<float, Font>();
    static Font ZnF(float r) { Font f; if (!shriftyZn.TryGetValue(r, out f)) shriftyZn[r] = f = Zn.Shrift(r); return f; }
    // выключенный Karing — везде одной фразой
    const string KARING_NE = "Karing (VPN) не запущен";

    // ширина содержимого: одна на всех экранах — до 1240 px, колонка по центру (правило 9: справа нет пустой полосы)
    // видимая ширина страницы — без полосы прокрутки: колонка по центру именно видимой части (слева и справа поровну)
    int Vidno { get { return Math.Max(600, soderzh.ClientSize.Width - soderzh.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth); } }
    int Shir { get { return Math.Min(MAX_SHIR, Math.Max(584, Vidno - 16)); } }
    internal int Sdvig { get { return Math.Max(0, (Vidno - Shir) / 2); } }
    static List<object> Spisok(object o) { return o is IEnumerable && !(o is string) && !(o is IDictionary) ? ((IEnumerable)o).Cast<object>().ToList() : new List<object>(); }
    static List<List<object>> Spiski(Dictionary<string, object> d, string k) { return Pult.L(d, k).Where(o => o is IEnumerable && !(o is string) && !(o is IDictionary)).Select(Spisok).ToList(); }
    internal static string Vremya(int sek) { sek = Math.Max(0, sek); return sek >= 3600 ? (sek / 3600) + " ч " + (sek % 3600 / 60) + " мин" : sek >= 60 ? (sek / 60) + " мин " + (sek % 60) + " с" : sek + " с"; }
    // коротко: до минуты — секунды, дальше — минуты («1162 с» → «19 мин»)
    internal static string VremyaKr(int sek) { sek = Math.Max(0, sek); return sek < 60 ? sek + " с" : sek < 3600 ? (int)Math.Round(sek / 60.0) + " мин" : (sek / 3600) + " ч " + (sek % 3600 / 60) + " мин"; }
    static int TextW(string t, Font f) { return TextRenderer.MeasureText(t ?? "", f, Size.Empty, TextFormatFlags.NoPrefix).Width; }
    // высота абзаца — тем же способом, каким её считает сама подпись (иначе строки налезают друг на друга)
    static readonly Label izmeritel = new Label { AutoSize = true, UseMnemonic = false };
    int VysTeksta(string t, int w, Font f = null)
    {
        izmeritel.Font = f ?? F_TEKST; izmeritel.MaximumSize = new Size(Math.Max(20, w), 0); izmeritel.Text = string.IsNullOrEmpty(t) ? " " : t;
        return izmeritel.PreferredSize.Height;
    }
    static string Mnozh(int n, string odin, string dva, string mnogo)
    {
        int a = Math.Abs(n) % 100, b = a % 10;
        return n + " " + (a >= 11 && a <= 14 ? mnogo : b == 1 ? odin : b >= 2 && b <= 4 ? dva : mnogo);
    }

    // ---------- заготовки ----------
    // Абзац: переносится по словам, не обрезается; высота — по тексту (VysTeksta с тем же шрифтом и шириной).
    // tag: "vt" — второстепенный цвет; "ok"/"zhelt"/"kras"/"sery" — цвет состояния (Krasit подставляет под тему)
    Nadpis Abz(string t, int x, int y, int w, string tag = null, Font f = null)
    {
        return new Nadpis { Text = t ?? "", Location = new Point(x, y), Tag = tag, Font = f ?? F_TEKST, MaximumSize = new Size(Math.Max(20, w), 0) };
    }
    // Короткое значение в столбце фиксированной ширины (числа и даты в списках — по одной линии)
    Nadpis Stolb(string t, int x, int y, int w, bool vprava, string tag = null, Font f = null)
    {
        var n = new Nadpis { Text = t ?? "", Location = new Point(x, y), Tag = tag, Font = f ?? F_TEKST, AutoSize = false };
        n.Size = new Size(Math.Max(10, w), (f ?? F_TEKST).Height + 4);
        n.TextAlign = vprava ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
        return n;
    }
    // Имя (программы, сайта, сервера): одна строка, не влезло — многоточие, полностью — в подсказке
    NadpisImya ImyaN(string t, int x, int y, int w, string tag = null, Font f = null, string podskazka = null)
    {
        var n = new NadpisImya { Text = t ?? "", Location = new Point(x, y), Tag = tag, Font = f ?? F_TEKST };
        n.Size = new Size(Math.Max(16, w), (f ?? F_TEKST).Height + 4);
        string p = podskazka ?? t;
        if (!string.IsNullOrEmpty(p)) podskazki.SetToolTip(n, Pult.Chisto(p));
        return n;
    }
    Nadpis Zk(string z, int x, int y, float r = 12f, string tag = "vt") { return new Nadpis { Text = z, Font = ZnF(r), Location = new Point(x, y), Tag = tag }; }
    // значок «?» — только для подсказок: длинное пояснение — в подсказке, а не абзацем на экране (правило 4)
    Nadpis Vopros(string podskazka, int x, int y)
    {
        var n = new Nadpis { Text = Zn.Vopros, Font = ZnF(11f), Location = new Point(x, y), Tag = "vt", Cursor = Cursors.Help, Name = "vopros" };
        podskazki.SetToolTip(n, podskazka);
        return n;
    }
    Knopa KnZ(string t, string zn, Action d, bool glavnaya = false, int w = KN_W) { var k = Kn(t, d, glavnaya, w); k.Znachok = zn; return k; }
    bool Sprosit(string v) { if (suho) return true; return MessageBox.Show(this, v, "Сторож сети", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
    static int ShirKnopki(Knopa k) { return Math.Max(KN_W, TextW(k.Text, F_TEKST) + (string.IsNullOrEmpty(k.Znachok) ? 0 : 26) + 40); }

    // Шапка экрана (правило 1): заголовок, под ним одна строка подписи, справа — не больше одной кнопки.
    // sost — у Монитора: заголовок — фраза общего состояния со значком её цвета; dop — компактная метка левее кнопки
    // (Claude); podskazkaPod — «?» после подписи (правило меток серверов).
    Panel Shapka(string zag, string pod, Knopa knopka, string sost = null, Control dop = null, string podskazkaPod = null)
    {
        int W = Shir;
        var p = new Panel { Width = W, Margin = new Padding(0, 0, 0, 16), Name = "shapka" };
        if (knopka != null) knopka.Width = ShirKnopki(knopka);
        int x = 0;
        if (sost != null) { p.Controls.Add(new ZnakSost { Sost = sost, Size = new Size(30, 30), Location = new Point(0, 3), Name = "shapka-znak" }); x = 42; }
        int pravo = (knopka != null ? knopka.Width + 24 : 0) + (dop != null ? dop.Width + 12 : 0);
        int tw = W - x - pravo;
        p.Controls.Add(Abz(zag, x, 0, tw, sost, F_STR));
        int y = VysTeksta(zag, tw, F_STR);
        if (!string.IsNullOrEmpty(pod))
        {
            int pw = tw - (podskazkaPod != null ? 26 : 0);
            p.Controls.Add(Abz(pod, x, y + 2, pw, "vt"));
            if (podskazkaPod != null) p.Controls.Add(Vopros(podskazkaPod, x + Math.Min(pw, TextW(pod, F_TEKST)) + 2, y + 4));
            y += 2 + VysTeksta(pod, pw);
        }
        int h = Math.Max(y, knopka != null ? KN_H : 0);
        int seredina = Math.Min(h, 64) / 2;
        if (knopka != null) { knopka.Location = new Point(W - knopka.Width, Math.Max(0, seredina - knopka.Height / 2)); knopka.Margin = Padding.Empty; p.Controls.Add(knopka); }
        if (dop != null) { dop.Location = new Point(W - (knopka != null ? knopka.Width + 12 : 0) - dop.Width, Math.Max(0, seredina - dop.Height / 2)); p.Controls.Add(dop); }
        p.Height = h + 2;
        return p;
    }

    // Карточка (правило 3): заголовок, под ним подпись; rezerv — место справа под столбец кнопок; y — где начинать содержимое
    Kartochka Karta(int W, string zag, string pod, out int y, string imya = null, string podskazka = null, int rezerv = 0)
    {
        var k = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = imya ?? "" };
        y = OTS - 2;
        int tw = W - 2 * OTS - rezerv;
        if (!string.IsNullOrEmpty(zag))
        {
            int zw = tw - (podskazka != null ? 26 : 0);
            k.Controls.Add(Abz(zag, OTS, y, zw, null, F_ZAG));
            if (podskazka != null) k.Controls.Add(Vopros(podskazka, OTS + Math.Min(zw, TextW(zag, F_ZAG)) + 4, y + 3));
            y += VysTeksta(zag, zw, F_ZAG);
        }
        if (!string.IsNullOrEmpty(pod)) { k.Controls.Add(Abz(pod, OTS, y + 2, tw, "vt")); y += 2 + VysTeksta(pod, tw); }
        y += 12;
        return k;
    }

    // Строка действия внутри карточки: слева — что это (одна строка) и пояснение под ней, справа — кнопки столбцом одной ширины
    int StrokaDeystviya(Kartochka kar, int W, int y, string zag, string opis, string podskazka, params Knopa[] knopki)
    {
        return StrokaDeystviyaF(kar, W, y, zag, opis, podskazka, F_TEKST, knopki);
    }
    int StrokaDeystviyaF(Kartochka kar, int W, int y, string zag, string opis, string podskazka, Font fZag, params Knopa[] knopki)
    {
        knopki = knopki.Where(x => x != null).ToArray();
        int tw = W - 2 * OTS - (knopki.Length > 0 ? KN_W + 20 : 0);
        int zw = tw - (podskazka != null ? 26 : 0);
        int h = 0;
        if (!string.IsNullOrEmpty(zag))
        {
            kar.Controls.Add(Abz(zag, OTS, y, zw, null, fZag));
            if (podskazka != null) kar.Controls.Add(Vopros(podskazka, OTS + Math.Min(zw, TextW(zag, fZag)) + 4, y + 2));
            h = VysTeksta(zag, zw, fZag);
        }
        if (!string.IsNullOrEmpty(opis)) { kar.Controls.Add(Abz(opis, OTS, y + h + 2, tw, "vt")); h += 2 + VysTeksta(opis, tw); }
        int kh = knopki.Length * KN_H + Math.Max(0, knopki.Length - 1) * 8;
        int ky = y + Math.Max(0, (h - kh) / 2);
        foreach (var kn in knopki)
        {
            kn.Width = KN_W; kn.Height = KN_H; kn.Margin = Padding.Empty;
            kn.Location = new Point(W - OTS - KN_W, ky); kar.Controls.Add(kn);
            ky += KN_H + 8;
        }
        return y + Math.Max(h, kh) + 16;
    }

    // Полоса беды (правило 8): одна вверху экрана, с кнопками в правом столбце, а не повтор в каждой строке
    Kartochka PolosaBedy(int W, string sost, string zag, string opis, params Knopa[] knopki)
    {
        knopki = knopki.Where(kb => kb != null).ToArray();
        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = "polosa-bedy", AccessibleDescription = sost,
                                  Ottenok = sost == "sery" ? (Color?)null : Ris.Smes(pal.Poverhnost, ZnakSost.Cvet(sost), pal.Temnaya ? 0.16 : 0.09) };
        kar.Controls.Add(new ZnakSost { Sost = sost, Location = new Point(OTS, 16), Size = new Size(24, 24) });
        int x = OTS + 36, tw = W - x - OTS - (knopki.Length > 0 ? KN_W + 20 : 0);
        kar.Controls.Add(Abz(zag, x, 14, tw, null, F_ZAG));
        int y = 14 + VysTeksta(zag, tw, F_ZAG);
        if (!string.IsNullOrEmpty(opis)) { kar.Controls.Add(Abz(opis, x, y + 2, tw, "vt")); y += 2 + VysTeksta(opis, tw); }
        int kh = knopki.Length * KN_H + Math.Max(0, knopki.Length - 1) * 8;
        int h = Math.Max(y + 14, knopki.Length > 0 ? kh + 28 : 0);
        int ky = (h - kh) / 2;
        foreach (var kn in knopki) { kn.Width = KN_W; kn.Height = KN_H; kn.Margin = Padding.Empty; kn.Location = new Point(W - OTS - KN_W, ky); kar.Controls.Add(kn); ky += KN_H + 8; }
        kar.Height = h;
        return kar;
    }

    // Пульт ответил ошибкой: что не получилось — словами; сырой текст ошибки — только в подсказке у «?» и в журнале окна
    static bool Syroe(string t) { return Regex.IsMatch(t ?? "", @"WinError|Traceback|Error|errno|urlopen|Exception|\.py\b|<|\[|ModuleNotFound", RegexOptions.IgnoreCase); }
    Kartochka KartaOshibki(int W, string zag, string info, Action povtor)
    {
        bool syroe = Syroe(info);
        if (syroe) Zapis("ошибка пульта (в окне — словами): " + info);
        string t = syroe ? "Сторож ответил ошибкой. Окно спросит его ещё раз само через минуту" : Chel.Tekst(info);
        var kn = povtor != null ? KnZ("Повторить", Zn.Obnovit, povtor) : null;
        var kar = PolosaBedy(W, "kras", zag, t, kn);
        kar.Name = "karta-oshibki";
        if (syroe)
        {
            int tw = W - OTS * 2 - 36 - (kn != null ? KN_W + 20 : 0);
            var abz = kar.Controls.OfType<Nadpis>().LastOrDefault();
            if (abz != null && TextW(t, F_TEKST) + 30 < tw) kar.Controls.Add(Vopros("Текст ошибки:\n" + info, abz.Left + TextW(t, F_TEKST) + 4, abz.Top + 1));
        }
        return kar;
    }

    Kartochka KartaZagruzki(int W, string t)
    {
        var kar = new Kartochka { Width = W, Height = 56, Margin = new Padding(0, 0, 0, PROM), Name = "karta-zagruzki" };
        kar.Controls.Add(Abz(t, OTS, 18, W - 2 * OTS, "vt"));
        return kar;
    }

    // Метка: нейтральная (маршрут: «через VPN», «напрямую»), акцентная («Сейчас») или цвета состояния (sost)
    Kartochka Metka(string t, string sost, int x, int y)
    {
        Color ott = sost == null ? Ris.Smes(pal.Poverhnost, pal.Tekst, pal.Temnaya ? 0.12 : 0.07)
                  : sost == "akc" ? Ris.Smes(pal.Poverhnost, Ris.AkcentV(pal), pal.Temnaya ? 0.3 : 0.14)
                  : Ris.Smes(pal.Poverhnost, ZnakSost.Cvet(sost), pal.Temnaya ? 0.3 : 0.18);
        var m = new Kartochka { Location = new Point(x, y), Size = new Size(TextW(t, F_TEKST) + 16, 24), Ottenok = ott, Skruglenie = 11, Name = "metka", Margin = Padding.Empty };
        m.Controls.Add(new Nadpis { Text = t, Font = F_TEKST, Location = new Point(7, 3), Tag = sost == null || sost == "akc" ? null : sost });
        return m;
    }

    // ---------- плитки состояния (его выбор 21.11: как «Безопасность Windows») ----------
    class Plit { public string Id, Zag, Sost, Stroka, Podskazka; public List<Knopa> Kn = new List<Knopa>(); }

    // Ряд равных плиток: у каждой заголовок, значок состояния и одна строка; кнопки — только где нужно действие,
    // во всю ширину плитки, столбцом. Высота у всех одна. Узкое окно — по две в ряд.
    Panel RyadPlitok(int W, List<Plit> pl, string imya)
    {
        var ryad = new Panel { Name = imya, Width = W, Margin = new Padding(0, 0, 0, PROM) };
        bool odinRyad = W >= 900 || pl.Count <= 2;
        int kol = odinRyad ? Math.Max(1, pl.Count) : 2;
        int tw = (W - PROM * (kol - 1)) / kol;
        bool estKn = pl.Any(p => p.Kn.Count > 0);
        int hStr = pl.Max(p => VysTeksta(p.Stroka, tw - 2 * OTS - 30));
        int h = 48 + Math.Max(22, hStr) + (estKn ? 16 + KN_H + OTS : 18);
        int rows = (pl.Count + kol - 1) / kol;
        for (int i = 0; i < pl.Count; i++)
            ryad.Controls.Add(Plitka(pl[i], new Rectangle(i % kol * (tw + PROM), i / kol * (h + PROM), tw, h)));
        ryad.Height = rows * h + (rows - 1) * PROM;
        return ryad;
    }

    Kartochka Plitka(Plit p, Rectangle r)
    {
        var kar = new Kartochka { Name = "plit-" + p.Id, Bounds = r, Margin = Padding.Empty, AccessibleDescription = p.Sost, AccessibleName = p.Zag };
        kar.Controls.Add(Abz(p.Zag, OTS, 14, r.Width - 2 * OTS, null, F_ZAG));
        var zn = new ZnakSost { Sost = p.Sost, Location = new Point(OTS, 47), Size = new Size(22, 22) };
        var s = Abz(p.Stroka, OTS + 30, 48, r.Width - 2 * OTS - 30);
        kar.Controls.Add(zn); kar.Controls.Add(s);
        if (!string.IsNullOrEmpty(p.Podskazka)) foreach (Control c in new Control[] { kar, zn, s }) podskazki.SetToolTip(c, p.Podskazka);
        // кнопки — в одну строку внизу: поровну, если подписи влезают, иначе — по длине подписей
        int n = p.Kn.Count;
        if (n > 0)
        {
            int dost = r.Width - 2 * OTS - 8 * (n - 1), x = OTS, ravn = dost / n;
            var nuzhno = p.Kn.Select(kn => TextW(kn.Text, F_TEKST) + 24).ToList();
            bool porovnu = nuzhno.All(v => v <= ravn);
            for (int i = 0; i < n; i++)
            {
                int bw = i == n - 1 ? r.Width - OTS - x : porovnu ? ravn : dost * nuzhno[i] / nuzhno.Sum();
                p.Kn[i].Bounds = new Rectangle(x, r.Height - OTS - KN_H, bw, KN_H); p.Kn[i].Margin = Padding.Empty; kar.Controls.Add(p.Kn[i]); x += bw + 8;
            }
        }
        return kar;
    }

    // Karing выключен — замер, смена сервера и выбор для программ не пройдут: сказать сразу, а не через минуту ожидания
    bool KaringVyklyuchen() { return sost != null && !Pult.Sboy(sost) && Pult.G(sost, "karing") is bool && !Pult.B(sost, "karing"); }

    // Где стоит karing.exe — как у сторожа (storozh.py, karing_exe): из service.json самого Karing, иначе обычные места
    static string NaytiKaring()
    {
        try
        {
            var mesta = new List<string>();
            string sj = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"karing\karing\service.json");
            if (File.Exists(sj))
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(sj));
                string wd = Pult.S(d, "work_dir");
                if (wd != "") mesta.Add(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(wd.TrimEnd('\\', '/'))) ?? "", "karing.exe"));
            }
            mesta.Add(Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Karing\karing.exe"));
            mesta.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Karing\karing.exe"));
            mesta.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Karing\karing.exe"));
            return mesta.FirstOrDefault(File.Exists);
        }
        catch { return null; }
    }

    // «Запустить Karing»: через Проводник (Karing не зависит от окна), через 10 с — спросить сторожа заново
    void ZapustitKaring()
    {
        if (suho) { Zapis("[тест] запуск Karing"); return; }
        var exe = NaytiKaring();
        if (exe == null) { zanyatoStroka.Text = "Не нашёл Karing на этом компьютере — установите его и добавьте подписку VPN."; return; }
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + exe + "\"") { UseShellExecute = false }); }
        catch (Exception e) { zanyatoStroka.Text = "Не запустил Karing: " + e.Message; return; }
        Zapis("запуск Karing: " + exe);
        zanyatoStroka.Text = "Запускаю Karing. Если подключение в нём не включится само — включите его.";
        var t = new System.Windows.Forms.Timer { Interval = 10000 };
        taymery.Add(t);
        t.Tick += (s, e) => { t.Stop(); taymery.Remove(t); t.Dispose(); Obnovit(() => zanyatoStroka.Text = ""); };
        t.Start();
    }

    void VklyuchitZadaniya() { Deystvie("Включение проверки каждые 10 минут", new[] { "ustanovit-zadaniya" }, null); }
    void ProveritSnova() { zanyatoStroka.Text = "Спрашиваю сторожа…"; Obnovit(() => zanyatoStroka.Text = ""); }

    // ---------- живое: скорость раз в 5 с ----------
    Dictionary<string, object> skorData;
    readonly Dictionary<string, Tuple<Nadpis, Uroven, Nadpis>> zhivye = new Dictionary<string, Tuple<Nadpis, Uroven, Nadpis>>();
    bool skorIdet;
    Dictionary<string, object> setData, testData;
    string testKogdaSyr = "";
    Chislo setVniz, setVverh;
    NadpisImya setTop;
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

    // «0,11 МБ/с» → число и единица отдельно (крупное число и единица на одной линии)
    static string[] Razbit(string mb) { int i = mb.LastIndexOf(' '); return i > 0 ? new[] { mb.Substring(0, i), mb.Substring(i + 1) } : new[] { mb, "" }; }

    void PokazatSet()
    {
        if (setVniz == null || setVniz.IsDisposed || setData == null) return;
        foreach (var t in new[] { Tuple.Create(setVniz, "vniz"), Tuple.Create(setVverh, "vverh") })
        {
            if (Pult.Sboy(setData)) { t.Item1.Znach = "—"; t.Item1.Ed = ""; continue; }
            var r = Razbit(MBs(Pult.Ch(Pult.G(setData, t.Item2))));
            t.Item1.Znach = r[0]; t.Item1.Ed = r[1];
        }
    }

    // служебные программы самого сторожа (Python пульта и проверок, curl замеров) — в списках не показываем
    static bool Sluzhebnaya(string exe)
    {
        var e = (exe ?? "").ToLowerInvariant();
        return e == "python.exe" || e == "pythonw.exe" || e == "curl.exe";
    }

    void PokazatSkorosti()
    {
        var sk = Pult.D(Pult.G(skorData, "skorosti"));
        foreach (var kv in zhivye)
        {
            if (kv.Value.Item1.IsDisposed) continue;
            object v; double mb = 0; string kuda = null;
            bool est = sk.TryGetValue(kv.Key, out v);
            if (est) { mb = Pult.Ch(Pult.G(Pult.D(v), "mbayt")); kuda = Pult.S(Pult.D(v), "kuda"); }
            kv.Value.Item1.Text = est ? MBs(mb) : "—";
            if (!est && skorData != null) podskazki.SetToolTip(kv.Value.Item1, "Скорость этой программы сейчас не видна.");
            if (kv.Value.Item2 != null) { kv.Value.Item2.Dolya = Math.Min(1, mb / 5); kv.Value.Item2.Invalidate(); }
            if (!string.IsNullOrEmpty(kuda) && kv.Value.Item3 != null) { kv.Value.Item3.Text = KudaTekst(kuda); podskazki.SetToolTip(kv.Value.Item3, kv.Value.Item3.Text); }
        }
        if (setTop != null && !setTop.IsDisposed)
        {
            string t = "";
            // по программам не видно (Karing не запущен или не ответил) — строку не показываем: про Karing уже сказано выше
            if (skorData != null && !Pult.Sboy(skorData))
            {
                var top = sk.Where(x => !Sluzhebnaya(x.Key)).Select(x => Tuple.Create(x.Key, Pult.Ch(Pult.G(Pult.D(x.Value), "mbayt")))).OrderByDescending(x => x.Item2).FirstOrDefault();
                t = top != null && top.Item2 >= 0.05 ? "Больше всего качает " + Imya(top.Item1) + " — " + MBs(top.Item2) : "Программы сейчас почти ничего не качают";
            }
            setTop.Text = t;
            podskazki.SetToolTip(setTop, t);
        }
    }

    // значок программы — из самого exe (по пути запущенного процесса); не нашёлся — общий значок из шрифта
    static readonly Dictionary<string, Bitmap> ikonki = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    static Bitmap IkonkaIzExe(string exe)
    {
        Bitmap b;
        string k = exe ?? "";
        if (ikonki.TryGetValue(k, out b)) return b;
        b = null;
        try
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(k)))
            {
                try
                {
                    if (b != null) continue;
                    string put = p.MainModule.FileName;
                    using (var ic = Icon.ExtractAssociatedIcon(put))
                    using (var bm = ic.ToBitmap())
                    {
                        b = new Bitmap(20, 20);
                        using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(bm, new Rectangle(0, 0, 20, 20)); }
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        ikonki[k] = b;
        return b;
    }
    Control ZnachokProgrammy(string exe, int x, int y)
    {
        var b = IkonkaIzExe(exe);
        if (b != null) return new PictureBox { Image = b, Size = new Size(20, 20), Location = new Point(x, y), BackColor = Color.Transparent, Name = "ikonka" };
        return Zk(Znachok(exe), x, y - 1, 12f);
    }

    static string Znachok(string exe)
    {
        var e = (exe ?? "").ToLowerInvariant();
        if (e.Contains("zen") || e.Contains("edge") || e.Contains("chrome") || e.Contains("firefox") || e.Contains("browser") || e.Contains("opera") || e.Contains("yandex")) return Zn.Globus;
        if (e.Contains("steam") || e.Contains("roblox") || e.Contains("dota") || e.Contains("game")) return Zn.Igra;
        if (e.Contains("discord") || e.Contains("telegram")) return Zn.Soobshch;
        if (e.Contains("claude")) return Zn.Chat;
        if (e.Contains("python") || e.Contains("curl") || e.Contains("powershell") || e.Contains("cmd")) return Zn.Komstroka;
        return Zn.OknoProg;
    }

    // имя программы для человека: известным — их название, остальным — имя файла без «.exe»
    static string Imya(string exe)
    {
        string n;
        try { n = Path.GetFileNameWithoutExtension(exe ?? ""); } catch { n = exe ?? ""; }
        var izv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "zen", "Zen" }, { "msedge", "Edge" }, { "msedgewebview2", "Edge (встроенный в программы)" }, { "chrome", "Chrome" }, { "firefox", "Firefox" },
            { "claude", "Claude" }, { "Discord", "Discord" }, { "Telegram", "Telegram" }, { "steam", "Steam" }, { "steamwebhelper", "Steam: магазин и чат" },
            { "RobloxPlayerBeta", "Roblox" }, { "pythonw", "Сторож сети" }, { "python", "Python" }, { "tailscaled", "Tailscale" }, { "tailscale-ipn", "Tailscale" },
            { "svchost", "Службы Windows" }, { "explorer", "Проводник" }, { "Code", "VS Code" }, { "obs64", "OBS" }, { "Spotify", "Spotify" },
            { "EpicGamesLauncher", "Epic Games" }, { "Battle.net", "Battle.net" }, { "OneDrive", "OneDrive" }, { "scrcpy", "Экран телефона" }, { "karing", "Karing" },
            { "gamingservices", "Службы игр Xbox" }, { "MpDefenderCoreService", "Защитник Windows" }, { "MsMpEng", "Защитник Windows" }, { "gamingservicesnet", "Службы игр Xbox" }, { "artcraft-launcher", "ArtCraft" }, { "XboxPcApp", "Xbox" } };
        string v; if (izv.TryGetValue(n, out v)) return v;
        n = n.Replace('-', ' ').Replace('_', ' ').Trim();
        return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
    }

    // куда программа идёт сейчас — словами без противоречий: «Германия, напрямую» → «Через VPN (Германия) и напрямую»
    static string KudaTekst(string seychas)
    {
        var s = (Pult.Chisto(seychas) ?? "").Trim();
        if (s == "") return "";
        if (s.Equals("мимо Karing", StringComparison.OrdinalIgnoreCase)) return "Мимо Karing (VPN)";
        if (s.Equals("сейчас не в сети", StringComparison.OrdinalIgnoreCase)) return "Сейчас не в сети";
        var chasti = s.Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
        bool napr = chasti.Any(x => x.Equals("напрямую", StringComparison.OrdinalIgnoreCase));
        var srv = chasti.Where(x => !x.Equals("напрямую", StringComparison.OrdinalIgnoreCase)).ToList();
        if (srv.Count == 0) return "Напрямую";
        return "Через VPN (" + string.Join(", ", srv) + ")" + (napr ? " и напрямую" : "");
    }

    // у новичка «мимо Karing» значит просто «Karing не запущен»: программа идёт напрямую
    string KudaTekstN(string seychas)
    {
        string t = KudaTekst(seychas);
        return t == "Мимо Karing (VPN)" && KaringVyklyuchen() ? "Напрямую — Karing не запущен" : t;
    }

    // скорость: всё в мегабайтах в секунду (1 МБ/с = 8 Мбит/с)
    static string MBs(double mbayt) { return Chel.MB(mbayt); }
    static string MBsIzMbit(double mbit) { return Chel.MBizMbit(mbit); }

    // «20.11.2026 21:50» → «в 21:50» сегодня, иначе «18.11 в 21:50»
    static string Kogda(string dt)
    {
        DateTime d;
        if (DateTime.TryParseExact(dt ?? "", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d.Date == DateTime.Today ? "в " + d.ToString("HH:mm") : d.ToString("dd.MM") + " в " + d.ToString("HH:mm");
        if (DateTime.TryParseExact(dt ?? "", "dd.MM HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            return d.Month == DateTime.Today.Month && d.Day == DateTime.Today.Day ? "в " + d.ToString("HH:mm") : d.ToString("dd.MM") + " в " + d.ToString("HH:mm");
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
        if (Sprosit("Сторож замерит скорость всех серверов VPN и подключит самый быстрый из надёжных. Это 2–5 минут, интернет пару раз моргнёт. Начать?"))
            Deystvie("Замер серверов", new[] { "zamer" }, d => servData = null);
    }

    // обрывы сервера за сутки: [обрывов, минут без ответа] из stat.nadezhnost (имена — без значков)
    int[] ObryvyServera(string srv)
    {
        var nad = Pult.D(Pult.G(Pult.D(Pult.G(sost, "stat")), "nadezhnost"));
        string c = (Pult.Chisto(srv) ?? "").Trim();
        foreach (var kv in nad)
            if (c != "" && (Pult.Chisto(kv.Key) ?? "").Trim() == c) { var l = Spisok(kv.Value); return new[] { Pult.Cel(Pult.El(l, 0)), Pult.Cel(Pult.El(l, 1)) }; }
        return null;
    }

    // что показано на экранах (для самопроверки: одно и то же число на Мониторе и в «Серверах» должно совпадать)
    internal readonly Dictionary<string, int> pokazanoObryvov = new Dictionary<string, int>();

    // ---------- Монитор: сводка по всем данным ----------
    class Svodka
    {
        public bool KaringIzv, Karing, ZapRab, ZapEst, ZapNeNuzhen, Zadaniya, Proveryal;
        public string Server = "", KaringBeda = "", Posledniy = "";
        public double SkorMbit;
        public int Obryvy, BezVpn, Dolgiy;
        public List<string> Pending = new List<string>(), Prochee = new List<string>(), Igra = new List<string>();
        public List<Tuple<string, int>> PoServeram = new List<Tuple<string, int>>();
    }

    Svodka Svesti()
    {
        var s = new Svodka();
        var bedy = Pult.L(sost, "bedy").OfType<string>().Select(b => b.Trim()).Where(b => b != "").ToList();
        var nastr = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (var p in Pult.Slovari(sost, "nastroyka")) nastr[Pult.S(p, "id")] = p;
        Func<string, Dictionary<string, object>> punkt = id => { Dictionary<string, object> p; return nastr.TryGetValue(id, out p) ? p : null; };
        var nas = Pult.D(Pult.G(sost, "nastroyki"));
        s.KaringIzv = Pult.G(sost, "karing") is bool;
        s.Karing = Pult.B(sost, "karing");
        var pk = punkt("karing");
        string sovetK = pk != null && !Pult.B(pk, "gotovo") ? (Pult.G(pk, "sovet") as string ?? "").Trim() : "";
        s.KaringBeda = sovetK != "" ? sovetK : bedy.FirstOrDefault(b => b.StartsWith("Karing")) ?? "";
        s.Server = (Pult.Chisto(Pult.G(sost, "server") as string ?? "") ?? "").Trim();
        s.SkorMbit = Pult.Ch(Pult.G(sost, "skorost"));
        s.Posledniy = Pult.G(sost, "posledniy") as string ?? "";
        s.Proveryal = s.Posledniy.Trim() != "";
        s.Obryvy = Math.Max(0, Pult.Cel(Pult.G(sost, "miganiya")));
        var st = Pult.D(Pult.G(sost, "stat"));
        s.BezVpn = Math.Max(0, Pult.Cel(Pult.G(st, "bez_vpn"))); s.Dolgiy = Math.Max(0, Pult.Cel(Pult.G(st, "samyy_dlinnyy")));
        foreach (var z in Spiski(st, "po_serveram"))
        {
            string kto = Pult.Chisto(Pult.El(z, 0) as string ?? "").Trim();
            if (kto != "") s.PoServeram.Add(Tuple.Create(kto, Pult.Cel(Pult.El(z, 1))));
        }
        s.ZapRab = Pult.B(sost, "zapret");
        s.ZapEst = Pult.G(sost, "zapret_est") is bool ? Pult.B(sost, "zapret_est") || s.ZapRab : s.ZapRab || (Pult.G(sost, "strategiya") as string ?? "").Trim() != "";
        s.ZapNeNuzhen = !s.ZapRab && Pult.B(nas, "zapret_ne_nuzhen");
        var pz = punkt("zadaniya");
        s.Zadaniya = pz != null ? Pult.B(pz, "gotovo") : !bedy.Any(b => b.StartsWith("Сторож не запускается"));
        foreach (var b in bedy)
        {
            if (b.StartsWith("Karing") || b.StartsWith("zapret") || b.StartsWith("Сторож не запускается")) continue;
            if (b.StartsWith("Пустить через VPN")) s.Pending.Add(b); else s.Prochee.Add(b);
        }
        s.Igra = Pult.L(sost, "igra").OfType<string>().Where(x => x.Trim() != "").ToList();
        return s;
    }

    // строка плитки VPN, когда Karing работает, но что-то не так: «в Karing нет серверов — добавьте…» → «В Karing нет серверов»
    static string KaringStroka(string beda)
    {
        string t = (beda ?? "").Trim();
        int i = t.IndexOf(" — ", StringComparison.Ordinal);
        if (i > 0) t = t.Substring(0, i);
        return Chel.Pervaya(t == "" ? "Karing работает не так" : t);
    }

    const string LEGENDA = "Это все случаи, когда VPN не отвечал вовсе — на любом сервере, в том числе на сервере Claude.\nКак обрывался каждый сервер — в разделе «Серверы».\n" +
                           "Каждая клетка — час, справа — текущий. Зелёная — связь не пропадала, жёлтая — пропадала 1–2 раза, красная — 3 и больше или дольше минуты.\nНаведите на клетку — подробности.";

    // Значок Claude (его выбор: только если сторож за ним следит) — в уголке шапки Монитора, просто значок и текст,
    // не кнопка. Нажатие открывает «Серверы». Цвет: красный — Claude не отвечает; жёлтый — его сервер за сутки
    // обрывается (то же правило и те же числа, что в «Серверах»). Сервер Claude закреплён намеренно — менять его отсюда нельзя.
    Panel ZnachokClaude(Dictionary<string, object> cz, out string sostC)
    {
        var okV = Pult.G(cz, "ok");
        string srv = Pult.Chisto(Pult.G(cz, "server") as string ?? "").Trim();
        string kogda = Pult.Chisto(Convert.ToString(Pult.G(cz, "proveren") ?? "", CultureInfo.CurrentCulture)).Trim();
        var ob = srv != "" ? ObryvyServera(srv) : null;
        string so = ob != null ? SlovoObryvov(ob[0], ob[1]) : null;
        sostC = okV is bool && !(bool)okV ? "kras" : so != null ? "zhelt" : okV is bool ? "ok" : "sery";
        string t = "Claude" + (srv != "" ? " · " + srv : "");
        var z = new Panel { Name = "claude-znachok", Size = new Size(26 + TextW(t, F_TEKST) + 4, 26), AccessibleDescription = sostC, Cursor = Cursors.Hand, Margin = Padding.Empty };
        var zn = new ZnakSost { Sost = sostC, Size = new Size(18, 18), Location = new Point(0, 4), Cursor = Cursors.Hand };
        var n = new Nadpis { Text = t, Font = F_TEKST, Location = new Point(24, 4), Cursor = Cursors.Hand };
        z.Controls.Add(zn); z.Controls.Add(n);
        if (ob != null) pokazanoObryvov["glavnaya:" + srv] = ob[0];
        string pod = (okV is bool ? ((bool)okV ? "Claude отвечает" : "Claude не отвечает") + (kogda != "" ? " — проверено " + Kogda(kogda) : "") : "Claude ещё не проверяли") + "." +
                     (so != null ? "\nСервер " + srv + " обрывается — " + Mnozh(ob[0], "раз", "раза", "раз") + " за сутки, без ответа " + ob[1] + " мин." : "") +
                     (srv != "" ? "\nClaude закреплён за сервером " + srv + ". Нажмите — откроются «Серверы»." : "");
        foreach (Control x in new Control[] { z, zn, n }) { podskazki.SetToolTip(x, pod); x.Click += (s, e) => Otkryt("servery"); }
        return z;
    }

    Panel StrGlavnaya()
    {
        var k = Kolonka(); int W = Shir;
        if (sost == null)
        {
            k.Controls.Add(Shapka("Смотрю, как дела у сети", "Спрашиваю сторожа", null, "sery"));
            return k;
        }
        if (Pult.Sboy(sost))
        {
            bool netPy = Pult.B(sost, "net_python");
            k.Controls.Add(Shapka(netPy ? "Сторож не может работать" : "Сторож не отвечает", netPy ? "Без Python он не запускается" : "Окно спрашивает его раз в минуту само", null, "kras"));
            k.Controls.Add(KartaOshibki(W, netPy ? "Не нашёл Python" : "Не получилось узнать, как дела у сети", Pult.Info(sost), () => { sost = null; Perestroit(); Obnovit(); }));
            return k;
        }
        var s = Svesti();
        bool karOff = s.KaringIzv && !s.Karing;

        // ---- плитки: VPN, стабильность основного сервера, YouTube и Discord, автопроверка ----
        var vpn = new Plit { Id = "vpn", Zag = "VPN" };
        string zm = Pult.S(sost, "zamer");
        if (!s.KaringIzv) { vpn.Sost = "sery"; vpn.Stroka = "Нет данных о Karing (VPN)"; }
        else if (!s.Karing)
        {
            vpn.Sost = "kras"; vpn.Stroka = KARING_NE;
            vpn.Podskazka = (s.KaringBeda != "" ? Chel.Tekst(s.KaringBeda) + ".\n" : "") + "Без Karing заблокированные в России сайты не откроются.";
            vpn.Kn.Add(Kn("Запустить Karing", ZapustitKaring));
        }
        else if (s.KaringBeda != "")
        {
            vpn.Sost = "zhelt"; vpn.Stroka = KaringStroka(s.KaringBeda); vpn.Podskazka = Chel.Tekst(s.KaringBeda);
            vpn.Kn.Add(Kn("Открыть Karing", ZapustitKaring));
        }
        else if (s.Server == "")
        {
            vpn.Sost = "zhelt"; vpn.Stroka = "Сервер не выбран";
            vpn.Kn.Add(Kn("Выбрать лучший сервер", NaytiLuchshiy));
            vpn.Podskazka = "Сторож замерит все серверы VPN и подключит самый быстрый. 2–5 минут, интернет пару раз моргнёт.";
        }
        else
        {
            // скорость — с подписью, откуда она: ночной (или ваш) замер серверов, а не тест скорости
            vpn.Sost = "ok"; vpn.Stroka = s.Server + (s.SkorMbit > 0 ? " · " + MBsIzMbit(s.SkorMbit) + (zm != "" ? " · замер " + Kogda(zm) : "") : "");
            vpn.Podskazka = "Сайты и программы идут через сервер " + s.Server + "." + (s.SkorMbit > 0 ? "\nСкорость — по замеру серверов" + (zm != "" ? " " + Kogda(zm) : "") + "." : "");
        }

        // стабильность — по ОСНОВНОМУ серверу (обрывы сервера Claude — у его значка); числа — те же, что в «Серверах»
        var stab = new Plit { Id = "stabil", Zag = "Стабильность" };
        var obGl = s.Server != "" ? ObryvyServera(s.Server) : null;
        if (karOff || s.Server == "" || (obGl == null && !s.Proveryal)) { stab.Sost = "sery"; stab.Stroka = "Ещё нет данных"; stab.Podskazka = "Сторож ещё не проверял основной сервер. Обрывы появятся после первых проверок."; }
        else
        {
            int obr = obGl != null ? obGl[0] : 0, min = obGl != null ? obGl[1] : 0;
            pokazanoObryvov["glavnaya:" + s.Server] = obr;
            stab.Sost = SlovoObryvov(obr, min) != null ? "zhelt" : "ok";
            stab.Stroka = "Основной сервер — " + (obr == 0 ? "без обрывов" : Mnozh(obr, "обрыв", "обрыва", "обрывов")) + " за сутки";
            stab.Podskazka = "Сервер " + s.Server + " за последние сутки обрывался " + Mnozh(obr, "раз", "раза", "раз") + ", без ответа " + min + " мин.\nКак обрывались остальные серверы — в разделе «Серверы».";
            if (obr >= 10) stab.Kn.Add(Kn("Выбрать лучший сервер", NaytiLuchshiy));
        }

        var golos = Pult.D(Pult.G(sost, "golos"));
        bool golosEst = Pult.B(golos, "est");
        double poteri = Pult.Ch(Pult.G(golos, "poteri"));
        string ytSboy = Pult.G(sost, "youtube_sboy") as string ?? "";
        bool golosBeda = false;
        var yt = new Plit { Id = "youtube", Zag = "YouTube и Discord" };
        if (s.ZapRab)
        {
            if (ytSboy.Trim() != "" || (golosEst && poteri >= 8))
            {
                bool ytBeda = ytSboy.Trim() != "";
                golosBeda = !ytBeda;
                yt.Sost = "zhelt"; yt.Stroka = ytBeda ? "YouTube напрямую не открывался" : "Голос Discord прерывается";
                yt.Podskazka = (ytBeda ? "Сторож проверял YouTube без VPN " + Kogda(ytSboy) + " — не открылся." : "За последние 2 минуты теряется " + poteri.ToString("0.#") + " % голоса.") +
                               "\nСторож подберёт настройку обхода zapret, при которой всё работает. Около 10 минут.";
                yt.Kn.Add(Kn("Подобрать настройку", PodobratZapret));
            }
            else
            {
                yt.Sost = "ok"; yt.Stroka = "zapret работает";
                yt.Podskazka = "Программа zapret открывает YouTube и голос Discord без VPN. Сторож проверяет YouTube раз в час — сбоев не было." +
                               (golosEst ? "\nГолос в Discord идёт чисто." : "");
            }
        }
        else if (s.ZapNeNuzhen) { yt.Sost = "sery"; yt.Stroka = "Не нужно: идут через VPN"; yt.Podskazka = "Вы отметили, что zapret не нужен. Установить его можно в разделе «YouTube и Discord»."; }
        else if (s.ZapEst)
        {
            yt.Sost = "zhelt"; yt.Stroka = "zapret не работает";
            yt.Podskazka = "Сторож переберёт настройки обхода в zapret и поставит самую надёжную. Около 10 минут — всё это время YouTube и Discord без VPN рвутся.";
            yt.Kn.Add(Kn("Подобрать настройку", PodobratZapret));
        }
        else
        {
            yt.Sost = "sery"; yt.Stroka = "zapret не установлен";
            yt.Podskazka = "zapret открывает YouTube и голос Discord без VPN. Это не обязательно: без него они идут через VPN.";
            yt.Kn.Add(Kn("Установить zapret", UstanovitZapret));
            yt.Kn.Add(Kn("Не нужно", ZapretNeNuzhen));
            podskazki.SetToolTip(yt.Kn[0], "Сторож скачает zapret с GitHub (около 4 МБ) и включит его. Windows спросит разрешение.");
            podskazki.SetToolTip(yt.Kn[1], "Больше не напоминать: YouTube и голос Discord будут идти через VPN.");
        }

        var avto = new Plit { Id = "avto", Zag = "Автопроверка" };
        if (s.Zadaniya)
        {
            avto.Sost = "ok"; avto.Stroka = "Каждые 10 мин" + (s.Proveryal ? " · последняя " + Kogda(s.Posledniy) : "");
            avto.Podskazka = "Сторож сам проверяет сеть каждые 10 минут и чинит, что сломано, даже когда окно закрыто.";
        }
        else
        {
            avto.Sost = "zhelt"; avto.Stroka = "Выключена — проверяет, только пока окно открыто";
            avto.Podskazka = "Сторож будет сам проверять сеть каждые 10 минут, даже когда окно закрыто. Прав администратора не нужно.";
            avto.Kn.Add(Kn("Включить", VklyuchitZadaniya));
        }
        var plitki = new List<Plit> { vpn, stab, yt, avto };

        // Claude — значком в уголке шапки, только если сторож за ним следит; в «Требует внимания» не попадает: с ним нечего делать
        var cz = Pult.D(Pult.G(sost, "claude_znachok"));
        string sostC = null;
        Panel znCl = Pult.B(cz, "pokazat") ? ZnachokClaude(cz, out sostC) : null;

        // общая фраза — по плиткам: «Всё работает» рядом с жёлтой или красной плиткой не бывает
        string obshch, fraza;
        bool estZhelt = plitki.Any(p => p.Sost == "zhelt" || p.Sost == "kras") || s.Pending.Count > 0;
        if (!s.KaringIzv) { obshch = "sery"; fraza = "Нет данных о VPN"; }
        else if (!s.Karing) { obshch = "kras"; fraza = "VPN выключен — заблокированные сайты не откроются"; }
        else if (estZhelt) { obshch = "zhelt"; fraza = stab.Sost == "zhelt" ? "Работает, но есть перебои" : "Работает, но есть что поправить"; }
        else { obshch = "ok"; fraza = "Всё работает"; }
        // подпись — что требует внимания (время проверки — в плитке «Автопроверка», не повторяем)
        var imenaPlitok = new Dictionary<string, string> { { "vpn", "VPN" }, { "stabil", "стабильность" }, { "youtube", golosBeda ? "голос Discord" : "YouTube" }, { "avto", "автопроверка" } };
        var vnim = plitki.Where(p => (p.Sost == "zhelt" || p.Sost == "kras") && !(karOff && p.Id == "vpn")).Select(p => imenaPlitok[p.Id]).ToList();
        if (s.Pending.Count > 0) vnim.Add("сайты");
        string pod = !s.Proveryal ? "Сторож ещё не проверял сеть" : vnim.Count == 0 ? (karOff ? "" : "Ничего делать не нужно") : "Требует внимания: " + string.Join(", ", vnim);
        var proverit = KnZ("Проверить и починить", Zn.Obnovit, () => Deystvie("Проверка сети", new[] { "proverka" }, null));
        podskazki.SetToolTip(proverit, "Сторож сразу проверит VPN, сайты и программы и починит, что сломано.\nЕсли чинить нужно — Karing перезапустится, интернет моргнёт на пару секунд.");
        k.Controls.Add(Shapka(fraza, pod, proverit, obshch, znCl));

        // одна главная кнопка: самое нужное сейчас (VPN → автопроверка → zapret → стабильность → сайты); при «всё работает» — ни одной
        var sovety = new List<Tuple<string, string, Knopa>>();
        foreach (var b in s.Pending)
        {
            string sayty = b.Substring(b.IndexOf(':') + 1); int i = sayty.IndexOf(" — ", StringComparison.Ordinal); if (i > 0) sayty = sayty.Substring(0, i);
            var kn = Kn("Пустить через VPN", () => Deystvie("Проверка сети и правка", new[] { "proverka" }, null));
            sovety.Add(Tuple.Create("Не открываются напрямую: " + sayty.Trim(), "Сторож пустит эти сайты через VPN. Karing перезапустится, интернет моргнёт на пару секунд", kn));
        }
        foreach (var b in s.Prochee)
        {
            string t = Chel.Tekst(b); int i = t.IndexOf(" — ", StringComparison.Ordinal);
            sovety.Add(Tuple.Create(i > 0 ? t.Substring(0, i) : t, i > 0 ? Chel.Pervaya(t.Substring(i + 3)) : "", (Knopa)null));
        }
        if (obshch != "ok")
        {
            var glavn = new[] { vpn.Kn.FirstOrDefault(), avto.Kn.FirstOrDefault(), yt.Sost == "zhelt" ? yt.Kn.FirstOrDefault() : null, stab.Kn.FirstOrDefault() }
                        .Concat(sovety.Select(x => x.Item3)).FirstOrDefault(x => x != null);
            if (glavn != null) glavn.Glavnaya = true;
        }
        k.Controls.Add(RyadPlitok(W, plitki, "plitki"));

        if (s.Igra.Count > 0)
        {
            int zv = Pult.Cel(Pult.G(Pult.D(Pult.G(sost, "nastroyki")), "zvonok_min"));
            bool igraEst = s.Igra.Any(x => !x.Contains("звонок")), zvonok = s.Igra.Any(x => x.Contains("звонок"));
            var ig = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = "karta-igra" };
            ig.Controls.Add(Zk(Zn.Igra, OTS, 15, 12f));
            string t = "Сейчас " + (igraEst && zvonok ? "идут игра и звонок в Discord" : igraEst ? "идёт игра" : "идёт звонок в Discord") +
                       ". Сторож не перезапускает Karing (VPN), чтобы не оборвать" + (zvonok && zv > 0 ? " — звонок ждёт не дольше " + zv + " мин" : "");
            ig.Controls.Add(Abz(t, OTS + 30, 14, W - 2 * OTS - 30));
            ig.Height = 14 + VysTeksta(t, W - 2 * OTS - 30) + 14;
            k.Controls.Add(ig);
        }

        if (sovety.Count > 0)
        {
            int y;
            var ks = Karta(W, "Что ещё поправить", null, out y, "karta-sovety");
            foreach (var sv in sovety) y = StrokaDeystviya(ks, W, y, sv.Item1, sv.Item2, null, sv.Item3);
            ks.Height = y;
            k.Controls.Add(ks);
        }

        // обрывы связи за сутки (VPN не отвечал вовсе — на любом сервере, в том числе у Claude): сколько, сколько всего, по часам.
        // Число на плитке «Стабильность» — про основной сервер, здесь — про связь целиком: одно число дважды не повторяется.
        // Данных нет — одна строка вместо пустой шкалы («нет данных» уже сказано на плитке).
        {
            int y;
            bool netDannyh = !s.Proveryal && s.Obryvy == 0;
            if (netDannyh)
            {
                var ko0 = Karta(W, "Обрывы связи за сутки", "Шкала по часам появится после первой проверки сети", out y, "karta-obryvov");
                ko0.Height = y;
                k.Controls.Add(ko0);
            }
            else
            {
                string podO = s.Obryvy == 0 ? "Связь через VPN не пропадала" :
                              "Связь через VPN пропадала " + Mnozh(s.Obryvy, "раз", "раза", "раз") + " · всего " + VremyaKr(s.BezVpn) + " · самый долгий раз — " + VremyaKr(s.Dolgiy);
                var ko = Karta(W, "Обрывы связи за сутки", podO, out y, "karta-obryvov", LEGENDA);
                var ps = new PolosaSutok { Location = new Point(OTS, y), Size = new Size(W - 2 * OTS, 52) };
                var chasy = Pult.L(Pult.D(Pult.G(sost, "stat")), "chasy").ToList();
                int summa = 0;
                for (int h = 0; h < 24 && h < chasy.Count; h++) { var c = Spisok(chasy[h]); ps.Obryvy[h] = Pult.Cel(Pult.El(c, 0)); ps.Sekund[h] = Pult.Cel(Pult.El(c, 1)); summa += ps.Obryvy[h]; }
                pokazanoObryvov["svyaz:chislo"] = s.Obryvy; pokazanoObryvov["svyaz:chasy"] = summa;
                ko.Controls.Add(ps);
                ko.Height = y + 52 + OTS - 4;
                k.Controls.Add(ko);
            }
        }

        // сеть сейчас и тест скорости — две карточки рядом (узкое окно — одна под другой)
        {
            bool ryadom = W >= 900;
            int cw = ryadom ? (W - PROM) / 2 : W;
            var dva = new Panel { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = "ryad-seti" };
            var ks = KartaSeti(cw, karOff); var kt = KartaTesta(cw, s, karOff);
            ks.Margin = kt.Margin = Padding.Empty;
            if (ryadom) { int h = Math.Max(ks.Height, kt.Height); ks.Height = kt.Height = h; ks.Location = new Point(0, 0); kt.Location = new Point(cw + PROM, 0); dva.Height = h; }
            else { ks.Location = new Point(0, 0); kt.Location = new Point(0, ks.Height + PROM); dva.Height = ks.Height + PROM + kt.Height; }
            dva.Controls.Add(ks); dva.Controls.Add(kt);
            k.Controls.Add(dva);
        }
        PokazatSet();
        PokazatSkorosti();
        return k;
    }

    void ZapretNeNuzhen()
    {
        var nas = Pult.G(sost, "nastroyki") as Dictionary<string, object>;
        if (nas != null) nas["zapret_ne_nuzhen"] = true;   // сразу, не дожидаясь сторожа
        foreach (var p in Pult.Slovari(sost, "nastroyka")) if (Pult.S(p, "id") == "zapret") p["gotovo"] = true;
        Perestroit();
        Sohranit("zapret_ne_nuzhen", true);
    }

    Kartochka KartaSeti(int cw, bool karOff)
    {
        int y;
        var kar = Karta(cw, "Сеть сейчас", "Весь компьютер, обновляется каждые 5 с", out y, "karta-seti");
        int x = OTS;
        foreach (var c in new[] { Tuple.Create(Zn.Vniz, "Загрузка"), Tuple.Create(Zn.Vverh, "Отдача") })
        {
            kar.Controls.Add(Zk(c.Item1, x, y + 1, 10f));
            kar.Controls.Add(Abz(c.Item2, x + 20, y, 140, "vt"));
            var ch = new Chislo { Location = new Point(x, y + 24), Size = new Size(170, 34), Znach = "…" };
            kar.Controls.Add(ch);
            if (c.Item2 == "Загрузка") setVniz = ch; else setVverh = ch;
            x += 190;
        }
        y += 24 + 34 + 10;
        setTop = null;
        if (!karOff)
        {
            setTop = ImyaN("", OTS, y, cw - 2 * OTS, "vt");
            kar.Controls.Add(setTop);
            y += F_TEKST.Height + 4;
        }
        kar.Height = y + OTS;
        return kar;
    }

    const string TEST_PODSK = "Сторож скачает 25 МБ через VPN и 25 МБ напрямую и покажет обе скорости.\nЗаймёт около 30 секунд, интернет не рвётся.";
    const double TEST_SHKALA = 400;   // общая шкала полос теста: 0–50 МБ/с (400 Мбит/с), одна для обеих строк

    // время теста «18.11 23:13» или «18.11.2026 23:13» → когда; null — не понять
    static DateTime? VremyaTesta(string t)
    {
        DateTime d;
        if (DateTime.TryParseExact(t ?? "", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
        if (DateTime.TryParseExact(t ?? "", "dd.MM HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d > DateTime.Now.AddDays(1) ? d.AddYears(-1) : d;
        return null;
    }

    Kartochka KartaTesta(int cw, Svodka s, bool karOff)
    {
        int y;
        // результат: тест, запущенный в окне, или последний из истории сторожа
        double mv = 0, mp = 0; string pingV = "", srv = "", kogdaSyr = ""; bool est = false, sboy = false; string info = "";
        if (testData != null && Pult.Sboy(testData)) { est = true; sboy = true; info = Pult.Info(testData); kogdaSyr = DateTime.Now.ToString("dd.MM.yyyy HH:mm"); }
        else if (testData != null)
        {
            est = true;
            var v = Pult.D(Pult.G(testData, "vpn")); var pr = Pult.D(Pult.G(testData, "pryamo"));
            mv = Pult.Ch(Pult.G(v, "mbit")); mp = Pult.Ch(Pult.G(pr, "mbit")); pingV = Pult.S(v, "ping");
            srv = Pult.Chisto(Pult.S(testData, "server")).Trim();
            kogdaSyr = Pult.S(testData, "kogda") != "" ? Pult.S(testData, "kogda") : testKogdaSyr;
        }
        else
        {
            var tp = Pult.G(sost, "test_poslednii") as Dictionary<string, object>;
            if (tp != null)
            {
                est = true; mv = Pult.Ch(Pult.G(tp, "vpn")); mp = Pult.Ch(Pult.G(tp, "pryamo"));
                srv = Pult.Chisto(Pult.S(tp, "server")).Trim(); kogdaSyr = Pult.S(tp, "kogda");
            }
        }
        var kogdaD = VremyaTesta(kogdaSyr);
        bool ustarel = kogdaD != null && (DateTime.Now - kogdaD.Value).TotalHours > 24;
        string pod = !est ? "Тест ещё не запускали" : "Последний тест " + (kogdaD != null ? Kogda(kogdaD.Value.ToString("dd.MM.yyyy HH:mm")) : "— время не записано") + (ustarel ? " · устарел" : "");
        var kar = Karta(cw, "Тест скорости", pod, out y, "karta-testa", null, KN_W + 20);
        var kt = KnZ(testIdet ? "Идёт тест" : "Запустить тест", Zn.Skorost, () =>
        {
            if (testIdet) return;
            testIdet = true;
            if (!Deystvie("Тест скорости", new[] { "test-seti" }, d => { testIdet = false; testData = d; testKogdaSyr = DateTime.Now.ToString("dd.MM.yyyy HH:mm"); })) testIdet = false;
        });
        // без Karing через VPN мерить нечего — кнопка неактивна, почему — в подсказке (про Karing уже сказано вверху экрана)
        kt.Enabled = !testIdet && !karOff;
        kt.Location = new Point(cw - OTS - KN_W, OTS - 2); kt.Margin = Padding.Empty;
        podskazki.SetToolTip(kt, karOff ? "Тест заработает, когда Karing (VPN) будет запущен." : TEST_PODSK);
        kar.Controls.Add(kt);
        y = Math.Max(y, OTS - 2 + KN_H + 12);
        int tw = cw - 2 * OTS;
        if (!est) { kar.Height = y + 4; return kar; }
        if (sboy)
        {
            string t = "Тест не прошёл: " + (Syroe(info) ? "сторож ответил ошибкой" : Chel.Tekst(info).ToLowerInvariant());
            kar.Controls.Add(Abz(t, OTS, y, tw, "vt")); kar.Height = y + VysTeksta(t, tw) + OTS;
            return kar;
        }
        int podpW = Math.Min(190, tw / 3), znW = 96, xPol = OTS + podpW + 8, polW = Math.Max(30, cw - OTS - znW - 8 - xPol);
        foreach (var t in new[] { Tuple.Create("Через VPN", srv, mv, pingV), Tuple.Create("Напрямую", "", mp, "") })
        {
            kar.Controls.Add(ImyaN(t.Item1 + (t.Item2 != "" ? " (" + t.Item2 + ")" : ""), OTS, y, podpW));
            // полосы — нейтральные (синий — только у главной кнопки) и по общей шкале 0–50 МБ/с
            var ur = new Uroven { Location = new Point(xPol, y + 8), Size = new Size(polW, 8), Dolya = Math.Min(1, t.Item3 / TEST_SHKALA), Neytralnaya = true, Name = "polosa-testa" };
            podskazki.SetToolTip(ur, "Шкала — от 0 до 50 МБ/с, одна для обеих строк.");
            kar.Controls.Add(ur);
            var zn = Stolb(t.Item3 > 0 ? MBsIzMbit(t.Item3) : "—", cw - OTS - znW, y - 1, znW, true, ustarel ? "vt" : null);
            podskazki.SetToolTip(zn, t.Item3 > 0 ? (t.Item4 != "" ? "Отклик " + t.Item4 + " мс: через сколько сервер отвечает. Меньше — лучше." : MBsIzMbit(t.Item3))
                                                 : karOff ? "Через VPN не проверялось: " + KARING_NE + "." : "Через VPN тест не прошёл: сервер не отвечал.");
            kar.Controls.Add(zn);
            y += 28;
        }
        // вывод — только когда есть что сравнить (про выключенный Karing уже сказано вверху экрана)
        string vv = ustarel ? "Тест старше суток — запустите заново, чтобы узнать скорость сейчас" :
                    mv > 0 && mp > 0 ? (mv >= 50 && mp >= 50 ? "И через VPN, и напрямую быстро" : mv < 50 && mp >= 50 ? "VPN медленнее 6 МБ/с — стоит выбрать лучший сервер" :
                    mp < 50 && mv >= 50 ? "Напрямую медленно — похоже, тормозит провайдер или zapret" : "Медленно везде — похоже на провайдера или Wi-Fi")
                  : mv <= 0 && !karOff ? "Через VPN тест не прошёл: сервер не отвечал" : "";
        if (vv != "") { kar.Controls.Add(Abz(vv, OTS, y + 4, tw, "vt")); y += 4 + VysTeksta(vv, tw); }
        kar.Height = y + OTS;
        return kar;
    }

    // ---------- Программы ----------
    Dictionary<string, object> progData;
    readonly Dictionary<string, string> izmeneniya = new Dictionary<string, string>();
    System.Windows.Forms.Timer marshrutTaymer;
    bool progIdet;

    void ZagruzitProgrammy()
    {
        if (progIdet) return;
        progIdet = true;
        ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("programmy"); try { BeginInvoke((Action)(() => { progIdet = false; progData = d; if (tekushchaya == "programmy") Perestroit(); ObnovitSkorosti(); })); } catch { progIdet = false; } });
    }

    const string KAK_IDET = "Авто — заблокированное в России идёт через VPN, остальное напрямую.\nVPN — всё через VPN. Напрямую — всё без VPN. Без сети — программа не выйдет в интернет.\n" +
                            "Выбор применяется сам через пару секунд: Karing перезапустится, интернет моргнёт один раз.";

    class KolProg { public int Imya, ImyaW, Skor, SkorW, Vybor, VyborW; public bool Dve; }
    static string[] PunktyVybora(string m)
    {
        return new[] { "Авто", m != "" && m != "avto" && m != "ubrat" && m != "vpn" && m != "direct" && m != "block" ? "VPN: " + Pult.Chisto(m) : "VPN", "Напрямую", "Без сети" };
    }

    KolProg KolonkiProgramm(int W, IEnumerable<string> marshruty)
    {
        int vybW = Math.Max(300, marshruty.Select(m => Segmenty.Nuzhno(PunktyVybora(m))).DefaultIfEmpty(0).Max());
        // столбцы — вплотную: имя (до 380 px), сразу за ним скорость, за ней выбор; пустое место — справа, а не между столбцами
        if (W >= 860) { int imW = Math.Min(380, W - 46 - 16 - 100 - 28 - vybW - OTS), xS = 46 + imW + 16, xV = xS + 100 + 28; return new KolProg { Imya = 46, ImyaW = imW, Skor = xS, SkorW = 100, Vybor = xV, VyborW = vybW }; }
        return new KolProg { Imya = 46, ImyaW = W - 46 - OTS - 110, Skor = W - OTS - 100, SkorW = 100, Vybor = 46, VyborW = Math.Min(vybW, W - 46 - OTS), Dve = true };
    }

    // строка заголовков столбцов (только широкое окно)
    int ShapkaStolbcov(Kartochka kar, KolProg kl, int y)
    {
        if (kl.Dve) return y;
        kar.Controls.Add(Abz("Программа", kl.Imya, y, 140, "vt", F_MELK));
        kar.Controls.Add(Stolb("Скорость", kl.Skor, y - 2, kl.SkorW, true, "vt", F_MELK));
        kar.Controls.Add(Abz("Как идёт", kl.Vybor, y, 120, "vt", F_MELK));
        kar.Controls.Add(Vopros(KAK_IDET, kl.Vybor + TextW("Как идёт", F_MELK) + 2, y));
        return y + 26;
    }

    Panel StrProgrammy()
    {
        var k = Kolonka(); int W = Shir;
        var dob = KnZ("Добавить программу", Zn.Dobavit, DobavitProgrammu);
        podskazki.SetToolTip(dob, "Программы нет в списке, потому что она сейчас не в сети? Впишите её имя — и выберите, как ей ходить.");
        k.Controls.Add(Shapka("Программы", "Как каждая программа выходит в интернет", dob));
        if (progData == null) { k.Controls.Add(KartaZagruzki(W, "Смотрю, какие программы в сети")); ZagruzitProgrammy(); return k; }
        if (Pult.Sboy(progData))
        {
            k.Controls.Add(KartaOshibki(W, "Не получилось узнать программы", Pult.Info(progData), () => { progData = null; Perestroit(); }));
            return k;
        }
        bool karOff = KaringVyklyuchen();
        if (karOff) k.Controls.Add(PolosaBedy(W, "kras", KARING_NE, "Выбор для программ заработает, когда Karing будет запущен", Kn("Запустить Karing", ZapustitKaring, true)));
        var vse = Pult.Slovari(progData, "programmy").Where(p => Pult.S(p, "exe") != "" && !Sluzhebnaya(Pult.S(p, "exe"))).ToList();
        Func<Dictionary<string, object>, string> ex = p => Pult.S(p, "exe");
        var pravila = vse.Where(p => (Pult.B(p, "pravilo") || izmeneniya.ContainsKey(ex(p))) && !(izmeneniya.ContainsKey(ex(p)) && izmeneniya[ex(p)] == "ubrat")).ToList();
        var ostalnye = vse.Where(p => !pravila.Contains(p) && Pult.B(p, "v_seti")).ToList();
        var kl = KolonkiProgramm(W, vse.Select(p => izmeneniya.ContainsKey(ex(p)) ? izmeneniya[ex(p)] : Pult.S(p, "marshrut")));

        int y;
        Kartochka kp = null;
        if (pravila.Count > 0)
        {
            kp = Karta(W, "Ваши правила", "Эти программы идут так, как вы выбрали. «Авто» — снова решает сторож", out y, "karta-pravila");
            y = ShapkaStolbcov(kp, kl, y);
            foreach (var p in pravila) y += StrokaProgrammy(kp, p, kl, y, karOff);
            kp.Height = y + 8;
            k.Controls.Add(kp);
        }

        var ko = Karta(W, "Сейчас в сети", "Программы, которые сейчас пользуются интернетом", out y, "karta-v-seti");
        if (ostalnye.Count == 0) { string t = "Других программ в сети сейчас нет"; ko.Controls.Add(Abz(t, OTS, y, W - 2 * OTS, "vt")); y += VysTeksta(t, W - 2 * OTS) + 6; }
        else { y = ShapkaStolbcov(ko, kl, y); foreach (var p in ostalnye) y += StrokaProgrammy(ko, p, kl, y, karOff); }
        ko.Height = y + 8;
        k.Controls.Add(ko);

        // своих правил нет — одной строкой внизу, а не пустой карточкой сверху
        if (pravila.Count == 0)
        {
            var pusto = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = "karta-pravila" };
            string t = "Своих правил пока нет: выберите способ у любой программы — она станет правилом";
            pusto.Controls.Add(Zk(Zn.Info, OTS, 15, 11f));
            pusto.Controls.Add(Abz(t, OTS + 28, 14, W - 2 * OTS - 28, "vt"));
            pusto.Height = 14 + VysTeksta(t, W - 2 * OTS - 28) + 14;
            k.Controls.Add(pusto);
        }
        PokazatSkorosti();
        return k;
    }

    // Одна строка программы (столбцы одни и те же в обеих карточках); возвращает её высоту.
    int StrokaProgrammy(Kartochka kar, Dictionary<string, object> p, KolProg kl, int y, bool karOff)
    {
        string exe = Pult.S(p, "exe");
        bool cherezKaring = Pult.B(p, "karing"), vSeti = Pult.B(p, "v_seti");
        int rowH = kl.Dve ? 92 : 54;
        kar.Controls.Add(ZnachokProgrammy(exe, OTS, y + 16));
        kar.Controls.Add(ImyaN(Imya(exe), kl.Imya, y + 5, kl.ImyaW, null, null, exe));
        var kuda = ImyaN(KudaTekstN(Pult.S(p, "seychas")), kl.Imya, y + 27, kl.ImyaW, "vt");
        kar.Controls.Add(kuda);
        var skor = Stolb(vSeti || cherezKaring ? "—" : "", kl.Skor, kl.Dve ? y + 5 : y + 15, kl.SkorW, true, "vt");
        kar.Controls.Add(skor);
        if (cherezKaring) zhivye[exe] = Tuple.Create(skor, (Uroven)null, (Nadpis)kuda);
        else if (vSeti) podskazki.SetToolTip(skor, "Программа выходит в интернет не через Karing — её скорость отдельно не видна.");
        int yV = kl.Dve ? y + 52 : y + 11;

        bool claude = exe.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) && Pult.S(sost, "claude") != "" && Pult.S(sost, "claude") != "как всё остальное";
        if (claude)
        {
            string t = "Всегда через сервер " + Pult.Chisto(Pult.S(sost, "claude"));
            kar.Controls.Add(Abz(t, kl.Vybor, yV + 6, kl.VyborW, "vt"));
            return rowH;
        }
        if (vSeti && !cherezKaring && !karOff && !Pult.B(p, "pravilo"))
        {
            // «мимо Karing» уже сказано под именем — здесь только следствие
            string t = "Выбор не действует";
            kar.Controls.Add(Abz(t, kl.Vybor, yV + 6, kl.VyborW - 26, "vt"));
            kar.Controls.Add(Vopros("Программа (часто игры и загрузки Steam) выходит в интернет сама, в обход Karing, — выбор на неё не подействует.", kl.Vybor + TextW(t, F_TEKST) + 2, yV + 8));
            return rowH;
        }
        // выбор есть и у правил программ, которых сейчас нет в сети: их правило можно сменить («Авто» — убрать)
        string m = izmeneniya.ContainsKey(exe) ? izmeneniya[exe] : Pult.S(p, "marshrut");
        if (m == "" || m == "ubrat") m = "avto";
        var seg = new Segmenty { Punkty = PunktyVybora(m), Location = new Point(kl.Vybor, yV), Size = new Size(kl.VyborW, 32), Enabled = !karOff };
        seg.Vybran = m == "avto" ? 0 : m == "direct" ? 2 : m == "block" ? 3 : 1;
        string tek = m;
        seg.Smena += i => { izmeneniya[exe] = i == 0 ? "avto" : i == 1 ? (tek != "avto" && tek != "direct" && tek != "block" ? tek : "vpn") : i == 2 ? "direct" : "block"; ZaplanirovatMarshruty(); };
        if (karOff) podskazki.SetToolTip(seg, KARING_NE + " — выбор заработает, когда он будет запущен.");
        kar.Controls.Add(seg);
        return rowH;
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
        k.Controls.Add(Shapka("Сайты", "Заблокированные в России — через VPN, остальные — напрямую. Списки обновляются сами", null));
        bool karOff = KaringVyklyuchen();
        // беда про Karing — так же, как на остальных экранах: одна полоса сверху с кнопкой
        if (karOff) k.Controls.Add(PolosaBedy(W, "kras", KARING_NE, "Сайты, которым нужен VPN, откроются, когда Karing будет запущен", Kn("Запустить Karing", ZapustitKaring, true)));

        int y;
        var kar = Karta(W, "Проверить сайт", "Сайт не открывается или тормозит? Сторож откроет его напрямую и через VPN и подскажет, как лучше", out y, "karta-proverki");
        int poleW = Math.Min(520, W - 2 * OTS - KN_W - 12);
        var pole = new Pole { Location = new Point(OTS, y), Size = new Size(poleW, KN_H), Primer = "Адрес сайта, например youtube.com", Name = "pole-ramka" };
        pole.T.Name = "pole-sayt"; pole.T.Text = saytPole; pole.T.Font = F_TEKST;
        pole.T.TextChanged += (s, e) => saytPole = pole.T.Text;
        var kn = KnZ(saytIdet ? "Проверяю сайт" : "Проверить сайт", Zn.Poisk, ProveritSayt);
        kn.Enabled = !saytIdet;
        kn.Location = new Point(OTS + poleW + 12, y); kn.Margin = Padding.Empty;
        podskazki.SetToolTip(kn, "До 25 секунд. Сторож ничего не меняет — только проверяет.");
        pole.T.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; kn.PerformClick(); } };
        kar.Controls.Add(pole); kar.Controls.Add(kn);
        y += KN_H + 18;
        if (saytProverka != null && Pult.Sboy(saytProverka))
        {
            string t = "Не получилось проверить сайт: " + (Syroe(Pult.Info(saytProverka)) ? "сторож ответил ошибкой" : Chel.Tekst(Pult.Info(saytProverka)).ToLowerInvariant());
            kar.Controls.Add(Abz(t, OTS, y, W - 2 * OTS, "vt"));
            y += VysTeksta(t, W - 2 * OTS) + OTS;
        }
        else if (saytProverka != null)
        {
            var dve = new[] { Spisok(Pult.G(saytProverka, "napryamuyu")), Spisok(Pult.G(saytProverka, "vpn")) };
            string srv = Pult.Chisto(Pult.S(saytProverka, "server")).Trim();
            string dom = Pult.S(saytProverka, "domen");
            bool netNapr = false, okNapr = false;
            // итог проверки — двумя строками списка (без рамок внутри карточки): путь, значок, словами
            for (int i = 0; i < 2; i++)
            {
                string oc = Convert.ToString(Pult.El(dve[i], 2) ?? "");
                if (oc == "") oc = "нет ответа";
                double t = Pult.Ch(Pult.El(dve[i], 1));
                bool ok = oc == "быстро", ne = oc == "не открывается" || oc == "нет ответа";
                if (i == 0) { netNapr = ne; okNapr = ok; }
                string sostR, stroka;
                if (i == 1 && karOff) { sostR = "sery"; stroka = "Не проверялся"; }
                else { sostR = ok ? "ok" : ne ? "kras" : "zhelt"; stroka = ok ? "Открывается за " + t.ToString("0.0") + " с" : ne ? "Не открывается" : Chel.Pervaya(oc) + ": " + t.ToString("0.0") + " с"; }
                kar.Controls.Add(ImyaN(i == 0 ? "Напрямую" : "Через VPN" + (srv != "" ? " (" + srv + ")" : ""), OTS, y + 1, 240));
                var zn = new ZnakSost { Sost = sostR, Location = new Point(OTS + 250, y + 1), Size = new Size(20, 20) };
                var st = Abz(stroka, OTS + 278, y + 1, W - OTS * 2 - 278);
                if (i == 1 && karOff) { podskazki.SetToolTip(zn, KARING_NE + "."); podskazki.SetToolTip(st, KARING_NE + "."); }
                kar.Controls.Add(zn); kar.Controls.Add(st);
                y += 30;
            }
            y += 8;
            string dey = Pult.S(saytProverka, "deystvie"), sovet = Pult.S(saytProverka, "sovet");
            string seych = Pult.B(saytProverka, "uzhe_vpn") ? "через VPN" : Pult.B(saytProverka, "uzhe_napr") ? "напрямую" : "как решит сторож";
            string domT = dom != "" ? dom : "Сайт";
            if (karOff)
            {
                // вывод со смыслом; про Karing и кнопку — уже полоса вверху экрана
                string zagV = netNapr ? domT + " напрямую не открывается — ему нужен VPN"
                            : okNapr ? domT + " открывается напрямую — VPN для него не нужен" : domT + " напрямую открывается медленно — через VPN может быть быстрее";
                y = StrokaDeystviyaF(kar, W, y, zagV, null, null, F_ZHIR);
            }
            else
            {
                bool estDey = (dey == "vpn" || dey == "direct") && dom != "";
                Knopa kd = null, ko = null;
                if (estDey)
                {
                    kd = Kn(dey == "vpn" ? "Пустить через VPN" : "Пустить напрямую", () => Deystvie((dey == "vpn" ? "Пускаю через VPN: " : "Пускаю напрямую: ") + dom, new[] { "sayt", dom, dey }, d => { saytyIst = null; saytProverka = null; }), true);
                    ko = Kn("Оставить как есть", () => { saytProverka = null; Perestroit(); });
                    podskazki.SetToolTip(kd, "Karing перезапустится — интернет моргнёт на пару секунд.");
                }
                y = StrokaDeystviyaF(kar, W, y, domT + " сейчас идёт " + seych, Chel.Pervaya(sovet),
                    seych == "как решит сторож" ? "Если сайт в списке заблокированных в России — через VPN, иначе напрямую." : null, F_ZHIR, kd, ko);
            }
        }
        kar.Height = y;
        k.Controls.Add(kar);

        // история: что сторож поменял сам
        if (saytyIst == null)
        {
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("sayty-istoriya"); try { BeginInvoke((Action)(() => { saytyIst = d; if (tekushchaya == "sayty") Perestroit(); })); } catch { } });
            return k;
        }
        if (Pult.Sboy(saytyIst))
        {
            k.Controls.Add(KartaOshibki(W, "Не получилось узнать, что сторож поменял сам", Pult.Info(saytyIst), () => { saytyIst = null; Perestroit(); }));
            return k;
        }
        var ist = Pult.Slovari(saytyIst, "sayty").Where(s => Pult.S(s, "domen") != "").Take(30).ToList();
        var ki = Karta(W, "Что сторож поменял сам", "Сайты, которые сторож сам пустил другим путём, потому что они не открывались", out y, "karta-istoriya");
        if (ist.Count == 0) { string t = "Пока сторож ничего не менял"; ki.Controls.Add(Abz(t, OTS, y, W - 2 * OTS, "vt")); y += VysTeksta(t, W - 2 * OTS) + OTS; }
        else
        {
            // столбцы вплотную: сайт — как идёт и почему (одним столбцом) — когда — «Отменить» видна в каждой строке
            int kogdaW = 96, otmW = 110, domW = Math.Min(260, (W - 2 * OTS) / 3);
            int kakW = Math.Min(330, W - 2 * OTS - domW - kogdaW - otmW - 48);
            if (kakW < 150) { domW = Math.Max(120, domW - (150 - kakW)); kakW = Math.Min(330, W - 2 * OTS - domW - kogdaW - otmW - 48); }
            int xKak = OTS + domW + 16, xKogda = xKak + kakW + 16, xOtm = xKogda + kogdaW + 16;
            ki.Controls.Add(Abz("Сайт", OTS, y, 120, "vt", F_MELK));
            ki.Controls.Add(Abz("Как идёт и почему", xKak, y, kakW, "vt", F_MELK));
            ki.Controls.Add(Stolb("Когда", xKogda, y - 2, kogdaW, false, "vt", F_MELK));
            y += 24;
            foreach (var s in ist)
            {
                bool vpn = Pult.S(s, "vid") == "vpn";
                string dom = Pult.S(s, "domen");
                string prich = vpn ? "напрямую не открывался" : "через VPN не открывался";
                ki.Controls.Add(ImyaN(dom, OTS, y + 8, domW));
                var m = Metka(vpn ? "Через VPN" : "Напрямую", null, xKak, y + 6);
                ki.Controls.Add(m);
                ki.Controls.Add(ImyaN(prich, xKak + m.Width + 8, y + 8, kakW - m.Width - 8, "vt"));
                ki.Controls.Add(Stolb(Korotko(Pult.S(s, "kogda")), xKogda, y + 6, kogdaW, false, "vt"));
                var kv = Kn("Отменить", () => { if (Sprosit("Отменить правку для " + dom + "? Сайт снова пойдёт так, как шёл до неё. Karing перезапустится — интернет моргнёт на пару секунд.")) Deystvie("Отмена правки " + dom, new[] { "vernut", dom }, d => saytyIst = null); }, false, otmW);
                kv.Legkaya = true; kv.Height = 30; kv.Location = new Point(xOtm, y + 4); kv.Margin = Padding.Empty;
                kv.Enabled = !karOff;
                podskazki.SetToolTip(kv, karOff ? "Отменить правку можно, когда Karing (VPN) будет запущен." : "Сайт снова пойдёт как до правки. Karing перезапустится — интернет моргнёт на пару секунд.");
                ki.Controls.Add(kv);
                y += 40;
            }
            y += 8;
        }
        ki.Height = y;
        k.Controls.Add(ki);
        return k;
    }

    // ---------- Серверы ----------
    Dictionary<string, object> servData;
    bool servIdet;

    // ---------- состояние сервера — из чисел: три состояния, одно слово и один цвет у каждого (правило — в «?» у шапки «Серверов») ----------
    const string LEGENDA_SERVEROV = "Состояние — по обрывам за последние сутки и последнему замеру:\n" +
        "Хороший — не больше 2 обрывов за сутки и меньше 5 минут без ответа.\n" +
        "С перебоями — от 3 обрывов за сутки или от 5 минут без ответа.\n" +
        "Не работает — почти не отвечает.\n" +
        "Пометки: «Медленный» — медленнее 6 МБ/с, для фильмов и игр маловато; «Адрес меняется» — во время замера сервер выходил " +
        "в интернет с разных адресов: сайты видят то одну, то другую страну и могут выкидывать из аккаунтов. Сторож такие сам не выбирает.\n" +
        "Отклик — через сколько сервер отвечает: быстрый — до 80 мс, средний — до 150 мс, медленный — дольше. Меньше — лучше для игр и звонков.";

    static string SlovoObryvov(int obr, int minut) { return obr >= 3 || minut >= 5 ? "С перебоями" : null; }
    static string OtklikSlovami(int ping) { return ping < 80 ? "Быстрый отклик" : ping < 150 ? "Средний отклик" : "Медленный отклик"; }

    class MetkaS { public string Slovo, Sost; public List<string> Prichiny = new List<string>(), Pometki = new List<string>(); }
    static MetkaS MetkaServera(double skMbit, int ping, int adr, int obr, int minut, bool neOtvechaet)
    {
        var m = new MetkaS { Slovo = "Хороший", Sost = "ok" };
        if (neOtvechaet || skMbit < 5) { m.Slovo = "Не работает"; m.Sost = "kras"; m.Prichiny.Add(skMbit < 5 ? "Скорость почти ноль" : "Сейчас не отвечает"); return m; }
        if (SlovoObryvov(obr, minut) != null) { m.Slovo = "С перебоями"; m.Sost = "zhelt"; m.Prichiny.Add("За сутки обрывался " + Mnozh(obr, "раз", "раза", "раз") + ", без ответа " + minut + " мин"); }
        if (adr > 1) m.Pometki.Add("Адрес меняется");
        if (skMbit < 50) { m.Pometki.Add("Медленный"); m.Prichiny.Add("Скорость " + MBsIzMbit(skMbit) + " — для фильмов и игр маловато"); }
        return m;
    }

    Panel StrServery()
    {
        var k = Kolonka(); int W = Shir;
        bool karOff = KaringVyklyuchen();
        if (servData == null || Pult.Sboy(servData))
        {
            k.Controls.Add(Shapka("Серверы", "Через какой сервер VPN идут сайты и программы", null));
            if (servData == null)
            {
                k.Controls.Add(KartaZagruzki(W, "Загружаю серверы"));
                if (!servIdet)
                {
                    servIdet = true;
                    ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("servery"); try { BeginInvoke((Action)(() => { servIdet = false; servData = d; if (tekushchaya == "servery") Perestroit(); })); } catch { servIdet = false; } });
                }
            }
            else k.Controls.Add(KartaOshibki(W, "Не получилось узнать серверы", Pult.Info(servData), () => { servData = null; Perestroit(); }));
            return k;
        }
        var zamer = Pult.D(Pult.G(servData, "zamer"));
        var rez = Spiski(zamer, "results").Where(r => Pult.El(r, 0) is string && (string)r[0] != "").ToList();
        string kogda = Pult.S(zamer, "when");
        var zamerKn = KnZ("Выбрать лучший сервер", Zn.Skorost, NaytiLuchshiy, rez.Count == 0 && !karOff);
        zamerKn.Enabled = !karOff;
        podskazki.SetToolTip(zamerKn, karOff ? "Сначала запустите Karing (VPN): без него серверы не замерить." : "Сторож замерит скорость всех серверов VPN и подключит самый быстрый. 2–5 минут, интернет пару раз моргнёт.");
        string podS = (kogda != "" ? "Последний замер " + Kogda(kogda) : "Замера ещё не было") + (karOff ? "" : " · новый займёт 2–5 минут");
        k.Controls.Add(Shapka("Серверы", podS, zamerKn, null, null, rez.Count > 0 ? LEGENDA_SERVEROV : null));
        if (karOff) k.Controls.Add(PolosaBedy(W, "kras", KARING_NE, rez.Count == 0 ? "Серверы вашей подписки появятся, когда Karing заработает" : "Замерить и подключить сервер можно, когда Karing заработает", Kn("Запустить Karing", ZapustitKaring, true)));
        string seychas = Pult.S(servData, "seychas"), claude = Pult.S(servData, "claude");
        if (rez.Count == 0)
        {
            if (karOff) return k;
            var imena = Pult.L(servData, "servery").OfType<string>().Where(x => x != "").Select(Pult.Chisto).ToList();
            int y;
            var kar = Karta(W, "Серверы ещё не замерялись", imena.Count > 0 ? "В подписке: " + string.Join(", ", imena) : "Серверов пока не видно — проверьте, что в Karing добавлена подписка VPN", out y, "karta-net-zamera");
            kar.Height = y;
            k.Controls.Add(kar);
            return k;
        }
        var podrobno = Pult.D(Pult.G(servData, "podrobno"));
        int kol = W < 820 ? 2 : 3, cw = (W - PROM * (kol - 1)) / kol;
        // сетка карточек — ровно в ширину колонки (без запаса справа), места считаются сами
        int strok = (rez.Count + kol - 1) / kol;
        var setka = new Panel { Width = W, Height = strok * 152 + (strok - 1) * PROM, Margin = new Padding(0, 0, 0, PROM), Name = "setka-serverov" };
        int nomer = 0;
        foreach (var r in rez)
        {
            string imya = (string)r[0], imyaCh = Pult.Chisto(imya);
            int sk = Pult.Cel(Pult.El(r, 1)), ping = Pult.Cel(Pult.El(r, 2)), adr = Pult.Cel(Pult.El(r, 3));
            bool tek = imya == seychas, kl = imya == claude;
            var pod = Pult.D(Pult.G(podrobno, imya));
            // обрывы — из той же функции пульта, что и на Мониторе (stat.nadezhnost), запасной путь — podrobno
            int obr, minut;
            var ob = ObryvyServera(imya);
            if (ob != null) { obr = ob[0]; minut = ob[1]; }
            else if (Pult.G(pod, "obryvov") != null) { obr = Pult.Cel(Pult.G(pod, "obryvov")); minut = Pult.Cel(Pult.G(pod, "minut")); }
            else { obr = 0; minut = 0; }
            pokazanoObryvov["servery:" + imyaCh] = obr;
            var mt = MetkaServera(sk, ping, adr, obr, minut, Pult.S(pod, "slovo") == "Не работает" && sk >= 5);
            bool mertv = mt.Slovo == "Не работает";

            var kar = new Kartochka { Width = cw, Height = 152, Location = new Point(nomer % kol * (cw + PROM), nomer / kol * (152 + PROM)), Margin = Padding.Empty, Name = tek ? "server-seychas" : "server", Obvodka = tek ? Ris.AkcentV(pal) : (Color?)null };
            nomer++;
            string metka = tek ? "Сейчас" : kl ? "Для Claude" : "";
            int metkaW = metka != "" ? TextW(metka, F_TEKST) + 16 : 0;
            kar.Controls.Add(ImyaN(imyaCh, OTS, 14, cw - 2 * OTS - (metkaW > 0 ? metkaW + 8 : 0), null, F_ZAG, imyaCh + (imya.Contains("➗") ? "\nВ Karing этот сервер помечен значком ➗" : "")));
            if (metka != "") kar.Controls.Add(Metka(metka, tek ? "akc" : null, cw - OTS - metkaW, 13));
            var rz = Razbit(MBsIzMbit(sk));
            kar.Controls.Add(new Chislo { Location = new Point(OTS, 44), Size = new Size(cw - 2 * OTS, 34), Znach = rz[0], Ed = rz[1] });
            // отклик — словами (число — в подсказке); у неработающего сервера отклика нет
            string obrT = obr == 0 ? "Без обрывов за сутки" : Mnozh(obr, "обрыв", "обрыва", "обрывов") + " за сутки";
            string fakty = mertv ? obrT : OtklikSlovami(ping) + " · " + obrT.Substring(0, 1).ToLower() + obrT.Substring(1);
            // одна строка: на узкой карточке не влезет — целиком в подсказке
            var fk = ImyaN(fakty, OTS, 82, cw - 2 * OTS, "vt");
            podskazki.SetToolTip(fk, fakty + (mertv ? "" : "\nОтклик " + ping + " мс — через сколько миллисекунд сервер отвечает. Меньше — лучше: в играх и звонках меньше задержка."));
            kar.Controls.Add(fk);
            int mx = OTS;
            var mm = Metka(mt.Slovo, mt.Sost, mx, 112);
            podskazki.SetToolTip(mm, mt.Prichiny.Count > 0 ? string.Join("\n", mt.Prichiny) : "Не больше 2 обрывов за сутки, отвечает быстро.");
            kar.Controls.Add(mm); mx += mm.Width + 6;
            foreach (var pm in mt.Pometki)
            {
                // пометки — нейтральные (это не состояние, а особенность сервера)
                var p = Metka(pm, null, mx, 112);
                podskazki.SetToolTip(p, pm == "Адрес меняется" ? "Во время замера сервер выходил в интернет с разных адресов: сайты видят то одну, то другую страну и могут выкидывать из аккаунтов. Сторож такие сам не выбирает."
                                                              : "Медленнее 6 МБ/с — для фильмов и игр маловато.");
                if (mx + p.Width <= cw - OTS - 128) { kar.Controls.Add(p); mx += p.Width + 6; }
            }
            if (!tek)
            {
                var kp = Kn("Подключить", () =>
                {
                    if (KaringVyklyuchen()) { zanyatoStroka.Text = "Сначала запустите Karing: без него сервер не подключить."; return; }
                    if (Sprosit("Пустить сайты и программы через сервер " + imyaCh + "? Karing перезапустится, интернет моргнёт на пару секунд.")) Deystvie("Подключение сервера " + imyaCh, new[] { "server", imya }, d => servData = null);
                }, false, 120);
                kp.Height = 32; kp.Location = new Point(cw - OTS - 120, 108); kp.Margin = Padding.Empty;
                kp.Enabled = !mertv && !karOff;
                podskazki.SetToolTip(kp, mertv ? "Сервер почти не отвечает — подключать его нет смысла." : "Пустить сайты и программы через этот сервер. Karing перезапустится — интернет моргнёт на пару секунд.");
                kar.Controls.Add(kp);
            }
            setka.Controls.Add(kar);
        }
        k.Controls.Add(setka);
        return k;
    }

    // ---------- YouTube без VPN (zapret) ----------
    Dictionary<string, object> zapData;
    string skriptLog; long skriptLogStart; TextBox skriptPole;   // журнал скрипта zapret (для старого сторожа, который не пишет ход)
    bool zapIdet;

    Panel StrZapret()
    {
        var k = Kolonka(); int W = Shir;
        bool rab = Pult.B(sost, "zapret");
        string strat = Pult.Chisto(Pult.S(sost, "strategiya")).Trim();
        bool zapOk = zapData != null && !Pult.Sboy(zapData) && Pult.G(zapData, "youtube") != null;
        string ver = Pult.S(zapData, "versiya"), posl = Pult.S(zapData, "posledn");
        // не установлен: папки нет в «Загрузках», служба не работает и настройки обхода в реестре нет
        bool netZ = zapOk && ver == "" && !rab && strat == "";
        bool estNovee = zapOk && posl != "" && ver != "" && Sravnit(posl, ver) > 0;
        bool neNuzhen = Pult.B(Pult.D(Pult.G(sost, "nastroyki")), "zapret_ne_nuzhen");

        var kp = KnZ("Проверить YouTube и Discord", Zn.Obnovit, () => { zapData = null; Perestroit(); });
        kp.Enabled = !netZ;
        podskazki.SetToolTip(kp, netZ ? "Сначала установите zapret." : "Сторож проверит, открывается ли YouTube без VPN и как идёт голос в Discord. Ничего не меняет.");
        k.Controls.Add(Shapka("YouTube и Discord без VPN", "Программа zapret открывает YouTube и голос Discord без VPN", kp));

        if (zapData == null)
        {
            k.Controls.Add(KartaZagruzki(W, suho ? "Проверка zapret не запускалась" : "Проверяю YouTube и голос в Discord"));
            if (!suho && !zapIdet)
            {
                zapIdet = true;
                ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("zapret"); try { BeginInvoke((Action)(() => { zapIdet = false; zapData = d; if (tekushchaya == "zapret") Perestroit(); })); } catch { zapIdet = false; } });
            }
            return k;
        }
        if (!zapOk)
        {
            k.Controls.Add(KartaOshibki(W, "Не получилось проверить zapret", Pult.Info(zapData), () => { zapData = null; Perestroit(); }));
            return k;
        }
        if (netZ)
        {
            // не обязательно — поэтому и кнопка не главная (синей рядом с «не обязательно» не бывает)
            var kn = Kn("Установить zapret", UstanovitZapret);
            podskazki.SetToolTip(kn, "Сторож скачает zapret с GitHub (около 4 МБ) в «Загрузки» и включит — он будет работать сам и после перезагрузки. Windows спросит разрешение — нажмите «Да».");
            var kNe = neNuzhen ? null : Kn("Не нужно", ZapretNeNuzhen);
            if (kNe != null) podskazki.SetToolTip(kNe, "Больше не напоминать: YouTube и голос Discord будут идти через VPN.");
            string bezNego = KaringVyklyuchen() ? "Без него YouTube и голос Discord пойдут через VPN, когда Karing (VPN) будет запущен"
                           : neNuzhen ? "Вы отметили, что он не нужен: YouTube и голос Discord идут через VPN" : "Без него YouTube и голос Discord идут через VPN — так тоже работает";
            k.Controls.Add(PolosaBedy(W, "sery", "zapret не установлен", bezNego, kn, kNe));
            DobavitZhurnalSkripta(k, W);
            return k;
        }

        // состояние — двумя плитками: YouTube и голос в Discord; цифры — в подсказке
        var yt = Spisok(Pult.G(zapData, "youtube"));
        bool ytOk = Pult.El(yt, 2) is bool && (bool)Pult.El(yt, 2);
        var pYt = new Plit { Id = "zap-youtube", Zag = "YouTube", Sost = ytOk ? "ok" : "kras",
            Stroka = ytOk ? "Открывается без VPN" : rab ? "Не открывается без VPN" : "Не открывается: zapret не работает",
            Podskazka = ytOk ? "Открылся за " + Pult.Ch(Pult.El(yt, 1)).ToString("0.00") + " с." : null };
        var g = Pult.D(Pult.G(zapData, "golos"));
        var pGol = new Plit { Id = "zap-golos", Zag = "Голос в Discord" };
        string golosSost = "sery";
        if (Pult.B(g, "est"))
        {
            double pot = Pult.Ch(Pult.G(g, "poteri"));
            golosSost = pot < 2 ? "ok" : pot < 8 ? "zhelt" : "kras";
            pGol.Sost = golosSost;
            pGol.Stroka = pot < 2 ? "Голос идёт чисто" : pot < 8 ? "Голос иногда заикается" : "Голос прерывается";
            pGol.Podskazka = "За последние 2 минуты теряется " + pot.ToString("0.#") + " % голоса" + (Pult.S(g, "ping") != "" ? ", отклик " + Pult.S(g, "ping") + " мс" : "") + ".";
        }
        else { pGol.Sost = "sery"; pGol.Stroka = "Вы не в звонке"; pGol.Podskazka = "Зайдите в голосовой канал Discord и нажмите «Проверить YouTube и Discord»."; }
        // голос прерывается — так и сказано словами, цифра — в подсказке
        k.Controls.Add(RyadPlitok(W, new List<Plit> { pYt, pGol }, "plitki-zapret"));

        // что можно сделать: польза — строкой, подробности и цена — в «?»; главная кнопка — только если есть беда
        bool nadoPodobrat = !ytOk || golosSost == "kras" || !rab;
        int y;
        var dey = Karta(W, "Что можно сделать", null, out y, "karta-zapret", null);
        var k1 = Kn("Подобрать настройку", PodobratZapret, nadoPodobrat);
        y = StrokaDeystviya(dey, W, y, "Надёжная настройка обхода", "Чтобы YouTube и голос Discord без VPN не прерывались. Около 10 минут",
            "Сторож переберёт все настройки обхода в zapret и поставит ту, при которой ничего не обрывается." +
            (strat != "" ? "\nСейчас стоит «" + strat + "»." : "") + "\nПока идёт подбор, YouTube и Discord без VPN рвутся.", k1);
        var k2 = Kn("Ускорить YouTube", PodobratYoutube);
        y = StrokaDeystviya(dey, W, y, "Быстрый YouTube", "Чтобы видео на YouTube без VPN грузилось быстрее. Несколько минут",
            "Из надёжных настроек сторож выберет ту, при которой YouTube без VPN грузится быстрее всего.\nПока идёт подбор, видео без VPN прерывается.", k2);
        if (ver != "")
        {
            bool obnovit = estNovee || posl == "";
            var k3 = obnovit ? Kn("Обновить zapret", ObnovitZapret) : null;
            y = StrokaDeystviya(dey, W, y, estNovee ? "Новая версия zapret: " + posl : posl == "" ? "Версия zapret " + ver : "Версия zapret " + ver + " — последняя",
                estNovee ? "Сейчас " + ver + ". Около 4 МБ с GitHub, ваши списки сайтов сохранятся" : posl == "" ? "Новую версию на GitHub узнать не удалось" : "Обновлять не нужно",
                obnovit ? "Сторож скачает новую версию в «Загрузки», перенесёт ваши списки сайтов и включит с той же настройкой обхода.\nWindows спросит разрешение — нажмите «Да»." : null, k3);
        }
        dey.Height = y;
        k.Controls.Add(dey);
        DobavitZhurnalSkripta(k, W);
        return k;
    }

    // старый сторож не пишет ход — тогда видно хотя бы журнал скрипта
    void DobavitZhurnalSkripta(Panel k, int W)
    {
        if (skriptLog == null || (hod != null && hod.Fayl != null)) return;
        int y;
        var kar = Karta(W, "Ход скрипта", null, out y, "karta-skripta");
        skriptPole = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Location = new Point(OTS, y), Size = new Size(W - 2 * OTS, 184), Font = F_TEKST };
        kar.Controls.Add(skriptPole);
        ObnovitZhurnalSkripta();
        kar.Height = y + 184 + OTS;
        k.Controls.Add(kar);
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
            "Сторож переберёт все настройки обхода в zapret и поставит самую надёжную. Это около 10 минут, и всё это время YouTube, Discord и другие сайты без VPN будут обрываться. Начать?");
    }

    void PodobratYoutube()
    {
        ZapustitSkript("Ускорение YouTube без VPN", "zapret-youtube.ps1", "zapret-youtube.log",
            "Сторож переберёт настройки zapret и поставит ту, при которой YouTube без VPN грузится быстрее всего (из тех, что работают надёжно). Это несколько минут, и всё это время видео и сайты без VPN будут прерываться. Начать?");
    }

    // «Установить zapret» — одно действие и на плитке Монитора, и в разделе «YouTube без VPN»
    void UstanovitZapret()
    {
        ZapustitSkript("Установка zapret", "zapret-obnovit.ps1", "zapret-obnovit.log",
            "Сторож скачает zapret с GitHub (архив около 4 МБ) в папку «Загрузки» и включит его с настройкой обхода, которая подходит большинству. Он будет работать сам и после перезагрузки. Windows спросит разрешение — нажмите «Да». Начать?");
    }

    void ObnovitZapret()
    {
        ZapustitSkript("Обновление zapret", "zapret-obnovit.ps1", "zapret-obnovit.log",
            "Сторож проверит новую версию zapret на GitHub. Если она есть — скачает (около 4 МБ) в «Загрузки», перенесёт ваши списки сайтов и включит с той же настройкой обхода. Старая версия останется в «Загрузках» на всякий случай. Пока ставится, интернет без VPN может моргнуть. Windows спросит разрешение — нажмите «Да». Начать?");
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
        try { umeetHod = Regex.IsMatch(File.ReadAllText(put), @"\$Hod\b", RegexOptions.IgnoreCase); } catch { }
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
                              : new Dictionary<string, object> { { "ok", kodOk != false }, { "info", kodOk == false ? "Скрипт закончился с ошибкой — подробности в его журнале в папке storozh-seti" : "" } };
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

    class ZapisL { public string Vremya, Vid, Tekst, TekstOdin, Podskazka, Podskazka0; public bool Neudacha; public int Skolko = 1; public List<string> Vremena = new List<string>(); }

    // неудача — по смыслу строки, не только по метке пульта «НЕ ВЫШЛО.»
    static bool Neudacha(string t)
    {
        return Regex.IsMatch(t ?? "", @"не вышло|ошибк|не смог|не удалось|не получилось|не заработал|не подключился|не поднялся|\bупал[аи]?\b|не ответил|перезапуски не помогают|не помогли", RegexOptions.IgnoreCase)
               && !(t ?? "").StartsWith("Сторож исправил");
    }

    // «VPN не отвечал 19 мин — …» / «… 15 с …» / «… 1 ч 5 мин …» → секунды (null — не обрыв)
    static int? SekundObryva(string t)
    {
        var m = Regex.Match(t ?? "", @"^VPN не отвечал (?:(\d+) ч (\d+) мин|(\d+) мин|(\d+) с)");
        if (!m.Success) return null;
        if (m.Groups[1].Success) return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60;
        if (m.Groups[3].Success) return int.Parse(m.Groups[3].Value) * 60;
        return int.Parse(m.Groups[4].Value);
    }

    // записи сторожа: опрятный текст; однотипные «VPN не отвечал» подряд — одной строкой; одинаковые неудачи и
    // повторы «Сервер …, через который идёт Claude, не отвечает уже N мин» — одной строкой с числом раз; разовые — как есть
    List<ZapisL> ZapisiLenty()
    {
        var vse = Spiski(lentaData, "lenta").Where(e => e.Count >= 3).ToList();
        var itog = new List<ZapisL>();
        ZapisL gr = null; var grSek = new List<int>(); var grStroki = new List<string>(); string grPervoe = "";
        Action zakryt = () =>
        {
            if (gr == null) return;
            if (gr.Skolko > 1)
            {
                gr.Tekst = "VPN не отвечал " + Mnozh(gr.Skolko, "раз", "раза", "раз") + " с " + grPervoe + " — всего " + VremyaKr(grSek.Sum()) + ", дольше всего " + VremyaKr(grSek.Max());
                gr.Podskazka = string.Join("\n", grStroki.Take(30)) + (grStroki.Count > 30 ? "\n…и ещё " + (grStroki.Count - 30) : "");
            }
            itog.Add(gr); gr = null; grSek.Clear(); grStroki.Clear();
        };
        foreach (var e in vse)
        {
            string vr = Convert.ToString(e[0] ?? ""), vid = Convert.ToString(e[1] ?? ""), syr = Convert.ToString(e[2] ?? "");
            string t = Chel.Tekst(syr);
            var sek = vid == "obryv" ? SekundObryva(t) : null;
            if (sek != null)
            {
                if (gr == null) gr = new ZapisL { Vremya = vr, Vid = vid, Tekst = t };
                else gr.Skolko++;
                grSek.Add(sek.Value); grStroki.Add(vr + " — " + t); grPervoe = vr.Length >= 5 ? vr.Substring(vr.Length - 5) : vr;
                continue;
            }
            zakryt();
            bool neud = (e.Count > 3 && e[3] is bool && (bool)e[3]) || Neudacha(syr) || Neudacha(t);
            // повторяющиеся сообщения: одинаковая неудача — где бы ни была; «сервер Claude не отвечает уже N мин» — с любым N
            string klyuch = Regex.IsMatch(t, @"через который идёт Claude, не отвечает уже \d+ мин") ? Regex.Replace(t, @"\d+ мин", "N мин") : neud ? t : null;
            var pred = klyuch == null ? null : itog.FirstOrDefault(z => z.Podskazka0 == klyuch);
            if (pred != null)
            {
                pred.Skolko++;
                pred.Vremena.Add(vr);
                pred.Tekst = pred.TekstOdin + " Повторялось " + Mnozh(pred.Skolko, "раз", "раза", "раз") + ", первый раз — " + vr + ".";
                pred.Podskazka = "Когда: " + string.Join(", ", pred.Vremena);
                continue;
            }
            itog.Add(new ZapisL { Vremya = vr, Vid = vid, Tekst = t, TekstOdin = t.TrimEnd('.') + ".", Neudacha = neud, Podskazka0 = klyuch, Vremena = new List<string> { vr } });
        }
        zakryt();
        return itog;
    }

    Panel StrZhurnal()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Журнал", "Что сторож делал и что случалось с сетью, новые сверху", null));
        if (lentaData == null)
        {
            k.Controls.Add(KartaZagruzki(W, "Загружаю журнал"));
            ThreadPool.QueueUserWorkItem(_ => { var d = Pult.Vyzov("lenta"); try { BeginInvoke((Action)(() => { lentaData = d; if (tekushchaya == "zhurnal") Perestroit(); })); } catch { } });
            return k;
        }
        if (Pult.Sboy(lentaData))
        {
            k.Controls.Add(KartaOshibki(W, "Не получилось прочитать журнал сторожа", Pult.Info(lentaData), () => { lentaData = null; Perestroit(); }));
            return k;
        }
        var vse = ZapisiLenty();
        // разделы: пустые не показываем; «Неудачи» — всё, что не вышло, из любых записей
        var razd = new List<Tuple<string, string, int>>();
        foreach (var f in new[] { new[] { "", "Всё" }, new[] { "neud", "Неудачи" }, new[] { "obryv", "Обрывы" }, new[] { "ispr", "Исправлено" }, new[] { "zamer", "Замеры" }, new[] { "moi", "Ваши действия" } })
        {
            int n = f[0] == "" ? vse.Sum(z => z.Skolko) : f[0] == "neud" ? vse.Where(z => z.Neudacha).Sum(z => z.Skolko) : vse.Where(z => z.Vid == f[0]).Sum(z => z.Skolko);
            if (n > 0 || f[0] == "") razd.Add(Tuple.Create(f[0], f[1] + " · " + n, n));
        }
        if (!razd.Any(r => r.Item1 == lentaFiltr)) lentaFiltr = "";
        if (razd.Count > 1)
        {
            var punkty = razd.Select(r => r.Item2).ToArray();
            int sw = Math.Min(W, Segmenty.Nuzhno(punkty));
            var seg = new Segmenty { Punkty = punkty, Size = new Size(sw, 34), Margin = new Padding(0, 0, 0, PROM), Vybran = razd.FindIndex(r => r.Item1 == lentaFiltr), Name = "filtr-zhurnala" };
            seg.Smena += i => { lentaFiltr = razd[i].Item1; Perestroit(); };
            k.Controls.Add(seg);
        }
        var spis = vse.Where(z => lentaFiltr == "" || (lentaFiltr == "neud" ? z.Neudacha : z.Vid == lentaFiltr)).Take(60).ToList();
        var kar = new Kartochka { Width = W, Margin = new Padding(0, 0, 0, PROM), Name = "karta-zhurnala" };
        // строка не длиннее, чем удобно читать (≈ 860 px), значки — те же залитые кружки, что на Мониторе;
        // неудачи и обрывы — обычным цветом, рутина — серым (предупреждение не тише рутины)
        int y = 10, xVr = OTS, vrW = 100, xZn = xVr + vrW + 8, xT = xZn + 30, tw = Math.Min(W - xT - OTS, ZHURNAL_SHIR);
        foreach (var z in spis)
        {
            string sostZ = z.Neudacha ? "kras" : z.Vid == "obryv" ? "zhelt" : z.Vid == "ispr" || z.Vid == "moi" ? "ok" : "info";
            int th = VysTeksta(z.Tekst, tw);
            kar.Controls.Add(Stolb(z.Vremya, xVr, y + 6, vrW, false, "vt"));
            kar.Controls.Add(new ZnakSost { Sost = sostZ, Location = new Point(xZn, y + 7), Size = new Size(20, 20) });
            var n = Abz(z.Tekst, xT, y + 7, tw, z.Neudacha || z.Vid == "obryv" ? null : "vt");
            if (z.Podskazka != null) podskazki.SetToolTip(n, z.Podskazka);
            kar.Controls.Add(n);
            y += Math.Max(24, th) + 12;
        }
        if (spis.Count == 0)
        {
            string t = vse.Count == 0 ? "Пусто: сторож ещё ничего не делал. Записи появятся после первой проверки сети" : "В этом разделе пусто";
            kar.Controls.Add(Abz(t, OTS, 18, W - 2 * OTS, "vt")); y = 18 + VysTeksta(t, W - 2 * OTS) + 8;
        }
        kar.Height = y + 8;
        k.Controls.Add(kar);
        return k;
    }

    // ---------- Настройки ----------
    const int NASTR_W = 860;   // переключатель — рядом с подписью: карточки настроек не шире этого (на широком окне — две рядом)

    Panel StrNastroyki()
    {
        var k = Kolonka(); int W = Shir;
        k.Controls.Add(Shapka("Настройки", "Что сторож делает сам и когда он вам не мешает", null));
        if (sost == null) { k.Controls.Add(KartaZagruzki(W, "Загружаю настройки")); return k; }
        var nas = Pult.G(sost, "nastroyki") as Dictionary<string, object>;
        if (nas == null)
        {
            // без нынешних значений переключатели показали бы неправду — лучше честно сказать
            k.Controls.Add(KartaOshibki(W, "Не получилось прочитать настройки", Pult.Sboy(sost) ? Pult.Info(sost) : "Сторож не прислал настройки", () => { sost = null; Perestroit(); Obnovit(); }));
            return k;
        }
        var s = Svesti();
        // ширина та же, что у остальных экранов: на широком окне две группы рядом, переключатели — у правого края своей карточки
        bool ryadom = W >= 900;
        int cw = ryadom ? (W - PROM) / 2 : Math.Min(W, NASTR_W);
        int y;
        var g1 = Karta(cw, "Сторож сам", null, out y, "gruppa-sam");
        // главная настройка — такой же переключатель, как остальные. Включить — действие сторожа; выключить из окна нельзя
        // (у пульта нет такой команды): переключатель остаётся включённым, как выключить — в подсказке и строке внизу
        var tz = new Tumbler { Vkl = s.Zadaniya, Name = "tumbler-zadaniya" };
        tz.Smena += v =>
        {
            if (v) { VklyuchitZadaniya(); return; }
            tz.Vkl = true; tz.Invalidate();
            zanyatoStroka.Text = "Выключить проверку можно в «Планировщике заданий» Windows: задания «Storozh seti», «Storozh VPN» и «Nablyudatel migany».";
        };
        podskazki.SetToolTip(tz, s.Zadaniya ? "Включено. Выключить можно в «Планировщике заданий» Windows: задания «Storozh seti», «Storozh VPN» и «Nablyudatel migany»."
                                            : "Сторож будет сам проверять сеть каждые 10 минут, даже когда окно закрыто. Прав администратора не нужно.");
        y = StrokaNastroyki(g1, cw, y, "Проверять сеть сам каждые 10 минут", "Даже когда окно закрыто, и чинить, что сломано", tz);
        foreach (var p in new[] {
            new[] { "avto_server", "Выбирать лучший сервер сам", "Ночью замеряет все серверы и подключает самый быстрый из надёжных" },
            new[] { "avto_sayty", "Пускать через VPN сайты, которые не открываются", "Проверяет раз в час. Claude и YouTube не трогает" },
            new[] { "avto_spiski", "Обновлять списки заблокированных сайтов", "Раз в сутки: по ним сторож решает, что пускать через VPN" } })
            y = StrokaNastroyki(g1, cw, y, p[1], p[2], TumblerNastroyki(nas, p[0]));
        g1.Height = y;

        var g2 = Karta(cw, "Игры, звонки и уведомления", null, out y, "gruppa-ne-meshat");
        y = StrokaNastroyki(g2, cw, y, "Ждать конца игры, прежде чем перезапускать Karing (VPN)", "Правки подождут, пока игра закроется", TumblerNastroyki(nas, "ne_v_igre"));
        int tek; if (!int.TryParse(Pult.S(nas, "zvonok_min"), out tek)) tek = 20;
        var vars = new[] { 0, 10, 20, 60 };
        var punkty = vars.Select(v => v == 0 ? "Не ждать" : v + " мин").ToArray();
        var seg = new Segmenty { Punkty = punkty, Size = new Size(Math.Max(240, Segmenty.Nuzhno(punkty)), 32) };
        int idx = Array.IndexOf(vars, tek);
        seg.Vybran = idx >= 0 ? idx : Array.IndexOf(vars, vars.OrderBy(v => Math.Abs(v - tek)).First());
        seg.Smena += i => Sohranit("zvonok_min", vars[i]);
        y = StrokaNastroyki(g2, cw, y, "Во время звонка в Discord ждать не дольше", "Потом звонок прервётся секунд на 10", seg);
        var tzap = TumblerNastroyki(nas, "zapret_slezhka");
        if (!s.ZapEst) { tzap.Enabled = false; podskazki.SetToolTip(tzap, "Заработает, когда zapret будет установлен."); }
        y = StrokaNastroyki(g2, cw, y, "Сообщать, если zapret (YouTube и Discord без VPN) перестал работать", s.ZapEst ? "Уведомление Windows и запись в журнал" : "zapret не установлен — сообщать пока не о чем", tzap);
        g2.Height = y;

        if (ryadom)
        {
            var ryad = new Panel { Width = W, Height = Math.Max(g1.Height, g2.Height), Margin = new Padding(0, 0, 0, PROM), Name = "ryad-nastroek" };
            g1.Height = g2.Height = ryad.Height;
            g1.Margin = g2.Margin = Padding.Empty;
            g1.Location = new Point(0, 0); g2.Location = new Point(cw + PROM, 0);
            ryad.Controls.Add(g1); ryad.Controls.Add(g2);
            k.Controls.Add(ryad);
        }
        else { k.Controls.Add(g1); k.Controls.Add(g2); }
        return k;
    }

    Tumbler TumblerNastroyki(Dictionary<string, object> nas, string key)
    {
        var t = new Tumbler { Vkl = Pult.B(nas, key), Name = "tumbler-" + key };
        t.Smena += v => Sohranit(key, v);
        return t;
    }

    // строка настройки: подпись и пояснение слева, переключатель (или выбор) — сразу справа, в том же ряду
    int StrokaNastroyki(Kartochka kar, int W, int y, string zag, string opis, Control upr)
    {
        int tw = W - 2 * OTS - upr.Width - 24;
        kar.Controls.Add(Abz(zag, OTS, y, tw));
        int h = VysTeksta(zag, tw);
        kar.Controls.Add(Abz(opis, OTS, y + h + 2, tw, "vt"));
        h += 2 + VysTeksta(opis, tw);
        upr.Location = new Point(W - OTS - upr.Width, y + Math.Max(0, (h - upr.Height) / 2));
        kar.Controls.Add(upr);
        return y + Math.Max(h, upr.Height) + 18;
    }
}

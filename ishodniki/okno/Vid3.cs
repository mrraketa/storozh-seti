using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// Значок состояния (21.11, одно правило цвета на всё окно): галка в зелёном круге — хорошо, треугольник — есть что
// поправить, крестик в красном круге — не работает, прочерк в сером круге — нет данных или не установлено («?» — только у подсказок). Только знаки шрифта: в Segoe Fluent —
// круг/треугольник и знак поверх (как в «Безопасности Windows»), в Segoe MDL2 (Windows 10) — готовые цельные знаки.
class ZnakSost : Control
{
    public Palitra Pal = Palitra.Svoya();
    string sost = "sery";
    public string Sost { get { return sost; } set { sost = value ?? "sery"; AccessibleDescription = sost; Invalidate(); } }
    public ZnakSost() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; Size = new Size(22, 22); AccessibleDescription = sost; }
    public static Color Cvet(string s) { return s == "ok" ? Okno.Zel : s == "zhelt" ? Okno.Zhelt : s == "kras" ? Okno.Kras : Okno.Sery; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle(0, 0, Width, Height);
        const TextFormatFlags F = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        Color c = Cvet(sost);
        using (var f = Zn.Shrift(Height * 0.62f))
        {
            if (Zn.Fluent)
            {
                // форма — цветом состояния, знак — белым (на жёлтом — тёмным, белый там не читается)
                string forma = sost == "zhelt" ? "" : "";
                string znak = sost == "ok" ? "" : sost == "zhelt" ? "" : sost == "kras" ? "" : sost == "info" ? "" : "";
                TextRenderer.DrawText(g, forma, f, r, c, F);
                TextRenderer.DrawText(g, znak, f, sost == "zhelt" ? new Rectangle(0, 1, Width, Height) : r, sost == "zhelt" ? Color.FromArgb(40, 32, 10) : Color.White, F);
            }
            else
                TextRenderer.DrawText(g, sost == "ok" ? "" : sost == "zhelt" ? "" : sost == "kras" ? "" : sost == "info" ? "" : "", f, r, c, F);
        }
    }
}

// Крупное число и единица на одной линии («33 МБ/с»): число — крупным шрифтом, единица — обычным, по общей базовой линии.
class Chislo : Control
{
    public Palitra Pal = Palitra.Svoya();
    public static readonly Font FCh = new Font("Segoe UI Semibold", 18f), FEd = new Font("Segoe UI", 10f);
    string zn = "—", ed = "";
    public string Znach { get { return zn; } set { zn = value ?? ""; Text = zn + (ed != "" ? " " + ed : ""); Invalidate(); } }
    public string Ed { get { return ed; } set { ed = value ?? ""; Text = zn + (ed != "" ? " " + ed : ""); Invalidate(); } }
    public Chislo() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; Height = 34; }
    const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
    // ширина, которая нужна числу с единицей
    public int Nuzhno { get { return Chernila(zn, FCh).X + 1 + Probel + (ed != "" ? Chernila(ed, FEd).X + 2 : 0); } }
    // Где кончаются чернила строки (правый край) и где её базовая линия (низ буквы «0»/«М») — по настоящей отрисовке:
    // замер TextRenderer с запасом даёт лишний промежуток и сдвиг единицы вниз (21.11, замечание критика).
    static readonly System.Collections.Generic.Dictionary<string, Point> kesh = new System.Collections.Generic.Dictionary<string, Point>();
    static Point Chernila(string t, Font f)
    {
        string k = f.Size + "|" + t;
        Point p;
        if (kesh.TryGetValue(k, out p)) return p;
        int pravo = 0, niz = 0;
        var sz = TextRenderer.MeasureText(t == "" ? " " : t, f, Size.Empty, F);
        using (var b = new Bitmap(Math.Max(8, sz.Width + 8), Math.Max(8, sz.Height + 8)))
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            TextRenderer.DrawText(g, t, f, new Point(0, 0), Color.Black, F);
            for (int x = b.Width - 1; x >= 0 && pravo == 0; x--) for (int y = 0; y < b.Height; y++) if (b.GetPixel(x, y).R < 160) { pravo = x + 1; break; }
        }
        // базовая линия — по букве без выносных элементов
        string obr = f == FEd ? "М" : "0";
        using (var b = new Bitmap(60, 60))
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            TextRenderer.DrawText(g, obr, f, new Point(0, 0), Color.Black, F);
            for (int y = b.Height - 1; y >= 0 && niz == 0; y--) for (int x = 0; x < b.Width; x++) if (b.GetPixel(x, y).R < 160) { niz = y + 1; break; }
        }
        p = new Point(pravo, niz);
        if (kesh.Count > 500) kesh.Clear();
        kesh[k] = p;
        return p;
    }
    // промежуток между числом и единицей — как обычный пробел
    static int Probel { get { return 6; } }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var ch = Chernila(zn, FCh);
        TextRenderer.DrawText(g, zn, FCh, new Point(0, 0), Pal.Tekst, F);
        if (ed != "") { var ce = Chernila(ed, FEd); TextRenderer.DrawText(g, ed, FEd, new Point(ch.X + Probel, ch.Y - ce.Y), Pal.Vtorichnyy, F); }
    }
}

// Человеческий вид текстов сторожа (21.11, правила 5 и 10): МБ/с вместо Мбит/с, страны по-русски, без капса,
// без вложенных скобок и ссылок на файлы журналов, предложения с заглавной. Сам текст в Python не меняется.
static class Chel
{
    static readonly string[][] STRANY = {
        new[] { "United Kingdom", "Великобритания" }, new[] { "United States", "США" }, new[] { "Germany", "Германия" }, new[] { "Netherlands", "Нидерланды" },
        new[] { "England", "Англия" }, new[] { "Austria", "Австрия" }, new[] { "Lithuania", "Литва" }, new[] { "Latvia", "Латвия" }, new[] { "Poland", "Польша" },
        new[] { "Italy", "Италия" }, new[] { "Norway", "Норвегия" }, new[] { "France", "Франция" }, new[] { "Finland", "Финляндия" }, new[] { "Sweden", "Швеция" },
        new[] { "Estonia", "Эстония" }, new[] { "Turkey", "Турция" }, new[] { "Kazakhstan", "Казахстан" }, new[] { "Japan", "Япония" }, new[] { "Singapore", "Сингапур" },
        new[] { "Switzerland", "Швейцария" }, new[] { "Spain", "Испания" }, new[] { "Canada", "Канада" }, new[] { "USA", "США" } };

    // скорость: всё в мегабайтах в секунду, число и единица слитно
    public static string MB(double mbayt)
    {
        return mbayt <= 0 ? "0 МБ/с" : mbayt < 0.01 ? "< 0,01 МБ/с" : mbayt < 1 ? mbayt.ToString("0.00") + " МБ/с" : mbayt < 10 ? mbayt.ToString("0.0") + " МБ/с" : mbayt.ToString("0") + " МБ/с";
    }
    public static string MBizMbit(double mbit) { return MB(mbit / 8); }

    static string R(string t, string shablon, string zamena) { return System.Text.RegularExpressions.Regex.Replace(t, shablon, zamena); }
    static string R(string t, string shablon, System.Text.RegularExpressions.MatchEvaluator f) { return System.Text.RegularExpressions.Regex.Replace(t, shablon, f); }
    static string Raz(int n) { return n + (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14) ? " раза" : " раз"); }
    static string DlitKr(int sek) { return sek < 60 ? sek + " с" : sek < 3600 ? (int)Math.Round(sek / 60.0) + " мин" : sek / 3600 + " ч " + sek % 3600 / 60 + " мин"; }

    // Старые записи сторожа (до 21.11, когда тексты в Python ещё не были переписаны) — тем же языком, что и новые:
    // сторож в третьем лице, без «Мигания», «по правилам Karing», «Жду, пока выйдете», секунды обрывов — минутами.
    static string Staroe(string t)
    {
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(ИСПРАВЛЕНО\.|НЕ ВЫШЛО\.|Исправлено:)\s*(.*)$");
        if (m.Success)
        {
            bool isp = !m.Groups[1].Value.StartsWith("НЕ");
            var chasti = m.Groups[2].Value.Split(new[] { " | " }, StringSplitOptions.None).Select(c =>
            {
                c = c.Trim();
                c = R(c, @"^в VPN: ", "через VPN пущены: ");
                c = R(c, @"^напрямую \(через VPN сайт отказывает\): ", "напрямую пущены, потому что через VPN не открывались: ");
                c = R(c, @"^напрямую: ", "напрямую пущены: ");
                c = R(c, @"^чиню: ", "");
                c = R(c, @"^списки РФ: ", "списки заблокированных сайтов: ");
                return c;
            });
            t = (isp ? "Сторож исправил — " : "Не вышло исправить — ") + string.Join("; ", chasti);
        }
        t = t.Replace(" по правилам Karing", "");
        t = R(t, @"\s*\(вручную\)", " — по вашей просьбе");
        t = R(t, @"(?<![Сс]торож )\b[Вв]ернул прежние настройки", "сторож вернул прежние настройки");
        t = R(t, @"(?<![Сс]торож )\bвернул как было\b", "сторож вернул как было");
        t = R(t, @"проверка после перезапуска: (.*)$", mm =>
        {
            var p = mm.Groups[1].Value.Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
            Func<IEnumerable<string>, string> f = l => string.Join(", ", l.Select(x => x == "Steam вход" ? "вход в Steam" : x));
            var da = p.Where(x => x.EndsWith(" — да")).Select(x => x.Substring(0, x.Length - 5)).ToList();
            var net = p.Where(x => x.EndsWith(" — нет")).Select(x => x.Substring(0, x.Length - 6)).ToList();
            return (da.Count > 0 ? "после перезапуска работают: " + f(da) : "") + (net.Count > 0 ? "; не работают: " + f(net) : "");
        });
        // обрывы за день и обрывы по одному
        if (t.StartsWith("Мигания за "))
        {
            t = "Обрывы VPN за " + t.Substring("Мигания за ".Length);
            t = R(t, @"VPN-сервер: ([^—;]+?)(?= —)", mm => (mm.Groups[1].Value.Contains(",") ? "серверы " : "сервер ") + mm.Groups[1].Value.Trim());
            t = t.Replace("Karing не работал (перезапуск)", "Karing не работал").Replace("линия провайдера (пропал и прямой интернет)", "пропал интернет у провайдера");
            t = R(t, @": ни одного сбоя\.?$", ": ни одного.");
        }
        t = R(t, @"^VPN не отвечал (\d+) с \((.*)\)$", mm =>
        {
            string v = mm.Groups[2].Value.Trim();
            string kto = v.StartsWith("Karing не работал") ? "Karing не работал, сторож его перезапустил"
                       : v.StartsWith("линия провайдера") ? "пропал интернет у провайдера"
                       : (v.Contains(",") && v.IndexOf(" (", StringComparison.Ordinal) > v.IndexOf(',') ? "серверы " : "сервер ") + R(v, @"\s*\((прямой интернет при этом был)\)", ", $1");
            return "VPN не отвечал " + DlitKr(int.Parse(mm.Groups[1].Value)) + " — " + kto;
        });
        t = R(t, @"(\d+) раз\b", mm => Raz(int.Parse(mm.Groups[1].Value)));
        m = System.Text.RegularExpressions.Regex.Match(t, @"^Нужно поправить Karing \((.*)\), но запущена игра: (.*)\. Жду, пока выйдете\.?$");
        if (m.Success)
        {
            bool zv = m.Groups[2].Value.Contains("звонок"), ig = m.Groups[2].Value.Split(',').Any(x => !x.Contains("звонок"));
            string chto = m.Groups[1].Value.Trim();
            t = "Сторож отложил правку Karing: сейчас " + (ig && zv ? "идут игра и звонок в Discord" : ig ? "идёт игра" : "идёт звонок в Discord") +
                ". Поправит, когда " + (ig && zv ? "они закончатся" : ig ? "она закончится" : "он закончится") + "." + (chto != "" ? " Что поправить: " + chto + "." : "");
        }
        t = R(t, @"^Сервер (.+?) \(Claude\) не отвечает уже (\d+) мин\. Claude закреплён на .+? — на другой не перевожу\.?$",
              "Сервер $1, через который идёт Claude, не отвечает уже $2 мин. Claude закреплён за ним — сторож не переводит его на другой сервер.");
        t = R(t, @"^Karing не работал — запустил заново\. Поднялся\.", "Karing не работал — сторож запустил его заново, и он подключился.");
        t = R(t, @"^Karing не работал — запустил заново\. НЕ поднялся!", "Karing не работал — сторож запустил его заново, но он не подключился.");
        t = R(t, @"[Сс]ервер (.+?) \((скорость|Claude)\) перестал отвечать, временно (.+?)\.", mm =>
            "Сервер " + mm.Groups[1].Value + " перестал отвечать — сторож временно переключил " + (mm.Groups[2].Value == "Claude" ? "Claude" : "всё") + " на сервер " + mm.Groups[3].Value + ".");
        t = R(t, @"(снова стабилен), возвращаю\.", "$1 — сторож вернул его.");
        t = R(t, @"Сейчас: Claude — (.+?), остальное — (.+?)\.", "Сейчас Claude идёт через сервер $1, остальное — через сервер $2.");
        t = R(t, @"^Замер VPN-серверов: выбран (.+?) \((.+?), адрес выхода (.+?)\); стабильных серверов (\d+) из (\d+)\.", mm =>
            "Замер серверов: сторож выбрал сервер " + mm.Groups[1].Value + " — " + mm.Groups[2].Value + ", адрес выхода " + mm.Groups[3].Value.Replace("ПРЫГАЕТ — стабильных нет", "меняется") +
            ". Серверов с постоянным адресом: " + mm.Groups[4].Value + " из " + mm.Groups[5].Value + ".");
        t = R(t, @" Назначен на всё, кроме Claude \(Claude — (.+?)\)\.", " Через него идёт всё, кроме Claude: Claude идёт через сервер $1.");
        t = R(t, @"^Списки блокировок за (\d\d\.\d\d)\.\d{4}: проверено (\d+) адресов — открываются через Karing (\d+), мёртвые сайты \(не открываются нигде\) (\d+)",
              "Списки блокировок за $1: сторож проверил $2 адресов — через Karing открываются $3, не открываются нигде $4");
        t = R(t, @"(\S+): напрямую тормозит \((\d+)\.(\d) с\), через VPN (\d+)\.(\d) с", "$1: напрямую открывается медленно — за $2,$3 с, через VPN — за $4,$5 с");
        t = R(t, @"^[Нн]е смог обновить список (.+?) — оставил прежний", "Сторож не смог обновить список $1 и оставил прежний");
        t = R(t, @"обновились — внесу в Karing", "обновились — сторож внесёт их в Karing");
        t = R(t, @"\bПерезапуск по его просьбе\b", "Перезапуск по вашей просьбе");
        t = R(t, @"стратегия zapret", "настройка обхода zapret");
        // «Готово — Замер серверов — Выбран …» → «Замер серверов: готово. Выбран …» (без трёх тире подряд)
        t = R(t, @"^(Готово|Не вышло) — (.+?) — (.+)$", mm => mm.Groups[2].Value + (mm.Groups[1].Value == "Готово" ? ": готово. " : ": не вышло. ") + mm.Groups[3].Value);
        t = R(t, @"[Пп]осле перезапуска не заработало: ([^.;—]+?)(?=\s*(?:[.;—]|$))", mm => (mm.Groups[1].Value.Contains(",") ? "после перезапуска так и не заработали " : "после перезапуска так и не заработал ") + mm.Groups[1].Value.Trim().Replace("Steam вход", "вход в Steam"));
        t = R(t, @"[Дд]омашний сервер (.+?) снова стабилен", "Основной сервер $1 снова работает ровно");
        t = R(t, @"(Замер серверов: сторож выбрал сервер .+? — [^,.]+?), адрес выхода [^.]*\.(?: Серверов с постоянным адресом: \d+ из \d+\.)?", "$1.");
        // итог дня по обрывам: число и время без разбивки по серверам (она — в «Серверах»); число = сумма записанных
        var mi = System.Text.RegularExpressions.Regex.Match(t, @"^Обрывы VPN за (\d\d\.\d\d): (.*)$");
        if (mi.Success)
        {
            int n = System.Text.RegularExpressions.Regex.Matches(mi.Groups[2].Value, @"— (\d+) раз").Cast<System.Text.RegularExpressions.Match>().Sum(x => int.Parse(x.Groups[1].Value));
            t = n > 0 ? "Итог за " + mi.Groups[1].Value + ": связь через VPN пропадала " + Raz(n) + "." : "Итог за " + mi.Groups[1].Value + ": связь через VPN не пропадала.";
        }
        // «Что поправить: X.» — целыми предложениями
        var mo = System.Text.RegularExpressions.Regex.Match(t, @"^(Сторож отложил правку Karing: .*?\. Поправит, когда .*?\.) Что поправить: (.*?)\.?$");
        if (mo.Success)
        {
            var chasti = mo.Groups[2].Value.Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
            var sayty = chasti.Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"^[a-z0-9-]+(\.[a-z0-9-]+)+$")).ToList();
            var bedy = chasti.Where(x => !sayty.Contains(x)).ToList();
            t = string.Concat(bedy.Select(x => Pervaya(x) + ". ")) + (sayty.Count > 0 ? (sayty.Count > 1 ? "Сайты " : "Сайт ") + string.Join(", ", sayty) + " нужно пустить через VPN. " : "") + mo.Groups[1].Value;
        }
        t = R(t, @"(\d\d\.\d\d)\.20\d\d\b", "$1");
        return t;
    }

    public static string Tekst(string t)
    {
        if (string.IsNullOrEmpty(t)) return "";
        t = Pult.Chisto(t);
        t = Staroe(t);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+(?:[.,]\d+)?)\s*Мбит/с", m =>
        {
            double v;
            double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            return MBizMbit(v);
        });
        foreach (var s in STRANY) t = System.Text.RegularExpressions.Regex.Replace(t, @"\b" + System.Text.RegularExpressions.Regex.Escape(s[0]) + @"\b", s[1]);
        // капс: «НЕ ВЫШЛО» → «не вышло» (короткие сокращения вроде США не трогаем)
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<![А-ЯЁа-яё])[А-ЯЁ]{2,}(?:\s+[А-ЯЁ]{2,})*(?![А-ЯЁа-яё])", m => m.Value.Length >= 4 ? m.Value.ToLowerInvariant() : m.Value);
        t = Ploskie(t);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\(заняло 0 с\)", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\(\s*\)", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\b([\w\-]+)\.exe\b", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // ссылки на файлы журналов: «Подробно — mignaniya.log.», «(подробности в pult.log)»
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*(Подробно|Подробности|подробно|подробности)[^.;()]*?\.log\.?", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\([^()]*\.log[^()]*\)", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[\w\-]+\.log", "журнал");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+([.,;:])", "$1");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"([.!?]\s+)([а-яё])", m => m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));
        t = System.Text.RegularExpressions.Regex.Replace(t, "  +", " ").Trim();
        // адрес сайта в начале — как есть, со строчной
        return System.Text.RegularExpressions.Regex.IsMatch(t, @"^[a-z0-9-]+(\.[a-z0-9-]+)+:") ? t : Pervaya(t);
    }

    public static string Pervaya(string t) { t = (t ?? "").Trim(); return t.Length > 0 ? char.ToUpper(t[0]) + t.Substring(1) : t; }

    // вложенные скобки: «(Англия (прямой интернет при этом был))» → «(Англия, прямой интернет при этом был)»
    static string Ploskie(string t)
    {
        var sb = new System.Text.StringBuilder();
        int gl = 0;
        foreach (char c in t)
        {
            if (c == '(') { gl++; if (gl == 1) sb.Append(c); else { while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--; sb.Append(", "); } }
            else if (c == ')') { if (gl <= 1) sb.Append(c); if (gl > 0) gl--; }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

// Полоса суток: 24 клетки, цвет — как работал VPN в этот час (те же зелёный/жёлтый/красный, что во всём окне);
// данных нет — все клетки серые. Наведение — подробности.
class PolosaSutok : Control
{
    public Palitra Pal = Palitra.Svoya();
    public int[] Obryvy = new int[24], Sekund = new int[24];
    public int TekChas = DateTime.Now.Hour;
    public bool NetDannyh;
    readonly ToolTip tip = new ToolTip { InitialDelay = 60 };
    int nav = -1;
    public PolosaSutok() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void Dispose(bool d) { if (d) tip.Dispose(); base.Dispose(d); }   // раздел перестраивается раз в минуту — подсказки не копить
    int Chas(int i) { return (TekChas + 1 + i) % 24; }   // слева — сутки назад, справа — текущий час
    Color Cvet(int h) { return NetDannyh ? Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.14) : Obryvy[h] == 0 ? Okno.Zel : (Obryvy[h] >= 3 || Sekund[h] > 60 ? Okno.Kras : Okno.Zhelt); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = (Width - 23 * 3f) / 24f, h = Height - 20;
        using (var fm = new Font(Font.FontFamily, Font.Size - 1))
            for (int i = 0; i < 24; i++)
            {
                int ch = Chas(i);
                var c = Cvet(ch); if (i == nav) c = Ris.Smes(c, Color.White, 0.3);
                using (var b = new SolidBrush(c)) using (var p = Ris.Skrugl(new RectangleF(i * (w + 3), 0, w, h), 3)) g.FillPath(b, p);
                if (i % 6 == 0) TextRenderer.DrawText(g, ch.ToString("00") + ":00", fm, new Point((int)(i * (w + 3)), (int)h + 3), Pal.Vtorichnyy);
                if (i == 23) TextRenderer.DrawText(g, "сейчас", fm, new Rectangle((int)(i * (w + 3)) - 40, (int)h + 3, (int)w + 40, 18), Pal.Vtorichnyy, TextFormatFlags.Right);
            }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        float w = (Width - 23 * 3f) / 24f; int i = (int)(e.X / (w + 3));
        if (i < 0 || i > 23 || e.Y > Height - 20) i = -1;
        if (i != nav)
        {
            nav = i; Invalidate();
            if (i >= 0) { int ch = Chas(i); tip.Show(ch.ToString("00") + ":00–" + ch.ToString("00") + ":59\n" + (NetDannyh ? "Нет данных: сторож ещё не проверял сеть" : Obryvy[ch] == 0 ? "Без обрывов" : "Обрывов " + Obryvy[ch] + ", VPN не отвечал " + Okno.Vremya(Sekund[ch])), this, e.X + 12, e.Y + 12); }
            else tip.Hide(this);
        }
    }
    protected override void OnMouseLeave(EventArgs e) { nav = -1; tip.Hide(this); Invalidate(); }
}

// Переключатель-сегменты: «Авто | VPN | Напрямую | Без сети». 21.11: нейтральный — выбранный сегмент светлой «таблеткой»
// на сером поле, а не синей заливкой (синий — только у главной кнопки экрана). Выключенный — бледный и не нажимается.
class Segmenty : Control
{
    public Palitra Pal = Palitra.Svoya();
    public string[] Punkty = new string[0];
    public int Vybran;
    public event Action<int> Smena;
    int nav = -1;
    static readonly Font fObych = new Font("Segoe UI", 9f), fZhir = new Font("Segoe UI Semibold", 9f);
    public Segmenty() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; Cursor = Cursors.Hand; }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    static float NuzhnoOdnomu(string p) { return TextRenderer.MeasureText(p, fZhir).Width + 18f; }
    float[] Shiriny()
    {
        int n = Math.Max(1, Punkty.Length);
        float dost = Width - 6f, ravn = dost / n;
        var nuzh = Punkty.Select(NuzhnoOdnomu).ToArray();
        if (nuzh.All(x => x <= ravn)) return Enumerable.Repeat(ravn, n).ToArray();
        float lishn = dost - nuzh.Sum();
        return nuzh.Select(x => lishn >= 0 ? x + lishn / n : x * dost / nuzh.Sum()).ToArray();
    }
    RectangleF Kl(int i) { var w = Shiriny(); float x = 3; for (int k = 0; k < i && k < w.Length; k++) x += w[k]; return new RectangleF(x, 3, i < w.Length ? w[i] : 0, Height - 6); }
    // все подписи влезают (для самопроверки)
    public bool Vlezaet() { var w = Shiriny(); for (int i = 0; i < Punkty.Length; i++) if (NuzhnoOdnomu(Punkty[i]) - 8 > w[i]) return false; return true; }
    // ширина, при которой все подписи влезают целиком
    public static int Nuzhno(string[] punkty) { return punkty.Length == 0 ? 40 : (int)Math.Ceiling(punkty.Sum(p => NuzhnoOdnomu(p))) + 6; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color pod = Ris.FonPod(this, Pal.Poverhnost);
        Color pole = Ris.Smes(pod, Pal.Tekst, Pal.Temnaya ? 0.08 : 0.06);
        using (var b = new SolidBrush(pole)) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), Math.Max(6, Pal.Radius))) g.FillPath(b, p);
        for (int i = 0; i < Punkty.Length; i++)
        {
            var r = Kl(i);
            bool vyb = i == Vybran;
            if (vyb)
            {
                Color tab = Pal.Temnaya ? Ris.Smes(pod, Pal.Tekst, 0.2) : Pal.Poverhnost;
                using (var b = new SolidBrush(tab)) using (var p = Ris.Skrugl(r, Math.Max(4, Pal.Radius - 2))) g.FillPath(b, p);
                if (!Pal.Temnaya) using (var pen = new Pen(Pal.Liniya)) using (var p = Ris.Skrugl(r, Math.Max(4, Pal.Radius - 2))) g.DrawPath(pen, p);
            }
            else if (i == nav && Enabled) using (var b = new SolidBrush(Ris.Smes(pole, Pal.Tekst, 0.06))) using (var p = Ris.Skrugl(r, Math.Max(4, Pal.Radius - 2))) g.FillPath(b, p);
            Color c = !Enabled ? Ris.Smes(pole, Pal.Tekst, vyb ? 0.62 : 0.45) : vyb ? Pal.Tekst : Pal.Vtorichnyy;
            TextRenderer.DrawText(g, Punkty[i], vyb ? fZhir : fObych, Rectangle.Round(r), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = -1; for (int k = 0; k < Punkty.Length; k++) if (Kl(k).Contains(e.Location)) i = k;
        if (i != nav) { nav = i; Invalidate(); }
    }
    protected override void OnMouseLeave(EventArgs e) { nav = -1; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (int i = 0; i < Punkty.Length; i++)
            if (Kl(i).Contains(e.Location) && i != Vybran) { Vybran = i; Invalidate(); if (Smena != null) Smena(i); }
    }
}

// Тумблер вкл/выкл
class Tumbler : Control
{
    public Palitra Pal = Palitra.Svoya();
    public bool Vkl;
    public event Action<bool> Smena;
    public Tumbler() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Size = new Size(42, 24); Cursor = Cursors.Hand; }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color pod = Ris.FonPod(this, Pal.Poverhnost);
        Color fon = Vkl ? Pal.Akcent : Ris.Smes(pod, Pal.Tekst, 0.25);
        if (!Enabled) fon = Ris.Smes(pod, fon, 0.6);   // выключен — бледнее, но виден
        using (var b = new SolidBrush(fon)) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f)) g.FillPath(b, p);
        float d = Height - 6;
        using (var b = new SolidBrush(Vkl ? Pal.NaAkcente : Pal.Vtorichnyy)) g.FillEllipse(b, Vkl ? Width - d - 3 : 3, 3, d, d);
    }
    protected override void OnMouseClick(MouseEventArgs e) { Vkl = !Vkl; Invalidate(); if (Smena != null) Smena(Vkl); }
}

// Тонкая полоса-уровень (скорость программы)
class Uroven : Control
{
    public Palitra Pal = Palitra.Svoya();
    public double Dolya;
    public bool Neytralnaya;   // полоса теста скорости: не синяя (синий — у главной кнопки)
    public Uroven() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.12))) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f)) g.FillPath(b, p);
        float w = (float)(Width * Math.Min(1, Math.Max(0, Dolya)));
        if (w > 2) using (var b = new SolidBrush(Neytralnaya ? Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.5) : Pal.Akcent.GetBrightness() > 0.92 ? Pal.Tekst : Pal.Akcent)) using (var p = Ris.Skrugl(new RectangleF(0, 0, w, Height - 1), Height / 2f)) g.FillPath(b, p);
    }
}

// Карточка хода долгого действия (его выбор по концептам, 21.11): «Замер серверов — 48%», справа «осталось ~1 мин 20 с»,
// полоса, ниже шаги: сделанные — зелёная галочка, текущий — значок «идёт» и подробность, будущие — пустой кружок.
// По окончании — итог в той же карточке («Готово: …» / «Не вышло: …») и крестик «закрыть».
// Рисует себя сама и обновляется на месте (окно читает файл хода раз в 0,7 с) — страница при этом не перестраивается.
class KartaHoda : Kartochka
{
    enum Cv { Tekst, Vt, Zel, Kras, Zhelt, Akcent, ZagOk, ZagNe }
    class El { public string T; public Rectangle R; public Cv C; public Font F; public bool Perenos, Znak; }

    public Hod H;
    public readonly Knopa Zakryt;
    public readonly System.Collections.Generic.List<string> Teksty = new System.Collections.Generic.List<string>();   // всё, что написано (для самопроверки)
    public int NizSoderzhimogo, Sdelano, Tekushchiy = -1, Vperedi;
    readonly System.Collections.Generic.List<El> el = new System.Collections.Generic.List<El>();
    Rectangle polosa;
    int? procent;
    readonly Font fZag = new Font("Segoe UI Semibold", 11f), fObych = new Font("Segoe UI", 10f), fZhir = new Font("Segoe UI Semibold", 10f),
                  fZn = Zn.Shrift(12f), fZnB = Zn.Shrift(14f);
    readonly Timer anim = new Timer { Interval = 90 };   // полоса без процентов — бегущий отрезок

    public KartaHoda()
    {
        Name = "karta-hoda";
        Zakryt = new Knopa { Text = "Закрыть", Znachok = Zn.Krestik, Size = new Size(34, 30), Visible = false, Margin = Padding.Empty, Tihaya = true };
        Controls.Add(Zakryt);
        anim.Tick += (s, e) => { if (IsHandleCreated && polosa.Width > 0) Invalidate(polosa); };
    }

    protected override void Dispose(bool d)
    {
        if (d) { anim.Dispose(); fZag.Dispose(); fObych.Dispose(); fZhir.Dispose(); fZn.Dispose(); fZnB.Dispose(); }
        base.Dispose(d);
    }

    static int VysT(string t, int w, Font f) { return TextRenderer.MeasureText(string.IsNullOrEmpty(t) ? " " : t, f, new Size(Math.Max(10, w), 10000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height; }
    static int ShirT(string t, Font f) { return TextRenderer.MeasureText(t, f, Size.Empty, TextFormatFlags.NoPrefix).Width; }

    void Dob(string t, int x, int y, int w, int h, Cv c, Font f, bool perenos = false, bool znak = false)
    {
        el.Add(new El { T = t ?? "", R = new Rectangle(x, y, Math.Max(1, w), h), C = c, F = f, Perenos = perenos, Znak = znak });
        if (!znak) Teksty.Add(t ?? "");
        NizSoderzhimogo = Math.Max(NizSoderzhimogo, y + h);
    }

    // разложить по месту под текущее состояние хода; высота карточки — по содержимому
    public void Pokazat(Hod h)
    {
        H = h; el.Clear(); Teksty.Clear(); NizSoderzhimogo = 0; Sdelano = Vperedi = 0; Tekushchiy = -1; polosa = Rectangle.Empty; procent = null;
        if (h == null) { anim.Stop(); Zakryt.Visible = false; Height = 24; Invalidate(); return; }
        int W = Width, x0 = 16, pr = W - 16, y = 12;
        int stroka = fObych.Height + 4;
        if (h.Idet)
        {
            procent = h.Procent;
            int? ost = h.Ostalos;
            string zag = Chel.Tekst(h.Nazvanie) + (procent != null ? " — " + procent + "%" : " — идёт…");
            // зависло — прошлая оценка «осталось» уже неправда: показываем, сколько прошло
            string sprava = ost != null && !h.Zavis ? "осталось ~" + Okno.Vremya(ost.Value) : "прошло " + Okno.Vremya(h.Proshlo);
            int sw = ShirT(sprava, fObych) + 4;
            Dob(zag, x0, y, pr - x0 - sw - 12, fZag.Height + 4, Cv.Tekst, fZag);
            Dob(sprava, pr - sw, y + 2, sw, stroka, Cv.Vt, fObych);
            y += fZag.Height + 12;
            polosa = new Rectangle(x0, y, pr - x0, 8);
            NizSoderzhimogo = Math.Max(NizSoderzhimogo, polosa.Bottom);
            y += 8 + 12;
            var shagi = h.Shagi;
            int tek = h.Shag;
            for (int i = 0; i < shagi.Count; i++)
            {
                bool sd = i < tek, tk = i == tek;
                if (sd) Sdelano++; else if (tk) Tekushchiy = i; else Vperedi++;
                Dob(sd ? Zn.GalkaKrug : tk ? Zn.Idet : Zn.Krug0, x0, y + 1, 22, 22, sd ? Cv.Zel : tk ? Cv.Akcent : Cv.Vt, fZn, false, true);
                Dob(Chel.Tekst(tk ? h.TekstShaga(i) : shagi[i]), x0 + 30, y + 2, pr - x0 - 30, stroka, tk ? Cv.Tekst : Cv.Vt, tk ? fZhir : fObych);
                y += 26;
            }
            if (shagi.Count > 0) y += 2;
            if (h.Zavis)
            {
                string t = "Нет вестей уже " + Okno.Vremya(h.Molchit) + " — возможно, действие зависло";
                int tw = pr - x0 - 30, th = VysT(t, tw, fObych);
                Dob(Zn.Preduprezhd, x0, y + 1, 22, 22, Cv.Zhelt, fZn, false, true);
                Dob(t, x0 + 30, y + 2, tw, th, Cv.Tekst, fObych, true);
                y += Math.Max(26, th + 6);
            }
            Height = Math.Max(NizSoderzhimogo, y) + 8;
            Zakryt.Visible = false;
            if (procent == null) { if (!anim.Enabled) anim.Start(); } else anim.Stop();
        }
        else
        {
            anim.Stop();
            bool ok = h.Ok == true;
            Dob(ok ? Zn.GalkaKrug : Zn.OshibkaKrug, x0, y + 1, 26, 26, ok ? Cv.Zel : Cv.Kras, fZnB, false, true);
            int tx = x0 + 36, tw = pr - Zakryt.Width - 10 - tx;
            Dob((ok ? "Готово: " : "Не вышло: ") + Chel.Tekst(h.Nazvanie), tx, y + 2, tw, fZag.Height + 4, ok ? Cv.ZagOk : Cv.ZagNe, fZag);
            y += fZag.Height + 10;
            string itog = Chel.Tekst(h.Itog);
            if (itog != "")
            {
                int th = VysT(itog, tw, fObych);
                Dob(itog, tx, y, tw, th, Cv.Tekst, fObych, true);
                y += th + 4;
            }
            if (h.Proshlo >= 1) { Dob("Заняло " + Okno.Vremya(h.Proshlo), tx, y, tw, stroka, Cv.Vt, fObych); y += stroka; }
            Height = Math.Max(NizSoderzhimogo, y) + 12;
            Zakryt.Location = new Point(W - 16 - Zakryt.Width, 10);
            Zakryt.Visible = true;
        }
        Invalidate();
    }

    Color Cvet(Cv c)
    {
        switch (c)
        {
            case Cv.Vt: return Pal.Vtorichnyy;
            case Cv.Zel: return Okno.Zel;
            case Cv.Kras: return Okno.Kras;
            case Cv.Zhelt: return Okno.Zhelt;
            case Cv.Akcent: return Pal.Akcent.GetBrightness() > 0.92 ? Pal.Tekst : Pal.Akcent;
            // заголовок итога: темнее на светлой теме, светлее на тёмной — читается на подкрашенной карточке
            case Cv.ZagOk: return Pal.Temnaya ? Ris.Smes(Okno.Zel, Color.White, 0.25) : Ris.Smes(Okno.Zel, Color.Black, 0.3);
            case Cv.ZagNe: return Pal.Temnaya ? Ris.Smes(Okno.Kras, Color.White, 0.25) : Ris.Smes(Okno.Kras, Color.Black, 0.2);
            default: return Pal.Tekst;
        }
    }

    // итог — подкрашен цветом состояния (тот же оттенок, что у плиток и карточек ошибок)
    public override Color Zalivka { get { return H != null && !H.Idet ? Ris.Smes(Pal.Poverhnost, H.Ok == true ? Okno.Zel : Okno.Kras, Pal.Temnaya ? 0.16 : 0.10) : Pal.Poverhnost; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var x in el)
        {
            var f = x.Znak ? TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                  : x.Perenos ? TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak
                  : TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, x.T, x.F, x.R, Cvet(x.C), f);
        }
        if (polosa.Width > 0)
        {
            using (var b = new SolidBrush(Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.12))) using (var p = Ris.Skrugl(polosa, 4)) g.FillPath(b, p);
            RectangleF zap;
            if (procent != null) zap = new RectangleF(polosa.X, polosa.Y, polosa.Width * Math.Max(0, Math.Min(100, procent.Value)) / 100f, polosa.Height);
            else
            {
                float dl = polosa.Width * 0.25f, t = (Environment.TickCount & int.MaxValue) % 1600 / 1600f;
                zap = RectangleF.Intersect(new RectangleF(polosa.X - dl + (polosa.Width + dl) * t, polosa.Y, dl, polosa.Height), polosa);
            }
            if (zap.Width > 2) using (var b = new SolidBrush(Cvet(Cv.Akcent))) using (var p = Ris.Skrugl(zap, 4)) g.FillPath(b, p);
        }
    }
}

// «Сторож сети» — окно над сторожем (storozh-seti\storozh.py) через pult.py.
// Логика сети вся в Python; окно показывает состояние и зовёт команды пульта.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Programma
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

    static int MasshtabEkrana()
    {
        try
        {
            var z = Environment.GetEnvironmentVariable("STOROZH_DPI");   // для проверки
            if (!string.IsNullOrEmpty(z)) return int.Parse(z);
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics"))
            {
                var v = k == null ? null : k.GetValue("AppliedDPI");
                return v is int ? (int)v : 96;
            }
        }
        catch { return 96; }
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Раскладка окна — в пикселях при 100%. На экране с масштабом 125–150% (ноутбук друга) «знающее про масштаб» окно
        // увеличило бы шрифты, а места под них — нет, и надписи налезли бы. Поэтому при масштабе больше 100% окно
        // рисуется как при 100%, а Windows растягивает его целиком, текст остаётся чётким (GDI scaled). При 100% — как было.
        try { SetProcessDpiAwarenessContext(new IntPtr(MasshtabEkrana() > 96 ? -5 : -4)); } catch { }
        Application.EnableVisualStyles();
        // ни одного окна «Необрабатываемое исключение»: ошибка уходит в журнал, программа работает дальше
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => { Okno.OshibokVFone++; Okno.Zapis("ОШИБКА (окно не показано): " + e.Exception.GetType().Name + ": " + e.Exception.Message + " | " + (e.Exception.StackTrace ?? "").Split('\n').FirstOrDefault()); };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Okno.Zapis("ОШИБКА в фоне: " + e.ExceptionObject);
        int i = Array.IndexOf(args, "--snimki");
        if (i >= 0 && i + 1 < args.Length) { Okno.Snimki(args[i + 1]); return; }
        int iPr = Array.IndexOf(args, "--proba-okno");
        if (iPr >= 0 && iPr + 1 < args.Length) { Okno.ProbaOkno(args[iPr + 1]); return; }
        // точная последовательность Коробки (встал во вкладку → другая вкладка → назад → «Вынуть окно») за экраном
        int iV = Array.IndexOf(args, "--proba-vynos");
        if (iV >= 0 && iV + 1 < args.Length) { Environment.ExitCode = Okno.ProbaVynos(args[iV + 1], true); return; }
        // как окно поймёт файл хода сторожа (сверка со storozh-seti\hod.py): тексты карточки в консоль
        int iH = Array.IndexOf(args, "--proba-hoda");
        if (iH >= 0 && iH + 1 < args.Length) { Environment.ExitCode = Okno.ProbaHoda(args[iH + 1]); return; }
        if (Array.IndexOf(args, "--samoproverka") >= 0) { try { Environment.ExitCode = Okno.Samoproverka(); } catch (Exception e) { Console.WriteLine("ОШИБКА: " + e); Environment.ExitCode = 99; } return; }
        if (Array.IndexOf(args, "--test-klik") >= 0) { try { Environment.ExitCode = Okno.TestKlik(); } catch (Exception e) { Console.WriteLine("ОШИБКА: " + e); Environment.ExitCode = 99; } return; }
        bool novaya;
        using (var m = new Mutex(true, "StorozhSetiOkno" + (Environment.GetEnvironmentVariable("STOROZH_MUTEX") ?? ""), out novaya))
        {
            if (!novaya)
            {
                Vstraivanie.RazreshitVperedi();
                foreach (var p in Process.GetProcessesByName("StorozhSeti"))
                    if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero) Okno.Vpered(p.MainWindowHandle);
                return;
            }
            Application.AddMessageFilter(new SledMyshi());
            Application.Run(new Okno(true));
        }
    }
}

// ---------- связь с пультом ----------
static class Pult
{
    public static readonly string Papka = NaytiPapku();
    // полный путь к python.exe; null — Python не найден нигде
    public static readonly string Python = NaytiPython();
    static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    // проверки окна: готовые ответы пульта (команда → JSON; "*" — на всё остальное). Python не запускается вовсе.
    public static Dictionary<string, string> Poddelka;
    // проверки на настоящих данных: только команды, которые читают и не ходят в сеть
    public static bool TolkoChtenie;
    static readonly HashSet<string> CHTENIE = new HashSet<string> { "sostoyanie", "programmy", "servery", "sayty-istoriya", "lenta", "skorosti", "set-seychas" };
    // команды, которые ходят в сеть, но ничего не меняют: им — срок, как и чтению
    static readonly HashSet<string> BYSTRYE = new HashSet<string> { "zapret", "proverit-sayt", "test-seti" };

    public const string NET_PYTHON = "Не нашёл Python: он лежит рядом с программой, в папке python. Распакуйте архив целиком — вместе с папками python и storozh-seti — и запускайте StorozhSeti.exe из распакованной папки.";

    static string NaytiPapku()
    {
        var baza = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var p in new[] { Path.Combine(baza, "storozh-seti"), Path.Combine(baza, @"..\storozh-seti"), baza })
            if (File.Exists(Path.Combine(p, "pult.py"))) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baza, @"..\storozh-seti"));
    }

    // Порядок: встроенный (папка python рядом с программой — так в архиве для друзей), потом установленный.
    static string NaytiPython()
    {
        var baza = AppDomain.CurrentDomain.BaseDirectory;
        var kandidaty = new List<string> { Path.Combine(baza, @"python\python.exe") };
        try
        {
            var ryadomSPultom = Path.GetDirectoryName(Papka.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(ryadomSPultom)) kandidaty.Add(Path.Combine(ryadomSPultom, @"python\python.exe"));
            foreach (var koren in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python"),
                                          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                if (Directory.Exists(koren))
                    kandidaty.AddRange(Directory.GetDirectories(koren, "Python3*").OrderByDescending(NomerVersii).Select(d => Path.Combine(d, "python.exe")));
            // PATH — без заглушки магазина (WindowsApps\python.exe открывает Microsoft Store, а не Python)
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (d.Trim() != "" && d.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
                    try { kandidaty.Add(Path.Combine(d.Trim(), "python.exe")); } catch { }
        }
        catch { }
        foreach (var k in kandidaty)
            try { if (File.Exists(k)) return Path.GetFullPath(k); } catch { }
        return null;
    }

    // Python39 и Python312: по номеру, а не по буквам (по буквам 3.9 «новее» 3.12)
    static int NomerVersii(string papka)
    {
        var m = Regex.Match(Path.GetFileName(papka) ?? "", @"^Python3(\d+)");
        int n; return m.Success && int.TryParse(m.Groups[1].Value, out n) ? n : 0;
    }

    // действия, которые пишут ход в файл (DOGOVOR-hod.md): им окно даёт путь в STOROZH_HOD
    public static readonly HashSet<string> S_HODOM = new HashSet<string> { "zamer", "proverka", "test-seti", "proverit-sayt", "sayt", "vernut", "primenit-marshruty", "server", "ustanovit-zadaniya" };

    public static Dictionary<string, object> Vyzov(params string[] arg) { return VyzovH(null, arg); }

    // hod — полный путь к файлу хода (только для действий из S_HODOM); null — ход не нужен
    public static Dictionary<string, object> VyzovH(string hod, string[] arg)
    {
        string kom = arg.Length > 0 ? arg[0] : "";
        if (Poddelka != null)
        {
            string j;
            if (!Poddelka.TryGetValue(kom, out j) && !Poddelka.TryGetValue("*", out j)) j = "{\"ok\":false,\"info\":\"проба: нет ответа для " + kom + "\"}";
            if (j == "!net_python") return Oshibka(NET_PYTHON, true);
            return Razobrat(j, j == "" ? "Traceback (most recent call last): ... ModuleNotFoundError: No module named 'storozh' (проба)" : "");
        }
        if (TolkoChtenie && !CHTENIE.Contains(kom)) return Oshibka("проверка: команда «" + kom + "» в проверке не вызывается");
        if (Python == null) return Oshibka(NET_PYTHON, true);
        if (!File.Exists(Path.Combine(Papka, "pult.py")))
            return Oshibka("Не нашёл папку storozh-seti с файлом pult.py рядом с программой. Распакуйте архив целиком и запускайте StorozhSeti.exe из распакованной папки.");
        var a = new StringBuilder("-X utf8 \"" + Path.Combine(Papka, "pult.py") + "\"");
        foreach (var x in arg) a.Append(" \"" + x.Replace("\"", "\\\"") + "\"");
        var psi = new ProcessStartInfo(Python, a.ToString())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Papka
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        if (hod != null && S_HODOM.Contains(kom)) psi.EnvironmentVariables["STOROZH_HOD"] = hod;
        var vyhod = new StringBuilder(); var oshibki = new StringBuilder();
        var konecVyhoda = new ManualResetEvent(false);
        Process p;
        try
        {
            p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) => { if (e.Data == null) konecVyhoda.Set(); else lock (vyhod) vyhod.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (oshibki) oshibki.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }
        catch (Exception e) { return Oshibka("не смог запустить сторожа: Python не запустился (" + Python + "): " + e.Message); }
        using (p)
        {
            // чтение и проверки — со сроком: зависший пульт не должен держать окно «занятым» навсегда.
            // Действия (замер, перезапуск Karing) идут долго и ждут очередь сторожа — им срок большой.
            int srok = CHTENIE.Contains(kom) || BYSTRYE.Contains(kom) ? 120000 : 30 * 60000;
            if (!p.WaitForExit(srok))
            {
                try { p.Kill(); } catch { }
                return Oshibka("сторож не ответил за " + Okno.Vremya(srok / 1000) + " (команда " + kom + ")");
            }
            konecVyhoda.WaitOne(3000);   // программа, запущенная пультом, может держать вывод — ждём недолго
            string o, err;
            lock (vyhod) o = vyhod.ToString();
            lock (oshibki) err = oshibki.ToString();
            return Razobrat(o, err);
        }
    }

    static Dictionary<string, object> Razobrat(string o, string err)
    {
        string stroka = (o ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.StartsWith("{"));
        if (stroka != null)
            try { var d = js.Deserialize<Dictionary<string, object>>(stroka); if (d != null) return d; } catch { }
        string posl = (err ?? "").Trim().Split('\n').Select(x => x.Trim()).LastOrDefault(x => x != "") ?? "";
        return Oshibka("сторож не ответил" + (posl != "" ? ": " + posl : ""));
    }

    public static Dictionary<string, object> Oshibka(string info, bool netPython = false)
    {
        var d = new Dictionary<string, object> { { "ok", false }, { "info", info } };
        if (netPython) d["net_python"] = true;
        return d;
    }

    // ответ — ошибка пульта (а не данные): пусто или {"ok": false, ...}
    public static bool Sboy(Dictionary<string, object> d) { object v; return d == null || (d.TryGetValue("ok", out v) && v is bool && !(bool)v); }
    public static string Info(Dictionary<string, object> d) { var t = S(d, "info"); return t != "" ? t : "сторож не ответил"; }

    public static string Json(object o) { return js.Serialize(o); }

    // флаги и значки из названий серверов: шрифт окна рисует их квадратиками
    public static string Chisto(string t)
    {
        if (string.IsNullOrEmpty(t)) return t;
        var sb = new StringBuilder();
        t = t.Replace("➗", " 2");   // «Германия➗» — отдельный сервер: без значка имена совпали бы; «÷» человеку непонятен (21.11)
        foreach (char c in t)
            if (!char.IsSurrogate(c) && !(c >= 0x2600 && c <= 0x27BF) && c != 0xFE0F && c != 0x200D) sb.Append(c);
        return Regex.Replace(sb.ToString(), "  +", " ").Trim();
    }

    public static object G(Dictionary<string, object> d, string k) { object v; return d != null && k != null && d.TryGetValue(k, out v) ? v : null; }
    public static string S(Dictionary<string, object> d, string k) { var v = G(d, k); return v != null ? Convert.ToString(v, CultureInfo.CurrentCulture) : ""; }
    public static bool B(Dictionary<string, object> d, string k) { var v = G(d, k); return v is bool && (bool)v; }
    public static IEnumerable<object> L(Dictionary<string, object> d, string k) { var v = G(d, k); return v is IEnumerable && !(v is string) && !(v is IDictionary) ? ((IEnumerable)v).Cast<object>() : new object[0]; }
    public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static List<Dictionary<string, object>> Slovari(Dictionary<string, object> d, string k) { return L(d, k).OfType<Dictionary<string, object>>().ToList(); }

    // число из ответа пульта: целое, дробное, строка или null (тогда 0)
    public static double Ch(object v)
    {
        if (v == null || v is bool) return 0;
        if (v is string)
        {
            double r;
            return double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) || double.TryParse((string)v, out r) ? r : 0;
        }
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
    }
    public static int Cel(object v) { double d = Ch(v); return d > int.MaxValue ? int.MaxValue : d < int.MinValue ? int.MinValue : (int)Math.Round(d); }
    public static object El(IList<object> l, int i) { return l != null && i >= 0 && i < l.Count ? l[i] : null; }

    // число или null: null, логическое, строка-не-число — «не знаем» (а не 0)
    public static double? ChIliNet(object v)
    {
        if (v == null || v is bool) return null;
        if (v is string)
        {
            double r;
            return double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : (double?)null;
        }
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
    }
}

// ---------- ход долгого действия (DOGOVOR-hod.md) ----------
// Что окно запустило (Chto, начало) и что сторож пишет в файл хода (Fayl). Fayl == null — сторож ход не пишет
// (старая версия или ещё не начал): карточка тогда показывает «идёт…» и сколько прошло, без процентов.
class Hod
{
    public string Chto = "";
    public string Put;                       // файл хода
    public DateTime Nachalo = DateTime.Now;
    public bool Idet = true;                 // процесс ещё работает
    public DateTime Konec;
    public bool? Ok;
    public string Itog = "";
    public Dictionary<string, object> Fayl;  // последнее удачно прочитанное

    static readonly DateTime EPOHA = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static double Seychas() { return (DateTime.UtcNow - EPOHA).TotalSeconds; }

    public string Nazvanie { get { var c = Pult.Chisto(Pult.G(Fayl, "chto") as string ?? "").Trim(); return c != "" ? c : Chto; } }
    public List<string> Shagi
    {
        get
        {
            var l = Pult.L(Fayl, "shagi").ToList();
            return l.Select((x, i) => { var t = Pult.Chisto(x as string ?? "").Trim(); return t != "" ? t : "Шаг " + (i + 1); }).ToList();
        }
    }
    public int Shag { get { var v = Pult.ChIliNet(Pult.G(Fayl, "shag")); return v == null ? 0 : (int)Math.Max(0, Math.Min(1000, v.Value)); } }
    public int? Procent { get { var v = Pult.ChIliNet(Pult.G(Fayl, "procent")); return v == null ? (int?)null : (int)Math.Round(Math.Max(0, Math.Min(100, v.Value))); } }
    public int? Ostalos { get { var v = Pult.ChIliNet(Pult.G(Fayl, "ostalos")); return v == null || v.Value < 0 ? (int?)null : (int)Math.Round(Math.Min(v.Value, 360000)); } }
    public bool KonecVFayle { get { return Pult.B(Fayl, "konec"); } }
    // текущий шаг: подробность сторожа целой фразой («Меряю скорость: 7 из 14», «Проверяю general (ALT13): 3 из 19»);
    // нет её — название шага. Склеивать «шаг: подробность» нельзя — выходит «Проверяю … по очереди: Проверяю …».
    public string TekstShaga(int i)
    {
        var sh = Shagi; string nazv = i >= 0 && i < sh.Count ? sh[i] : "";
        string t = Pult.Chisto(Pult.G(Fayl, "tekst") as string ?? "").Trim();
        return t != "" ? t : nazv;
    }
    public int Proshlo { get { return (int)Math.Max(0, ((Idet ? DateTime.Now : Konec) - Nachalo).TotalSeconds); } }
    // сколько секунд сторож не обновлял файл (0 — файла нет или действие кончилось)
    public int Molchit
    {
        get
        {
            var o = Pult.ChIliNet(Pult.G(Fayl, "obnovleno"));
            if (Fayl == null || KonecVFayle || o == null || o.Value <= 0) return 0;
            return (int)Math.Max(0, Math.Round(Seychas() - o.Value));   // с округлением: 99,99 с — это «1 мин 40 с», а не 39
        }
    }
    public bool Zavis { get { return Idet && Molchit > 90; } }

    // конец: ответ пульта (или скрипта) + последнее, что написал сторож. Успех — по ответу; слова — сторожа, если он их дал.
    public void Zakonchit(Dictionary<string, object> otvet)
    {
        Idet = false; Konec = DateTime.Now;
        bool ok = !Pult.Sboy(otvet) && Pult.B(otvet, "ok");
        string fItog = KonecVFayle ? Pult.Chisto(Pult.G(Fayl, "itog") as string ?? "").Trim() : "";
        Ok = ok;
        if (ok)
        {
            string seychas = Pult.Chisto(Pult.G(otvet, "seychas") as string ?? "").Trim(), info = Pult.Chisto(Pult.G(otvet, "info") as string ?? "").Trim();
            Itog = fItog != "" ? fItog : info != "" ? info : seychas != "" ? "Сейчас сервер: " + seychas : "";
        }
        else Itog = fItog != "" && !Pult.B(Fayl, "ok") ? fItog : Pult.Chisto(Pult.Info(otvet));
    }
}

// ---------- окно ----------
partial class Okno : Form
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    public static void Vpered(IntPtr h) { ShowWindow(h, 9); SetForegroundWindow(h); }

    static string ZHURNAL { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, proba ? "storozh-okno-proba.log" : "storozh-okno.log"); } }
    internal static readonly Color Zel = Color.FromArgb(46, 160, 90), Kras = Color.FromArgb(214, 72, 64), Zhelt = Color.FromArgb(214, 150, 40), Sery = Color.FromArgb(150, 150, 160);

    // проверки (самопроверка, нажатия, пробы): окно за экраном, без кнопки на панели задач, без значка в трее, фокус не берёт
    static bool proba;
    protected override bool ShowWithoutActivation { get { return proba; } }

    Palitra pal = SvoyaPal();
    static Palitra SvoyaPal()
    {
        var p = Palitra.Svoya();
        p.Fon = Color.FromArgb(23, 23, 27); p.Poverhnost = Color.FromArgb(34, 34, 42); p.Knopka = Color.FromArgb(45, 45, 54);
        p.Akcent = Color.FromArgb(79, 116, 217); p.NaAkcente = Color.White; p.Liniya = Color.FromArgb(56, 56, 66);
        p.Navedenie = Color.FromArgb(54, 54, 64); p.Radius = 8; p.Vtorichnyy = Color.FromArgb(145, 145, 158);
        return p;
    }
    Panel menu; internal Panel soderzh;
    Label nazv;
    Label zanyatoStroka;
    readonly List<Knopa> punkty = new List<Knopa>();
    string tekushchaya = "glavnaya", postroennaya;
    Dictionary<string, object> sost;
    volatile bool zanyat;
    NotifyIcon trey;
    Action temaObrabotchik;
    readonly List<System.Windows.Forms.Timer> taymery = new List<System.Windows.Forms.Timer>();
    // одна подсказка на всё окно: новая на каждую перестройку раздела копила бы окна Windows
    readonly ToolTip podskazki = new ToolTip { InitialDelay = 300, AutoPopDelay = 20000 };

    public Okno(bool nastoyashchee)
    {
        Text = "Сторож сети";
        Size = new Size(900, 620);
        MinimumSize = new Size(760, 480);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        if (proba) { ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000); }   // до создания окна: потом смена пересоздаёт его
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        menu = new Panel { Dock = DockStyle.Left, Width = 204, Padding = new Padding(10, 12, 10, 10) };
        soderzh = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16) };
        zanyatoStroka = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(22, 6, 0, 0), Text = "", AutoEllipsis = true };
        Controls.Add(soderzh); Controls.Add(zanyatoStroka); Controls.Add(menu);

        nazv = new Label { Text = "Сторож сети", Font = new Font("Segoe UI Semibold", 11f), Dock = DockStyle.Top, Height = 46, Padding = new Padding(6, 6, 0, 0) };
        var punktyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        // 21.11: меню без рамок, у каждого раздела свой значок; zapret назван по задаче — «YouTube без VPN»
        foreach (var p in new[] { new[] { "glavnaya", "Монитор", Zn.Monitor }, new[] { "programmy", "Программы", Zn.Prilozheniya }, new[] { "sayty", "Сайты", Zn.Globus },
                                  new[] { "servery", "Серверы", Zn.Vyshka }, new[] { "zapret", "YouTube и Discord", Zn.Play }, new[] { "zhurnal", "Журнал", Zn.Istoriya }, new[] { "nastroyki", "Настройки", Zn.Nastr } })
        {
            var id = p[0];
            var k = new Knopa { Text = p[1], Znachok = p[2], Width = 184, Height = 38, Margin = new Padding(0, 0, 0, 2), Tag = id, Menyu = true };
            k.Click += (s, e) => { Zapis("меню: " + id); Otkryt(id); };
            punkty.Add(k); punktyPanel.Controls.Add(k);
        }
        menu.Controls.Add(punktyPanel); menu.Controls.Add(nazv);

        if (nastoyashchee)
        {
            trey = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Сторож сети" };
            trey.DoubleClick += (s, e) => { if (Vstraivanie.Pokazat()) return; Show(); Vpered(Handle); };
            temaObrabotchik = () => { pal = Vstraivanie.Tema == null ? SvoyaPal() : Palitra.Iz(Vstraivanie.Tema); Perestroit(); };   // перестроить: оттенки карточек считаются от темы
            Vstraivanie.TemaSmenilas += temaObrabotchik;
            Vstraivanie.Podklyuchit(Handle, v => { if (!proba && trey != null) trey.Visible = v; });
            FormClosed += (s, e) => { if (trey != null) trey.Dispose(); };
            Taymer(60000, () => { if (!zanyat) Obnovit(); });
            Taymer(5000, ObnovitSkorosti);
            Taymer(1500, () => { if (zanyat && tekushchaya == "zapret") ObnovitZhurnalSkripta(); });
            Taymer(700, TikHoda);   // ход долгого действия: файл сторожа -> карточка вверху страницы
            Zapis("запуск; пульт: " + Pult.Papka + "; python: " + (Pult.Python ?? "НЕ НАЙДЕН") + "; админ: " + Admin());
        }
        Otkryt("glavnaya");
        if (nastoyashchee) Obnovit();
        // без оболочки окно растягивают руками — разделы перестраиваются под новую ширину (раз, после остановки)
        int shirBylo = ClientSize.Width;
        var tRazmer = new System.Windows.Forms.Timer { Interval = 250 };
        taymery.Add(tRazmer);
        tRazmer.Tick += (s2, e2) => { tRazmer.Stop(); if (Math.Abs(ClientSize.Width - shirBylo) > 16) { shirBylo = ClientSize.Width; Perestroit(); } };
        Resize += (s2, e2) => { tRazmer.Stop(); tRazmer.Start(); };
    }

    void Taymer(int ms, Action d)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (s, e) => d();
        t.Start();
        taymery.Add(t);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var t in taymery) t.Dispose();
            taymery.Clear();
            if (temaObrabotchik != null) { Vstraivanie.TemaSmenilas -= temaObrabotchik; temaObrabotchik = null; }
            if (trey != null) { trey.Dispose(); trey = null; }
            podskazki.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- общее ----------
    public static int OshibokVFone;   // ошибки, пойманные Windows Forms (для самопроверки)

    public static void Zapis(string s)
    {
        try { File.AppendAllText(ZHURNAL, "[" + DateTime.Now.ToString("dd.MM HH:mm:ss") + "] " + s + "\r\n", Encoding.UTF8); } catch { }
    }

    static bool Admin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; }
    }

    void Obnovit(Action posle = null)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var d = Pult.Vyzov("sostoyanie");
            try { BeginInvoke((Action)(() => { sost = d; Perestroit(); if (posle != null) posle(); })); } catch { }
        });
    }

    // долгое действие: строка внизу, пульт в фоне, потом обновить
    static bool suho;   // проверки: действия только пишутся, пульт не зовётся

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

    // Долгое действие: карточка хода вверху страницы, пульт в фоне, потом обновить. false — не начато (идёт другое / проверка).
    bool Deystvie(string chto, string[] arg, Action<Dictionary<string, object>> itog)
    {
        if (suho) { Zapis("[тест] нажато: " + chto); return false; }
        if (zanyat) { NeSeychas(); return false; }
        zanyat = true; deystvieSeychas = chto;
        var h = NachatHod(chto, true);
        Zapis("начал: " + chto + " " + string.Join(" ", arg));
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var d = Pult.VyzovH(h.Put, arg);
            try
            {
                BeginInvoke((Action)(() =>
                {
                    zanyat = false;
                    ZakonchitHod(h, d);
                    if (itog != null) itog(d);
                    Obnovit();
                }));
            }
            catch { }
        });
        return true;
    }

    // уже что-то идёт: второе действие не начинаем, а говорим, что идёт (карточка хода и так вверху)
    void NeSeychas() { zanyatoStroka.Text = "Сейчас идёт «" + deystvieSeychas + "» — дождитесь конца (ход — вверху страницы), потом нажмите ещё раз."; }

    // ---------- ход долгого действия (DOGOVOR-hod.md) ----------
    internal static readonly string HOD_FAYL = Path.Combine(Path.GetTempPath(), "storozh-seti-hod.json");
    // проверки пишут свой файл: настоящий (окна в Коробке) не трогаем
    static readonly string HOD_PROBA = Path.Combine(Path.GetTempPath(), "storozh-seti-hod-proba-" + Process.GetCurrentProcess().Id + ".json");
    internal Hod hod;              // null — карточки нет
    KartaHoda kartaHoda;           // карточка на текущей странице
    bool kNachalu;                 // следующая перестройка — без восстановления прокрутки (показать карточку хода)

    // новое действие: старый файл хода удалить (иначе окно показало бы прошлый ход), карточку показать сразу
    Hod NachatHod(string chto, bool udalitFayl)
    {
        if (udalitFayl) try { if (File.Exists(HOD_FAYL)) File.Delete(HOD_FAYL); } catch (Exception e) { Zapis("файл хода не удалился: " + e.Message); }
        hod = new Hod { Chto = chto, Put = HOD_FAYL };
        kNachalu = true;   // 21.11: нажали кнопку внизу прокрученной страницы — карточка хода вверху была бы не видна
        zanyatoStroka.Text = "Идёт: " + chto + " — ход вверху страницы";
        Perestroit();
        return hod;
    }

    void ZakonchitHod(Hod h, Dictionary<string, object> otvet)
    {
        var d = ProchitatHod(h.Put);
        if (d != null) h.Fayl = d;
        h.Zakonchit(otvet);
        if (zanyatoStroka.Text.StartsWith("Идёт: ")) zanyatoStroka.Text = (h.Ok == true ? "Готово: " : "Не вышло: ") + h.Chto + " — итог вверху страницы";
        Zapis((h.Ok == true ? "Готово: " : "Не вышло: ") + h.Chto + (h.Itog != "" ? " — " + h.Itog : "") + " (заняло " + Vremya(h.Proshlo) + ")");
        if (h == hod && kartaHoda != null && !kartaHoda.IsDisposed) kartaHoda.Pokazat(h);
    }

    // Файл хода читается, пока сторож его пишет: без блокировки (ReadWrite | Delete) и быстро; любая ошибка — пропуск (null).
    internal static Dictionary<string, object> ProchitatHod(string put)
    {
        try
        {
            if (string.IsNullOrEmpty(put) || !File.Exists(put)) return null;
            byte[] b;
            using (var f = new FileStream(put, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (f.Length > 1000000) return null;
                b = new byte[f.Length];
                int n = 0;
                while (n < b.Length) { int r = f.Read(b, n, b.Length - n); if (r <= 0) break; n += r; }
            }
            var t = Encoding.UTF8.GetString(b).TrimStart('\uFEFF').Trim();
            if (!t.StartsWith("{")) return null;
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(t);
        }
        catch { return null; }
    }

    void TikHoda()
    {
        if (hod == null || !hod.Idet) return;
        var d = ProchitatHod(hod.Put);
        if (d != null) hod.Fayl = d;
        if (kartaHoda != null && !kartaHoda.IsDisposed) kartaHoda.Pokazat(hod);
    }

    // карточка хода — первой на странице (на любой странице, пока действие идёт или итог не закрыт)
    void VstavitKartuHoda(Panel str)
    {
        kartaHoda = null;
        if (hod == null || !(str is FlowLayoutPanel)) return;
        var kh = new KartaHoda { Width = Shir, Margin = new Padding(0, 0, 0, 12) };
        kh.Zakryt.Click += (s, e) => { Zapis("кнопка: закрыть итог «" + (hod != null ? hod.Chto : "") + "»"); if (hod != null && !hod.Idet) hod = null; Perestroit(); };
        podskazki.SetToolTip(kh.Zakryt, "Закрыть");
        kh.Pokazat(hod);
        str.Controls.Add(kh);
        str.Controls.SetChildIndex(kh, 0);
        kartaHoda = kh;
    }

    // проверки: ход из набора подделки ("~hod") — через настоящий файл и настоящее чтение
    void PoddelnyyHod(string json)
    {
        Dictionary<string, object> d;
        try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return; }
        if (d == null) return;
        double proshlo = Pult.ChIliNet(Pult.G(d, "proshlo")) ?? 0;
        var h = new Hod { Chto = Pult.G(d, "chto") as string ?? "", Put = HOD_PROBA, Nachalo = DateTime.Now.AddSeconds(-proshlo) };
        try { if (File.Exists(HOD_PROBA)) File.Delete(HOD_PROBA); } catch { }
        var f = Pult.G(d, "fayl");
        try
        {
            if (f is Dictionary<string, object>)
            {
                var fd = new Dictionary<string, object>((Dictionary<string, object>)f);
                double nazad = Pult.ChIliNet(Pult.G(fd, "obnovleno_nazad")) ?? 0;
                fd.Remove("obnovleno_nazad");
                fd["nachalo"] = Hod.Seychas() - proshlo;
                fd["obnovleno"] = Hod.Seychas() - nazad;
                File.WriteAllText(HOD_PROBA, Pult.Json(fd), new UTF8Encoding(false));
            }
            else if (f is string) File.WriteAllText(HOD_PROBA, (string)f, new UTF8Encoding(false));
        }
        catch { }
        h.Fayl = ProchitatHod(HOD_PROBA);
        hod = h;
        if (!Pult.B(d, "idet")) h.Zakonchit(Pult.D(Pult.G(d, "otvet")));
    }

    static void UbratProbu() { try { if (File.Exists(HOD_PROBA)) File.Delete(HOD_PROBA); } catch { } }

    void Otkryt(string id)
    {
        tekushchaya = id;
        Perestroit();
    }

    // меню: на узком окне — одни значки (подписи — в подсказке), чтобы разделу хватило ширины
    void PodognatMenu()
    {
        bool uzko = ClientSize.Width < 900;
        int w = uzko ? 64 : 204;
        if (menu.Width != w) menu.Width = w;
        nazv.Visible = !uzko;
        foreach (var k in punkty)
        {
            k.Width = uzko ? 44 : 184;
            podskazki.SetToolTip(k, uzko ? k.Text : null);
        }
    }

    bool oshibkaRazdela;   // последняя перестройка упала (для самопроверки)

    void Perestroit()
    {
        // прокрутку и поле ввода сохраняем: раздел перестраивается раз в минуту сам
        Point prokrutka = Point.Empty;
        string fokus = null; int kursor = -1;
        var staraya = soderzh.Controls.Count > 0 ? soderzh.Controls[0] as ScrollableControl : null;
        if (staraya != null && postroennaya == tekushchaya)
        {
            prokrutka = new Point(-staraya.AutoScrollPosition.X, -staraya.AutoScrollPosition.Y);
            var f = NaytiFokus(staraya);
            if (f != null) { fokus = f.Name; if (f is TextBox) kursor = ((TextBox)f).SelectionStart; }
        }
        if (kNachalu) { prokrutka = Point.Empty; kNachalu = false; }
        var starye = soderzh.Controls.Cast<Control>().ToArray();
        oshibkaRazdela = false;
        zhivye.Clear();
        podskazki.RemoveAll();
        PodognatMenu();
        soderzh.SuspendLayout();
        Panel p = null;
        try
        {
            soderzh.Controls.Clear();
            switch (tekushchaya)
            {
                case "programmy": p = StrProgrammy(); break;
                case "sayty": p = StrSayty(); break;
                case "servery": p = StrServery(); break;
                case "zapret": p = StrZapret(); break;
                case "zhurnal": p = StrZhurnal(); break;
                case "nastroyki": p = StrNastroyki(); break;
                default: p = StrGlavnaya(); break;
            }
            VstavitKartuHoda(p);
            p.Dock = DockStyle.Fill;
            soderzh.Controls.Add(p);
            postroennaya = tekushchaya;

        }
        catch (Exception e)
        {
            // 19.11: в Коробке раздел оставался пустым. Ошибка раздела — видна и в окне, и в журнале.
            oshibkaRazdela = true;
            p = null;
            Zapis("ОШИБКА раздела " + tekushchaya + ": " + e.GetType().Name + ": " + e.Message + " | " + (e.StackTrace ?? "").Replace(Environment.NewLine, " / "));
            soderzh.Controls.Clear();
            soderzh.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Не получилось показать раздел: " + e.Message + Environment.NewLine + "Подробности — в журнале программы. Нажмите раздел ещё раз.", Padding = new Padding(10) });
            postroennaya = null;
        }
        finally { soderzh.ResumeLayout(); }
        // колонка — строго по центру видимой части: отступ слева пересчитать, когда страница уже встала на место
        // и известно, есть ли полоса прокрутки
        try
        {
            if (p is FlowLayoutPanel)
            {
                p.PerformLayout();
                // с полосой прокрутки правый отступ окна оказывается за ней — центр считаем между меню и полосой
                int sd = Math.Max(0, (p.ClientSize.Width - Shir - (p.VerticalScroll.Visible ? soderzh.Padding.Right : 0)) / 2);
                if (Math.Abs(p.Padding.Left - sd) > 1) { p.Padding = new Padding(sd, 0, 0, 0); p.PerformLayout(); }
            }
        }
        catch { }
        try
        {
            if (p != null && prokrutka != Point.Empty) { p.PerformLayout(); p.AutoScrollPosition = prokrutka; }
            if (p != null && fokus != null)
            {
                var c = p.Controls.Find(fokus, true).FirstOrDefault();
                if (c != null) { c.Focus(); if (c is TextBox && kursor >= 0) ((TextBox)c).SelectionStart = Math.Min(kursor, c.Text.Length); }
            }
        }
        catch { }
        // Старую страницу — освободить. Controls.Clear() её не уничтожает: окна Windows копились бы
        // с каждой перестройкой (раз в минуту), пока у программы не кончатся дескрипторы.
        // Не сразу: кнопка, нажатая на старой странице, ещё внутри своего обработчика.
        if (starye.Length > 0)
        {
            if (IsHandleCreated) BeginInvoke((Action)(() => { foreach (var c in starye) c.Dispose(); }));
            else foreach (var c in starye) c.Dispose();
        }
        try { Perekrasit(); } catch (Exception e) { Zapis("ОШИБКА покраски: " + e.Message); }
    }

    static Control NaytiFokus(Control r)
    {
        foreach (Control c in r.Controls)
        {
            if (c.Focused && !string.IsNullOrEmpty(c.Name)) return c;
            var v = NaytiFokus(c);
            if (v != null) return v;
        }
        return null;
    }

    // ---------- вид ----------
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    bool zagolovokTemnyy;
    // заголовок отдельного окна — в тон палитре: на тёмной — тёмный (20 — Windows 11 и новые 10, 19 — старые 10)
    void ZagolovokOkna()
    {
        if (!IsHandleCreated || zagolovokTemnyy == pal.Temnaya) return;
        int v = pal.Temnaya ? 1 : 0;
        try { if (DwmSetWindowAttribute(Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { }
        zagolovokTemnyy = pal.Temnaya;
        try { SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); } catch { }   // перерисовать рамку
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); zagolovokTemnyy = !pal.Temnaya; ZagolovokOkna(); }

    void Perekrasit()
    {
        ZagolovokOkna();
        BackColor = pal.Fon; ForeColor = pal.Tekst;
        menu.BackColor = pal.Poverhnost; soderzh.BackColor = pal.Fon;
        zanyatoStroka.BackColor = pal.Fon; zanyatoStroka.ForeColor = pal.Vtorichnyy;
        foreach (var k in punkty) { k.Aktivna = (string)k.Tag == tekushchaya; k.Pal = pal; }
        Krasit(menu); Krasit(soderzh);
        Invalidate(true);
    }

    void Krasit(Control r)
    {
        foreach (Control c in r.Controls)
        {
            if (c is Knopa) { if (!punkty.Contains((Knopa)c)) ((Knopa)c).Pal = pal; }
            else if (c is Kartochka) { ((Kartochka)c).Pal = pal; c.Invalidate(); }
            else if (c is Grafik2) { ((Grafik2)c).Pal = pal; c.Invalidate(); }
            else if (c is Iskra) { ((Iskra)c).Pal = pal; c.Invalidate(); }
            else if (c is ZnakSost) { ((ZnakSost)c).Pal = pal; c.Invalidate(); }
            else if (c is Chislo) { ((Chislo)c).Pal = pal; c.Invalidate(); }
            else if (c is PictureBox) c.BackColor = Color.Transparent;
            else if (c is PolosaSutok) { ((PolosaSutok)c).Pal = pal; c.Invalidate(); }
            else if (c is Segmenty) { ((Segmenty)c).Pal = pal; c.Invalidate(); }
            else if (c is Tumbler) { ((Tumbler)c).Pal = pal; c.Invalidate(); }
            else if (c is Uroven) { ((Uroven)c).Pal = pal; c.Invalidate(); }
            else if (c is TextBox || c is ComboBox || c is NumericUpDown)
            {
                c.BackColor = pal.Poverhnost; c.ForeColor = pal.Tekst;
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, pal.Temnaya ? "DarkMode_Explorer" : "Explorer", null);
                else c.HandleCreated += (s, e) => SetWindowTheme(((Control)s).Handle, pal.Temnaya ? "DarkMode_Explorer" : "Explorer", null);
            }
            else if (c is Grafik) { ((Grafik)c).Pal = pal; c.Invalidate(); }
            else if (c is Label)
            {
                var l = (Label)c;
                l.Text = Pult.Chisto(l.Text);
                if (l.Tag is Color) l.ForeColor = (Color)l.Tag;
                else if (l.Tag is string && (string)l.Tag != "vt") l.ForeColor = CvetTeksta((string)l.Tag);
                else l.ForeColor = "vt".Equals(l.Tag) ? pal.Vtorichnyy : pal.Tekst;
                l.BackColor = Color.Transparent;
            }
            else if (c is CheckBox) { c.ForeColor = pal.Tekst; c.BackColor = Color.Transparent; }
            else if (c is Panel && "kartochka".Equals(c.Tag)) c.BackColor = pal.Poverhnost;
            else if (c is Panel)
            {
                c.BackColor = Color.Transparent;
                if (((Panel)c).AutoScroll) { if (c.IsHandleCreated) SetWindowTheme(c.Handle, pal.Temnaya ? "DarkMode_Explorer" : "Explorer", null); else c.HandleCreated += (s2, e2) => SetWindowTheme(((Control)s2).Handle, pal.Temnaya ? "DarkMode_Explorer" : "Explorer", null); }
            }
            Krasit(c);
        }
    }

    // заготовки элементов
    // цвет текста состояния: тот же зелёный/жёлтый/красный, что у значков, — темнее на светлой теме, светлее на тёмной
    Color CvetTeksta(string sost)
    {
        if (sost == "akc") return Ris.AkcentV(pal);
        if (sost != "ok" && sost != "zhelt" && sost != "kras") return sost == "sery" ? pal.Vtorichnyy : pal.Tekst;
        var c = ZnakSost.Cvet(sost);
        return pal.Temnaya ? Ris.Smes(c, Color.White, 0.3) : Ris.Smes(c, Color.Black, sost == "zhelt" ? 0.38 : 0.28);
    }
    Knopa Kn(string t, Action d, bool glavnaya = false, int w = KN_W)
    {
        var k = new Knopa { Text = t, Width = w, Height = KN_H, Glavnaya = glavnaya, Margin = new Padding(0, 0, 10, 8) };
        k.Click += (s, e) => { Zapis("кнопка: " + t); d(); };
        return k;
    }
    // страница: прокрутка только вниз — без полосы вбок и светлого квадрата в углу между полосами
    FlowLayoutPanel Kolonka()
    {
        var k = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Sdvig, 0, 0, 0) };
        k.HorizontalScroll.Maximum = 0; k.AutoScroll = false; k.HorizontalScroll.Visible = false; k.AutoScroll = true;
        return k;
    }
    FlowLayoutPanel Ryad() { return new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8) }; }

    // ---------- Настройки: сохранить одну ----------
    void Sohranit(string key, object val)
    {
        if (suho) { Zapis("[тест] настройка " + key + " = " + val); return; }
        zanyatoStroka.Text = "Сохраняю…";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var d = Pult.Vyzov("nastroyki-zadat", Pult.Json(new Dictionary<string, object> { { key, val } }));
            bool ok = !Pult.Sboy(d) && Pult.B(d, "ok");
            Zapis("настройка " + key + " = " + val + ": " + (ok ? "сохранена" : "не сохранилась — " + Pult.Info(d)));
            try { BeginInvoke((Action)(() => { zanyatoStroka.Text = ok ? "Сохранено" : "Не сохранилось: " + Pult.Info(d); Obnovit(); })); } catch { }
        });
    }

    // ---------- проверки окна ----------
    static void Zhdat(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
    }

    // все данные сразу из пульта (в проверках — из подделки)
    void ZagruzitVse()
    {
        sost = Pult.Vyzov("sostoyanie"); progData = Pult.Vyzov("programmy"); servData = Pult.Vyzov("servery");
        saytyIst = Pult.Vyzov("sayty-istoriya"); lentaData = Pult.Vyzov("lenta");
        setData = Pult.Vyzov("set-seychas"); skorData = Pult.Vyzov("skorosti");
        if (Pult.Poddelka != null)
        {
            zapData = Pult.Vyzov("zapret");
            testData = Pult.Poddelka.ContainsKey("test-seti") || Pult.Poddelka.ContainsKey("*") && !Pult.Poddelka.ContainsKey("sostoyanie") ? Pult.Vyzov("test-seti") : null;
            saytProverka = Pult.Poddelka.ContainsKey("proverit-sayt") || !Pult.Poddelka.ContainsKey("sostoyanie") ? Pult.Vyzov("proverit-sayt") : null;
            saytPole = Pult.S(saytProverka, "domen");
        }
        else
        {
            // настоящие данные: то, что ходит в сеть, — примером
            zapData = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"youtube\":[\"204\",0.12,true],\"golos\":{\"est\":true,\"poteri\":8.8,\"ping\":71},\"versiya\":\"1.10.4\",\"posledn\":\"1.10.5\"}");
            testData = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"ok\":true,\"vpn\":{\"mbit\":96,\"ping\":112},\"pryamo\":{\"mbit\":410,\"ping\":9},\"server\":\"Литва\"}");
            saytProverka = null;
        }
        izmeneniya.Clear();
        hod = null;
        string hj;
        if (Pult.Poddelka != null && Pult.Poddelka.TryGetValue("~hod", out hj)) PoddelnyyHod(hj);
    }

    static readonly string[] RAZDELY = { "glavnaya", "programmy", "sayty", "servery", "zapret", "zhurnal", "nastroyki" };

    // Самопроверка без оболочки: все разделы на настоящих и поддельных данных («как у друга», «всё сломано»…)
    // в пяти размерах окна — падения, ошибки в фоне, элементы за краем, наложения, утечка окон Windows,
    // и вынос окна из оболочки. Код возврата — число замечаний.
    public static int Samoproverka()
    {
        Console.WriteLine("python: " + (Pult.Python ?? "НЕ НАЙДЕН"));
        suho = true; proba = true;
        int plohih = 0;
        // настоящие данные: пульт спрашивается один раз (только чтение, без сети), дальше — как подделка
        var nabory = new List<Tuple<string, Dictionary<string, string>>> { Tuple.Create("настоящие данные", Poddelki.IzNastoyashchih()) };
        nabory.AddRange(Poddelki.Vse());
        var razmery = new[] { new Size(760, 480), new Size(900, 620), new Size(1078, 694), new Size(1600, 950), new Size(1658, 1100) };
        foreach (var nabor in nabory)
        {
            Pult.Poddelka = nabor.Item2;
            int bylo = plohih, fonDo = OshibokVFone;
            using (var o = new Okno(false))
            {
                o.Show(); o.Location = new Point(-20000, -20000);
                o.pokazanoObryvov.Clear();
                foreach (var r in razmery)
                {
                    o.Size = r; Zhdat(30);
                    foreach (var s in RAZDELY)
                    {
                        string gde = "[" + nabor.Item1 + "] " + r.Width + "x" + r.Height + " " + s + ": ";
                        try { o.ZagruzitVse(); o.Otkryt(s); Zhdat(40); }
                        catch (Exception e) { Console.WriteLine("ПАДЕНИЕ " + gde + e.GetType().Name + ": " + e.Message); plohih++; continue; }
                        if (o.oshibkaRazdela) { Console.WriteLine("ПАДЕНИЕ " + gde + "раздел показал ошибку (подробности в журнале)"); plohih++; continue; }
                        if (o.soderzh.Controls.Count == 0) { Console.WriteLine("ПУСТО " + gde + "раздел не построился"); plohih++; continue; }
                        foreach (var pm in o.punkty) if (!pm.Vlezaet()) { Console.WriteLine("ЗАМЕЧАНИЕ " + gde + "пункт меню не влезает: «" + pm.Text + "»" + (pm.Aktivna ? " (выбранный)" : "")); plohih++; }
                        foreach (var z in Vylezaet(o.soderzh).Concat(Nalezaet(o.soderzh)).Concat(ShireOkna(o.soderzh)).Concat(o.Osoboe(nabor.Item1, s)).Concat(o.Pravila(s)).Distinct())
                        { Console.WriteLine("ЗАМЕЧАНИЕ " + gde + z); plohih++; }
                    }
                }
                // одни и те же числа на разных экранах: обрывы сервера на Мониторе и в «Серверах», число обрывов связи и сумма по часам
                if (nabor.Item1 != "кривые данные")
                    foreach (var kv in o.pokazanoObryvov.Where(x => x.Key.StartsWith("glavnaya:")).ToList())
                    {
                        int v; string imya = kv.Key.Substring("glavnaya:".Length);
                        if (o.pokazanoObryvov.TryGetValue("servery:" + imya, out v) && v != kv.Value) { Console.WriteLine("ЗАМЕЧАНИЕ [" + nabor.Item1 + "] обрывы сервера " + imya + ": Монитор " + kv.Value + ", Серверы " + v); plohih++; }
                    }
                int ch1, ch2;
                if (nabor.Item1 != "кривые данные" && o.pokazanoObryvov.TryGetValue("svyaz:chislo", out ch1) && o.pokazanoObryvov.TryGetValue("svyaz:chasy", out ch2) && ch1 != ch2)
                { Console.WriteLine("ЗАМЕЧАНИЕ [" + nabor.Item1 + "] обрывы связи: число " + ch1 + ", а по часам " + ch2); plohih++; }
                if (OshibokVFone > fonDo) { Console.WriteLine("ОШИБКИ В ФОНЕ [" + nabor.Item1 + "]: " + (OshibokVFone - fonDo) + " (подробности в журнале окна)"); plohih += OshibokVFone - fonDo; }
                // утечка: раздел перестраивается раз в минуту — окна Windows не должны копиться
                if (nabor.Item2 != null && (nabor.Item1 == "всё полно" || nabor.Item1 == "ход: середина"))
                {
                    o.Size = new Size(1078, 694); o.Otkryt("glavnaya"); Zhdat(100);
                    uint do_ = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                    for (int n = 0; n < 40; n++) { o.Otkryt(n % 2 == 0 ? "glavnaya" : "programmy"); Zhdat(15); }
                    o.Otkryt("glavnaya"); Zhdat(200);
                    uint posle = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
                    Console.WriteLine("окна Windows за 40 перестроек: было " + do_ + ", стало " + posle);
                    if (posle > do_ + 40) { Console.WriteLine("УТЕЧКА: окна Windows копятся (было " + do_ + ", стало " + posle + ")"); plohih++; }
                }
            }
            Console.WriteLine("набор «" + nabor.Item1 + "»: " + (plohih == bylo ? "в порядке" : "замечаний " + (plohih - bylo)));
        }
        Pult.Poddelka = null; Pult.TolkoChtenie = false;
        // тёмная палитра — тёмный заголовок отдельного окна (снимки DrawToBitmap рамку так не рисуют — проверяем само окно)
        Pult.Poddelka = Poddelki.Polnye();
        using (var o = new Okno(false))
        {
            o.Show(); o.Location = new Point(-20000, -20000); Zhdat(50);
            int v = -1;
            bool est = DwmGetWindowAttribute(o.Handle, 20, out v, 4) == 0 || DwmGetWindowAttribute(o.Handle, 19, out v, 4) == 0;
            Console.WriteLine("тёмный заголовок окна на тёмной палитре: " + (!est ? "Windows не сообщает" : v == 1 ? "в порядке" : "НЕТ"));
            if (est && v != 1) plohih++;
        }
        Pult.Poddelka = null;
        int zh = ProbaZhivogoHoda();
        Console.WriteLine("живой ход (файл меняется, карточка обновляется на месте): " + (zh == 0 ? "в порядке" : "замечаний " + zh));
        plohih += zh;
        UbratProbu();
        // вынос из оболочки — последовательность Коробки
        int vyn = ProbaVynos(null, false);
        Console.WriteLine("вынос из оболочки: " + (vyn == 0 ? "в порядке" : "замечаний " + vyn));
        plohih += vyn;
        Console.WriteLine(plohih == 0 ? "ИТОГ: всё в порядке" : "ИТОГ: замечаний " + plohih);
        return plohih;
    }

    // Особые проверки (21.11) — то, что он выбрал по концептам: карточка хода вверху каждой страницы, плитки на Мониторе
    // (они же — первичная настройка новичка), значок Claude — малой плиткой, только если сторож за ним следит.
    IEnumerable<string> Osoboe(string nabor, string razdel)
    {
        var str = soderzh.Controls.Count > 0 ? soderzh.Controls[0] : null;
        if (str == null || Pult.Poddelka == null) yield break;
        bool zhdemHod = Pult.Poddelka.ContainsKey("~hod");
        var kh = str.Controls.OfType<KartaHoda>().FirstOrDefault();
        if (!zhdemHod) { if (kh != null) yield return "карточка хода есть, хотя ничего не идёт"; }
        else if (kh == null || !kh.Visible) yield return "нет карточки хода вверху страницы";
        else
        {
            if (str.Controls.GetChildIndex(kh) != 0) yield return "карточка хода не первая на странице";
            if (kh.NizSoderzhimogo > kh.Height) yield return "карточка хода: текст ниже края (" + kh.NizSoderzhimogo + " > " + kh.Height + ")";
            string t = string.Join(" | ", kh.Teksty);
            Func<string, bool> est = x => t.IndexOf(x, StringComparison.Ordinal) >= 0;
            switch (nabor)
            {
                case "ход: середина":
                    if (!est("Замер серверов — 48%")) yield return "карточка хода: нет заголовка «Замер серверов — 48%»: " + t;
                    if (!est("осталось ~1 мин 20 с")) yield return "карточка хода: нет «осталось ~1 мин 20 с»: " + t;
                    if (!est("Меряю скорость: 7 из 14")) yield return "карточка хода: нет подробности текущего шага: " + t;
                    if (kh.Sdelano != 1 || kh.Tekushchiy != 1 || kh.Vperedi != 2) yield return "карточка хода: шаги не так (сделано " + kh.Sdelano + ", текущий " + kh.Tekushchiy + ", впереди " + kh.Vperedi + ")";
                    if (est("Нет вестей")) yield return "карточка хода: «нет вестей» при свежем ходе";
                    if (kh.Zakryt.Visible) yield return "карточка хода: крестик виден, пока действие идёт";
                    break;
                case "ход: без процентов":
                    if (!est("Замер серверов — идёт…") || !est("прошло 1 мин 5 с")) yield return "карточка хода без процентов: нет «идёт…» и времени: " + t;
                    if (est("%")) yield return "карточка хода без процентов: проценты взялись ниоткуда: " + t;
                    break;
                case "ход: молчит":
                    if (!est("Нет вестей уже 2 мин 0 с")) yield return "карточка хода: нет «нет вестей от замера»: " + t;
                    break;
                case "ход: конец ok":
                    // итог сторожа «Выбран Germany 1, 260 Мбит/с» окно показывает по-человечески
                    if (!est("Готово: Замер серверов") || !est("Выбран Германия 1, 33 МБ/с")) yield return "карточка хода: нет итога «Готово» в МБ/с и по-русски: " + t;
                    if (!kh.Zakryt.Visible) yield return "карточка хода: нет крестика «закрыть» после конца";
                    break;
                case "ход: конец с ошибкой":
                    if (!est("Не вышло: Замер серверов") || !est("Karing не запущен — запустите его")) yield return "карточка хода: нет итога «Не вышло»: " + t;
                    if (!kh.Zakryt.Visible) yield return "карточка хода: нет крестика «закрыть» после конца";
                    break;
            }
        }
        // новичок: беда «Karing не запущен» — одинаково на всех экранах, где без Karing не работает: полоса сверху с кнопкой
        if (nabor == "как у друга" && (razdel == "programmy" || razdel == "servery" || razdel == "sayty"))
        {
            var pb = str.Controls.Find("polosa-bedy", false).FirstOrDefault();
            if (pb == null || !Elementy(pb).Any(c => c is Knopa && c.Text == "Запустить Karing")) yield return "у друга нет полосы «Karing (VPN) не запущен» с кнопкой";
        }
        if (razdel != "glavnaya" || sost == null || Pult.Sboy(sost)) yield break;
        if (str.Controls.Find("karta-nastroyki", true).Any()) yield return "осталась отдельная карточка «Настройка» — её заменили плитки";
        // плитки: четыре области, у каждой беды — кнопка
        var plitki = new[] { "plit-vpn", "plit-stabil", "plit-youtube", "plit-avto" }.Select(id => str.Controls.Find(id, true).FirstOrDefault()).ToList();
        if (plitki.Any(x => x == null)) { yield return "нет плиток VPN / Стабильность / YouTube без VPN / Автопроверка"; yield break; }
        foreach (var pl in plitki)
        {
            string ss = pl.AccessibleDescription ?? "";
            if (ss != "ok" && ss != "zhelt" && ss != "kras" && ss != "sery") yield return "плитка «" + pl.AccessibleName + "»: непонятное состояние «" + ss + "»";
            // у «Стабильности» кнопки может не быть: полоса обрывов — прямо под ней; «Выбрать лучший сервер» — только когда поможет
            if ((ss == "zhelt" || ss == "kras") && pl.Name != "plit-stabil" && !Elementy(pl).Any(c => c is Knopa && c.Visible)) yield return "плитка «" + pl.AccessibleName + "» в беде, а кнопки нет";
            var knp = Elementy(pl).OfType<Knopa>().Where(c => c.Visible).ToList();
            if (knp.Select(c => c.Top).Distinct().Count() > 1) yield return "плитка «" + pl.AccessibleName + "»: кнопки не в одну строку";
            if (ss == "ok" && Elementy(pl).Any(c => c is Knopa && c.Visible)) yield return "плитка «" + pl.AccessibleName + "» в порядке, а кнопка есть";
        }
        var shapka = str.Controls.Find("shapka", false).FirstOrDefault();
        var znak = shapka == null ? null : shapka.Controls.OfType<ZnakSost>().FirstOrDefault();
        if (znak == null) yield return "нет фразы общего состояния со значком";
        else
        {
            string obshch = znak.Sost;
            var sost4 = plitki.Select(x => x.AccessibleDescription).ToList();
            if (obshch == "ok" && sost4.Any(x => x == "zhelt" || x == "kras")) yield return "«Всё работает» рядом с жёлтой или красной плиткой";
            if (obshch == "ok" && Elementy(str).Any(c => c is Knopa && ((Knopa)c).Glavnaya && c.Visible && c.Enabled)) yield return "всё работает, а синяя кнопка есть";
            // Claude в «Требует внимания» не попадает: с ним нечего делать (сервер закреплён намеренно)
            if (shapka.Controls.OfType<Label>().Any(l => l.Text.StartsWith("Требует внимания") && l.Text.Contains("Claude"))) yield return "Claude в строке «Требует внимания»";
        }
        switch (nabor)
        {
            case "как у друга":
                if (znak != null && znak.Sost != "kras") yield return "у друга (Karing выключен) итог не красный";
                if (plitki[0].AccessibleDescription != "kras" || !Elementy(plitki[0]).Any(c => c is Knopa && c.Text == "Запустить Karing" && ((Knopa)c).Glavnaya)) yield return "у друга нет красной плитки VPN с главной кнопкой «Запустить Karing»";
                if (plitki[2].AccessibleDescription == "kras") yield return "у друга красная плитка zapret (он не обязателен)";
                if (plitki[1].AccessibleDescription != "sery") yield return "у друга (данных нет) плитка «Стабильность» не серая";
                foreach (var nk in new[] { "Включить", "Установить zapret", "Не нужно" })
                    if (!Elementy(str).Any(c => c is Knopa && c.Text == nk && c.Visible)) yield return "у друга нет кнопки «" + nk + "» в плитках";
                if (Vse(str).OfType<PolosaSutok>().Any()) yield return "у друга пустая шкала суток вместо одной строки";
                // беда про Karing — ровно один раз (плитка VPN), «нет данных» — один раз (плитка «Стабильность»)
                var pro = Vse(str).OfType<Label>().Where(l => VidnoVsyo(l) && l.Text.Contains("Karing")).Select(l => l.Text).ToList();
                if (pro.Count > 1) yield return "у друга Karing упомянут " + pro.Count + " раз: " + string.Join(" | ", pro);
                var nd = Vse(str).OfType<Label>().Where(l => VidnoVsyo(l) && l.Text.IndexOf("нет данных", StringComparison.OrdinalIgnoreCase) >= 0).Select(l => l.Text).ToList();
                if (nd.Count > 1) yield return "у друга «нет данных» " + nd.Count + " раз: " + string.Join(" | ", nd);
                break;
            case "всё хорошо":
                if (znak != null && znak.Sost != "ok") yield return "всё хорошо, а итог не зелёный";
                break;
            case "всё полно":
                if (znak != null && znak.Sost != "zhelt") yield return "есть обрывы и выключена автопроверка, а итог не жёлтый";
                break;
        }
        // значок Claude — компактной меткой в шапке; жёлтый, если его сервер за сутки обрывался («всё полно»: Англия — 19 обрывов)
        var z = str.Controls.Find("claude-znachok", true).FirstOrDefault();
        string zhdem = nabor == "Claude: красный" ? "kras" : nabor == "Claude: серый" ? "sery" : nabor == "Claude: зелёный" || nabor == "всё хорошо" ? "ok" :
                       nabor == "всё полно" || nabor == "Claude: нестабилен" || nabor.StartsWith("ход:") ? "zhelt" : null;
        if (nabor == "Claude: не показывать" || nabor == "как у друга" || nabor.StartsWith("настройка:")) { if (z != null) yield return "значок Claude виден, хотя pokazat = false"; }
        else if (zhdem != null)
        {
            if (z == null) yield return "нет значка Claude";
            else
            {
                var zz = z.Controls.OfType<ZnakSost>().FirstOrDefault();
                if (zz == null || zz.Sost != zhdem) yield return "значок Claude: состояние не то (" + (zz == null ? "нет" : zz.Sost) + ", ждали " + zhdem + ")";
                if (!TekstyVnutri(z).Any(x => x.StartsWith("Claude"))) yield return "значок Claude без надписи «Claude»";
                if (z.Parent == null || z.Parent.Name != "shapka") yield return "значок Claude не в шапке Монитора";
                if (z is Kartochka) yield return "значок Claude выглядит как кнопка (карточка с рамкой)";
                if (z.Cursor != Cursors.Hand) yield return "значок Claude не нажимается (должен открывать «Серверы»)";
                if (string.IsNullOrEmpty(podskazki.GetToolTip(z))) yield return "у значка Claude нет подсказки";
            }
        }
    }

    // Правила разбора DIZAYN-2111.md, которые проверяются на любом экране и любых данных
    static readonly float[] RAZMERY_SHRIFTA = { 9f, 10f, 11f, 15f, 18f };
    static readonly string[] NELZYA_V_TEKSTE = { "Мбит", "÷", ".log", "НЕ ВЫШЛО", "((", "))", "Germany", "Traceback", "WinError", "urlopen", "Python)", "две первые",
                                                 "Karing выключен", "Karing (VPN) выключен", "Мигания", "по правилам Karing", ".exe", "Жду, пока", "не перевожу", "Чиню" };
    // старые имена действий (одно имя — одно действие): «Поставить» → «Подключить» / «Установить zapret», «Проверить» → по смыслу
    static readonly string[] STARYE_KNOPKI = { "Поставить", "Проверить", "Замерить", "Показать обрывы", "Проверить снова", "Скачать и поставить zapret", "Лучший сервер" };
    IEnumerable<string> Pravila(string razdel)
    {
        var str = soderzh.Controls.Count > 0 ? soderzh.Controls[0] : null;
        if (str == null) yield break;
        var vse = Vse(str);
        // одна синяя кнопка на экран (меню не в счёт)
        var sinie = vse.OfType<Knopa>().Where(k => k.Glavnaya && k.Visible && k.Enabled).ToList();
        if (sinie.Count > 1) yield return "синих кнопок " + sinie.Count + ": " + string.Join(", ", sinie.Select(k => "«" + k.Text + "»"));
        // одна шапка, в ней не больше одной кнопки
        var shapki = str.Controls.Cast<Control>().Where(c => c.Name == "shapka").ToList();
        if (shapki.Count != 1) yield return "шапок на экране: " + shapki.Count;
        else if (shapki[0].Controls.OfType<Knopa>().Count() > 1) yield return "в шапке больше одной кнопки";
        // сносок нет: весь текст — в карточках и шапке
        foreach (Control c in str.Controls) if (c is Label && c.Visible) yield return "текст прямо на странице (сноска): «" + c.Text + "»";
        foreach (var c in vse)
        {
            if (!VidnoVsyo(c)) continue;
            var l = c as Label;
            if (l != null && l.Font.FontFamily.Name != Zn.Semya)
            {
                if (!RAZMERY_SHRIFTA.Contains((float)Math.Round(l.Font.SizeInPoints, 1))) yield return "шрифт " + l.Font.SizeInPoints + " у «" + l.Text + "» (можно 9, 10, 11, 15 и 18)";
                int tw = TextRenderer.MeasureText(l.Text, l.Font).Width;
                // пояснения не режутся многоточием (имена — можно, у них подсказка)
                if (!(l is NadpisImya) && !l.AutoSize && tw > l.Width + 4) yield return "обрезано: «" + l.Text + "» (" + tw + " > " + l.Width + ")";
                if (l is NadpisImya && tw > l.Width + 4 && string.IsNullOrEmpty(podskazki.GetToolTip(l))) yield return "имя обрезано без подсказки: «" + l.Text + "»";
                foreach (var x in NELZYA_V_TEKSTE) if (l.Text.Contains(x)) yield return "служебное или сырое в тексте («" + x + "»): «" + l.Text + "»";
            }
            var kn = c as Knopa;
            if (kn != null && !kn.Vlezaet()) yield return "подпись кнопки не влезает: «" + kn.Text + "» (" + kn.Width + " px)";
            if (kn != null && STARYE_KNOPKI.Contains(kn.Text)) yield return "старое имя действия на кнопке: «" + kn.Text + "»";
            if (kn != null && kn.Text == "Проверить и починить" && razdel != "glavnaya") yield return "«Проверить и починить» не на Мониторе";
            if (kn != null && kn.Tihaya && kn.Width >= 70) yield return "голый текст вместо кнопки: «" + kn.Text + "»";
            var sg = c as Segmenty;
            if (sg != null && !sg.Vlezaet()) yield return "подписи выбора не влезают: «" + string.Join("|", sg.Punkty) + "» (" + sg.Width + " px)";
        }
        var kh = str.Controls.OfType<KartaHoda>().FirstOrDefault();
        if (kh != null) foreach (var t in kh.Teksty) foreach (var x in NELZYA_V_TEKSTE) if (t.Contains(x)) yield return "служебное в карточке хода («" + x + "»): «" + t + "»";
        // подпись и её переключатель рядом
        foreach (var t in vse.OfType<Tumbler>()) if (t.Left > NASTR_W) yield return "переключатель далеко от подписи: x = " + t.Left;
        // полосы теста — не синие (синий — цвет главной кнопки); строки журнала — не шире удобного для чтения
        foreach (var u in vse.OfType<Uroven>().Where(u => u.Name == "polosa-testa")) if (!u.Neytralnaya) yield return "полоса теста синяя";
        var kz = str.Controls.Find("karta-zhurnala", false).FirstOrDefault();
        if (kz != null) foreach (var l in kz.Controls.OfType<Label>()) if (l.Width > ZHURNAL_SHIR + 4) yield return "строка журнала шире " + ZHURNAL_SHIR + " px: " + l.Width;
        // одна ширина содержимого на всех экранах: каждая карточка и ряд — во всю колонку, как шапка
        var sh = shapki.FirstOrDefault();
        if (sh != null)
            foreach (Control c in str.Controls)
                if (c.Visible && c != sh && !(c is FlowLayoutPanel) && !(c is Segmenty) && (c is Kartochka || c is Panel) && c.Width != sh.Width)
                    yield return "ширина не как у шапки: " + c.Name + " " + c.Width + " ≠ " + sh.Width;
    }
    static List<Control> Vse(Control r) { var l = new List<Control>(); foreach (Control c in r.Controls) { l.Add(c); l.AddRange(Vse(c)); } return l; }
    static bool VidnoVsyo(Control c) { for (var r = c; r != null; r = r.Parent) if (!r.Visible) return false; return true; }

    // Живой ход: сторож переписывает файл, окно читает его таймером — карточка та же (страница не перестраивается),
    // недописанный файл и файл, открытый писателем, не сбивают показ; конец — итог и крестик; крестик убирает карточку.
    static int ProbaZhivogoHoda()
    {
        int plohih = 0;
        Action<string> ploho = z => { Console.WriteLine("ЗАМЕЧАНИЕ [живой ход] " + z); plohih++; };
        Action<Dictionary<string, object>> pisat = d => File.WriteAllText(HOD_PROBA, Pult.Json(d), new UTF8Encoding(false));
        Func<int, int, int?, string, double, bool, Dictionary<string, object>> fayl = (shag, proc, ost, tekst, nazad, konec) => new Dictionary<string, object> {
            { "chto", "Замер серверов" }, { "shagi", new[] { "Нахожу серверы подписки", "Меряю скорость", "Ставлю лучший сервер", "Проверяю, что интернет работает" } },
            { "shag", shag }, { "tekst", tekst }, { "procent", proc }, { "ostalos", ost }, { "nachalo", Hod.Seychas() - 60 }, { "obnovleno", Hod.Seychas() - nazad },
            { "konec", konec }, { "ok", konec ? (object)true : null }, { "itog", konec ? "Выбран Germany 1, 260 Мбит/с" : "" } };
        var bylo = Pult.Poddelka;
        Pult.Poddelka = Poddelki.Polnye();
        try
        {
            using (var o = new Okno(false))
            {
                o.Size = new Size(1078, 694); o.Show(); o.Location = new Point(-20000, -20000);
                o.ZagruzitVse();
                UbratProbu();
                o.hod = new Hod { Chto = "Замер серверов", Put = HOD_PROBA };
                o.Otkryt("glavnaya"); Zhdat(30);
                var kh = o.kartaHoda;
                if (kh == null) { ploho("карточки хода нет"); return plohih; }
                Func<string> t = () => string.Join(" | ", kh.Teksty);
                o.TikHoda();
                if (!t().Contains("Замер серверов — идёт…")) ploho("файла ещё нет — нет «идёт…»: " + t());
                int h0 = kh.Height;
                pisat(fayl(1, 48, 80, "Меряю скорость: 7 из 14", 0, false)); o.TikHoda();
                if (o.kartaHoda != kh || kh.IsDisposed) ploho("карточка пересоздана, а должна обновиться на месте");
                if (!t().Contains("Замер серверов — 48%") || !t().Contains("Меряю скорость: 7 из 14") || !t().Contains("осталось ~1 мин 20 с")) ploho("после записи файла нет хода: " + t());
                if (kh.Height <= h0) ploho("высота не выросла под шаги (" + h0 + " → " + kh.Height + ")");
                if (kh.NizSoderzhimogo > kh.Height) ploho("текст ниже края карточки");
                File.WriteAllText(HOD_PROBA, "{\"shag\": 2, \"proc"); o.TikHoda();
                if (!t().Contains("48%")) ploho("недописанный файл сбил показ: " + t());
                pisat(fayl(2, 70, 30, "Ставлю Germany 1", 0, false));
                using (new FileStream(HOD_PROBA, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) o.TikHoda();
                if (!t().Contains("Замер серверов — 70%") || kh.Sdelano != 2) ploho("файл, открытый писателем, не прочитался: " + t());
                pisat(fayl(2, 70, 30, "Ставлю Germany 1", 100, false)); o.TikHoda();
                if (!System.Text.RegularExpressions.Regex.IsMatch(t(), "Нет вестей уже 1 мин (39|40|41) с")) ploho("молчит 100 с — нет «нет вестей»: " + t());
                if (t().Contains("осталось")) ploho("при зависшем ходе показано старое «осталось»");
                pisat(fayl(3, 100, 0, "", 0, true));
                o.ZakonchitHod(o.hod, new Dictionary<string, object> { { "ok", true } });
                if (!t().Contains("Готово: Замер серверов") || !t().Contains("Выбран Германия 1, 33 МБ/с") || !kh.Zakryt.Visible) ploho("конец: нет итога или крестика: " + t());
                o.TikHoda();   // после конца таймер ничего не трогает
                kh.Zakryt.PerformClick(); Zhdat(30);
                if (o.hod != null || o.soderzh.Controls[0].Controls.OfType<KartaHoda>().Any()) ploho("крестик не убрал карточку");
            }
        }
        catch (Exception e) { ploho("упало: " + e.GetType().Name + ": " + e.Message); }
        finally { Pult.Poddelka = bylo; UbratProbu(); }
        return plohih;
    }

    // --proba-hoda <файл>: что окно покажет по этому файлу хода (без окон на экране, без сети)
    public static int ProbaHoda(string put)
    {
        proba = true;
        var d = ProchitatHod(put);
        if (d == null) { Console.WriteLine("НЕ ПРОЧИТАЛ файл хода: " + put); return 1; }
        var nach = Pult.ChIliNet(Pult.G(d, "nachalo"));
        var h = new Hod { Chto = "(название от окна)", Put = put, Fayl = d, Nachalo = nach != null ? DateTime.Now.AddSeconds(-(Hod.Seychas() - nach.Value)) : DateTime.Now };
        if (h.KonecVFayle) h.Zakonchit(new Dictionary<string, object> { { "ok", Pult.B(d, "ok") } });
        using (var kh = new KartaHoda { Width = 794 })
        {
            kh.Pokazat(h);
            foreach (var t in kh.Teksty) Console.WriteLine("  " + t);
            Console.WriteLine("шаги: сделано " + kh.Sdelano + ", текущий " + (kh.Tekushchiy >= 0 ? (kh.Tekushchiy + 1).ToString() : "нет") + ", впереди " + kh.Vperedi +
                              "; молчит " + h.Molchit + " с; крестик " + (kh.Zakryt.Visible ? "есть" : "нет") + "; высота " + kh.Height + ", текст до " + kh.NizSoderzhimogo);
        }
        return 0;
    }

    static List<string> TekstyVnutri(Control r) { var l = new List<string>(); foreach (Control c in r.Controls) { if (c is Label) l.Add(c.Text); l.AddRange(TekstyVnutri(c)); } return l; }

    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr p, uint f);

    // элемент за краем карточки/панели
    static IEnumerable<string> Vylezaet(Control r)
    {
        foreach (Control c in r.Controls)
        {
            if (c.Visible && (r is Kartochka || (r is Panel && !(r is FlowLayoutPanel) && r.Parent is FlowLayoutPanel)))
            {
                if (c.Right > r.ClientSize.Width + 2) yield return "вылезает справа: " + c.GetType().Name + " «" + Pult.Chisto(c.Text) + "» " + c.Right + " > " + r.ClientSize.Width;
                if (c.Bottom > r.ClientSize.Height + 2) yield return "вылезает снизу: " + c.GetType().Name + " «" + Pult.Chisto(c.Text) + "» " + c.Bottom + " > " + r.ClientSize.Height;
                if (c.Left < -2 || c.Top < -2) yield return "вылезает слева/сверху: " + c.GetType().Name + " «" + Pult.Chisto(c.Text) + "»";
            }
            foreach (var z in Vylezaet(c)) yield return z;
        }
    }

    // элементы одной карточки налезают друг на друга
    static IEnumerable<string> Nalezaet(Control r)
    {
        if (r is Kartochka || (r is Panel && !(r is FlowLayoutPanel)))
        {
            var vid = r.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0).ToList();
            for (int i = 0; i < vid.Count; i++)
                for (int j = i + 1; j < vid.Count; j++)
                {
                    var x = Rectangle.Intersect(vid[i].Bounds, vid[j].Bounds);
                    if (x.Width >= 3 && x.Height >= 3)
                        yield return "налезает: " + vid[i].GetType().Name + " «" + Pult.Chisto(vid[i].Text) + "» " + vid[i].Bounds + " и " + vid[j].GetType().Name + " «" + Pult.Chisto(vid[j].Text) + "» " + vid[j].Bounds;
                }
        }
        foreach (Control c in r.Controls)
            foreach (var z in Nalezaet(c)) yield return z;
    }

    // страница шире видимого: справа прокрутка вбок
    static IEnumerable<string> ShireOkna(Control soderzh)
    {
        foreach (Control str in soderzh.Controls)
            foreach (Control c in str.Controls)
                if (c.Visible && c.Right > str.ClientSize.Width + 2)
                    yield return "шире окна: " + c.GetType().Name + " «" + Pult.Chisto(c.Text) + "» " + c.Right + " > " + str.ClientSize.Width;
    }

    // Нажать мышью (WM_LBUTTONDOWN/UP) каждую кнопку, переключатель и выбор каждого раздела на поддельных данных.
    public static int TestKlik()
    {
        suho = true; proba = true;
        int vsego = 0, ne = 0, skryto = 0;
        foreach (var nabor in Poddelki.Vse().Where(x => x.Item1 == "всё полно" || x.Item1 == "как у друга" || x.Item1 == "всё сломано" || x.Item1 == "ход: конец ok" || x.Item1 == "ход: середина" || x.Item1 == "zapret сломан" || x.Item1 == "всё хорошо"))
        {
            Pult.Poddelka = nabor.Item2;
            Console.WriteLine("== " + nabor.Item1);
            using (var o = new Okno(false))
            {
                o.Size = new Size(1078, 2400);   // как у него по ширине; высокое — чтобы кнопки внизу были видны без прокрутки
                // своё место за экраном: клик засчитывается, только если в точке клика нет чужого окна (другой проверки)
                o.Show(); o.Location = new Point(-26000, -20000);
                foreach (var s in RAZDELY)
                {
                    o.ZagruzitVse(); o.Otkryt(s); Zhdat(60);
                    int skolko = Elementy(o.soderzh).Count;
                    for (int n = 0; n < skolko; n++)
                    {
                        o.ZagruzitVse(); o.Otkryt(s); Zhdat(40);
                        var el = Elementy(o.soderzh);
                        if (n >= el.Count) break;
                        var c = el[n];
                        string imya = c is Knopa ? c.Text : c is Tumbler ? "переключатель" : "выбор «" + string.Join("|", ((Segmenty)c).Punkty) + "»";
                        if (!c.Visible) { skryto++; Console.WriteLine(s + " | " + imya + " | скрыта при этих данных"); continue; }
                        if (!c.Enabled) { skryto++; Console.WriteLine(s + " | " + Pult.Chisto(imya) + " | неактивна при этих данных (не может сработать)"); continue; }
                        bool bylo = false;
                        EventHandler h = (a, b2) => bylo = true;
                        Action<bool> hT = v => bylo = true;
                        Action<int> hS = v => bylo = true;
                        int x = c.Width / 2, y = c.Height / 2;
                        if (c is Knopa) c.Click += h;
                        else if (c is Tumbler) ((Tumbler)c).Smena += hT;
                        else { var sg = (Segmenty)c; sg.Smena += hS; int i = sg.Vybran == 0 ? 1 : 0; x = (int)(2 + (c.Width - 4f) / sg.Punkty.Length * (i + 0.5f)); }
                        var t = (IntPtr)((y << 16) | x);
                        SendMessage(c.Handle, 0x201, (IntPtr)1, t); SendMessage(c.Handle, 0x202, IntPtr.Zero, t);
                        Zhdat(20);
                        if (!c.IsDisposed) { if (c is Knopa) c.Click -= h; }
                        vsego++; if (!bylo) ne++;
                        string pod = "";
                        if (!bylo && !c.IsDisposed)
                        {
                            // клик засчитывается, только если в этой точке экрана — сама кнопка (WindowFromPoint в WinForms)
                            var tochka = c.PointToScreen(new Point(x, y));
                            var tam = Control.FromHandle(WindowFromPoint(tochka));
                            pod = ", в точке клика: " + (tam == null ? "чужое окно" : tam == c ? "она сама" : tam.GetType().Name + " «" + Pult.Chisto(tam.Text) + "»");
                        }
                        Console.WriteLine(s + " | " + Pult.Chisto(imya) + " | " + (bylo ? "нажалась" : "НЕ НАЖАЛАСЬ [" + c.Bounds + " в " + (c.Parent == null ? "-" : c.Parent.GetType().Name + " " + c.Parent.ClientSize) + pod + "]"));
                    }
                }
            }
        }
        Pult.Poddelka = null;
        UbratProbu();
        Console.WriteLine("ИТОГ: нажалось " + (vsego - ne) + " из " + vsego + (skryto > 0 ? " (ещё " + skryto + " скрыты или неактивны при тех данных — так задумано)" : ""));
        return ne;
    }

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);

    static List<Control> Elementy(Control r) { var l = new List<Control>(); Sobrat(r, l); return l; }
    static void Sobrat(Control r, List<Control> l) { foreach (Control c in r.Controls) { if (c is Knopa || c is Tumbler || c is Segmenty) l.Add(c); Sobrat(c, l); } }

    // ---------- проба выноса из оболочки ----------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint c);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);

    // как Коробка (korobka\src\Korobka.cs, Soobshchenie): новое имя и параллельно старое Korobka.*.v1
    static void Obolochke(IntPtr okno, string imya, long wp, long lp)
    {
        PostMessage(okno, RegisterWindowMessage("Vstraivanie." + imya + ".v1"), new IntPtr(wp), new IntPtr(lp));
        PostMessage(okno, RegisterWindowMessage("Korobka." + imya + ".v1"), new IntPtr(wp), new IntPtr(lp));
    }
    static long Para(int a, int b) { return ((long)(uint)a) | ((long)b << 32); }

    // Коробка шлёт цвета темы по одному (PoslatTemu), в конце 0 — «применяй»
    static void TemaObolochki(IntPtr okno)
    {
        uint m = RegisterWindowMessage("Vstraivanie.Tema.v1");
        int[] z = { 0x1C1B22, 0x26252E, 0xE8E6F0, 0x9C99AD, 0x7C6CF0, 0xFFFFFF, 0x3A3946, 0x3FB27F, 0x6B6880, 10, 1, 0x34333E };
        for (int i = 0; i < z.Length; i++) PostMessage(okno, m, (IntPtr)(i + 1), (IntPtr)z[i]);
        PostMessage(okno, m, IntPtr.Zero, (IntPtr)z.Length);
    }

    void Nazhat(string razdel)
    {
        var k = punkty.First(x => (string)x.Tag == razdel);
        var c = (IntPtr)((k.Height / 2 << 16) | (k.Width / 2));
        SendMessage(k.Handle, 0x201, (IntPtr)1, c); SendMessage(k.Handle, 0x202, IntPtr.Zero, c);
    }

    // что сейчас видно в окне: число окон Windows внутри раздела (их рисует Windows; 0 — раздел пустой)
    int Osmotr(string papka, string shag, bool proverit, bool podrobno)
    {
        int vnutri = 0;
        if (soderzh.IsHandleCreated) EnumChildWindows(soderzh.Handle, (h, l) => { if (IsWindowVisible(h)) vnutri++; return true; }, IntPtr.Zero);
        var str = soderzh.Controls.Count > 0 ? soderzh.Controls[0] : null;
        bool vidnoWin = IsWindowVisible(Handle);
        bool ramka = (GetWindowLong(Handle, -16) & 0x00C00000) == 0x00C00000;
        bool pusto = vidnoWin && (vnutri == 0 || str == null || !str.IsHandleCreated);
        string s = shag + ": видно в Windows " + Da(vidnoWin) + ", видно для WinForms " + Da(Visible) + ", рамка " + Da(ramka) + ", раздел «" + tekushchaya + "»: элементов " + soderzh.Controls.Count +
                   ", окон внутри " + vnutri + (str != null ? ", у страницы окно " + (str.IsHandleCreated ? "есть" : "НЕ СОЗДАНО") : "") + (proverit && pusto ? "  <- ПУСТО" : "");
        if (podrobno || (proverit && pusto)) Console.WriteLine(s);
        if (papka != null)
            using (var b = new Bitmap(Width, Height)) { DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(papka, Regex.Replace(shag, @"[^\w\-]+", "-").Trim('-') + ".png")); }
        return proverit && pusto ? 1 : 0;
    }
    static string Da(bool b) { return b ? "да" : "нет"; }

    // Повторяет то, что делает Коробка (korobka\src\Korobka.cs): Prikrepit (Privet, тема, Mesto) →
    // переход на другую вкладку (Spryatat: Mesto за экран −25000 и Spryach) → назад (PostavitAktivnoe: Mesto) →
    // «Вынуть окно из Коробки» (Otkrepit: владелец = 0 и Otpusti). Окно за экраном, без значка, без фокуса.
    public static int ProbaVynos(string papka, bool podrobno)
    {
        bool bylSuho = suho, bylaProba = proba; var bylaPoddelka = Pult.Poddelka;
        suho = true; proba = true; Pult.Poddelka = Poddelki.Polnye();
        if (papka != null) Directory.CreateDirectory(papka);
        if (podrobno) Console.WriteLine(Vstraivanie.Flag ? "запущено с --vstroeno, как из Коробки" : "без флага --vstroeno (окно показывается само до первого «места»)");
        int plohih = 0;
        var mesto = new Rectangle(-20000, -20000, 1078, 694);
        try
        {
            foreach (bool smenaVkladok in new[] { false, true })
            {
                string sc = smenaVkladok ? "Б" : "А";
                if (podrobno) Console.WriteLine("== сценарий " + sc + (smenaVkladok ? ": встал во вкладку, другая вкладка, назад, вынуть" : ": встал во вкладку, сразу вынуть"));
                using (var ob = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Size = new Size(50, 50), Text = "проба оболочки" })
                using (var o = new Okno(true))
                {
                    IntPtr hOb = ob.Handle;   // окно «Коробки» создано, но не показано
                    o.Show(); Zhdat(500);
                    IntPtr h = o.Handle;
                    o.Osmotr(papka, sc + "0 запущено", false, podrobno);
                    // Prikrepit: Privet, тема, место
                    Obolochke(h, "Privet", hOb.ToInt64(), 0);
                    TemaObolochki(h);
                    Obolochke(h, "Mesto", Para(mesto.X, mesto.Y), Para(mesto.Width, mesto.Height));
                    Zhdat(1200);
                    plohih += o.Osmotr(papka, sc + "1 встало во вкладку", true, podrobno);
                    o.Nazhat("programmy"); Zhdat(500);
                    plohih += o.Osmotr(papka, sc + "2 клик «Программы» во вкладке", true, podrobno);
                    if (smenaVkladok)
                    {
                        Obolochke(h, "Mesto", Para(-25000, -25000), Para(mesto.Width, mesto.Height));
                        Obolochke(h, "Spryach", 0, 0);
                        Zhdat(500);
                        o.Osmotr(papka, sc + "3 другая вкладка (спрятано)", false, podrobno);
                        Obolochke(h, "Mesto", Para(mesto.X, mesto.Y), Para(mesto.Width, mesto.Height));
                        Zhdat(800);
                        plohih += o.Osmotr(papka, sc + "4 вкладка снова открыта", true, podrobno);
                        o.Nazhat("servery"); Zhdat(500);
                        plohih += o.Osmotr(papka, sc + "5 клик «Серверы» после возврата", true, podrobno);
                    }
                    // Otkrepit: владельца снимает Коробка, остальное программа возвращает сама по Otpusti
                    if (GetWindow(h, 4) == hOb) SetWindowLongPtr(h, -8, IntPtr.Zero);
                    Obolochke(h, "Otpusti", 0, 0);
                    Zhdat(1500);
                    plohih += o.Osmotr(papka, sc + "6 вынуто из оболочки", true, podrobno);
                    o.Nazhat("glavnaya"); Zhdat(500);
                    plohih += o.Osmotr(papka, sc + "7 клик «Монитор» после выноса", true, podrobno);
                    o.Nazhat("zhurnal"); Zhdat(500);
                    plohih += o.Osmotr(papka, sc + "8 клик «Журнал» после выноса", true, podrobno);
                }
                Zhdat(100);
            }
        }
        finally { suho = bylSuho; proba = bylaProba; Pult.Poddelka = bylaPoddelka; }
        if (podrobno) Console.WriteLine(plohih == 0 ? "ИТОГ: окно рисуется на всех шагах" : "ИТОГ: пусто на шагах: " + plohih);
        return plohih;
    }

    // --proba-okno: настоящий путь запуска (с фоновым обновлением), но за экраном, без значка и без фокуса
    public static void ProbaOkno(string png)
    {
        proba = true;
        var o = new Okno(true);
        var t = new System.Windows.Forms.Timer { Interval = 12000 };
        t.Tick += (s2, e2) =>
        {
            t.Stop();
            using (var b = new Bitmap(o.Width, o.Height)) { o.DrawToBitmap(b, new Rectangle(0, 0, o.Width, o.Height)); b.Save(png); }
            Zapis("[проба] элементов на странице: " + o.soderzh.Controls.Count + (o.soderzh.Controls.Count > 0 ? ", первая: " + o.soderzh.Controls[0].GetType().Name + " " + o.soderzh.Controls[0].Size + " детей " + o.soderzh.Controls[0].Controls.Count : ""));
            o.Close();
        };
        o.Shown += (s2, e2) => t.Start();
        Application.Run(o);
    }

    public static void Snimki(string papka)
    {
        Directory.CreateDirectory(papka);
        proba = true; suho = true;
        var nabor = Environment.GetEnvironmentVariable("STOROZH_NABOR");   // имя набора подделки; пусто — настоящие данные
        var pd = Poddelki.Vse().FirstOrDefault(x => x.Item1 == nabor);
        Pult.Poddelka = pd != null ? pd.Item2 : Poddelki.IzNastoyashchih();
        using (var o = new Okno(false))
        {
            int vys; if (!int.TryParse(Environment.GetEnvironmentVariable("STOROZH_VYS"), out vys)) vys = 694;
            int shir; if (!int.TryParse(Environment.GetEnvironmentVariable("STOROZH_SHIR"), out shir)) shir = 1078;
            o.Size = new Size(shir, vys);
            o.ZagruzitVse();
            o.Show(); o.Location = new Point(-20000, -20000);
            foreach (var temy in new[] { "svoya", "svetloe" })
            {
                o.pal = temy == "svoya" ? SvoyaPal() : Palitra.Iz(new TemaObolochki { Fon = 0xF5F7FA, Poverhnost = 0xFFFFFF, Tekst = 0x1A1C21, Vtorichnyy = 0x5F6980, Akcent = 0x2563EB, NaAkcente = 0xFFFFFF, Liniya = 0xD6D8DB, Navedenie = 0xD7DCE6, Radius = 9, Temnaya = false });
                foreach (var s in RAZDELY)
                {
                    o.ZagruzitVse(); o.Otkryt(s);
                    Zhdat(150);
                    using (var b = new Bitmap(o.Width, o.Height)) { o.DrawToBitmap(b, new Rectangle(0, 0, o.Width, o.Height)); b.Save(Path.Combine(papka, temy + "-" + s + ".png")); }
                }
            }
        }
        UbratProbu();
    }
}

// ---------- поддельные ответы пульта для проверок (без сети и без Python) ----------
static class Poddelki
{
    static string J(object o) { return Pult.Json(o); }
    static object[] Chasy(params int[] obryvy) { return Enumerable.Range(0, 24).Select(i => (object)new[] { i < obryvy.Length ? obryvy[i] : 0, i < obryvy.Length ? obryvy[i] * 20 : 0 }).ToArray(); }

    public static List<Tuple<string, Dictionary<string, string>>> Vse()
    {
        var polnye = Polnye(); var drug = Drug();
        return new List<Tuple<string, Dictionary<string, string>>>
        {
            Tuple.Create("как у друга", drug), Tuple.Create("всё полно", polnye), Tuple.Create("всё сломано", Slomano()),
            Tuple.Create("пульт молчит", Molchit()), Tuple.Create("нет Python", NetPythona()), Tuple.Create("кривые данные", Krivye()),
            // ход долгого действия (DOGOVOR-hod.md): поверх «всё полно» — карточка вверху каждой страницы
            Tuple.Create("ход: середина", Var(polnye, null, HodSeredina())),
            Tuple.Create("ход: без процентов", Var(polnye, null, J(new { chto = "Замер серверов", idet = true, proshlo = 65, fayl = (object)null }))),
            Tuple.Create("ход: молчит", Var(polnye, null, J(new { chto = "Замер серверов", idet = true, proshlo = 200, fayl = FaylHoda(1, 48, 80, "Меряю скорость: 7 из 14", 120, false, null, "") }))),
            Tuple.Create("ход: конец ok", Var(polnye, null, J(new { chto = "Замер серверов", idet = false, proshlo = 130, fayl = FaylHoda(3, 100, 0, "", 1, true, true, "Выбран Germany 1, 260 Мбит/с"), otvet = new { ok = true } }))),
            Tuple.Create("ход: конец с ошибкой", Var(polnye, null, J(new { chto = "Замер серверов", idet = false, proshlo = 12, fayl = FaylHoda(0, 5, null, "", 1, true, false, "Karing не запущен — запустите его"), otvet = new { ok = false, info = "Karing не отвечает" } }))),
            // всё хорошо: зелёный итог, ни одной синей кнопки на Мониторе
            Tuple.Create("всё хорошо", Var(polnye, d => { d["bedy"] = new string[0]; d["miganiya"] = 0; d["igra"] = new string[0]; d["nastroyka"] = Nastroyka(true, true, true, true);
                                                          d["golos"] = new Dictionary<string, object> { { "est", false } };
                                                          var st = (Dictionary<string, object>)d["stat"]; st["chasy"] = Chasy(); st["bez_vpn"] = 0; st["samyy_dlinnyy"] = 0; st["po_serveram"] = new object[0];
                                                          ((Dictionary<string, object>)st["nadezhnost"])["Англия🇬🇧🔃"] = new[] { 0, 0, 50 };
                                                          ((Dictionary<string, object>)st["nadezhnost"])["Германия🇩🇪🔃"] = new[] { 0, 0, 90 }; })),
            // zapret поставлен, но не работает: плитка «Подобрать настройку»
            Tuple.Create("zapret сломан", Var(polnye, d => { d["zapret"] = false; d["bedy"] = new[] { "zapret не работает" }; d["nastroyka"] = Nastroyka(true, true, true, false); })),
            // список первичной настройки
            Tuple.Create("настройка: всё готово", Var(drug, d => { d["nastroyka"] = Nastroyka(true, true, true, true); })),
            Tuple.Create("настройка: скрыта", Var(drug, d => { d["nastroyka_skryta"] = true; })),
            // значок Claude на главной карточке
            Tuple.Create("Claude: зелёный", Var(polnye, d => { d["claude_znachok"] = new Dictionary<string, object> { { "pokazat", true }, { "server", "Англия🇬🇧🔃" }, { "ok", true }, { "proveren", "20.11.2026 22:30" } };
                                                               ((Dictionary<string, object>)((Dictionary<string, object>)d["stat"])["nadezhnost"])["Англия🇬🇧🔃"] = new[] { 0, 0, 50 }; })),
            Tuple.Create("Claude: нестабилен", Var(polnye, d => { d["claude_znachok"] = new Dictionary<string, object> { { "pokazat", true }, { "server", "Англия🇬🇧🔃" }, { "ok", true }, { "proveren", "20.11.2026 22:30" } }; })),
            Tuple.Create("Claude: красный", Var(polnye, d => { d["claude_znachok"] = new Dictionary<string, object> { { "pokazat", true }, { "server", "Англия🇬🇧🔃" }, { "ok", false }, { "proveren", "20.11.2026 22:30" } }; })),
            Tuple.Create("Claude: серый", Var(polnye, d => { d["claude_znachok"] = new Dictionary<string, object> { { "pokazat", true }, { "server", "" }, { "ok", null }, { "proveren", "" } }; })),
            Tuple.Create("Claude: не показывать", Var(polnye, d => { d["claude_znachok"] = new Dictionary<string, object> { { "pokazat", false }, { "server", "Англия" }, { "ok", true }, { "proveren", "20.11.2026 22:30" } }; })),
        };
    }

    // набор на основе другого: поправить ответ sostoyanie и/или добавить поддельный ход ("~hod" — не команда пульта)
    static Dictionary<string, string> Var(Dictionary<string, string> baza, Action<Dictionary<string, object>> sost, string hod = null)
    {
        var n = new Dictionary<string, string>(baza);
        if (sost != null)
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(n["sostoyanie"]);
            sost(d);
            n["sostoyanie"] = J(d);
        }
        if (hod != null) n["~hod"] = hod;
        return n;
    }

    static readonly string[] SHAGI_ZAMERA = { "Нахожу серверы подписки", "Меряю скорость", "Ставлю лучший сервер", "Проверяю, что интернет работает" };

    // файл хода, как его пишет сторож; время — «сколько секунд назад» (окно пересчитает в настоящее при загрузке)
    static Dictionary<string, object> FaylHoda(int shag, int procent, int? ostalos, string tekst, int obnovlenoNazad, bool konec, bool? ok, string itog)
    {
        return new Dictionary<string, object> { { "chto", "Замер серверов" }, { "shagi", SHAGI_ZAMERA }, { "shag", shag }, { "tekst", tekst }, { "procent", procent }, { "ostalos", ostalos },
                                                { "obnovleno_nazad", obnovlenoNazad }, { "konec", konec }, { "ok", ok }, { "itog", itog } };
    }

    // замер на середине: шаг 2 из 4, 48 %, осталось 80 с (пример из договора)
    public static string HodSeredina() { return J(new { chto = "Замер серверов", idet = true, proshlo = 42, fayl = FaylHoda(1, 48, 80, "Меряю скорость: 7 из 14", 1, false, null, "") }); }

    // список настройки из договора (4 пункта в этом порядке)
    static object[] Nastroyka(bool karing, bool zadaniya, bool server, bool zapret)
    {
        return new object[]
        {
            new Dictionary<string, object> { { "id", "karing" }, { "gotovo", karing }, { "tekst", "Karing найден, подписка есть, подключение включено" }, { "sovet", karing ? "" : "Karing не запущен — запустите его и включите подключение" } },
            new Dictionary<string, object> { { "id", "zadaniya" }, { "gotovo", zadaniya }, { "tekst", "Сторож проверяет сеть сам, каждые 10 минут" }, { "sovet", zadaniya ? "" : "Сейчас сторож работает, только пока открыто окно" } },
            new Dictionary<string, object> { { "id", "server" }, { "gotovo", server }, { "tekst", "Выбран самый быстрый сервер" }, { "sovet", server ? "" : "Замер занимает пару минут" } },
            new Dictionary<string, object> { { "id", "zapret" }, { "gotovo", zapret }, { "neobyaz", true }, { "tekst", "zapret: YouTube и голос Discord без VPN" }, { "sovet", zapret ? "" : "Не обязательно" } },
        };
    }

    // ответы настоящего пульта — один раз (только команды чтения, без сети); сетевые — примером
    public static Dictionary<string, string> IzNastoyashchih()
    {
        var bylo = Pult.Poddelka; Pult.Poddelka = null; Pult.TolkoChtenie = true;
        var d = new Dictionary<string, string>();
        try
        {
            foreach (var k in new[] { "sostoyanie", "programmy", "servery", "sayty-istoriya", "lenta", "skorosti", "set-seychas" }) d[k] = J(Pult.Vyzov(k));
        }
        finally { Pult.Poddelka = bylo; Pult.TolkoChtenie = false; }
        d["zapret"] = "{\"youtube\":[\"204\",0.12,true],\"golos\":{\"est\":true,\"poteri\":8.8,\"ping\":71},\"versiya\":\"1.10.4\",\"posledn\":\"1.10.5\"}";
        d["*"] = J(new { ok = false, info = "проверка: команда не вызывается" });
        return d;
    }

    // друг: Karing выключен, серверов 0, zapret не установлен, заданий планировщика нет, Claude не закреплён
    public static Dictionary<string, string> Drug()
    {
        var nas = new Dictionary<string, object> { { "avto_server", true }, { "avto_sayty", true }, { "avto_spiski", true }, { "zapret_slezhka", true }, { "ne_v_igre", true }, { "zvonok_min", 20 },
                                                   { "marshruty", new Dictionary<string, object> { { "RobloxPlayerBeta.exe", "vpn" } } }, { "claude_strana", "" } };
        return new Dictionary<string, string>
        {
            { "sostoyanie", J(new { karing = false, zapret = false, strategiya = "", server = (string)null, skorost = (object)null, claude = "как всё остальное", posledniy = (string)null, zamer = (string)null,
                                    miganiya = 0, igra = new string[0], zapret_est = false, golos = new { est = false }, youtube_sboy = "", test_poslednii = (object)null,
                                    bedy = new[] { "Karing не запущен — запустите его и включите подключение", "zapret не установлен — без него YouTube и голос Discord напрямую не работают", "Сторож не запускается сам — включите проверку каждые 10 минут" },
                                    nastroyka = Nastroyka(false, false, false, false), nastroyka_skryta = false,
                                    claude_znachok = new { pokazat = false, server = "", ok = (object)null, proveren = "" },
                                    otchet = new string[0], mig_log = new string[0], pult_log = new string[0], nastroyki = nas,
                                    stat = new { dni = new object[0], chasy = Chasy(), nadezhnost = new Dictionary<string, object>(), vybory = new object[0], samyy_dlinnyy = 0, bez_vpn = 0 } }) },
            { "programmy", J(new { programmy = new object[] {
                new { exe = "chrome.exe", seychas = "мимо Karing", kb = 0, marshrut = "avto", pravilo = false, karing = false, v_seti = true },
                new { exe = "Discord.exe", seychas = "мимо Karing", kb = 0, marshrut = "avto", pravilo = false, karing = false, v_seti = true },
                new { exe = "RobloxPlayerBeta.exe", seychas = "сейчас не в сети", kb = 0, marshrut = "vpn", pravilo = true, karing = false, v_seti = false } }, servery = new string[0] }) },
            { "servery", J(new { zamer = new Dictionary<string, object>(), servery = new string[0], seychas = (string)null, vybor = (string)null, claude = "как всё остальное", podrobno = new Dictionary<string, object>() }) },
            { "sayty-istoriya", J(new { sayty = new object[0] }) },
            { "lenta", J(new { lenta = new object[0] }) },
            { "skorosti", J(new { ok = false, info = "ошибка: <urlopen error [WinError 10061] Подключение не установлено>" }) },
            { "set-seychas", J(new { ok = true, vniz = 0.12, vverh = 0.01, vpn = 0.0 }) },
            { "zapret", J(new { youtube = new object[] { 0, 8.0, false }, golos = new { est = false }, versiya = "", posledn = "" }) },
            { "proverit-sayt", J(new { ok = true, domen = "youtube.com", napryamuyu = new object[] { 0, 12.0, "не открывается" }, vpn = new object[] { 0, 12.0, "не открывается" }, server = (string)null,
                                       sovet = "не открывается нигде — похоже, сайт сам не работает", deystvie = "", uzhe_vpn = false, uzhe_napr = false }) },
            { "*", J(new { ok = true, info = "проба" }) },
        };
    }

    // всё есть: для покрытия всех кнопок
    public static Dictionary<string, string> Polnye()
    {
        var nas = new Dictionary<string, object> { { "avto_server", true }, { "avto_sayty", false }, { "avto_spiski", true }, { "zapret_slezhka", true }, { "ne_v_igre", true }, { "zvonok_min", 10 },
                                                   { "marshruty", new Dictionary<string, object> { { "Telegram.exe", "vpn" }, { "RobloxPlayerBeta.exe", "Германия🇩🇪🔃" }, { "steam.exe", "direct" } } }, { "claude_strana", "Англия🇬🇧🔃" } };
        var nad = new Dictionary<string, object> { { "Германия🇩🇪🔃", new[] { 3, 2, 90 } }, { "Англия🇬🇧🔃", new[] { 19, 61, 50 } }, { "Нидерланды🇳🇱🔃➗", new[] { 0, 0, 100 } } };
        return new Dictionary<string, string>
        {
            { "sostoyanie", J(new { karing = true, zapret = true, strategiya = "general (ALT13)", server = "Германия🇩🇪🔃", skorost = 217, claude = "Англия🇬🇧🔃", posledniy = "20.11.2026 21:50", zamer = "19.11.2026 01:31",
                                    miganiya = 7, igra = new[] { "dota2.exe", "звонок в Discord" },
                                    bedy = new[] { "Пустить через VPN: rutracker.org, nnmclub.to — напрямую не открывается или тормозит", "Сторож не запускается сам — включите проверку каждые 10 минут",
                                                   "Нет программы curl.exe (она есть в Windows 10 и 11) — без неё сторож не проверяет сайты и не меряет скорость" },
                                    zapret_est = true, nastroyka = Nastroyka(true, false, true, true), nastroyka_skryta = false,
                                    golos = new { est = true, poteri = 8.8, ping = 71 }, youtube_sboy = "",
                                    test_poslednii = new { kogda = "20.11 21:30", vpn = 96, pryamo = 410, server = "Германия🇩🇪🔃" },
                                    claude_znachok = new { pokazat = true, server = "Англия🇬🇧🔃", ok = true, proveren = "20.11.2026 22:30" },
                                    otchet = new string[0], mig_log = new string[0], pult_log = new string[0], nastroyki = nas,
                                    stat = new { dni = new object[0], chasy = Chasy(0, 1, 0, 0, 3, 0, 0, 2, 0, 0, 0, 0, 1), nadezhnost = nad,
                                                 vybory = new object[] { new object[] { "18.11 03:00", "Германия🇩🇪🔃", 284 }, new object[] { "19.11 03:00", "Австрия🇦🇹🔃", 217 }, new object[] { "20.11 03:00", "Германия🇩🇪🔃", 260 } },
                                                 samyy_dlinnyy = 245, bez_vpn = 3725,
                                                 po_serveram = new object[] { new object[] { "Англия🇬🇧🔃", 4, 140 }, new object[] { "Германия🇩🇪🔃", 3, 105 } } } }) },
            { "programmy", J(new { programmy = new object[] {
                new { exe = "msedge.exe", seychas = "Германия🇩🇪🔃, напрямую", kb = 50000, marshrut = "avto", pravilo = false, karing = true, v_seti = true },
                new { exe = "claude.exe", seychas = "Англия🇬🇧🔃", kb = 900, marshrut = "avto", pravilo = false, karing = true, v_seti = true },
                new { exe = "Telegram.exe", seychas = "Германия🇩🇪🔃", kb = 300, marshrut = "vpn", pravilo = true, karing = true, v_seti = true },
                new { exe = "RobloxPlayerBeta.exe", seychas = "сейчас не в сети", kb = 0, marshrut = "Германия🇩🇪🔃", pravilo = true, karing = false, v_seti = false },
                new { exe = "steam.exe", seychas = "мимо Karing", kb = 0, marshrut = "direct", pravilo = true, karing = false, v_seti = true },
                new { exe = "Discord.exe", seychas = "напрямую", kb = 120, marshrut = "avto", pravilo = false, karing = true, v_seti = true },
                new { exe = "OchenDlinnoeImyaProgrammyObnovleniyaServisa64.exe", seychas = "мимо Karing", kb = 0, marshrut = "avto", pravilo = false, karing = false, v_seti = true },
                new { exe = "python.exe", seychas = "Нидерланды🇳🇱🔃➗, Германия🇩🇪🔃, Англия🇬🇧🔃, напрямую", kb = 10, marshrut = "avto", pravilo = false, karing = true, v_seti = true } },
                servery = new[] { "Германия🇩🇪🔃", "Англия🇬🇧🔃", "Нидерланды🇳🇱🔃➗", "США🇺🇸🔃" } }) },
            { "servery", J(new { zamer = new { when = "19.11.2026 01:31", results = new object[] {
                    new object[] { "Англия🇬🇧🔃", 298, 60, 1 }, new object[] { "Германия🇩🇪🔃", 284, 58, 4 }, new object[] { "Нидерланды🇳🇱🔃➗", 45, 53, 1 }, new object[] { "США🇺🇸🔃", 3, 135, 1 }, new object[] { "Сервер с очень длинным именем Берлин-Франкфурт", 120, 160, 1 } } },
                servery = new[] { "Германия🇩🇪🔃", "Англия🇬🇧🔃", "Нидерланды🇳🇱🔃➗", "США🇺🇸🔃" }, seychas = "Германия🇩🇪🔃", vybor = "Германия🇩🇪🔃", claude = "Англия🇬🇧🔃",
                podrobno = new Dictionary<string, object> {
                    { "Англия🇬🇧🔃", new { vid = "nado_znat", slovo = "Нестабильный", prichiny = new[] { "за сутки обрывался 19 раз, без ответа 61 мин" }, obryvov = 19, minut = 61 } },
                    { "Германия🇩🇪🔃", new { vid = "nado_znat", slovo = "Адрес меняется", prichiny = new[] { "за замер сменил 4 адреса — сайты могут выкидывать из аккаунтов" }, obryvov = 3, minut = 2 } },
                    { "Нидерланды🇳🇱🔃➗", new { vid = "nado_znat", slovo = "Медленный", prichiny = new[] { "скорость 45 Мбит/с — ниже 50" }, obryvov = 0, minut = 0 } },
                    { "США🇺🇸🔃", new { vid = "plohoy", slovo = "Не работает", prichiny = new[] { "скорость почти ноль" }, obryvov = 0, minut = 0 } } } }) },
            { "sayty-istoriya", J(new { sayty = new object[] {
                new { domen = "animestars.org", vid = "direct", kogda = "19.11.2026 07:19" }, new { domen = "steampowered.com", vid = "vpn", kogda = "18.11.2026 22:59" },
                new { domen = "ochen-dlinnyy-adres-sayta-dlya-proverki.example.com", vid = "vpn", kogda = "18.11.2026 19:48" } } }) },
            { "lenta", J(new { lenta = new object[] {
                new object[] { "21.11 01:26", "moi", "Готово — Замер серверов — Выбран Нидерланды🇳🇱🔃, 300 Мбит/с (заняло 0 с)" },
                new object[] { "21.11 00:00", "info", "Мигания за 20.11: VPN-сервер: Англия — 15 раз; Karing не работал — 2 раз. Подробно — mignaniya.log." },
                new object[] { "20.11 21:21", "obryv", "VPN не отвечал 15 с (Англия🇬🇧🔃 (прямой интернет при этом был))" },
                new object[] { "20.11 21:14", "obryv", "VPN не отвечал 37 с (Англия🇬🇧🔃 (прямой интернет при этом был))" },
                new object[] { "20.11 20:58", "obryv", "VPN не отвечал 245 с (Karing не работал (перезапуск))" },
                new object[] { "20.11 18:40", "info", "НЕ ВЫШЛО. чиню: Claude не отвечает по правилам Karing. после перезапуска не заработало: Claude. Вернул прежние настройки, а ещё очень длинная строка, чтобы проверить, что текст не вылезает за край карточки журнала" },
                new object[] { "20.11 03:00", "zamer", "Замер серверов: выбран Германия🇩🇪🔃" },
                new object[] { "19.11 22:59", "ispr", "Исправлено: в VPN: steampowered.com" },
                new object[] { "19.11 22:00", "moi", "Готово — проверка сети" } } }) },
            { "skorosti", J(new { skorosti = new Dictionary<string, object> { { "msedge.exe", new { mbayt = 1.234, kuda = "Германия🇩🇪🔃, напрямую" } }, { "claude.exe", new { mbayt = 0.004, kuda = "Англия🇬🇧🔃" } },
                                                                              { "Telegram.exe", new { mbayt = 0.0, kuda = "Германия🇩🇪🔃" } } } }) },
            { "set-seychas", J(new { ok = true, vniz = 3.21, vverh = 0.12, vpn = 2.5 }) },
            { "zapret", J(new { youtube = new object[] { 204, 0.12, true }, golos = new { est = true, poteri = 8.8, ping = 71, zamerov = 120 }, versiya = "1.10.4", posledn = "1.10.5" }) },
            { "test-seti", J(new { ok = true, vpn = new { mbit = 96, ping = 112 }, pryamo = new { mbit = 410, ping = 9 }, server = "Нидерланды🇳🇱🔃➗ резервный длинный", kogda = "21.11.2026 01:20" }) },
            { "proverit-sayt", J(new { ok = true, domen = "rutracker.org", napryamuyu = new object[] { 0, 12.0, "не открывается" }, vpn = new object[] { 200, 0.8, "быстро" }, server = "Германия🇩🇪🔃",
                                       sovet = "пустить через VPN — напрямую не открывается", deystvie = "vpn", uzhe_vpn = false, uzhe_napr = false }) },
            { "*", J(new { ok = true, info = "проба" }) },
        };
    }

    // пульт на всё отвечает ошибкой
    public static Dictionary<string, string> Slomano()
    {
        return new Dictionary<string, string> { { "*", J(new { ok = false, info = "ошибка: [WinError 2] Не удается найти указанный файл (проба)" }) } };
    }

    // пульт молчит: пустой вывод (упал до первой строки)
    public static Dictionary<string, string> Molchit() { return new Dictionary<string, string> { { "*", "" } }; }

    // Python не найден
    public static Dictionary<string, string> NetPythona() { return new Dictionary<string, string> { { "*", "!net_python" } }; }

    // кривые данные: null и не те типы там, где ждём списки и числа
    public static Dictionary<string, string> Krivye()
    {
        return new Dictionary<string, string>
        {
            { "sostoyanie", "{\"golos\":\"x\",\"test_poslednii\":5,\"youtube_sboy\":7,\"karing\":null,\"zapret\":\"да\",\"server\":5,\"skorost\":\"быстро\",\"claude\":null,\"posledniy\":7,\"miganiya\":null,\"igra\":\"dota\",\"bedy\":[null,5,\"zapret не работает\"],\"nastroyki\":null,\"stat\":{\"po_serveram\":[null,[\"a\"],5,[7,\"x\"]],\"chasy\":[null,[1],[\"a\",\"b\"],5],\"vybory\":[[\"x\"],null],\"nadezhnost\":{\"A\":null},\"bez_vpn\":\"много\",\"samyy_dlinnyy\":null}," +
                            "\"nastroyka\":[null,5,{\"id\":null,\"gotovo\":\"нет\",\"tekst\":7},{\"id\":\"zapret\",\"neobyaz\":\"да\",\"sovet\":null},{\"id\":\"karing\",\"tekst\":\"очень длинный текст пункта настройки, чтобы проверить перенос строк и то, что кнопки справа не налезают на текст\",\"sovet\":\"и такой же длинный совет: запустите Karing, добавьте подписку, включите подключение и нажмите «Проверить снова»\"}],\"nastroyka_skryta\":\"нет\",\"claude_znachok\":{\"pokazat\":true,\"server\":[1,2],\"ok\":\"да\",\"proveren\":5}}" },
            // файл хода с мусором: не те типы, шаг за пределами списка, проценты строкой
            { "~hod", "{\"chto\":null,\"idet\":true,\"proshlo\":\"x\",\"fayl\":{\"chto\":5,\"shagi\":[null,\"Шаг\",7],\"shag\":\"два\",\"procent\":\"много\",\"ostalos\":-5,\"tekst\":null,\"obnovleno_nazad\":\"x\",\"konec\":\"нет\"}}" },
            { "programmy", "{\"programmy\":[null,5,{\"exe\":null,\"v_seti\":true},{\"exe\":\"a.exe\",\"v_seti\":true,\"karing\":true,\"marshrut\":null,\"pravilo\":true}]}" },
            { "servery", "{\"zamer\":{\"results\":[[\"X\"],null,[\"Y\",\"abc\",null,null],5]},\"podrobno\":{\"X\":null,\"Y\":{\"prichiny\":null,\"vid\":5}},\"claude\":null}" },
            { "lenta", "{\"lenta\":[[\"a\"],null,5,[null,null,null]]}" },
            { "sayty-istoriya", "{\"sayty\":[null,{\"domen\":null},5]}" },
            { "skorosti", "{\"skorosti\":{\"a.exe\":null,\"b.exe\":{\"mbayt\":\"x\"}}}" },
            { "set-seychas", "{\"ok\":true}" },
            { "zapret", "{\"youtube\":null,\"golos\":null,\"versiya\":null}" },
            { "test-seti", "{\"ok\":true,\"vpn\":null}" },
            { "proverit-sayt", "{\"ok\":true,\"napryamuyu\":null,\"vpn\":[1]}" },
            { "*", "{\"ok\":true}" },
        };
    }
}

// журнал нажатий мыши: куда приходят клики (для разбора «кнопки не работают»)
class SledMyshi : IMessageFilter
{
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == 0x201)
        {
            var c = Control.FromHandle(m.HWnd);
            Okno.Zapis("мышь: " + (c == null ? "чужое окно " + m.HWnd : c.GetType().Name + " «" + c.Text + "»" + (c.Enabled ? "" : " (выключено)")));
        }
        return false;
    }
}

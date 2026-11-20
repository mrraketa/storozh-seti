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

    public static Dictionary<string, object> Vyzov(params string[] arg)
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
        catch (Exception e) { return Oshibka("не смог запустить Python (" + Python + "): " + e.Message); }
        using (p)
        {
            // чтение и проверки — со сроком: зависший пульт не должен держать окно «занятым» навсегда.
            // Действия (замер, перезапуск Karing) идут долго и ждут очередь сторожа — им срок большой.
            int srok = CHTENIE.Contains(kom) || BYSTRYE.Contains(kom) ? 120000 : 30 * 60000;
            if (!p.WaitForExit(srok))
            {
                try { p.Kill(); } catch { }
                return Oshibka("пульт не ответил за " + srok / 1000 + " с (" + kom + ")");
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
        return Oshibka("пульт не ответил" + (posl != "" ? ": " + posl : ""));
    }

    public static Dictionary<string, object> Oshibka(string info, bool netPython = false)
    {
        var d = new Dictionary<string, object> { { "ok", false }, { "info", info } };
        if (netPython) d["net_python"] = true;
        return d;
    }

    // ответ — ошибка пульта (а не данные): пусто или {"ok": false, ...}
    public static bool Sboy(Dictionary<string, object> d) { object v; return d == null || (d.TryGetValue("ok", out v) && v is bool && !(bool)v); }
    public static string Info(Dictionary<string, object> d) { var t = S(d, "info"); return t != "" ? t : "пульт не ответил"; }

    public static string Json(object o) { return js.Serialize(o); }

    // флаги и значки из названий серверов: шрифт окна рисует их квадратиками
    public static string Chisto(string t)
    {
        if (string.IsNullOrEmpty(t)) return t;
        var sb = new StringBuilder();
        t = t.Replace("➗", " (÷)");   // «Германия➗» — отдельный сервер, без значка имена совпадут
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
}

// ---------- окно ----------
partial class Okno : Form
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    public static void Vpered(IntPtr h) { ShowWindow(h, 9); SetForegroundWindow(h); }

    static readonly string ZHURNAL = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storozh-okno.log");
    static readonly Color Zel = Color.FromArgb(46, 160, 90), Kras = Color.FromArgb(214, 72, 64), Zhelt = Color.FromArgb(214, 150, 40);

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

        menu = new Panel { Dock = DockStyle.Left, Width = 190, Padding = new Padding(10, 12, 10, 10) };
        soderzh = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16) };
        zanyatoStroka = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(22, 6, 0, 0), Text = "", AutoEllipsis = true };
        Controls.Add(soderzh); Controls.Add(zanyatoStroka); Controls.Add(menu);

        nazv = new Label { Text = "Сторож сети", Font = new Font("Segoe UI Semibold", 12f), Dock = DockStyle.Top, Height = 46, Padding = new Padding(4, 4, 0, 0) };
        var punktyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var p in new[] { new[] { "glavnaya", "Монитор", "" }, new[] { "programmy", "Программы", Zn.Prilozh }, new[] { "sayty", "Сайты", Zn.Globus },
                                  new[] { "servery", "Серверы", Zn.Server }, new[] { "zapret", "zapret", Zn.Molniya }, new[] { "zhurnal", "Журнал", Zn.Spisok }, new[] { "nastroyki", "Настройки", Zn.Nastr } })
        {
            var id = p[0];
            var k = new Knopa { Text = p[1], Znachok = p[2], Width = 168, Height = 38, Margin = new Padding(0, 0, 0, 4), Tag = id };
            k.Click += (s, e) => { Zapis("меню: " + id); Otkryt(id); };
            punkty.Add(k); punktyPanel.Controls.Add(k);
        }
        menu.Controls.Add(punktyPanel); menu.Controls.Add(nazv);

        if (nastoyashchee)
        {
            trey = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Сторож сети" };
            trey.DoubleClick += (s, e) => { if (Vstraivanie.Pokazat()) return; Show(); Vpered(Handle); };
            temaObrabotchik = () => { pal = Vstraivanie.Tema == null ? SvoyaPal() : Palitra.Iz(Vstraivanie.Tema); Perekrasit(); };
            Vstraivanie.TemaSmenilas += temaObrabotchik;
            Vstraivanie.Podklyuchit(Handle, v => { if (!proba && trey != null) trey.Visible = v; });
            FormClosed += (s, e) => { if (trey != null) trey.Dispose(); };
            Taymer(60000, () => { if (!zanyat) Obnovit(); });
            Taymer(5000, ObnovitSkorosti);
            Taymer(1500, () => { if (zanyat && tekushchaya == "zapret") ObnovitHod(); });
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

    void Deystvie(string chto, string[] arg, Action<Dictionary<string, object>> itog)
    {
        if (suho) { Zapis("[тест] нажато: " + chto); return; }
        if (zanyat) { MessageBox.Show(this, "Сначала дождитесь конца: " + deystvieSeychas + ".", "Сторож сети"); return; }
        zanyat = true; deystvieSeychas = chto;
        zanyatoStroka.Text = "Идёт: " + chto + "…";
        Zapis("начал: " + chto + " " + string.Join(" ", arg));
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var d = Pult.Vyzov(arg);
            try
            {
                BeginInvoke((Action)(() =>
                {
                    zanyat = false;
                    bool ok = !Pult.Sboy(d) && Pult.B(d, "ok");
                    zanyatoStroka.Text = (ok ? "Готово: " : "Не вышло: ") + chto + (Pult.S(d, "info") != "" ? " — " + Pult.S(d, "info") : "");
                    Zapis(zanyatoStroka.Text);
                    if (itog != null) itog(d);
                    Obnovit();
                }));
            }
            catch { }
        });
    }

    void Otkryt(string id)
    {
        tekushchaya = id;
        Perestroit();
    }

    // меню: на узком окне — одни значки (подписи — в подсказке), чтобы разделу хватило ширины
    void PodognatMenu()
    {
        bool uzko = ClientSize.Width < 900;
        int w = uzko ? 64 : 190;
        if (menu.Width != w) menu.Width = w;
        nazv.Visible = !uzko;
        foreach (var k in punkty)
        {
            k.Width = uzko ? 44 : 168;
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
    void Perekrasit()
    {
        BackColor = pal.Fon; ForeColor = pal.Tekst;
        menu.BackColor = pal.Poverhnost; soderzh.BackColor = pal.Fon;
        zanyatoStroka.BackColor = pal.Fon; zanyatoStroka.ForeColor = pal.Vtorichnyy;
        foreach (var k in punkty) { k.Glavnaya = (string)k.Tag == tekushchaya; k.Pal = pal; }
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
            else if (c is Spidometr) { ((Spidometr)c).Pal = pal; c.Invalidate(); }
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
            else if (c is Krug) c.BackColor = c.Parent is Kartochka ? (((Kartochka)c.Parent).Ottenok ?? pal.Poverhnost) : pal.Fon;
            else if (c is Grafik) { ((Grafik)c).Pal = pal; c.Invalidate(); }
            else if (c is Label)
            {
                var l = (Label)c;
                l.Text = Pult.Chisto(l.Text);
                if (!(l.Tag is Color)) l.ForeColor = "vt".Equals(l.Tag) ? pal.Vtorichnyy : pal.Tekst;
                else l.ForeColor = (Color)l.Tag;
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
    Label Zag(string t) { return new Label { Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 0, 10) }; }
    Label Tx(string t, bool vt = false) { return new Label { Text = t, AutoSize = true, MaximumSize = new Size(Math.Min(620, Shir), 0), Tag = vt ? "vt" : null, Margin = new Padding(0, 0, 0, 8) }; }
    Knopa Kn(string t, Action d, bool glavnaya = false, int w = 230)
    {
        var k = new Knopa { Text = t, Width = w, Height = 38, Glavnaya = glavnaya, Margin = new Padding(0, 0, 10, 8) };
        k.Click += (s, e) => { Zapis("кнопка: " + t); d(); };
        return k;
    }
    FlowLayoutPanel Kolonka() { return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; }
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
            zapData = Pult.Vyzov("zapret"); testData = Pult.Vyzov("test-seti"); saytProverka = Pult.Vyzov("proverit-sayt");
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
                        foreach (var z in Vylezaet(o.soderzh).Concat(Nalezaet(o.soderzh)).Concat(ShireOkna(o.soderzh)).Distinct())
                        { Console.WriteLine("ЗАМЕЧАНИЕ " + gde + z); plohih++; }
                    }
                }
                if (OshibokVFone > fonDo) { Console.WriteLine("ОШИБКИ В ФОНЕ [" + nabor.Item1 + "]: " + (OshibokVFone - fonDo) + " (подробности в журнале окна)"); plohih += OshibokVFone - fonDo; }
                // утечка: раздел перестраивается раз в минуту — окна Windows не должны копиться
                if (nabor.Item2 != null && nabor.Item1 == "всё полно")
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
        // вынос из оболочки — последовательность Коробки
        int vyn = ProbaVynos(null, false);
        Console.WriteLine("вынос из оболочки: " + (vyn == 0 ? "в порядке" : "замечаний " + vyn));
        plohih += vyn;
        Console.WriteLine(plohih == 0 ? "ИТОГ: всё в порядке" : "ИТОГ: замечаний " + plohih);
        return plohih;
    }

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
        foreach (var nabor in Poddelki.Vse().Where(x => x.Item1 == "всё полно" || x.Item1 == "как у друга" || x.Item1 == "всё сломано"))
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
        Console.WriteLine("ИТОГ: нажалось " + (vsego - ne) + " из " + vsego + (skryto > 0 ? " (ещё " + skryto + " скрыты при тех данных — так задумано)" : ""));
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
    }
}

// ---------- поддельные ответы пульта для проверок (без сети и без Python) ----------
static class Poddelki
{
    static string J(object o) { return Pult.Json(o); }
    static object[] Chasy(params int[] obryvy) { return Enumerable.Range(0, 24).Select(i => (object)new[] { i < obryvy.Length ? obryvy[i] : 0, i < obryvy.Length ? obryvy[i] * 20 : 0 }).ToArray(); }

    public static List<Tuple<string, Dictionary<string, string>>> Vse()
    {
        return new List<Tuple<string, Dictionary<string, string>>>
        {
            Tuple.Create("как у друга", Drug()), Tuple.Create("всё полно", Polnye()), Tuple.Create("всё сломано", Slomano()),
            Tuple.Create("пульт молчит", Molchit()), Tuple.Create("нет Python", NetPythona()), Tuple.Create("кривые данные", Krivye())
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
        d["test-seti"] = "{\"ok\":true,\"vpn\":{\"mbit\":96,\"ping\":112},\"pryamo\":{\"mbit\":410,\"ping\":9},\"server\":\"Литва\"}";
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
                                    miganiya = 0, igra = new string[0], bedy = new[] { "Karing не работает", "zapret не работает", "Сторож не запускается сам — включите проверку каждые 10 минут" },
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
            { "test-seti", J(new { ok = true, vpn = new { mbit = 0, ping = (object)null }, pryamo = new { mbit = 85.3, ping = 14 }, server = (string)null }) },
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
                                    bedy = new[] { "zapret не работает", "Пустить через VPN: rutracker.org, nnmclub.to — напрямую не открывается или тормозит", "Сторож не запускается сам — включите проверку каждые 10 минут" },
                                    otchet = new string[0], mig_log = new string[0], pult_log = new string[0], nastroyki = nas,
                                    stat = new { dni = new object[0], chasy = Chasy(0, 1, 0, 0, 3, 0, 0, 2, 0, 0, 0, 0, 1), nadezhnost = nad,
                                                 vybory = new object[] { new object[] { "18.11 03:00", "Германия🇩🇪🔃", 284 }, new object[] { "19.11 03:00", "Австрия🇦🇹🔃", 217 }, new object[] { "20.11 03:00", "Германия🇩🇪🔃", 260 } },
                                                 samyy_dlinnyy = 245, bez_vpn = 3725 } }) },
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
                new object[] { "20.11 21:21", "obryv", "VPN не отвечал 15 с (Англия🇬🇧🔃 (прямой интернет при этом был))" },
                new object[] { "20.11 18:40", "info", "НЕ ВЫШЛО. чиню: Claude не отвечает по правилам Karing. после перезапуска не заработало: Claude. Вернул прежние настройки, а ещё очень длинная строка, чтобы проверить, что текст не вылезает за край карточки журнала" },
                new object[] { "20.11 03:00", "zamer", "Замер серверов: выбран Германия🇩🇪🔃" },
                new object[] { "19.11 22:59", "ispr", "Исправлено: в VPN: steampowered.com" },
                new object[] { "19.11 22:00", "moi", "Готово — проверка сети" } } }) },
            { "skorosti", J(new { skorosti = new Dictionary<string, object> { { "msedge.exe", new { mbayt = 1.234, kuda = "Германия🇩🇪🔃, напрямую" } }, { "claude.exe", new { mbayt = 0.004, kuda = "Англия🇬🇧🔃" } },
                                                                              { "Telegram.exe", new { mbayt = 0.0, kuda = "Германия🇩🇪🔃" } } } }) },
            { "set-seychas", J(new { ok = true, vniz = 3.21, vverh = 0.12, vpn = 2.5 }) },
            { "zapret", J(new { youtube = new object[] { 204, 0.12, true }, golos = new { est = true, poteri = 8.8, ping = 71, zamerov = 120 }, versiya = "1.10.4", posledn = "1.10.5" }) },
            { "test-seti", J(new { ok = true, vpn = new { mbit = 96, ping = 112 }, pryamo = new { mbit = 410, ping = 9 }, server = "Нидерланды🇳🇱🔃➗ резервный длинный" }) },
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
            { "sostoyanie", "{\"karing\":null,\"zapret\":\"да\",\"server\":5,\"skorost\":\"быстро\",\"claude\":null,\"posledniy\":7,\"miganiya\":null,\"igra\":\"dota\",\"bedy\":[null,5,\"zapret не работает\"],\"nastroyki\":null,\"stat\":{\"chasy\":[null,[1],[\"a\",\"b\"],5],\"vybory\":[[\"x\"],null],\"nadezhnost\":{\"A\":null},\"bez_vpn\":\"много\",\"samyy_dlinnyy\":null}}" },
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

// большой круг состояния на главной
class Krug : Control
{
    public Color Cvet;
    public bool Galka;
    public Krug() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Cvet)) g.FillEllipse(b, 2, 2, Width - 5, Height - 5);
        using (var p = new Pen(Color.White, Width / 14f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            float w = Width, h = Height;
            if (Galka) g.DrawLines(p, new[] { new PointF(w * .28f, h * .52f), new PointF(w * .44f, h * .67f), new PointF(w * .73f, h * .36f) });
            else { g.DrawLine(p, w * .5f, h * .28f, w * .5f, h * .56f); g.DrawLine(p, w * .5f, h * .71f, w * .5f, h * .72f); }
        }
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

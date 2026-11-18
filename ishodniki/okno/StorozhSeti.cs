// «Сторож сети» — окно над сторожем (storozh-seti\storozh.py) через pult.py.
// Логика сети вся в Python; окно показывает состояние и зовёт команды пульта.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Programma
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        Application.EnableVisualStyles();
        // ни одного окна «Необрабатываемое исключение»: ошибка уходит в журнал, программа работает дальше
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Okno.Zapis("ОШИБКА (окно не показано): " + e.Exception.GetType().Name + ": " + e.Exception.Message + " | " + (e.Exception.StackTrace ?? "").Split('\n').FirstOrDefault());
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Okno.Zapis("ОШИБКА в фоне: " + e.ExceptionObject);
        int i = Array.IndexOf(args, "--snimki");
        if (i >= 0 && i + 1 < args.Length) { Okno.Snimki(args[i + 1]); return; }
        int iPr = Array.IndexOf(args, "--proba-okno");
        if (iPr >= 0 && iPr + 1 < args.Length)
        {
            // проверка настоящего пути запуска (как у пользователя, с фоновым обновлением), но за экраном и без значка
            var o = new Okno(true); o.ShowInTaskbar = false; o.StartPosition = FormStartPosition.Manual; o.Location = new Point(-20000, -20000);
            var t = new System.Windows.Forms.Timer { Interval = 12000 };
            t.Tick += (s2, e2) =>
            {
                t.Stop();
                using (var b = new Bitmap(o.Width, o.Height)) { o.DrawToBitmap(b, new Rectangle(0, 0, o.Width, o.Height)); b.Save(args[iPr + 1]); }
                Okno.Zapis("[проба] элементов на странице: " + o.soderzh.Controls.Count + (o.soderzh.Controls.Count > 0 ? ", первая: " + o.soderzh.Controls[0].GetType().Name + " " + o.soderzh.Controls[0].Size + " детей " + o.soderzh.Controls[0].Controls.Count : ""));
                o.Close();
            };
            o.Shown += (s2, e2) => t.Start();
            Application.Run(o);
            return;
        }
        if (Array.IndexOf(args, "--samoproverka") >= 0) { try { Environment.ExitCode = Okno.Samoproverka(); } catch (Exception e) { Console.WriteLine("ОШИБКА: " + e); Environment.ExitCode = 99; } return; }
        if (Array.IndexOf(args, "--test-klik") >= 0) { try { Okno.TestKlik(); } catch (Exception e) { Console.WriteLine("ОШИБКА: " + e); } return; }
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
    public static readonly string Python = NaytiPython();
    static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    static string NaytiPapku()
    {
        var baza = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var p in new[] { Path.Combine(baza, "storozh-seti"), Path.Combine(baza, @"..\storozh-seti"), baza })
            if (File.Exists(Path.Combine(p, "pult.py"))) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baza, @"..\storozh-seti"));
    }

    static string NaytiPython()
    {
        try
        {
            foreach (var baza in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python"),
                                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                if (Directory.Exists(baza))
                    foreach (var d in Directory.GetDirectories(baza, "Python3*").OrderByDescending(x => x))
                        if (File.Exists(Path.Combine(d, "python.exe"))) return Path.Combine(d, "python.exe");
        }
        catch { }
        return "python.exe";
    }

    public static Dictionary<string, object> Vyzov(params string[] arg)
    {
        var a = new StringBuilder("-X utf8 \"" + Path.Combine(Papka, "pult.py") + "\"");
        foreach (var x in arg) a.Append(" \"" + x.Replace("\"", "\\\"") + "\"");
        var psi = new ProcessStartInfo(Python, a.ToString())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Papka
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        Process zap;
        try { zap = Process.Start(psi); }
        catch (Exception e) { return new Dictionary<string, object> { { "ok", false }, { "info", "не нашёл сторожа или Python: " + e.Message } }; }
        using (var p = zap)
        {
            var oshibki = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            try { return js.Deserialize<Dictionary<string, object>>(o); }
            catch { return new Dictionary<string, object> { { "ok", false }, { "info", "пульт не ответил: " + (oshibki.Result ?? "").Trim().Split('\n').LastOrDefault() } }; }
        }
    }

    public static string Json(object o) { return js.Serialize(o); }

    // флаги и значки из названий серверов: шрифт окна рисует их квадратиками
    public static string Chisto(string t)
    {
        if (string.IsNullOrEmpty(t)) return t;
        var sb = new StringBuilder();
        t = t.Replace("➗", " (÷)");   // «Германия➗» — отдельный сервер, без значка имена совпадут
        foreach (char c in t)
            if (!char.IsSurrogate(c) && !(c >= 0x2600 && c <= 0x27BF) && c != 0xFE0F && c != 0x200D) sb.Append(c);
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "  +", " ");
    }

    public static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
    public static bool B(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
    public static IEnumerable<object> L(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is IEnumerable && !(v is string) ? ((IEnumerable)v).Cast<object>() : new object[0]; }
    public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
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

    Palitra pal = SvoyaPal();
    readonly Dictionary<string, string> iskh = new Dictionary<string, string>();   // чистое имя сервера -> настоящее
    string Nast(string chistoe) { string v; return iskh.TryGetValue(chistoe, out v) ? v : chistoe; }
    static Palitra SvoyaPal()
    {
        var p = Palitra.Svoya();
        p.Fon = Color.FromArgb(23, 23, 27); p.Poverhnost = Color.FromArgb(34, 34, 42); p.Knopka = Color.FromArgb(45, 45, 54);
        p.Akcent = Color.FromArgb(79, 116, 217); p.NaAkcente = Color.White; p.Liniya = Color.FromArgb(56, 56, 66);
        p.Navedenie = Color.FromArgb(54, 54, 64); p.Radius = 8; p.Vtorichnyy = Color.FromArgb(145, 145, 158);
        return p;
    }
    Panel menu; internal Panel soderzh;
    Label zanyatoStroka;
    readonly List<Knopa> punkty = new List<Knopa>();
    readonly Dictionary<string, Panel> stranicy = new Dictionary<string, Panel>();
    string tekushchaya = "glavnaya";
    Dictionary<string, object> sost;
    volatile bool zanyat;
    NotifyIcon trey;

    public Okno(bool nastoyashchee)
    {
        Text = "Сторож сети";
        Size = new Size(900, 620);
        MinimumSize = new Size(720, 480);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        menu = new Panel { Dock = DockStyle.Left, Width = 190, Padding = new Padding(10, 12, 10, 10) };
        soderzh = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16) };
        zanyatoStroka = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(22, 6, 0, 0), Text = "" };
        Controls.Add(soderzh); Controls.Add(zanyatoStroka); Controls.Add(menu);

        var nazv = new Label { Text = "Сторож сети", Font = new Font("Segoe UI Semibold", 12f), Dock = DockStyle.Top, Height = 46, Padding = new Padding(4, 4, 0, 0) };
        var punktyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        foreach (var p in new[] { new[] { "glavnaya", "Монитор", "\uE9D9" }, new[] { "programmy", "Программы", Zn.Prilozh }, new[] { "sayty", "Сайты", Zn.Globus },
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
            Vstraivanie.TemaSmenilas += () => { pal = Vstraivanie.Tema == null ? SvoyaPal() : Palitra.Iz(Vstraivanie.Tema); Perekrasit(); };
            Vstraivanie.Podklyuchit(Handle, v => trey.Visible = v);
            FormClosed += (s, e) => trey.Dispose();
            var t = new System.Windows.Forms.Timer { Interval = 60000 };
            t.Tick += (s, e) => { if (!zanyat) Obnovit(); };
            t.Start();
            var t5 = new System.Windows.Forms.Timer { Interval = 5000 };
            t5.Tick += (s, e) => ObnovitSkorosti();
            t5.Start();
            var th = new System.Windows.Forms.Timer { Interval = 1500 };
            th.Tick += (s, e) => { if (zanyat && tekushchaya == "zapret") ObnovitHod(); };
            th.Start();
            Zapis("запуск; пульт: " + Pult.Papka + "; python: " + Pult.Python + "; админ: " + Admin());
        }
        Otkryt("glavnaya");
        if (nastoyashchee) Obnovit();
        // без оболочки окно растягивают руками — разделы перестраиваются под новую ширину (раз, после остановки)
        int shirBylo = ClientSize.Width;
        var tRazmer = new System.Windows.Forms.Timer { Interval = 250 };
        tRazmer.Tick += (s2, e2) => { tRazmer.Stop(); if (Math.Abs(ClientSize.Width - shirBylo) > 16) { shirBylo = ClientSize.Width; Perestroit(); } };
        Resize += (s2, e2) => { tRazmer.Stop(); tRazmer.Start(); };
    }

    // ---------- общее ----------
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
    static bool suho;   // --test-klik: действия только пишутся, пульт не зовётся

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

    // проверка: нажать мышью (WM_LBUTTONDOWN/UP) каждую кнопку каждого раздела и записать, что сработало
    public static void TestKlik()
    {
        suho = true;
        using (var o = new Okno(false))
        {
            o.ShowInTaskbar = false; o.StartPosition = FormStartPosition.Manual; o.Location = new Point(-20000, -20000);
            o.Size = new Size(1078, 2400);   // как у него по ширине; высокое — чтобы кнопки внизу были видны без прокрутки
            o.sost = Pult.Vyzov("sostoyanie"); o.progData = Pult.Vyzov("programmy"); o.servData = Pult.Vyzov("servery");
            o.zapData = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"youtube\":[\"204\",0.12,true],\"golos\":{\"est\":false},\"versiya\":\"тест\"}");
            o.saytyIst = Pult.Vyzov("sayty-istoriya"); o.lentaData = Pult.Vyzov("lenta");
            o.Show(); o.Location = new Point(-20000, -20000);
            foreach (var s in new[] { "glavnaya", "programmy", "sayty", "servery", "zapret", "zhurnal", "nastroyki" })
            {
                o.Otkryt(s); Application.DoEvents();
                var pervye = new List<Knopa>(); Sobrat(o.soderzh, pervye);
                for (int n = 0; n < pervye.Count; n++)
                {
                    o.Otkryt(s); Application.DoEvents();
                    var knopki = new List<Knopa>(); Sobrat(o.soderzh, knopki);
                    if (n >= knopki.Count) break;
                    var k = knopki[n];
                    bool bylo = false; EventHandler h = (a, b2) => bylo = true; k.Click += h;
                    var c = (IntPtr)((k.Height / 2 << 16) | (k.Width / 2));
                    SendMessage(k.Handle, 0x201, (IntPtr)1, c); SendMessage(k.Handle, 0x202, IntPtr.Zero, c);
                    Application.DoEvents();
                    if (!k.IsDisposed) k.Click -= h;
                    Console.WriteLine(s + " | " + k.Text + " | " + (bylo ? "нажалась" : "НЕ НАЖАЛАСЬ [" + k.Bounds + " в " + (k.Parent == null ? "-" : k.Parent.GetType().Name + " " + k.Parent.ClientSize + (k.Parent.Parent == null ? "" : " / " + k.Parent.Parent.ClientSize)) + ", видна " + k.Visible + "]"));
                }
            }
        }
    }

    static void Sobrat(Control r, List<Knopa> l) { foreach (Control c in r.Controls) { if (c is Knopa) l.Add((Knopa)c); Sobrat(c, l); } }

    void Deystvie(string chto, string[] arg, Action<Dictionary<string, object>> itog)
    {
        if (suho) { Zapis("[тест] нажато: " + chto); return; }
        if (zanyat) { MessageBox.Show(this, "Уже идёт: " + zanyatoStroka.Text, "Сторож сети"); return; }
        zanyat = true;
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
                    zanyatoStroka.Text = (Pult.B(d, "ok") ? "Готово: " : "Не вышло: ") + chto + (Pult.S(d, "info") != "" ? " — " + Pult.S(d, "info") : "");
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

    void Perestroit()
    {
        soderzh.SuspendLayout();
        try
        {
            soderzh.Controls.Clear();
            Panel p;
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
        }
        catch (Exception e)
        {
            // 19.11: в Коробке раздел оставался пустым — ошибку глотал Windows. Теперь видно и в окне, и в журнале.
            Zapis("ОШИБКА раздела " + tekushchaya + ": " + e.GetType().Name + ": " + e.Message + " | " + (e.StackTrace ?? "").Replace(Environment.NewLine, " / "));
            soderzh.Controls.Clear();
            soderzh.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Не получилось показать раздел: " + e.Message + Environment.NewLine + "Подробности — в журнале программы. Нажмите раздел ещё раз.", Padding = new Padding(10) });
        }
        finally { soderzh.ResumeLayout(); }
        try { Perekrasit(); } catch (Exception e) { Zapis("ОШИБКА покраски: " + e.Message); }
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
    Label Tx(string t, bool vt = false) { return new Label { Text = t, AutoSize = true, MaximumSize = new Size(620, 0), Tag = vt ? "vt" : null, Margin = new Padding(0, 0, 0, 8) }; }
    Knopa Kn(string t, Action d, bool glavnaya = false, int w = 230)
    {
        var k = new Knopa { Text = t, Width = w, Height = 38, Glavnaya = glavnaya, Margin = new Padding(0, 0, 10, 8) };
        k.Click += (s, e) => { Zapis("кнопка: " + t); d(); };
        return k;
    }
    FlowLayoutPanel Kolonka() { return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; }
    FlowLayoutPanel Ryad() { return new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8) }; }

    // ---------- Главная ----------
    void Sohranit(string key, object val)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var d = Pult.Vyzov("nastroyki-zadat", Pult.Json(new Dictionary<string, object> { { key, val } }));
            Zapis("настройка " + key + " = " + val + ": " + (Pult.B(d, "ok") ? "сохранена" : Pult.S(d, "info")));
            try { BeginInvoke((Action)(() => { zanyatoStroka.Text = "Сохранено"; Obnovit(); })); } catch { }
        });
    }

    // ---------- проверка вида: все разделы снимками ----------
    // самопроверка без оболочки: все разделы в трёх размерах окна — падения и элементы, вылезающие за край
    public static int Samoproverka()
    {
        suho = true;
        int plohih = 0;
        using (var o = new Okno(false))
        {
            o.ShowInTaskbar = false; o.StartPosition = FormStartPosition.Manual; o.Location = new Point(-20000, -20000);
            o.sost = Pult.Vyzov("sostoyanie"); o.progData = Pult.Vyzov("programmy"); o.servData = Pult.Vyzov("servery");
            o.zapData = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"youtube\":[\"204\",0.12,true],\"golos\":{\"est\":true,\"poteri\":8.8,\"ping\":71},\"versiya\":\"1.10.4\",\"posledn\":\"1.10.5\"}");
            o.saytyIst = Pult.Vyzov("sayty-istoriya"); o.lentaData = Pult.Vyzov("lenta");
            o.testData = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"ok\":true,\"vpn\":{\"mbit\":96,\"ping\":112},\"pryamo\":{\"mbit\":410,\"ping\":9},\"server\":\"Литва\"}");
            o.Show(); o.Location = new Point(-20000, -20000);
            foreach (var r in new[] { new Size(720, 480), new Size(900, 620), new Size(1078, 694), new Size(1600, 950) })
            {
                o.Size = r; Application.DoEvents();
                foreach (var s in new[] { "glavnaya", "programmy", "sayty", "servery", "zapret", "zhurnal", "nastroyki" })
                {
                    try { o.Otkryt(s); Application.DoEvents(); }
                    catch (Exception e) { Console.WriteLine("ПАДЕНИЕ " + r.Width + "x" + r.Height + " " + s + ": " + e.Message); plohih++; continue; }
                    foreach (var z in Vylezaet(o.soderzh)) { Console.WriteLine("ВЫЛЕЗАЕТ " + r.Width + "x" + r.Height + " " + s + ": " + z); plohih++; }
                }
            }
        }
        Console.WriteLine(plohih == 0 ? "ИТОГ: всё в порядке" : "ИТОГ: замечаний " + plohih);
        return plohih;
    }

    static IEnumerable<string> Vylezaet(Control r)
    {
        foreach (Control c in r.Controls)
        {
            if (c.Visible && (r is Kartochka || (r is Panel && !(r is FlowLayoutPanel) && r != null && r.Parent is FlowLayoutPanel)))
            {
                if (c.Right > r.ClientSize.Width + 2) yield return c.GetType().Name + " «" + Pult.Chisto(c.Text) + "» правый край " + c.Right + " > " + r.ClientSize.Width;
                if (c.Bottom > r.ClientSize.Height + 2) yield return c.GetType().Name + " «" + Pult.Chisto(c.Text) + "» нижний край " + c.Bottom + " > " + r.ClientSize.Height;
            }
            foreach (var z in Vylezaet(c)) yield return z;
        }
    }

    public static void Snimki(string papka)
    {
        Directory.CreateDirectory(papka);
        using (var o = new Okno(false))
        {
            o.ShowInTaskbar = false; o.StartPosition = FormStartPosition.Manual; o.Location = new Point(-20000, -20000);
            o.Size = new Size(1078, Environment.GetEnvironmentVariable("STOROZH_VYS") != null ? int.Parse(Environment.GetEnvironmentVariable("STOROZH_VYS")) : 694);
            o.sost = Pult.Vyzov("sostoyanie");
            o.progData = Pult.Vyzov("programmy");
            o.servData = Pult.Vyzov("servery");
            o.zapData = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"youtube\":[\"204\",0.12,true],\"golos\":{\"est\":true,\"poteri\":8.8,\"ping\":71},\"versiya\":\"1.10.4 (пример)\"}");   // снимки: без прямых запросов
            o.saytyIst = Pult.Vyzov("sayty-istoriya"); o.lentaData = Pult.Vyzov("lenta");
            o.testData = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"ok\":true,\"vpn\":{\"mbit\":96,\"ping\":112},\"pryamo\":{\"mbit\":410,\"ping\":9},\"server\":\"Литва (пример)\"}");
            o.setData = Pult.Vyzov("set-seychas");
            o.skorData = Pult.Vyzov("skorosti");
            o.Show(); o.Location = new Point(-20000, -20000);
            foreach (var temy in new[] { "svoya", "svetloe" })
            {
                o.pal = temy == "svoya" ? SvoyaPal() : Palitra.Iz(new TemaObolochki { Fon = 0xF5F7FA, Poverhnost = 0xFFFFFF, Tekst = 0x1A1C21, Vtorichnyy = 0x5F6980, Akcent = 0x2563EB, NaAkcente = 0xFFFFFF, Liniya = 0xD6D8DB, Navedenie = 0xD7DCE6, Radius = 9, Temnaya = false });
                foreach (var s in new[] { "glavnaya", "programmy", "sayty", "servery", "zapret", "zhurnal", "nastroyki" })
                {
                    o.Otkryt(s);
                    Application.DoEvents(); Thread.Sleep(100); Application.DoEvents();
                    using (var b = new Bitmap(o.Width, o.Height)) { o.DrawToBitmap(b, new Rectangle(0, 0, o.Width, o.Height)); b.Save(Path.Combine(papka, temy + "-" + s + ".png")); }
                }
            }
        }
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

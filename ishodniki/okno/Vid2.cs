using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// Значки — из системного шрифта Windows 11 «Segoe Fluent Icons» (на Windows 10 — «Segoe MDL2 Assets»). Сами не рисуем.
static class Zn
{
    public const string Dom = "", Prilozh = "", Globus = "", Server = "", Molniya = "",
        Spisok = "", Nastr = "", Shit = "", Galka = "", Obnovit = "", Skorost = "",
        Preduprezhd = "", Chasy = "", Raketa = "";
    // 21.11: ход действий и список настройки — тоже из шрифта (EC61 CompletedSolid, ECCA RadioBtnOff, ECCB RadioBtnOn,
    // E711 Cancel, EB90 StatusErrorFull; F16A ProgressRingDots есть только в Segoe Fluent — на Windows 10 вместо него ECCB)
    public const string GalkaKrug = "", Krug0 = "", KrugTochka = "", Krestik = "", OshibkaKrug = "";
    public static string Idet { get { return Semya == "Segoe Fluent Icons" ? "" : KrugTochka; } }
    static string semya;
    public static string Semya
    {
        get
        {
            if (semya == null)
                using (var f = new System.Drawing.Text.InstalledFontCollection())
                    semya = f.Families.Any(x => x.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            return semya;
        }
    }
    public static Font Shrift(float r) { return new Font(Semya, r); }
}

static class Ris
{
    public static GraphicsPath Skrugl(RectangleF p, float r)
    {
        var gp = new GraphicsPath();
        if (r < 1) { gp.AddRectangle(p); return gp; }
        float d = Math.Min(r * 2, Math.Min(p.Width, p.Height));
        gp.AddArc(p.X, p.Y, d, d, 180, 90); gp.AddArc(p.Right - d, p.Y, d, d, 270, 90);
        gp.AddArc(p.Right - d, p.Bottom - d, d, d, 0, 90); gp.AddArc(p.X, p.Bottom - d, d, d, 90, 90);
        gp.CloseFigure();
        return gp;
    }
    public static Color Smes(Color a, Color b, double k) { return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k)); }
}

// Карточка со скруглёнными углами. Цвет — Poverhnost или свой (Ottenok), фон вокруг — цвет родителя.
class Kartochka : Panel
{
    public Palitra Pal = Palitra.Svoya();
    public Color? Ottenok;
    public Kartochka() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color vokrug = Pal.Fon;
        for (var r = Parent; r != null; r = r.Parent) if (r.BackColor.A == 255) { vokrug = r.BackColor; break; }
        g.Clear(vokrug);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), Math.Max(6, Pal.Radius + 4)))
        using (var b = new SolidBrush(Ottenok ?? Pal.Poverhnost)) g.FillPath(b, p);
    }
    protected override void OnPaint(PaintEventArgs e) { }
}

// Подписи внутри карточки: прозрачные, рисуются поверх её заливки
class Nadpis : Label
{
    public Nadpis() { SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; AutoSize = true; }
}

// Подробный график: столбики (с сеткой и подписями оси), полосы (по серверам) или линия (по замерам).
// Наведение мыши — подсказка с подробностями точки.
class Grafik2 : Control
{
    public class T { public string Podpis, Nad, Podskazka; public double Zn; public bool Vyd; public double? Vtoroe; }
    public enum Vid { Stolbiki, Polosy, Liniya }
    public Palitra Pal = Palitra.Svoya();
    public Vid Rezhim;
    public string Os = "";          // подпись к шкале (например, «обрывов»)
    public Color? Cvet;             // цвет выделенного/линии; иначе Akcent
    public List<T> Tochki = new List<T>();
    readonly ToolTip tip = new ToolTip { InitialDelay = 80, ReshowDelay = 40 };
    int navedeno = -1;
    readonly List<RectangleF> zony = new List<RectangleF>();

    public Grafik2() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void Dispose(bool d) { if (d) tip.Dispose(); base.Dispose(d); }

    Color Glavn { get { return Cvet ?? (Pal.Akcent.GetBrightness() > 0.92 ? Pal.Tekst : Pal.Akcent); } }
    Color Tusk { get { return Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.22); } }
    Color Setka { get { return Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.09); } }

    static double Kruglo(double max)
    {
        if (max <= 5) return 5;
        double st = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var k in new[] { 1, 2, 2.5, 5, 10 }) if (k * st >= max) return k * st;
        return 10 * st;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = zony.FindIndex(z => z.Contains(e.Location));
        if (i != navedeno)
        {
            navedeno = i; Invalidate();
            if (i >= 0 && i < Tochki.Count && !string.IsNullOrEmpty(Tochki[i].Podskazka)) tip.Show(Tochki[i].Podskazka, this, e.X + 14, e.Y + 10);
            else tip.Hide(this);
        }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { navedeno = -1; tip.Hide(this); Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        zony.Clear();
        if (Tochki.Count == 0) return;
        var f = Font; var fm = new Font(Font.FontFamily, Font.Size - 1f);
        double max = Kruglo(Tochki.Max(t => Math.Max(t.Zn, t.Vtoroe ?? 0)));
        if (Rezhim == Vid.Polosy) { RisPolosy(g, f, fm, max); return; }

        int lev = 40, niz = 24, verh = 14, prav = 8;
        var obl = new RectangleF(lev, verh, Width - lev - prav, Height - verh - niz);
        using (var ps = new Pen(Setka))
            for (int k = 0; k <= 4; k++)
            {
                float y = obl.Bottom - obl.Height * k / 4f;
                g.DrawLine(ps, obl.Left, y, obl.Right, y);
                TextRenderer.DrawText(g, (max * k / 4).ToString("0"), fm, new Rectangle(0, (int)y - 8, lev - 6, 16), Pal.Vtorichnyy, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        if (Os != "") TextRenderer.DrawText(g, Os, fm, new Point(lev, 0), Pal.Vtorichnyy);
        int n = Tochki.Count; float col = obl.Width / n;
        int shag = Math.Max(1, (int)Math.Ceiling(n / (obl.Width / 46f)));   // подписи оси не налезают друг на друга
        if (Rezhim == Vid.Stolbiki)
        {
            for (int i = 0; i < n; i++)
            {
                var t = Tochki[i];
                float h = (float)(obl.Height * t.Zn / max), w = Math.Max(4, Math.Min(34, col * 0.62f)), x = obl.Left + col * i + (col - w) / 2;
                var c = t.Vyd ? Glavn : Tusk;
                if (i == navedeno) c = Ris.Smes(c, Pal.Tekst, 0.25);
                if (h > 0.5f)
                    using (var b = new SolidBrush(c)) using (var p = Ris.Skrugl(new RectangleF(x, obl.Bottom - h, w, h), Math.Min(5, w / 2))) g.FillPath(b, p);
                if (t.Nad != "" && t.Nad != null && (n <= 12 || i == navedeno || t.Vyd)) TextRenderer.DrawText(g, t.Nad, fm, new Rectangle((int)(x - 20), (int)(obl.Bottom - h - 17), (int)w + 40, 16), Pal.Tekst, TextFormatFlags.HorizontalCenter);
                if (i % shag == 0 || i == n - 1) TextRenderer.DrawText(g, t.Podpis, fm, new Rectangle((int)(obl.Left + col * i - 10), (int)obl.Bottom + 4, (int)col + 20, 18), Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter);
                zony.Add(new RectangleF(obl.Left + col * i, obl.Top, col, obl.Height + niz));
            }
        }
        else
        {
            var pts = new PointF[n];
            for (int i = 0; i < n; i++) pts[i] = new PointF(obl.Left + col * i + col / 2, (float)(obl.Bottom - obl.Height * Tochki[i].Zn / max));
            using (var zal = new GraphicsPath())
            {
                zal.AddLines(pts); zal.AddLine(pts[n - 1], new PointF(pts[n - 1].X, obl.Bottom)); zal.AddLine(new PointF(pts[n - 1].X, obl.Bottom), new PointF(pts[0].X, obl.Bottom));
                using (var b = new SolidBrush(Color.FromArgb(40, Glavn))) g.FillPath(b, zal);
            }
            using (var p = new Pen(Glavn, 2.4f) { LineJoin = LineJoin.Round }) if (n > 1) g.DrawLines(p, pts);
            for (int i = 0; i < n; i++)
            {
                float r = i == navedeno ? 6 : 4;
                using (var b = new SolidBrush(Glavn)) g.FillEllipse(b, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
                using (var b = new SolidBrush(Pal.Poverhnost)) g.FillEllipse(b, pts[i].X - r / 2, pts[i].Y - r / 2, r, r);
                if (Tochki[i].Nad != null && (i == navedeno || i == n - 1)) TextRenderer.DrawText(g, Tochki[i].Nad, fm, new Rectangle((int)pts[i].X - 60, (int)pts[i].Y - 22, 120, 16), Pal.Tekst, TextFormatFlags.HorizontalCenter);
                if (i % shag == 0 || i == n - 1) TextRenderer.DrawText(g, Tochki[i].Podpis, fm, new Rectangle((int)(obl.Left + col * i - 10), (int)obl.Bottom + 4, (int)col + 20, 18), Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter);
                zony.Add(new RectangleF(obl.Left + col * i, obl.Top, col, obl.Height + niz));
            }
        }
    }

    void RisPolosy(Graphics g, Font f, Font fm, double max)
    {
        int n = Tochki.Count, rowH = Math.Max(22, (Height - 22) / n), podp = 120, prav = 330;
        float shir = Width - podp - prav;
        using (var ps = new Pen(Setka))
            for (int k = 0; k <= 4; k++)
            {
                float x = podp + shir * k / 4f;
                g.DrawLine(ps, x, 0, x, n * rowH);
                TextRenderer.DrawText(g, (max * k / 4).ToString("0"), fm, new Rectangle((int)x - 20, n * rowH + 2, 40, 18), Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter);
            }
        if (Os != "") TextRenderer.DrawText(g, Os, fm, new Point(podp + (int)shir + 8, n * rowH + 2), Pal.Vtorichnyy);
        for (int i = 0; i < n; i++)
        {
            var t = Tochki[i]; int y = i * rowH;
            if (i == navedeno) using (var b = new SolidBrush(Setka)) g.FillRectangle(b, 0, y, Width, rowH);
            TextRenderer.DrawText(g, t.Podpis, f, new Rectangle(6, y, podp - 10, rowH), t.Vyd ? Pal.Tekst : Pal.Vtorichnyy, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            float w = (float)(shir * t.Zn / max);
            using (var b = new SolidBrush(t.Vyd ? Glavn : Tusk)) using (var p = Ris.Skrugl(new RectangleF(podp, y + rowH * 0.22f, Math.Max(3, w), rowH * 0.56f), rowH * 0.2f)) g.FillPath(b, p);
            TextRenderer.DrawText(g, t.Nad, fm, new Rectangle(podp + (int)shir + 8, y, prav - 8, rowH), t.Vyd ? Pal.Tekst : Pal.Vtorichnyy, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            zony.Add(new RectangleF(0, y, Width, rowH));
        }
    }
}

// Мини-график в плитке
class Iskra : Control
{
    public Palitra Pal = Palitra.Svoya();
    public Color? Cvet;
    public List<double> Zn = new List<double>();
    public Iskra() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Zn.Count < 2) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        double max = Math.Max(1, Zn.Max()), min = Math.Min(0, Zn.Min());
        var pts = Zn.Select((v, i) => new PointF(2 + (Width - 4) * i / (float)(Zn.Count - 1), (float)(Height - 3 - (Height - 6) * (v - min) / (max - min)))).ToArray();
        var c = Cvet ?? Pal.Akcent;
        using (var zal = new GraphicsPath())
        {
            zal.AddLines(pts); zal.AddLine(pts.Last(), new PointF(pts.Last().X, Height)); zal.AddLine(new PointF(pts.Last().X, Height), new PointF(pts[0].X, Height));
            using (var b = new SolidBrush(Color.FromArgb(45, c))) g.FillPath(b, zal);
        }
        using (var p = new Pen(c, 2f) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
    }
}

using System.Collections.Generic;
using System.Linq;
using System;
using System.Drawing;
using System.Windows.Forms;

// Палитра окна: одна точка, где заданы все цвета. Тема оболочки подменяет её целиком.
class Palitra
{
    public Color Fon, Poverhnost, Tekst, Vtorichnyy, Akcent, NaAkcente, Liniya, Navedenie, Knopka;
    public int Radius;
    public bool Temnaya;

    static Color C(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }

    // свой вид (без оболочки)
    public static Palitra Svoya()
    {
        return new Palitra
        {
            Fon = Color.FromArgb(24, 24, 28), Poverhnost = Color.FromArgb(36, 36, 42),
            Tekst = Color.FromArgb(230, 230, 235), Vtorichnyy = Color.FromArgb(160, 160, 170),
            Knopka = Color.FromArgb(52, 52, 60), Akcent = Color.FromArgb(52, 52, 60), NaAkcente = Color.FromArgb(230, 230, 235),
            Liniya = Color.FromArgb(70, 70, 80), Navedenie = Color.FromArgb(64, 64, 74), Radius = 0, Temnaya = true
        };
    }

    public static Palitra Iz(TemaObolochki t)
    {
        if (t == null) return Svoya();
        return new Palitra
        {
            Fon = C(t.Fon), Poverhnost = C(t.Poverhnost), Tekst = C(t.Tekst), Vtorichnyy = C(t.Vtorichnyy),
            Knopka = C(t.Fon), Akcent = C(t.Akcent), NaAkcente = C(t.NaAkcente), Liniya = C(t.Liniya),
            Navedenie = C(t.Navedenie), Radius = Math.Max(0, t.Radius), Temnaya = t.Temnaya
        };
    }
}

// Кнопка, нарисованная своими цветами (скругление, наведение, главная — акцентом).
class Knopa : Button
{
    Palitra pal = Palitra.Svoya();
    bool pod, nazhata;
    public bool Glavnaya;
    public string Znachok;   // символ из Segoe Fluent Icons (класс Zn)
    public Palitra Pal { get { return pal; } set { pal = value; Invalidate(); } }

    public Knopa()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { pod = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { pod = false; nazhata = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { nazhata = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { nazhata = false; Invalidate(); base.OnMouseUp(e); }

    static Color Smes(Color a, Color b, double k)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // фон под скруглёнными углами — первый непрозрачный родитель (прозрачный даёт чёрные углы)
        Color podFon = pal.Poverhnost;
        for (var rod = Parent; rod != null; rod = rod.Parent) if (rod.BackColor.A == 255) { podFon = rod.BackColor; break; }
        g.Clear(podFon);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color fon = Glavnaya ? pal.Akcent : pal.Knopka;
        Color tekst = Glavnaya ? pal.NaAkcente : pal.Tekst;
        if (Glavnaya) { if (pod) fon = Smes(fon, pal.Temnaya ? Color.Black : Color.White, nazhata ? 0.25 : 0.12); }
        else if (pod) fon = nazhata ? Smes(pal.Navedenie, pal.Tekst, 0.1) : pal.Navedenie;
        if (!Enabled) tekst = pal.Vtorichnyy;
        float r = pal.Radius * DeviceDpi / 96f;
        var pr = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var put = Put(pr, r))
        {
            using (var k = new SolidBrush(fon)) g.FillPath(k, put);
            if (!Glavnaya) using (var p = new Pen(pal.Liniya)) g.DrawPath(p, put);
        }
        int otst = 12;
        // узкая кнопка со значком (меню узкого окна) — только значок посередине, подпись в подсказке
        if (!string.IsNullOrEmpty(Znachok) && Width < 70)
        {
            using (var fz = Zn.Shrift(Font.Size + 2f))
                TextRenderer.DrawText(g, Znachok, fz, new Rectangle(0, 0, Width, Height), tekst, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            return;
        }
        if (!string.IsNullOrEmpty(Znachok))
        {
            using (var fz = Zn.Shrift(Font.Size + 1f))
                TextRenderer.DrawText(g, Znachok, fz, new Rectangle(12, 0, 22, Height), tekst, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            otst = 42;
        }
        var obl = new Rectangle(otst, 0, Width - otst - 8, Height);
        TextRenderer.DrawText(g, Text, Font, obl, tekst, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    static System.Drawing.Drawing2D.GraphicsPath Put(RectangleF p, float r)
    {
        var gp = new System.Drawing.Drawing2D.GraphicsPath();
        if (r < 1) { gp.AddRectangle(p); return gp; }
        float d = Math.Min(r * 2, Math.Min(p.Width, p.Height));
        gp.AddArc(p.X, p.Y, d, d, 180, 90);
        gp.AddArc(p.Right - d, p.Y, d, d, 270, 90);
        gp.AddArc(p.Right - d, p.Bottom - d, d, d, 0, 90);
        gp.AddArc(p.X, p.Bottom - d, d, d, 90, 90);
        gp.CloseFigure();
        return gp;
    }
}

// Простой график столбиками: вертикальный (по дням) или горизонтальный (по серверам).
class Grafik : Control
{
    public Palitra Pal = Palitra.Svoya();
    public bool Gorizont;
    public string Edinica = "";
    public List<Tuple<string, double, string, bool>> Dannye = new List<Tuple<string, double, string, bool>>();  // подпись, значение, надпись у столбика, выделить

    public Grafik() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Pal.Poverhnost);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (Dannye.Count == 0) return;
        double max = Math.Max(1, Dannye.Max(d => d.Item2));
        var f = Font;
        using (var bVyd = new SolidBrush(Pal.Akcent))
        using (var bObych = new SolidBrush(Smes(Pal.Poverhnost, Pal.Tekst, 0.28)))
        {
            if (Gorizont)
            {
                int n = Dannye.Count, rowH = Math.Max(18, (Height - 8) / n), podp = 130, prav = 290;
                for (int i = 0; i < n; i++)
                {
                    var d = Dannye[i]; int y = 4 + i * rowH;
                    TextRenderer.DrawText(g, d.Item1, f, new Rectangle(6, y, podp - 8, rowH), d.Item4 ? Pal.Tekst : Pal.Vtorichnyy, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    int w = (int)((Width - podp - prav) * d.Item2 / max);
                    g.FillRectangle(d.Item4 ? bVyd : bObych, podp, y + rowH / 4, Math.Max(2, w), rowH / 2);
                    TextRenderer.DrawText(g, d.Item3, f, new Rectangle(podp + w + 6, y, prav + 200, rowH), d.Item4 ? Pal.Tekst : Pal.Vtorichnyy, TextFormatFlags.VerticalCenter);
                }
            }
            else
            {
                int n = Dannye.Count, niz = 22, verh = 18, colW = Math.Max(20, (Width - 10) / n);
                for (int i = 0; i < n; i++)
                {
                    var d = Dannye[i]; int x = 5 + i * colW;
                    int h = (int)((Height - niz - verh) * d.Item2 / max);
                    g.FillRectangle(d.Item4 ? bVyd : bObych, x + colW / 4, Height - niz - h, colW / 2, Math.Max(1, h));
                    TextRenderer.DrawText(g, d.Item3, f, new Rectangle(x, Height - niz - h - verh, colW, verh), Pal.Tekst, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
                    TextRenderer.DrawText(g, d.Item1, f, new Rectangle(x, Height - niz + 2, colW, niz), Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter);
                }
            }
        }
    }

    static Color Smes(Color a, Color b, double k) { return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k)); }
}

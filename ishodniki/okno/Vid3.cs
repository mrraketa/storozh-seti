using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// Спидометр: дуга, число в центре, подпись снизу
class Spidometr : Control
{
    public Palitra Pal = Palitra.Svoya();
    public double Zn, Max = 300;
    public string Ed = "Мбит/с";
    public Spidometr() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float t = Width / 13f, d = Math.Min(Width - t * 2, (Height - t) * 2 - t);
        var r = new RectangleF((Width - d) / 2, t, d, d);
        using (var p = new Pen(Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.12), t) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, 180, 180);
        float ugol = (float)(180 * Math.Min(1, Math.Max(0.01, Zn / Max)));
        using (var p = new Pen(Pal.Akcent.GetBrightness() > 0.92 ? Pal.Tekst : Pal.Akcent, t) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, r, 180, ugol);
        using (var f = new Font("Segoe UI Semibold", Width / 7.5f))
            TextRenderer.DrawText(g, Zn.ToString("0"), f, new Rectangle(0, (int)(r.Top + d * 0.16f), Width, (int)(d * 0.36f)), Pal.Tekst, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Ed, Font, new Rectangle(0, (int)(r.Top + d * 0.5f) - 6, Width, 20), Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter);
    }
}

// Полоса суток: 24 клетки, цвет — как работал VPN в этот час; наведение — подробности
class PolosaSutok : Control
{
    public Palitra Pal = Palitra.Svoya();
    public int[] Obryvy = new int[24], Sekund = new int[24];
    public int TekChas = DateTime.Now.Hour;
    readonly ToolTip tip = new ToolTip { InitialDelay = 60 };
    int nav = -1;
    public static readonly Color Zel = Color.FromArgb(47, 163, 94), Zhel = Color.FromArgb(217, 162, 79), Kras = Color.FromArgb(214, 90, 79);
    public PolosaSutok() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    int Chas(int i) { return (TekChas + 1 + i) % 24; }   // слева — сутки назад, справа — текущий час
    Color Cvet(int h) { return Obryvy[h] == 0 ? Zel : (Obryvy[h] >= 3 || Sekund[h] > 60 ? Kras : Zhel); }
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
            if (i >= 0) { int ch = Chas(i); tip.Show(ch.ToString("00") + ":00–" + ch.ToString("00") + ":59\n" + (Obryvy[ch] == 0 ? "без обрывов" : "обрывов " + Obryvy[ch] + ", VPN не отвечал " + Sekund[ch] + " с"), this, e.X + 12, e.Y + 12); }
            else tip.Hide(this);
        }
    }
    protected override void OnMouseLeave(EventArgs e) { nav = -1; tip.Hide(this); Invalidate(); }
}

// Переключатель-сегменты: «Авто | VPN | Напрямую | Запрет»
class Segmenty : Control
{
    public Palitra Pal = Palitra.Svoya();
    public string[] Punkty = new string[0];
    public int Vybran;
    public event Action<int> Smena;
    int nav = -1;
    public Segmenty() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; Cursor = Cursors.Hand; }
    RectangleF Kl(int i) { float w = (Width - 4f) / Punkty.Length; return new RectangleF(2 + i * w, 2, w, Height - 4); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Pal.Knopka)) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), 8)) g.FillPath(b, p);
        using (var f = new Font(Font.FontFamily, Font.Size - 1))
            for (int i = 0; i < Punkty.Length; i++)
            {
                var r = Kl(i);
                if (i == Vybran) using (var b = new SolidBrush(Pal.Akcent)) using (var p = Ris.Skrugl(r, 6)) g.FillPath(b, p);
                else if (i == nav) using (var b = new SolidBrush(Pal.Navedenie)) using (var p = Ris.Skrugl(r, 6)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Punkty[i], f, Rectangle.Round(r), i == Vybran ? Pal.NaAkcente : Pal.Vtorichnyy, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Vkl ? Pal.Akcent : Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.25))) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f)) g.FillPath(b, p);
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
    public Uroven() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Ris.Smes(Pal.Poverhnost, Pal.Tekst, 0.12))) using (var p = Ris.Skrugl(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f)) g.FillPath(b, p);
        float w = (float)(Width * Math.Min(1, Math.Max(0, Dolya)));
        if (w > 2) using (var b = new SolidBrush(Pal.Akcent.GetBrightness() > 0.92 ? Pal.Tekst : Pal.Akcent)) using (var p = Ris.Skrugl(new RectangleF(0, 0, w, Height - 1), Height / 2f)) g.FillPath(b, p);
    }
}

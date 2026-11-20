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
    protected override void Dispose(bool d) { if (d) tip.Dispose(); base.Dispose(d); }   // раздел перестраивается раз в минуту — подсказки не копить
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
        Zakryt = new Knopa { Text = "Закрыть", Znachok = Zn.Krestik, Size = new Size(34, 30), Visible = false, Margin = Padding.Empty };
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
            string zag = h.Nazvanie + (procent != null ? " — " + procent + "%" : " — идёт…");
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
                Dob(tk ? h.TekstShaga(i) : shagi[i], x0 + 30, y + 2, pr - x0 - 30, stroka, tk ? Cv.Tekst : Cv.Vt, tk ? fZhir : fObych);
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
            Dob((ok ? "Готово: " : "Не вышло: ") + h.Nazvanie, tx, y + 2, tw, fZag.Height + 4, ok ? Cv.ZagOk : Cv.ZagNe, fZag);
            y += fZag.Height + 10;
            if (h.Itog != "")
            {
                int th = VysT(h.Itog, tw, fObych);
                Dob(h.Itog, tx, y, tw, th, Cv.Tekst, fObych, true);
                y += th + 4;
            }
            Dob("заняло " + Okno.Vremya(h.Proshlo), tx, y, tw, stroka, Cv.Vt, fObych);
            y += stroka;
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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Ottenok = H != null && !H.Idet ? Ris.Smes(Pal.Poverhnost, H.Ok == true ? Okno.Zel : Okno.Kras, Pal.Temnaya ? 0.16 : 0.10) : (Color?)null;
        base.OnPaintBackground(e);
    }

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

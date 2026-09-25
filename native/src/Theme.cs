// SoundMaster theme — light editorial system per DESIGN-figma.md:
// white canvas + black ink chrome, weight-based hierarchy, pill buttons, hairline borders,
// pastel color-block section headers, mono uppercase eyebrows, shadow-free depth.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SoundMaster
{
    public static class Theme
    {
        public static bool IsDark;

        // ---- system core (assigned by Apply) ----
        public static Color Ink, Canvas, InverseInk, InverseCanvas, SurfaceSoft, Hairline, HairlineSoft;
        public static Color TextSub, TextDim, WireIn, WireMix, WireIdle, WireMuted;
        public static Color Meter, SliderTrack, SliderThumb, ScrollThumb;

        // ---- color blocks (theme-stable: sticky notes read on both grounds) ----
        public static Color BlockLime = ColorTranslator.FromHtml("#dceeb1");
        public static Color BlockLilac = ColorTranslator.FromHtml("#c5b0f4");
        public static Color BlockCream = ColorTranslator.FromHtml("#f4ecd6");
        public static Color BlockPink = ColorTranslator.FromHtml("#efd4d4");
        public static Color BlockMint = ColorTranslator.FromHtml("#c8e6cd");
        public static Color BlockCoral = ColorTranslator.FromHtml("#f3c9b6");
        public static Color BlockNavy = ColorTranslator.FromHtml("#1f1d3d");

        // ---- accents (scarce by design) ----
        public static Color AccentMagenta = ColorTranslator.FromHtml("#ff3d8b");
        public static Color Success;

        // legacy aliases still referenced by views (kept in sync by Apply)
        public static Color Bg, Card, CardHover, CardSel, Text, Side, Line, Accent, Danger, WireOut;

        static Theme() { Apply(false); }

        // The dark theme is the editorial system inverted: ink and canvas trade places
        // ("selected = primary" becomes a white pill), pastel blocks stay saturated, and
        // navy input wires hand over to lilac — navy is invisible on a dark ground.
        public static void Apply(bool dark)
        {
            IsDark = dark;
            if (!dark)
            {
                Ink = ColorTranslator.FromHtml("#000000");
                Canvas = ColorTranslator.FromHtml("#ffffff");
                InverseInk = ColorTranslator.FromHtml("#ffffff");
                InverseCanvas = ColorTranslator.FromHtml("#000000");
                SurfaceSoft = ColorTranslator.FromHtml("#f7f7f5");
                Hairline = ColorTranslator.FromHtml("#e6e6e6");
                HairlineSoft = ColorTranslator.FromHtml("#f1f1f1");
                TextSub = ColorTranslator.FromHtml("#4d4d4d");
                TextDim = ColorTranslator.FromHtml("#9a9a9a");
                WireIn = BlockNavy;
                WireMix = AccentMagenta;
                WireIdle = ColorTranslator.FromHtml("#dcdcdc");
                WireMuted = ColorTranslator.FromHtml("#9a9a9a");
                Success = ColorTranslator.FromHtml("#1ea64a");
                SliderTrack = Hairline;
                ScrollThumb = Color.FromArgb(70, 0, 0, 0);
            }
            else
            {
                Ink = ColorTranslator.FromHtml("#f5f5f5");
                Canvas = ColorTranslator.FromHtml("#121212");
                InverseInk = ColorTranslator.FromHtml("#111111");
                InverseCanvas = ColorTranslator.FromHtml("#f5f5f5");
                SurfaceSoft = ColorTranslator.FromHtml("#1e1e1c");
                Hairline = ColorTranslator.FromHtml("#2f2f2f");
                HairlineSoft = ColorTranslator.FromHtml("#262626");
                TextSub = ColorTranslator.FromHtml("#b0b0b0");
                TextDim = ColorTranslator.FromHtml("#6e6e6e");
                WireIn = BlockLilac;
                WireMix = AccentMagenta;
                WireIdle = ColorTranslator.FromHtml("#303030");
                WireMuted = ColorTranslator.FromHtml("#6e6e6e");
                Success = ColorTranslator.FromHtml("#25c15b");
                SliderTrack = ColorTranslator.FromHtml("#303030");
                ScrollThumb = Color.FromArgb(80, 255, 255, 255);
            }
            Meter = Success;
            SliderThumb = Ink;
            Bg = Canvas; Card = Canvas; CardHover = SurfaceSoft; CardSel = Ink;
            Text = Ink; Side = Canvas; Line = Hairline; Accent = Ink;
            Danger = AccentMagenta; WireOut = Success;
        }

        // ---- type — Segoe UI stands in for figmaSans, Consolas for figmaMono ----
        public static Font Display = new Font("Segoe UI Semilight", 19f);          // view titles
        public static Font Headline = new Font("Segoe UI Semibold", 10.5f);
        public static Font F10b = new Font("Segoe UI Semibold", 10f);              // card titles
        public static Font F10 = new Font("Segoe UI", 10f);
        public static Font F9b = new Font("Segoe UI Semibold", 9f);
        public static Font F9 = new Font("Segoe UI", 9.25f);                       // body
        public static Font F8b = new Font("Segoe UI Semibold", 8.25f);             // pill labels
        public static Font F8 = new Font("Segoe UI", 8.5f);                        // body-sm
        public static Font Eyebrow = new Font("Consolas", 8.25f, FontStyle.Bold);  // MONO UPPERCASE
        public static Font Caption = new Font("Consolas", 7.75f);                  // mono captions
        public static Font F14b = new Font("Segoe UI Semilight", 15f);             // wordmark

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (GraphicsPath p = Round(r, rad))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        // Pill = fully rounded rect; the only button shape in the system.
        public static void FillPill(Graphics g, RectangleF r, Color c)
        {
            FillRound(g, r, r.Height / 2f, c);
        }
        public static void StrokePill(Graphics g, RectangleF r, Color c, float w)
        {
            StrokeRound(g, r, r.Height / 2f, c, w);
        }

        // Small vector glyphs, stroke-drawn, single icon voice.
        public static void Glyph(Graphics g, string kind, RectangleF r, Color c, float strokeW)
        {
            using (Pen pen = new Pen(c, strokeW))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                switch (kind)
                {
                    case "speaker":
                        using (GraphicsPath p = new GraphicsPath())
                        {
                            p.AddLines(new PointF[] {
                                new PointF(x + w*0.14f, y + h*0.38f), new PointF(x + w*0.32f, y + h*0.38f),
                                new PointF(x + w*0.52f, y + h*0.2f),  new PointF(x + w*0.52f, y + h*0.8f),
                                new PointF(x + w*0.32f, y + h*0.62f), new PointF(x + w*0.14f, y + h*0.62f) });
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        g.DrawArc(pen, x + w*0.42f, y + h*0.3f, w*0.36f, h*0.4f, -55, 110);
                        break;
                    case "mic":
                        g.DrawPath(pen, Round(new RectangleF(x + w*0.38f, y + h*0.1f, w*0.24f, h*0.42f), w*0.12f));
                        g.DrawArc(pen, x + w*0.24f, y + h*0.26f, w*0.52f, h*0.5f, 0, 180);
                        g.DrawLine(pen, x + w*0.5f, y + h*0.76f, x + w*0.5f, y + h*0.9f);
                        break;
                    case "app":
                        g.DrawPath(pen, Round(new RectangleF(x + w*0.18f, y + h*0.18f, w*0.64f, h*0.64f), w*0.14f));
                        g.DrawLine(pen, x + w*0.18f, y + h*0.4f, x + w*0.82f, y + h*0.4f);
                        break;
                    case "flow":
                        g.DrawEllipse(pen, x + w*0.1f, y + h*0.12f, w*0.22f, h*0.22f);
                        g.DrawEllipse(pen, x + w*0.1f, y + h*0.66f, w*0.22f, h*0.22f);
                        g.DrawEllipse(pen, x + w*0.68f, y + h*0.39f, w*0.22f, h*0.22f);
                        g.DrawLine(pen, x + w*0.32f, y + h*0.26f, x + w*0.68f, y + h*0.46f);
                        g.DrawLine(pen, x + w*0.32f, y + h*0.74f, x + w*0.68f, y + h*0.54f);
                        break;
                    case "wave":
                        float midY = y + h/2f;
                        float[] hh = { 0.12f, 0.3f, 0.46f, 0.28f, 0.38f, 0.2f, 0.32f, 0.12f };
                        for (int i = 0; i < hh.Length; i++)
                        {
                            float px = x + w * (0.12f + 0.76f * i / (hh.Length - 1));
                            g.DrawLine(pen, px, midY - h*hh[i], px, midY + h*hh[i]);
                        }
                        break;
                    case "moon":
                        g.DrawArc(pen, x + w*0.2f, y + h*0.18f, w*0.62f, h*0.62f, 65, 250);
                        g.DrawArc(pen, x + w*0.36f, y + h*0.14f, w*0.52f, h*0.52f, 160, 120);
                        break;
                    case "sun":
                        g.DrawEllipse(pen, x + w*0.32f, y + h*0.32f, w*0.36f, h*0.36f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * 2 * i / 8;
                            float cx = x + w/2, cy = y + h/2;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(a) * w*0.28f, cy + (float)Math.Sin(a) * h*0.28f,
                                cx + (float)Math.Cos(a) * w*0.42f, cy + (float)Math.Sin(a) * h*0.42f);
                        }
                        break;
                    case "gear":
                        g.DrawEllipse(pen, x + w*0.34f, y + h*0.34f, w*0.32f, h*0.32f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * 2 * i / 8;
                            float cx = x + w/2, cy = y + h/2;
                            g.DrawLine(pen,
                                cx + (float)Math.Cos(a) * w*0.3f, cy + (float)Math.Sin(a) * h*0.3f,
                                cx + (float)Math.Cos(a) * w*0.42f, cy + (float)Math.Sin(a) * h*0.42f);
                        }
                        break;
                    case "mute":
                        g.DrawLine(pen, x + w*0.2f, y + h*0.2f, x + w*0.8f, y + h*0.8f);
                        goto case "speaker-core";
                    case "speaker-core":
                        using (GraphicsPath p2 = new GraphicsPath())
                        {
                            p2.AddLines(new PointF[] {
                                new PointF(x + w*0.14f, y + h*0.38f), new PointF(x + w*0.3f, y + h*0.38f),
                                new PointF(x + w*0.48f, y + h*0.22f), new PointF(x + w*0.48f, y + h*0.78f),
                                new PointF(x + w*0.3f, y + h*0.62f),  new PointF(x + w*0.14f, y + h*0.62f) });
                            p2.CloseFigure();
                            g.DrawPath(pen, p2);
                        }
                        break;
                }
            }
        }
    }
}

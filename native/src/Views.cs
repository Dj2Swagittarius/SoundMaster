// List views: playback/recording devices and per-app sessions.
// Fully custom-painted rows with hit-tested sliders and buttons.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SoundMaster
{
    public abstract class DarkListView : Control
    {
        protected int RowH = 58;
        protected int scroll;
        protected int hoverRow = -1;
        protected string hoverZone;
        protected int dragRow = -1;          // row whose slider is being dragged
        protected string dragZone;

        protected DarkListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Canvas;
        }

        public bool IsDragging { get { return dragRow >= 0; } }

        protected abstract int RowCount { get; }
        protected abstract void PaintRow(Graphics g, int index, Rectangle r, bool hover);
        protected abstract void HitAction(int index, string zone, float sliderValue, bool drag);
        protected abstract string HitTest(int index, Rectangle r, Point p);

        protected Rectangle RowRect(int i)
        {
            return new Rectangle(16, 8 + i * (RowH + 8) - scroll, Math.Max(200, Width - 32), RowH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Re-clamp: the list may have shrunk since the user scrolled (device removed,
            // sessions ended) — otherwise every row culls and the view renders blank.
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll));
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Canvas);
            for (int i = 0; i < RowCount; i++)
            {
                Rectangle r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                PaintRow(g, i, r, i == hoverRow);
            }
            int content = ContentHeight();
            if (content > Height)   // minimal scrollbar
            {
                float frac = (float)Height / content;
                float pos = (float)scroll / content;
                RectangleF bar = new RectangleF(Width - 6, pos * Height, 4, Math.Max(24, frac * Height));
                Theme.FillRound(g, bar, 2, Theme.ScrollThumb);
            }
        }

        protected int ContentHeight() { return RowCount * (RowH + 8) + 16; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll - Math.Sign(e.Delta) * 60));
            Invalidate();
        }

        int RowAt(Point p)
        {
            for (int i = 0; i < RowCount; i++)
                if (RowRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragRow >= 0)
            {
                HitAction(dragRow, dragZone, SliderValue(dragRow, e.Location), true);
                Invalidate();
                return;
            }
            int row = RowAt(e.Location);
            string zone = row >= 0 ? HitTest(row, RowRect(row), e.Location) : null;
            if (row != hoverRow || zone != hoverZone)
            {
                hoverRow = row; hoverZone = zone;
                Cursor = zone != null && zone != "row" ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverRow = -1; hoverZone = null; Invalidate();
        }

        // Buttons act on the item captured at press time — the list can be re-sorted by a
        // poll between press and release, and "Disable" must never hit the wrong device.
        string pressZone, pressKey;

        protected abstract string RowKey(int index);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            pressZone = null; pressKey = null;
            int row = RowAt(e.Location);
            if (row < 0) return;
            string zone = HitTest(row, RowRect(row), e.Location);
            if (zone == null) return;
            if (zone.StartsWith("slider"))
            {
                dragRow = row; dragZone = zone;
                HitAction(row, zone, SliderValue(row, e.Location), true);
                Invalidate();
            }
            else if (zone != "row")
            {
                pressZone = zone; pressKey = RowKey(row);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragRow >= 0) { dragRow = -1; dragZone = null; return; }
            int row = RowAt(e.Location);
            string wasZone = pressZone; string wasKey = pressKey;
            pressZone = null; pressKey = null;
            if (row < 0 || wasZone == null) return;
            string zone = HitTest(row, RowRect(row), e.Location);
            if (zone == wasZone && RowKey(row) == wasKey) HitAction(row, zone, 0, false);
        }

        protected abstract Rectangle SliderRect(int index, Rectangle row, string zone);

        float SliderValue(int row, Point p)
        {
            Rectangle sr = SliderRect(row, RowRect(row), dragZone ?? "slider");
            if (sr.Width <= 0) return 0;
            return Math.Max(0f, Math.Min(1f, (p.X - sr.X) / (float)sr.Width));
        }

        // ---- shared row pieces ----
        protected void DrawSlider(Graphics g, Rectangle sr, float value, float peak, bool muted)
        {
            // hairline track, ink progress, ink dot thumb; live meter in success green below
            Theme.FillRound(g, new RectangleF(sr.X, sr.Y + sr.Height / 2 - 1.5f, sr.Width, 3), 1.5f, Theme.SliderTrack);
            float fillW = value * sr.Width;
            if (fillW > 2)
                Theme.FillRound(g, new RectangleF(sr.X, sr.Y + sr.Height / 2 - 1.5f, fillW, 3), 1.5f,
                    muted ? Theme.TextDim : Theme.Ink);
            if (peak > 0.004f)
            {
                float mw = Math.Min(1f, peak * 1.15f) * sr.Width;
                Theme.FillRound(g, new RectangleF(sr.X, sr.Y + sr.Height / 2 + 6, mw, 3), 1.5f,
                    muted ? Theme.TextDim : Theme.Meter);
            }
            float tx = sr.X + value * sr.Width;
            using (SolidBrush b = new SolidBrush(muted ? Theme.TextDim : Theme.SliderThumb))
                g.FillEllipse(b, tx - 6.5f, sr.Y + sr.Height / 2 - 8, 13, 13);
        }

        // Every button is a pill: primary = black fill, quiet = white fill + hairline stroke.
        protected void DrawBtn(Graphics g, Rectangle r, string text, bool primary, bool hot, Color? tint)
        {
            if (primary)
            {
                Theme.FillPill(g, r, Theme.Ink);
            }
            else
            {
                Theme.FillPill(g, r, hot ? Theme.SurfaceSoft : Theme.Canvas);
                Theme.StrokePill(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Theme.Hairline, 1f);
            }
            TextRenderer.DrawText(g, text, Theme.F8b, r,
                tint.HasValue ? tint.Value : (primary ? Theme.InverseInk : Theme.Ink),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected void DrawMute(Graphics g, Rectangle r, bool muted, bool hot)
        {
            // circular icon button on soft surface; muted flags with the single magenta accent
            using (SolidBrush b = new SolidBrush(hot ? Theme.Hairline : Theme.SurfaceSoft))
                g.FillEllipse(b, r.X, r.Y, r.Width - 1, r.Height - 1);
            Theme.Glyph(g, muted ? "mute" : "speaker", Rectangle.Inflate(r, -6, -6),
                muted ? Theme.AccentMagenta : Theme.Ink, 1.5f);
        }
    }

    // ---------------- devices ----------------
    public class DeviceListView : DarkListView
    {
        public List<DeviceInfo> Items = new List<DeviceInfo>();
        public AudioManager Audio;
        public Action Changed;                 // notify shell to refresh soon
        public Action<string> Toast;

        // layout: [icon 40] [text flex-left] | slider 220 | pct 44 | mute 30 | default 86 | comm 62 | disable 66
        Rectangle IconR(Rectangle r) { return new Rectangle(r.X + 10, r.Y + (r.Height - 30) / 2, 30, 30); }
        Rectangle DisableR(Rectangle r) { return new Rectangle(r.Right - 76, r.Y + (r.Height - 26) / 2, 66, 26); }
        Rectangle CommR(Rectangle r) { return new Rectangle(r.Right - 76 - 70, r.Y + (r.Height - 26) / 2, 62, 26); }
        Rectangle DefaultR(Rectangle r) { return new Rectangle(r.Right - 76 - 70 - 94, r.Y + (r.Height - 26) / 2, 86, 26); }
        Rectangle MuteR(Rectangle r) { return new Rectangle(r.Right - 76 - 70 - 94 - 40, r.Y + (r.Height - 30) / 2, 30, 30); }
        protected override Rectangle SliderRect(int i, Rectangle r, string zone)
        {
            int right = MuteR(r).X - 56;
            int left = Math.Max(r.X + 250, right - 260);
            return new Rectangle(left, r.Y + (r.Height - 20) / 2, Math.Max(40, right - left), 20);
        }

        protected override int RowCount { get { return Items.Count; } }
        protected override string RowKey(int i) { return i < Items.Count ? Items[i].Id : null; }

        protected override void PaintRow(Graphics g, int i, Rectangle r, bool hover)
        {
            DeviceInfo d = Items[i];
            bool active = d.State == DeviceState.Active;
            // white card, hairline stroke; hover fills soft — no shadows anywhere
            Theme.FillRound(g, r, 8, hover ? Theme.SurfaceSoft : Theme.Canvas);
            Theme.StrokeRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8, Theme.Hairline, 1f);

            Theme.Glyph(g, d.Flow == EDataFlow.eCapture ? "mic" : "speaker", IconR(r),
                active ? Theme.Ink : Theme.TextDim, 1.6f);

            // name + badges — badges must never spill into the slider column
            int tx = r.X + 50;
            int textLimit = SliderRect(i, r, "slider").X - 12;
            Color nameC = active ? Theme.Text : Theme.TextDim;
            int badgeSpace = (d.IsDefault ? 72 : 0) + (d.IsDefaultComm ? 56 : 0);
            Size nameSz = TextRenderer.MeasureText(d.Name, Theme.F10b);
            int nameW = Math.Max(40, Math.Min(nameSz.Width, textLimit - tx - badgeSpace));
            TextRenderer.DrawText(g, d.Name, Theme.F10b,
                new Rectangle(tx, r.Y + 9, nameW, 18), nameC,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int bx = tx + nameW + 8;
            if (d.IsDefault && bx + 56 <= textLimit) bx = Badge(g, bx, r.Y + 10, "DEFAULT", Theme.Ink);
            if (d.IsDefaultComm && bx + 44 <= textLimit) bx = Badge(g, bx, r.Y + 10, "COMM", Theme.BlockNavy);
            string stateTxt = active ? (Math.Round(d.Volume * 100) + "%" + (d.Muted ? " · muted" : ""))
                : d.State == DeviceState.Disabled ? "Disabled"
                : d.State == DeviceState.Unplugged ? "Unplugged" : "Not present";
            TextRenderer.DrawText(g, stateTxt, Theme.F9,
                new Rectangle(tx, r.Y + 29, 220, 16),
                d.Muted && active ? Theme.Danger : Theme.TextSub, TextFormatFlags.NoPrefix);

            if (active)
            {
                DrawSlider(g, SliderRect(i, r, "slider"), d.Volume, d.Peak, d.Muted);
                TextRenderer.DrawText(g, Math.Round(d.Volume * 100) + "%", Theme.F9,
                    new Rectangle(SliderRect(i, r, "slider").Right + 12, r.Y + (r.Height - 16) / 2, 44, 16),
                    Theme.TextSub, TextFormatFlags.NoPrefix);
                DrawMute(g, MuteR(r), d.Muted, hover && hoverZone == "mute");
                if (!d.IsDefault) DrawBtn(g, DefaultR(r), "Set default", false, hover && hoverZone == "default", null);
                if (!d.IsDefaultComm) DrawBtn(g, CommR(r), "Comm", false, hover && hoverZone == "comm", null);
                DrawBtn(g, DisableR(r), "Disable", false, hover && hoverZone == "disable", Theme.TextSub);
            }
            else if (d.State == DeviceState.Disabled)
            {
                DrawBtn(g, DisableR(r), "Enable", false, hover && hoverZone == "disable", Theme.WireOut);
            }
        }

        int Badge(Graphics g, int x, int y, string text, Color c)
        {
            // solid pill badge — selected state uses the primary (ink) surface
            Size sz = TextRenderer.MeasureText(text, Theme.Caption);
            Rectangle r = new Rectangle(x, y, sz.Width + 14, 17);
            Theme.FillPill(g, r, c);
            TextRenderer.DrawText(g, text, Theme.Caption, r, Theme.InverseInk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return r.Right + 6;
        }

        protected override string HitTest(int i, Rectangle r, Point p)
        {
            DeviceInfo d = Items[i];
            bool active = d.State == DeviceState.Active;
            if (active)
            {
                if (SliderRect(i, r, "slider").Contains(p)) return "slider";
                if (MuteR(r).Contains(p)) return "mute";
                if (!d.IsDefault && DefaultR(r).Contains(p)) return "default";
                if (!d.IsDefaultComm && CommR(r).Contains(p)) return "comm";
                if (DisableR(r).Contains(p)) return "disable";
            }
            else if (d.State == DeviceState.Disabled && DisableR(r).Contains(p)) return "disable";
            return "row";
        }

        protected override void HitAction(int i, string zone, float v, bool drag)
        {
            if (i >= Items.Count) return;
            DeviceInfo d = Items[i];
            if (zone == "slider") { d.Volume = v; Audio.SetDeviceVolume(d.Id, v); return; }
            if (zone == "mute") { d.Muted = !d.Muted; Audio.SetDeviceMute(d.Id, d.Muted); Invalidate(); return; }
            if (zone == "default")
            {
                if (Audio.SetDefaultDevice(d.Id, false)) { if (Toast != null) Toast(d.Name + " is now the default."); }
                else if (Toast != null) Toast("Couldn't set default device.");
                if (Changed != null) Changed();
                return;
            }
            if (zone == "comm")
            {
                if (Audio.SetDefaultCommunications(d.Id)) { if (Toast != null) Toast(d.Name + " is now the communications device."); }
                if (Changed != null) Changed();
                return;
            }
            if (zone == "disable")
            {
                bool enable = d.State == DeviceState.Disabled;
                string id = d.Id, name = d.Name;
                // The UAC prompt + reg.exe can block for a while — keep the UI thread free.
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    bool ok = Audio.SetDeviceEnabled(id, enable);
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            if (Toast != null) Toast(ok ? (name + (enable ? " enabled." : " disabled."))
                                : "Needs administrator approval — action was cancelled or blocked.");
                            if (Changed != null) Changed();
                        });
                    }
                    catch { /* window closed */ }
                });
            }
        }
    }

    // ---------------- app sessions ----------------
    public class AppsView : DarkListView
    {
        public List<SessionInfo> Items = new List<SessionInfo>();
        public Dictionary<string, string> DeviceNames = new Dictionary<string, string>();
        public AudioManager Audio;

        Rectangle IconR(Rectangle r) { return new Rectangle(r.X + 10, r.Y + (r.Height - 30) / 2, 30, 30); }
        Rectangle MuteR(Rectangle r) { return new Rectangle(r.Right - 44, r.Y + (r.Height - 30) / 2, 30, 30); }
        protected override Rectangle SliderRect(int i, Rectangle r, string zone)
        {
            int right = MuteR(r).X - 56;
            int left = Math.Max(r.X + 280, right - 280);
            return new Rectangle(left, r.Y + (r.Height - 20) / 2, Math.Max(40, right - left), 20);
        }

        protected override int RowCount { get { return Items.Count; } }
        protected override string RowKey(int i) { return i < Items.Count ? Items[i].InstanceId : null; }

        protected override void PaintRow(Graphics g, int i, Rectangle r, bool hover)
        {
            SessionInfo s = Items[i];
            bool activeNow = s.State == 1;
            Theme.FillRound(g, r, 8, hover ? Theme.SurfaceSoft : Theme.Canvas);
            Theme.StrokeRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 8, Theme.Hairline, 1f);
            Rectangle ir = IconR(r);
            Bitmap appIcon = AppIcons.Get(s.Pid);
            if (appIcon != null)
            {
                AppIcons.Draw(g, appIcon, new RectangleF(ir.X + 1, ir.Y + 1, ir.Width - 2, ir.Height - 2),
                    activeNow ? 1f : 0.45f);
                if (s.Flow == EDataFlow.eCapture)
                {
                    // small navy mic badge marks a recording stream
                    RectangleF br = new RectangleF(ir.Right - 13, ir.Bottom - 13, 14, 14);
                    using (SolidBrush b = new SolidBrush(Theme.WireIn)) g.FillEllipse(b, br);
                    Theme.Glyph(g, "mic", RectangleF.Inflate(br, -2.5f, -2.5f), Theme.InverseInk, 1.2f);
                }
            }
            else
            {
                Theme.Glyph(g, s.Flow == EDataFlow.eCapture ? "mic" : "app", ir,
                    activeNow ? (s.Flow == EDataFlow.eCapture ? Theme.WireIn : Theme.WireMix) : Theme.TextDim, 1.6f);
            }

            string title = s.Name + (s.Flow == EDataFlow.eCapture ? "  (recording)" : "");
            TextRenderer.DrawText(g, title, Theme.F10b,
                new Rectangle(r.X + 50, r.Y + 9, SliderRect(i, r, "slider").X - r.X - 60, 18),
                activeNow ? Theme.Text : Theme.TextSub,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string devName;
            DeviceNames.TryGetValue(s.DeviceId, out devName);
            TextRenderer.DrawText(g, (devName ?? "device") + (activeNow ? "" : " · idle"), Theme.F9,
                new Rectangle(r.X + 50, r.Y + 29, SliderRect(i, r, "slider").X - r.X - 60, 16),
                Theme.TextSub, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            DrawSlider(g, SliderRect(i, r, "slider"), s.Volume, s.Peak, s.Muted);
            TextRenderer.DrawText(g, Math.Round(s.Volume * 100) + "%", Theme.F9,
                new Rectangle(SliderRect(i, r, "slider").Right + 12, r.Y + (r.Height - 16) / 2, 44, 16),
                Theme.TextSub, TextFormatFlags.NoPrefix);
            DrawMute(g, MuteR(r), s.Muted, hover && hoverZone == "mute");
        }

        protected override string HitTest(int i, Rectangle r, Point p)
        {
            if (SliderRect(i, r, "slider").Contains(p)) return "slider";
            if (MuteR(r).Contains(p)) return "mute";
            return "row";
        }

        protected override void HitAction(int i, string zone, float v, bool drag)
        {
            if (i >= Items.Count) return;
            SessionInfo s = Items[i];
            if (zone == "slider") { s.Volume = v; Audio.SetSessionVolume(s.DeviceId, s.InstanceId, v, null); return; }
            if (zone == "mute") { s.Muted = !s.Muted; Audio.SetSessionVolume(s.DeviceId, s.InstanceId, -1, s.Muted); Invalidate(); }
        }
    }
}

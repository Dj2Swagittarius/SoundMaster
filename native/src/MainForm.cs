// SoundMaster shell: dark sidebar navigation, four views, poll timers, toasts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundMaster
{
    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly AudioManager audio = new AudioManager();
        readonly DeviceListView playbackView = new DeviceListView();
        readonly DeviceListView recordingView = new DeviceListView();
        readonly AppsView appsView = new AppsView();
        readonly FlowView flowView = new FlowView();
        readonly SidePanel side;
        readonly HeaderBlock header = new HeaderBlock();
        readonly ToastPill toast = new ToastPill();
        readonly Timer animTimer = new Timer();     // flow-view dot animation repaint only
        readonly Timer toastTimer = new Timer();
        System.Threading.Thread pollThread;         // all Core Audio polling lives off the UI thread
        volatile bool closing;
        volatile bool applyPending;
        volatile bool wantSessionsNow = true;
        string view = "flow";

        public MainForm()
        {
            // Restore the saved theme before any control paints.
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\SoundMaster", "Dark", 0);
                Theme.Apply(v is int && (int)v == 1);
            }
            catch { Theme.Apply(false); }

            Text = "SoundMaster";
            BackColor = Theme.Canvas;
            ClientSize = new Size(1220, 780);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.F9;
            try { Icon = MakeIcon(); } catch { }

            side = new SidePanel(this);
            side.Dock = DockStyle.Left;
            side.Width = 188;

            header.Dock = DockStyle.Top;
            header.Height = 138;

            playbackView.Dock = DockStyle.Fill;
            recordingView.Dock = DockStyle.Fill;
            appsView.Dock = DockStyle.Fill;
            flowView.Dock = DockStyle.Fill;
            playbackView.Audio = audio; recordingView.Audio = audio; appsView.Audio = audio;
            playbackView.Changed = RefreshSoon; recordingView.Changed = RefreshSoon;
            playbackView.Toast = ShowToast; recordingView.Toast = ShowToast;
            flowView.Audio = audio; flowView.Toast = ShowToast; flowView.Changed = RefreshSoon;

            Controls.Add(playbackView);
            Controls.Add(recordingView);
            Controls.Add(appsView);
            Controls.Add(flowView);
            Controls.Add(header);
            Controls.Add(side);

            toast.Visible = false;
            Controls.Add(toast);
            toast.BringToFront();

            animTimer.Interval = 33;   // dots + meters repaint; geometry is cached so this is cheap
            animTimer.Tick += delegate { if (view == "flow" && flowView.Visible) flowView.Invalidate(); };
            toastTimer.Interval = 3200;
            toastTimer.Tick += delegate { toast.Visible = false; toastTimer.Stop(); };

            Shown += delegate
            {
                TryDarkTitlebar();
                SetView("flow");
                animTimer.Start();
                pollThread = new System.Threading.Thread(PollLoop);
                pollThread.IsBackground = true;
                pollThread.Name = "SoundMaster poll";
                pollThread.Start();
            };
            FormClosed += delegate { closing = true; };
        }

        // Polls Core Audio on a worker with its own AudioManager, so a stalled endpoint
        // (Bluetooth reconnect, Audiosrv restart) can never freeze the window. Results are
        // marshalled to the UI thread; a tick is skipped while the previous apply is pending.
        void PollLoop()
        {
            AudioManager poll = null;
            int n = 0;
            while (!closing)
            {
                try
                {
                    if (poll == null) poll = new AudioManager();
                    bool sessions = wantSessionsNow || (n % 5 == 0);
                    wantSessionsNow = false;
                    n++;
                    AudioSnapshot s = poll.Snapshot(sessions, true);
                    if (!closing && !applyPending && IsHandleCreated)
                    {
                        applyPending = true;
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                try { Apply(s, sessions); }
                                catch { /* view mid-teardown */ }
                                finally { applyPending = false; }
                            });
                        }
                        catch { applyPending = false; }   // handle destroyed during shutdown
                    }
                }
                catch { poll = null; }   // COM hiccup: rebuild the manager next round
                System.Threading.Thread.Sleep(140);
            }
        }

        void TryDarkTitlebar()
        {
            int v = Theme.IsDark ? 1 : 0;
            try { DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
            try { DwmSetWindowAttribute(Handle, 19, ref v, 4); } catch { }
        }

        public void ToggleTheme()
        {
            Theme.Apply(!Theme.IsDark);
            try
            {
                Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\SoundMaster", "Dark",
                    Theme.IsDark ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { }
            BackColor = Theme.Canvas;
            playbackView.BackColor = Theme.Canvas;
            recordingView.BackColor = Theme.Canvas;
            appsView.BackColor = Theme.Canvas;
            flowView.BackColor = Theme.Canvas;
            side.BackColor = Theme.Canvas;
            header.BackColor = Theme.Canvas;
            TryDarkTitlebar();
            Refresh();
            ShowToast(Theme.IsDark ? "Dark theme on." : "Light theme on.");
        }

        static Icon MakeIcon()
        {
            // app.ico is embedded twice by build.bat: /win32icon for Explorer, /resource for the window
            // (the full multi-size set, so the 16px title-bar icon stays crisp).
            using (System.IO.Stream st = typeof(MainForm).Assembly.GetManifestResourceStream("SoundMaster.app.ico"))
                return new Icon(st);
        }

        public void SetView(string v)
        {
            view = v;
            playbackView.Visible = v == "playback";
            recordingView.Visible = v == "recording";
            appsView.Visible = v == "apps";
            flowView.Visible = v == "flow";
            switch (v)
            {
                case "playback":
                    header.Set("02 — OUTPUTS", "Playback devices",
                        "Set the default output, adjust volumes, disable devices you never use", Theme.BlockLime); break;
                case "recording":
                    header.Set("03 — INPUTS", "Recording devices",
                        "Set the default microphone, adjust levels, disable unused inputs", Theme.BlockMint); break;
                case "apps":
                    header.Set("04 — SESSIONS", "Apps",
                        "Per-app volume and mute, playback and recording", Theme.BlockCream); break;
                default:
                    header.Set("01 — SIGNAL PATH", "Audio flow",
                        "Your real signal path, live — click any element to trace it", Theme.BlockLilac); break;
            }
            side.Invalidate();
            wantSessionsNow = true;
            // Give the shown view keyboard focus so mouse wheel and Esc work immediately.
            Control shown = v == "playback" ? (Control)playbackView
                : v == "recording" ? (Control)recordingView
                : v == "apps" ? (Control)appsView : (Control)flowView;
            if (shown.CanFocus) shown.Focus();
        }

        public string CurrentView { get { return view; } }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D1)) { SetView("flow"); return true; }
            if (keyData == (Keys.Control | Keys.D2)) { SetView("playback"); return true; }
            if (keyData == (Keys.Control | Keys.D3)) { SetView("recording"); return true; }
            if (keyData == (Keys.Control | Keys.D4)) { SetView("apps"); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Scriptable view switching: PostMessage(hwnd, WM_APP, 0..3, 0).
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x8000)   // WM_APP: view switching
            {
                string[] map = { "flow", "playback", "recording", "apps" };
                long i = m.WParam.ToInt64();   // any process can post this; never let the cast throw
                if (i >= 0 && i < map.Length) SetView(map[(int)i]);
                return;
            }
            if (m.Msg == 0x8001)   // WM_APP+1: scroll the flow view (scriptable testing)
            {
                long px = m.WParam.ToInt64();
                if (px > -100000 && px < 100000) flowView.ScrollBy((int)px);
                return;
            }
            if (m.Msg == 0x8002)   // WM_APP+2: toggle theme (also scriptable)
            {
                ToggleTheme();
                return;
            }
            base.WndProc(ref m);
        }

        void RefreshSoon() { wantSessionsNow = true; }

        void ShowToast(string msg)
        {
            toast.SetText(msg);
            toast.Visible = true;
            toast.BringToFront();
            toast.Location = new Point(side.Width + (ClientSize.Width - side.Width - toast.Width) / 2,
                ClientSize.Height - 58);
            toastTimer.Stop(); toastTimer.Start();
        }

        // Runs on the UI thread with a snapshot produced by the worker.
        void Apply(AudioSnapshot s, bool hasSessions)
        {
            if (closing || s == null) return;
            Merge(playbackView, s.Playback);
            Merge(recordingView, s.Recording);
            if (hasSessions)
            {
                if (!appsView.IsDragging)   // never resort/replace rows under an active drag
                {
                    List<SessionInfo> sess = new List<SessionInfo>(s.Sessions);
                    sess.Sort(delegate(SessionInfo a, SessionInfo b)
                    {
                        if ((a.State == 1) != (b.State == 1)) return a.State == 1 ? -1 : 1;
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });
                    appsView.Items = sess;
                    Dictionary<string, string> names = new Dictionary<string, string>();
                    foreach (DeviceInfo d in s.Playback) names[d.Id] = d.Name;
                    foreach (DeviceInfo d in s.Recording) names[d.Id] = d.Name;
                    appsView.DeviceNames = names;
                    appsView.Invalidate();
                }
                flowView.UpdateData(s);
            }
        }

        static void Merge(DeviceListView v, List<DeviceInfo> items)
        {
            if (v.IsDragging) return;   // don't resort/replace rows under an active slider drag
            items.Sort(delegate(DeviceInfo a, DeviceInfo b)
            {
                bool aa = a.State == DeviceState.Active, bb = b.State == DeviceState.Active;
                if (aa != bb) return aa ? -1 : 1;
                if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            v.Items = items;
            v.Invalidate();
        }
    }

    // Color-block section header — the signature surface: a pastel panel with rounded
    // corners inset on white canvas, carrying a mono eyebrow + light display title.
    public class HeaderBlock : Control
    {
        string eyebrow = "", title = "", sub = "";
        Color block = Theme.BlockLilac;

        public HeaderBlock()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Canvas;
        }

        public void Set(string eye, string t, string s, Color c)
        {
            eyebrow = eye; title = t; sub = s; block = c;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Canvas);
            RectangleF r = new RectangleF(20, 14, Width - 44, Height - 24);
            Theme.FillRound(g, r, 22, block);
            // Pastel blocks carry near-black type in BOTH themes; only navy inverts.
            bool inverse = block == Theme.BlockNavy;
            Color ink = inverse ? Color.White : Color.FromArgb(17, 17, 17);
            int x = (int)r.X + 30;
            TextRenderer.DrawText(g, eyebrow.ToUpperInvariant(), Theme.Eyebrow,
                new Point(x + 1, (int)r.Y + 17), ink, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, title, Theme.Display,
                new Point(x - 2, (int)r.Y + 32), ink, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, Theme.F9,
                new Point(x + 1, (int)r.Y + 74), ink, TextFormatFlags.NoPrefix);
        }
    }

    // Inverse-canvas toast: black pill, white type.
    public class ToastPill : Control
    {
        string text = "";

        public ToastPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public void SetText(string t)
        {
            text = t;
            Size sz = TextRenderer.MeasureText(t, Theme.F9b);
            Size = new Size(sz.Width + 36, 36);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Theme.FillPill(g, new RectangleF(0, 0, Width - 1, Height - 1), Theme.InverseCanvas);
            TextRenderer.DrawText(g, text, Theme.F9b, new Rectangle(0, 0, Width, Height),
                Theme.InverseInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Sidebar: custom painted nav.
    public class SidePanel : Control
    {
        readonly MainForm shell;
        readonly string[][] items = {
            new[] { "flow", "Audio flow", "flow" },
            new[] { "playback", "Playback", "speaker" },
            new[] { "recording", "Recording", "mic" },
            new[] { "apps", "Apps", "app" },
        };
        int hover = -1;

        public SidePanel(MainForm f)
        {
            shell = f;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);   // never steal focus from the views
            BackColor = Theme.Side;
        }

        Rectangle ItemRect(int i) { return new Rectangle(14, 96 + i * 46, Width - 28, 40); }
        Rectangle ThemeRect() { return new Rectangle(18, Height - 96, 36, 36); }
        bool themeHover;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Canvas);
            using (Pen p = new Pen(Theme.Hairline)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);

            // wordmark: ink mark + light display type
            Theme.FillRound(g, new RectangleF(20, 22, 30, 30), 9, Theme.Ink);
            Theme.Glyph(g, "wave", new RectangleF(24, 26, 22, 22), Theme.InverseInk, 1.9f);
            TextRenderer.DrawText(g, "SoundMaster", Theme.F14b, new Point(58, 24), Theme.Ink,
                TextFormatFlags.NoPrefix);

            for (int i = 0; i < items.Length; i++)
            {
                Rectangle r = ItemRect(i);
                bool active = shell.CurrentView == items[i][0];
                // Selected = primary surface: a black pill. Hover = soft pill.
                if (active) Theme.FillPill(g, r, Theme.Ink);
                else if (i == hover) Theme.FillPill(g, r, Theme.SurfaceSoft);
                Color ink = active ? Theme.InverseInk : Theme.Ink;
                Theme.Glyph(g, items[i][2], new RectangleF(r.X + 14, r.Y + 11, 18, 18), ink, 1.5f);
                TextRenderer.DrawText(g, items[i][1], active ? Theme.F9b : Theme.F9,
                    new Rectangle(r.X + 42, r.Y, r.Width - 48, r.Height), ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // circular theme toggle — moon in light mode, sun in dark
            Rectangle tr = ThemeRect();
            using (SolidBrush b = new SolidBrush(themeHover ? Theme.Hairline : Theme.SurfaceSoft))
                g.FillEllipse(b, tr);
            Theme.Glyph(g, Theme.IsDark ? "sun" : "moon", Rectangle.Inflate(tr, -8, -8), Theme.Ink, 1.5f);

            TextRenderer.DrawText(g, "CHANGES APPLY TO\nWINDOWS IMMEDIATELY", Theme.Caption,
                new Rectangle(22, Height - 48, Width - 32, 40), Theme.TextSub, TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < items.Length; i++) if (ItemRect(i).Contains(e.Location)) h = i;
            bool th = ThemeRect().Contains(e.Location);
            if (h != hover || th != themeHover)
            {
                hover = h; themeHover = th;
                Cursor = h >= 0 || th ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; themeHover = false; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (ThemeRect().Contains(e.Location)) { shell.ToggleTheme(); return; }
            for (int i = 0; i < items.Length; i++)
                if (ItemRect(i).Contains(e.Location)) { shell.SetView(items[i][0]); return; }
        }
    }
}

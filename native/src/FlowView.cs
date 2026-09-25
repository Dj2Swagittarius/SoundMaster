// Audio flow — live diagram of the machine's real audio topology:
// capture devices -> apps -> playback devices. Click any node to trace its path.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SoundMaster
{
    public class FlowView : Control
    {
        class Node
        {
            public string Key;          // "in:{devid}" / "app:{pid}" / "out:{devid}"
            public int Col;
            public string Name, Sub;
            public string Glyph;
            public Color GlyphColor;
            public RectangleF Rect;
            public bool Default, Muted;
            public float Peak;
            public uint Pid;            // app nodes: real icon lookup
            public bool HasRender, HasCapture;
        }
        class Edge
        {
            public string From, To;
            public bool Active, Muted;
            public float Peak;
            public Color Color;
            public float Phase;
            public GraphicsPath Path;
            public PointF A, B;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly List<Edge> edges = new List<Edge>();
        readonly Dictionary<string, Node> nodeMap = new Dictionary<string, Node>();
        readonly Dictionary<string, float> phases = new Dictionary<string, float>();
        string selected;
        int scroll;
        string hoverKey;
        DateTime lastTick = DateTime.UtcNow;
        HashSet<string> traceCache;
        bool traceDirty = true;

        // Wired in by the shell — drag-to-reroute needs the engine and feedback surfaces.
        public AudioManager Audio;
        public Action<string> Toast;
        public Action Changed;

        // blueprint-style wire drag (world coordinates, i.e. scrolled space)
        bool dragging, dragMoved;
        uint dragPid;
        EDataFlow dragKind;
        PointF dragOrigin;
        Point dragCur;
        string dropKey;

        // TextRenderer draws through GDI, which ignores the GDI+ world transform unless
        // told to preserve it — without this flag, text detaches from cards when scrolled.
        const TextFormatFlags TT = TextFormatFlags.PreserveGraphicsTranslateTransform;

        const int CardW = 210, CardH = 44, Gap = 9;

        public FlowView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Canvas;
        }

        public void UpdateData(AudioSnapshot snap)
        {
            foreach (Edge old in edges) if (old.Path != null) old.Path.Dispose();   // GDI+ handles
            nodes.Clear(); edges.Clear();
            if (snap == null) { Invalidate(); return; }

            Dictionary<string, string> devName = new Dictionary<string, string>();

            List<DeviceInfo> ins = new List<DeviceInfo>();
            foreach (DeviceInfo d in snap.Recording) { devName[d.Id] = d.Name; if (d.State == DeviceState.Active) ins.Add(d); }
            List<DeviceInfo> outs = new List<DeviceInfo>();
            foreach (DeviceInfo d in snap.Playback) { devName[d.Id] = d.Name; if (d.State == DeviceState.Active) outs.Add(d); }
            ins.Sort(DevOrder); outs.Sort(DevOrder);

            // merge sessions into app nodes by pid
            Dictionary<uint, List<SessionInfo>> byPid = new Dictionary<uint, List<SessionInfo>>();
            foreach (SessionInfo s in snap.Sessions)
            {
                List<SessionInfo> l;
                if (!byPid.TryGetValue(s.Pid, out l)) { l = new List<SessionInfo>(); byPid[s.Pid] = l; }
                l.Add(s);
            }
            List<KeyValuePair<uint, List<SessionInfo>>> apps = new List<KeyValuePair<uint, List<SessionInfo>>>(byPid);
            apps.Sort(delegate(KeyValuePair<uint, List<SessionInfo>> a, KeyValuePair<uint, List<SessionInfo>> b)
            {
                int aa = AnyActive(a.Value) ? 0 : 1, bb = AnyActive(b.Value) ? 0 : 1;
                if (aa != bb) return aa - bb;
                return string.Compare(NameOf(a.Value), NameOf(b.Value), StringComparison.OrdinalIgnoreCase);
            });

            foreach (DeviceInfo d in ins)
                nodes.Add(new Node { Key = "in:" + d.Id, Col = 0, Name = d.Name, Glyph = "mic",
                    Sub = Math.Round(d.Volume * 100) + "%" + (d.Muted ? " · muted" : "") + (d.IsDefault ? "" : ""),
                    Default = d.IsDefault, Muted = d.Muted, Peak = d.Peak, GlyphColor = Theme.Ink });
            foreach (KeyValuePair<uint, List<SessionInfo>> a in apps)
            {
                bool act = AnyActive(a.Value);
                bool hasR = false, hasC = false;
                foreach (SessionInfo s in a.Value)
                {
                    if (s.Flow == EDataFlow.eRender) hasR = true; else hasC = true;
                }
                nodes.Add(new Node { Key = "app:" + a.Key, Col = 1, Name = NameOf(a.Value), Glyph = "app",
                    Sub = a.Value.Count + (a.Value.Count == 1 ? " stream" : " streams") + (act ? "" : " · idle"),
                    Peak = MaxPeak(a.Value), GlyphColor = act ? Theme.WireMix : Theme.TextSub, Pid = a.Key,
                    HasRender = hasR, HasCapture = hasC });
            }
            foreach (DeviceInfo d in outs)
                nodes.Add(new Node { Key = "out:" + d.Id, Col = 2, Name = d.Name, Glyph = "speaker",
                    Sub = Math.Round(d.Volume * 100) + "%" + (d.Muted ? " · muted" : ""),
                    Default = d.IsDefault, Muted = d.Muted, Peak = d.Peak, GlyphColor = Theme.Ink });

            foreach (SessionInfo s in snap.Sessions)
            {
                bool isCap = s.Flow == EDataFlow.eCapture;
                Edge e = new Edge
                {
                    From = isCap ? "in:" + s.DeviceId : "app:" + s.Pid,
                    To = isCap ? "app:" + s.Pid : "out:" + s.DeviceId,
                    Active = s.State == 1,
                    Muted = s.Muted,
                    Peak = s.Peak,
                    Color = isCap ? Theme.WireIn : Theme.WireMix,
                };
                string pk = e.From + ">" + e.To;
                float ph;
                if (!phases.TryGetValue(pk, out ph)) { ph = (pk.GetHashCode() & 0xFF) / 255f; phases[pk] = ph; }
                e.Phase = ph;
                edges.Add(e);
            }
            nodeMap.Clear();
            foreach (Node n in nodes) nodeMap[n.Key] = n;
            LayoutNodes();
            // Topology may have shrunk since the user scrolled — re-clamp or the whole
            // diagram translates above the viewport and the view looks empty.
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll));
            RebuildGeometry();
            traceDirty = true;
            Invalidate();
        }

        // Wire endpoints and bezier paths are rebuilt only when data or size changes —
        // never per animation frame (per-frame path allocation was the jank).
        void RebuildGeometry()
        {
            foreach (Edge e in edges)
            {
                Node a, b;
                if (!nodeMap.TryGetValue(e.From, out a) || !nodeMap.TryGetValue(e.To, out b)) continue;
                e.A = new PointF(a.Rect.Right, a.Rect.Y + CardH / 2f);
                e.B = new PointF(b.Rect.X, b.Rect.Y + CardH / 2f);
                float dx = Math.Max(30, (e.B.X - e.A.X) * 0.5f);
                if (e.Path != null) e.Path.Dispose();
                e.Path = new GraphicsPath();
                e.Path.AddBezier(e.A, new PointF(e.A.X + dx, e.A.Y), new PointF(e.B.X - dx, e.B.Y), e.B);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutNodes();
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll));
            RebuildGeometry();
            Invalidate();
        }

        static int DevOrder(DeviceInfo a, DeviceInfo b)
        {
            if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
        static bool AnyActive(List<SessionInfo> l) { foreach (SessionInfo s in l) if (s.State == 1) return true; return false; }
        static float MaxPeak(List<SessionInfo> l) { float m = 0; foreach (SessionInfo s in l) if (s.Peak > m) m = s.Peak; return m; }
        static string NameOf(List<SessionInfo> l) { return l.Count > 0 ? l[0].Name : "app"; }

        void LayoutNodes()
        {
            int w = Math.Max(760, Width);
            float[] colX = { 24, (w - CardW) / 2f, w - CardW - 24 };
            int[] colY = { 46, 46, 46 };
            foreach (Node n in nodes)
            {
                n.Rect = new RectangleF(colX[n.Col], colY[n.Col], CardW, CardH);
                colY[n.Col] += CardH + Gap;
            }
        }

        int ContentHeight()
        {
            float max = 0;
            foreach (Node n in nodes) if (n.Rect.Bottom > max) max = n.Rect.Bottom;
            return (int)max + 60;
        }

        Node Find(string key) { foreach (Node n in nodes) if (n.Key == key) return n; return null; }

        HashSet<string> TraceSet()
        {
            if (selected == null) return null;
            HashSet<string> lit = new HashSet<string>();
            lit.Add(selected);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Edge e in edges)
                {
                    if (!e.Active) continue;
                    if (lit.Contains(e.From) && !lit.Contains(e.To)) { lit.Add(e.To); grew = true; }
                    if (lit.Contains(e.To) && !lit.Contains(e.From)) { lit.Add(e.From); grew = true; }
                }
            }
            return lit;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Graphics g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Canvas);
            g.TranslateTransform(0, -scroll);

            float dt = (float)Math.Min(0.05, (DateTime.UtcNow - lastTick).TotalSeconds);
            lastTick = DateTime.UtcNow;

            if (traceDirty) { traceCache = TraceSet(); traceDirty = false; }
            HashSet<string> lit = traceCache;

            // column headers (scroll with the content; TT keeps the text on the pills)
            string[] heads = { "INPUTS", "APPS", "OUTPUTS" };
            for (int c = 0; c < 3; c++)
            {
                float cx = c == 0 ? 24 : c == 1 ? (Math.Max(760, Width) - CardW) / 2f : Math.Max(760, Width) - CardW - 24;
                // mono uppercase eyebrow — taxonomy, not chrome
                RectangleF hr = new RectangleF(cx, 8, CardW, 22);
                TextRenderer.DrawText(g, heads[c], Theme.Eyebrow, Rectangle.Round(hr), Theme.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TT);
            }

            // wires under cards — geometry is cached; one pen/brush pair reused for all edges
            int viewTop = scroll - 8, viewBottom = scroll + Height + 8;
            using (Pen pen = new Pen(Color.Black, 1f))
            using (SolidBrush dotBrush = new SolidBrush(Color.Black))
            {
                foreach (Edge e in edges)
                {
                    if (e.Path == null) continue;
                    float lo = Math.Min(e.A.Y, e.B.Y), hi = Math.Max(e.A.Y, e.B.Y);
                    if (hi < viewTop || lo > viewBottom) continue;   // fully off-screen
                    bool onPath = lit == null || (lit.Contains(e.From) && lit.Contains(e.To));
                    if (e.Muted && (lit == null || onPath))
                    {
                        pen.Color = Theme.WireMuted; pen.Width = 1.5f; pen.DashStyle = DashStyle.Dash;
                        g.DrawPath(pen, e.Path);
                    }
                    else if (e.Active && onPath)
                    {
                        pen.DashStyle = DashStyle.Solid;
                        if (lit != null)
                        {
                            pen.Color = Color.FromArgb(36, e.Color); pen.Width = 7f;
                            g.DrawPath(pen, e.Path);
                        }
                        pen.Color = e.Color; pen.Width = lit != null ? 2.2f : 1.6f;
                        g.DrawPath(pen, e.Path);
                        e.Phase = (e.Phase + dt * 0.5f) % 1f;
                        PointF pt = PointOn(e, e.Phase);
                        dotBrush.Color = Color.FromArgb((int)Math.Min(255, 90 + e.Peak * 600), e.Color);
                        g.FillEllipse(dotBrush, pt.X - 3.2f, pt.Y - 3.2f, 6.4f, 6.4f);
                    }
                    else
                    {
                        pen.Color = Theme.WireIdle; pen.Width = 1f; pen.DashStyle = DashStyle.Solid;
                        g.DrawPath(pen, e.Path);
                    }
                }
            }

            // cards
            using (SolidBrush accBrush = new SolidBrush(Theme.Accent))
            {
                foreach (Node n in nodes)
                {
                    if (n.Rect.Bottom < viewTop || n.Rect.Y > viewBottom) continue;
                    bool dim = lit != null && !lit.Contains(n.Key);
                    bool sel = n.Key == selected;
                    bool hot = n.Key == hoverKey;
                    // soft tile by default; selected = primary (ink) surface with inverse type;
                    // dimmed = white ghost with hairline stroke
                    if (sel)
                    {
                        Theme.FillRound(g, n.Rect, 8, Theme.Ink);
                    }
                    else if (dim)
                    {
                        Theme.FillRound(g, n.Rect, 8, Theme.Canvas);
                        Theme.StrokeRound(g, n.Rect, 8, Theme.HairlineSoft, 1f);
                    }
                    else
                    {
                        Theme.FillRound(g, n.Rect, 8, hot ? Theme.Hairline : Theme.SurfaceSoft);
                    }
                    Color txt = sel ? Theme.InverseInk : dim ? Theme.TextDim : Theme.Ink;
                    Color sub = sel ? Theme.InverseInk : dim ? Theme.TextDim : Theme.TextSub;
                    Bitmap appIcon = n.Pid != 0 ? AppIcons.Get(n.Pid) : null;
                    if (appIcon != null)
                        AppIcons.Draw(g, appIcon, new RectangleF(n.Rect.X + 10, n.Rect.Y + 10, 24, 24), dim ? 0.35f : 1f);
                    else
                        Theme.Glyph(g, n.Glyph, new RectangleF(n.Rect.X + 8, n.Rect.Y + 8, 28, 28),
                            sel ? Theme.InverseInk : dim ? Theme.TextDim : n.GlyphColor, 1.6f);
                    int tx = (int)n.Rect.X + 44;
                    int tw = (int)n.Rect.Width - 52;
                    TextRenderer.DrawText(g, n.Name, Theme.F9b,
                        new Rectangle(tx, (int)n.Rect.Y + 5, tw - (n.Default ? 18 : 0), 16), txt,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TT);
                    TextRenderer.DrawText(g, n.Sub, Theme.F8,
                        new Rectangle(tx, (int)n.Rect.Y + 23, tw, 14), n.Muted && !dim && !sel ? Theme.AccentMagenta : sub,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TT);
                    if (n.Default)
                    {
                        accBrush.Color = sel ? Theme.InverseInk : dim ? Theme.TextDim : Theme.Ink;
                        g.FillEllipse(accBrush, n.Rect.Right - 14, n.Rect.Y + 8, 6, 6);
                    }
                    if (n.Peak > 0.01f && !dim)
                    {
                        float mw = Math.Min(1f, n.Peak * 1.2f) * (n.Rect.Width - 16);
                        Theme.FillRound(g, new RectangleF(n.Rect.X + 8, n.Rect.Bottom - 4.5f, mw, 2.5f), 1.2f, Theme.Meter);
                    }
                }
            }

            // connection sockets — blueprint-style ports on every card edge that can route
            using (SolidBrush portFill = new SolidBrush(Theme.Canvas))
            using (Pen portRing = new Pen(Theme.WireMix, 1.6f))
            {
                foreach (Node n in nodes)
                {
                    if (n.Rect.Bottom < viewTop || n.Rect.Y > viewBottom) continue;
                    bool dim = lit != null && !lit.Contains(n.Key);
                    if (n.Col == 1 && n.Pid != 0)
                    {
                        if (n.HasRender) DrawPort(g, portFill, portRing, RightPort(n), dim ? Theme.WireIdle : Theme.WireMix);
                        if (n.HasCapture) DrawPort(g, portFill, portRing, LeftPort(n), dim ? Theme.WireIdle : Theme.WireIn);
                    }
                    else if (n.Col == 2) DrawPort(g, portFill, portRing, LeftPort(n), dim ? Theme.WireIdle : Theme.WireMix);
                    else if (n.Col == 0) DrawPort(g, portFill, portRing, RightPort(n), dim ? Theme.WireIdle : Theme.WireIn);
                }
            }

            // live drag: ghost wire from the origin port to the cursor + target highlight
            if (dragging)
            {
                Color dc = dragKind == EDataFlow.eRender ? Theme.WireMix : Theme.WireIn;
                float sign = dragKind == EDataFlow.eRender ? 1f : -1f;
                float hdx = Math.Max(30, Math.Abs(dragCur.X - dragOrigin.X) * 0.5f) * sign;
                using (Pen gp = new Pen(Color.FromArgb(180, dc), 2.4f))
                {
                    gp.StartCap = LineCap.Round; gp.EndCap = LineCap.Round;
                    g.DrawBezier(gp, dragOrigin,
                        new PointF(dragOrigin.X + hdx, dragOrigin.Y),
                        new PointF(dragCur.X - hdx, dragCur.Y),
                        new PointF(dragCur.X, dragCur.Y));
                }
                using (SolidBrush db = new SolidBrush(dc))
                    g.FillEllipse(db, dragCur.X - 4.5f, dragCur.Y - 4.5f, 9, 9);
                if (dropKey != null)
                {
                    Node t = Find(dropKey);
                    if (t != null)
                        Theme.StrokeRound(g, RectangleF.Inflate(t.Rect, 2.5f, 2.5f), 10, Theme.Ink, 2f);
                }
            }

            g.ResetTransform();

            // legend
            int ly = Height - 26;
            using (SolidBrush b = new SolidBrush(Theme.Canvas)) g.FillRectangle(b, 0, ly - 8, Width, 40);
            int lx = 24;
            lx = LegendItem(g, lx, ly, Theme.WireIn, "Input to app", false);
            lx = LegendItem(g, lx, ly, Theme.WireMix, "App to output", false);
            lx = LegendItem(g, lx, ly, Theme.WireMuted, "Muted", true);
            lx = LegendItem(g, lx, ly, Theme.WireIdle, selected != null ? "Not in path" : "Idle", false);
            string hint = dragging ? "DROP ON A DEVICE · DEFAULT DEVICE = UNPIN · ESC CANCELS"
                : selected != null ? "ESC OR EMPTY SPACE CLEARS"
                : "DRAG A PORT ONTO A DEVICE TO RE-ROUTE THAT APP";
            TextRenderer.DrawText(g, hint, Theme.Caption,
                new Rectangle(lx + 10, ly + 2, 420, 14), Theme.TextDim, TextFormatFlags.NoPrefix);

            // scrollbar
            int content = ContentHeight();
            if (content > Height)
            {
                float frac = (float)Height / content;
                float pos = (float)scroll / content;
                Theme.FillRound(g, new RectangleF(Width - 6, pos * Height, 4, Math.Max(24, frac * Height)),
                    2, Theme.ScrollThumb);
            }
        }

        static void DrawPort(Graphics g, SolidBrush fill, Pen ring, PointF c, Color col)
        {
            g.FillEllipse(fill, c.X - 4.5f, c.Y - 4.5f, 9, 9);
            ring.Color = col;
            g.DrawEllipse(ring, c.X - 4.5f, c.Y - 4.5f, 9, 9);
        }

        int LegendItem(Graphics g, int x, int y, Color c, string label, bool dash)
        {
            using (Pen p = new Pen(c, 2f))
            {
                if (dash) p.DashStyle = DashStyle.Dash;
                g.DrawLine(p, x, y + 8, x + 22, y + 8);
            }
            string cap = label.ToUpperInvariant();
            Size sz = TextRenderer.MeasureText(cap, Theme.Caption);
            TextRenderer.DrawText(g, cap, Theme.Caption, new Rectangle(x + 28, y + 2, sz.Width + 4, 14),
                Theme.TextSub, TextFormatFlags.NoPrefix);
            return x + 28 + sz.Width + 24;
        }

        static PointF PointOn(Edge e, float t)
        {
            // de Casteljau on the same control points used for the path
            float dx = Math.Max(30, (e.B.X - e.A.X) * 0.5f);
            PointF p1 = e.A, p2 = new PointF(e.A.X + dx, e.A.Y), p3 = new PointF(e.B.X - dx, e.B.Y), p4 = e.B;
            float u = 1 - t;
            return new PointF(
                u*u*u*p1.X + 3*u*u*t*p2.X + 3*u*t*t*p3.X + t*t*t*p4.X,
                u*u*u*p1.Y + 3*u*u*t*p2.Y + 3*u*t*t*p3.Y + t*t*t*p4.Y);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll - Math.Sign(e.Delta) * 60));
            Invalidate();
        }

        // ---- ports (world coordinates) ----
        static PointF RightPort(Node n) { return new PointF(n.Rect.Right, n.Rect.Y + CardH / 2f); }
        static PointF LeftPort(Node n) { return new PointF(n.Rect.X, n.Rect.Y + CardH / 2f); }
        static bool Near(Point p, PointF c, float r) { float dx = p.X - c.X, dy = p.Y - c.Y; return dx * dx + dy * dy <= r * r; }

        bool TryStartDrag(Point world)
        {
            // 1) explicit ports on app cards
            foreach (Node n in nodes)
            {
                if (n.Col != 1 || n.Pid == 0) continue;
                if (n.HasRender && Near(world, RightPort(n), 10))
                { BeginDrag(n.Pid, EDataFlow.eRender, RightPort(n), world); return true; }
                if (n.HasCapture && Near(world, LeftPort(n), 10))
                { BeginDrag(n.Pid, EDataFlow.eCapture, LeftPort(n), world); return true; }
            }
            // 2) grab an existing wire at its device end
            foreach (Edge e in edges)
            {
                if (e.Path == null) continue;
                if (e.From.StartsWith("app:") && e.To.StartsWith("out:") && Near(world, e.B, 8))
                {
                    uint pid;
                    if (uint.TryParse(e.From.Substring(4), out pid) && pid != 0)
                    {
                        Node app = Find(e.From);
                        BeginDrag(pid, EDataFlow.eRender, app != null ? RightPort(app) : e.A, world);
                        return true;
                    }
                }
                if (e.From.StartsWith("in:") && e.To.StartsWith("app:") && Near(world, e.A, 8))
                {
                    uint pid;
                    if (uint.TryParse(e.To.Substring(4), out pid) && pid != 0)
                    {
                        Node app = Find(e.To);
                        BeginDrag(pid, EDataFlow.eCapture, app != null ? LeftPort(app) : e.B, world);
                        return true;
                    }
                }
            }
            return false;
        }

        void BeginDrag(uint pid, EDataFlow kind, PointF origin, Point world)
        {
            dragging = true; dragMoved = false;
            dragPid = pid; dragKind = kind;
            dragOrigin = origin; dragCur = world; dropKey = null;
            Cursor = Cursors.Cross;
            Invalidate();
        }

        string FindTarget(Point world, EDataFlow kind)
        {
            string prefix = kind == EDataFlow.eRender ? "out:" : "in:";
            foreach (Node n in nodes)
                if (n.Key.StartsWith(prefix) && RectangleF.Inflate(n.Rect, 6, 6).Contains(world)) return n.Key;
            return null;
        }

        void DoRoute(uint pid, EDataFlow kind, string targetKey)
        {
            Node target = Find(targetKey);
            Node app = Find("app:" + pid);
            string appName = app != null ? app.Name : ("PID " + pid);
            if (target == null || Audio == null) return;
            string devId = targetKey.Substring(targetKey.IndexOf(':') + 1);
            // Dropping on the current DEFAULT device = unpin: the app follows the default again.
            bool toDefault = target.Default;
            bool ok = Audio.SetAppRoute(pid, kind, toDefault ? null : devId);
            if (Toast != null) Toast(ok
                ? (toDefault
                    ? appName + " follows the default " + (kind == EDataFlow.eRender ? "output" : "input") + " again."
                    : appName + " → " + target.Name)
                : "Couldn't re-route " + appName + ".");
            if (ok && Changed != null) Changed();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            Point world = new Point(e.X, e.Y + scroll);
            TryStartDrag(world);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = new Point(e.X, e.Y + scroll);
            if (dragging)
            {
                dragCur = p;
                if (!dragMoved)
                {
                    float dx = p.X - dragOrigin.X, dy = p.Y - dragOrigin.Y;
                    if (dx * dx + dy * dy > 36) dragMoved = true;
                }
                dropKey = FindTarget(p, dragKind);
                Invalidate();
                return;
            }
            string k = null;
            foreach (Node n in nodes) if (n.Rect.Contains(p)) { k = n.Key; break; }
            bool overPort = k == null && TestPortHover(p);
            if (k != hoverKey)
            {
                hoverKey = k;
                Cursor = k != null || overPort ? Cursors.Hand : Cursors.Default;
            }
            else if (overPort) Cursor = Cursors.Hand;
        }

        bool TestPortHover(Point world)
        {
            foreach (Node n in nodes)
            {
                if (n.Col != 1 || n.Pid == 0) continue;
                if (n.HasRender && Near(world, RightPort(n), 10)) return true;
                if (n.HasCapture && Near(world, LeftPort(n), 10)) return true;
            }
            return false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverKey = null;
            if (!dragging) Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Focus();
            Point p = new Point(e.X, e.Y + scroll);
            if (dragging)
            {
                bool moved = dragMoved;
                string target = dropKey;
                uint pid = dragPid;
                EDataFlow kind = dragKind;
                dragging = false; dragMoved = false; dropKey = null;
                Cursor = Cursors.Default;
                if (moved)
                {
                    if (target != null) DoRoute(pid, kind, target);
                }
                else
                {
                    selected = "app:" + pid;   // port tap without a drag = select that app
                }
                traceDirty = true;
                Invalidate();
                return;
            }
            foreach (Node n in nodes)
                if (n.Rect.Contains(p)) { selected = n.Key; traceDirty = true; Invalidate(); return; }
            selected = null;
            traceDirty = true;
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && dragging)
            {
                dragging = false; dragMoved = false; dropKey = null;
                Cursor = Cursors.Default;
                Invalidate();
                return true;
            }
            if (keyData == Keys.Escape && selected != null) { selected = null; traceDirty = true; Invalidate(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ScrollBy(int px)
        {
            scroll = Math.Max(0, Math.Min(Math.Max(0, ContentHeight() - Height), scroll + px));
            Invalidate();
        }
    }
}

// Live app icons: resolve a session's PID to its executable and extract the icon the
// app itself ships (exactly what Task Manager / the Volume Mixer show). Cached per
// pid and per exe path; falls back to the generic glyph when a process is protected.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace SoundMaster
{
    public static class AppIcons
    {
        static readonly Dictionary<uint, Bitmap> byPid = new Dictionary<uint, Bitmap>();
        static readonly Dictionary<string, Bitmap> byPath = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder exeName, ref uint size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        static string PathOf(uint pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (h == IntPtr.Zero) return null;
                StringBuilder sb = new StringBuilder(1024);
                uint len = 1024;
                return QueryFullProcessImageName(h, 0, sb, ref len) ? sb.ToString() : null;
            }
            catch { return null; }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }

        public static Bitmap Get(uint pid)
        {
            if (pid == 0) return null;   // System sounds keeps the glyph
            lock (gate)
            {
                Bitmap cached;
                if (byPid.TryGetValue(pid, out cached)) return cached;   // null = known miss
            }
            Bitmap bmp = null;
            string path = PathOf(pid);
            if (path != null)
            {
                lock (gate) { byPath.TryGetValue(path, out bmp); }
                if (bmp == null)
                {
                    try
                    {
                        using (Icon ic = Icon.ExtractAssociatedIcon(path))
                            if (ic != null) bmp = ic.ToBitmap();
                    }
                    catch { /* protected / packaged app — glyph fallback */ }
                }
            }
            lock (gate)
            {
                if (byPid.Count > 256) byPid.Clear();   // pids recycle; byPath keeps the pixels warm
                byPid[pid] = bmp;
                if (path != null && bmp != null) byPath[path] = bmp;
            }
            return bmp;
        }

        public static void Draw(Graphics g, Bitmap bmp, RectangleF r, float opacity)
        {
            System.Drawing.Drawing2D.InterpolationMode old = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            try
            {
                if (opacity >= 0.99f)
                {
                    g.DrawImage(bmp, r);
                }
                else
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = opacity;
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(bmp, Rectangle.Round(r), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            finally { g.InterpolationMode = old; }
        }
    }
}

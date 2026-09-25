// Per-app endpoint routing — the engine behind Settings > App volume and device
// preferences. Internal WinRT class Windows.Media.Internal.AudioPolicyConfig; the
// interface IID differs across Windows 10 servicing lines, so both are tried and the
// selftest verifies the vtable empirically before the UI relies on it.
using System;
using System.Runtime.InteropServices;

namespace SoundMaster
{
    public static class AudioPolicyConfig
    {
        [DllImport("combase.dll", PreserveSig = true)]
        static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
        static extern int WindowsCreateString(string src, int len, out IntPtr hstring);
        [DllImport("combase.dll", PreserveSig = true)]
        static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("combase.dll", PreserveSig = true)]
        static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int len);

        const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
        const string RenderIface = "{E6327CAD-DCEC-4949-AE8A-991E976A79D2}";
        const string CaptureIface = "{2EEF81BE-33FA-4800-9670-1CD474972C3F}";

        // Identical shapes, different IIDs per Windows servicing line.
        [Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        public interface IApcNew
        {
            [PreserveSig] int _p0(); [PreserveSig] int _p1(); [PreserveSig] int _p2(); [PreserveSig] int _p3();
            [PreserveSig] int _p4(); [PreserveSig] int _p5(); [PreserveSig] int _p6(); [PreserveSig] int _p7();
            [PreserveSig] int _p8(); [PreserveSig] int _p9(); [PreserveSig] int _p10(); [PreserveSig] int _p11();
            [PreserveSig] int _p12(); [PreserveSig] int _p13(); [PreserveSig] int _p14(); [PreserveSig] int _p15();
            [PreserveSig] int _p16(); [PreserveSig] int _p17(); [PreserveSig] int _p18();
            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out IntPtr deviceId);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        [Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        public interface IApcOld
        {
            [PreserveSig] int _p0(); [PreserveSig] int _p1(); [PreserveSig] int _p2(); [PreserveSig] int _p3();
            [PreserveSig] int _p4(); [PreserveSig] int _p5(); [PreserveSig] int _p6(); [PreserveSig] int _p7();
            [PreserveSig] int _p8(); [PreserveSig] int _p9(); [PreserveSig] int _p10(); [PreserveSig] int _p11();
            [PreserveSig] int _p12(); [PreserveSig] int _p13(); [PreserveSig] int _p14(); [PreserveSig] int _p15();
            [PreserveSig] int _p16(); [PreserveSig] int _p17(); [PreserveSig] int _p18();
            [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
            [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, out IntPtr deviceId);
            [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
        }

        static IApcNew apcNew;
        static IApcOld apcOld;
        static readonly object gate = new object();

        static IntPtr Hstr(string s)
        {
            if (string.IsNullOrEmpty(s)) return IntPtr.Zero;
            IntPtr h;
            return WindowsCreateString(s, s.Length, out h) == 0 ? h : IntPtr.Zero;
        }

        static string FromHstr(IntPtr h)
        {
            if (h == IntPtr.Zero) return null;
            int len;
            IntPtr buf = WindowsGetStringRawBuffer(h, out len);
            return buf == IntPtr.Zero ? null : Marshal.PtrToStringUni(buf, len);
        }

        static bool Ensure()
        {
            lock (gate)
            {
                if (apcNew != null || apcOld != null) return true;
                IntPtr cls = Hstr(ClassName);
                if (cls == IntPtr.Zero) return false;
                try
                {
                    IntPtr f;
                    Guid g = typeof(IApcNew).GUID;
                    if (RoGetActivationFactory(cls, ref g, out f) == 0 && f != IntPtr.Zero)
                    {
                        apcNew = (IApcNew)Marshal.GetObjectForIUnknown(f);
                        Marshal.Release(f);
                        return true;
                    }
                    g = typeof(IApcOld).GUID;
                    if (RoGetActivationFactory(cls, ref g, out f) == 0 && f != IntPtr.Zero)
                    {
                        apcOld = (IApcOld)Marshal.GetObjectForIUnknown(f);
                        Marshal.Release(f);
                        return true;
                    }
                }
                catch { }
                finally { WindowsDeleteString(cls); }
                return false;
            }
        }

        public static bool Available { get { return Ensure(); } }
        public static string VariantName { get { return apcNew != null ? "21H2+" : apcOld != null ? "pre-21H2" : "none"; } }

        static string Wrap(string mmDeviceId, EDataFlow flow)
        {
            return "\\\\?\\SWD#MMDEVAPI#" + mmDeviceId + "#" +
                (flow == EDataFlow.eCapture ? CaptureIface : RenderIface);
        }

        static string Unwrap(string wrapped)
        {
            if (string.IsNullOrEmpty(wrapped)) return null;
            int start = wrapped.IndexOf("MMDEVAPI#", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return wrapped;
            start += "MMDEVAPI#".Length;
            int end = wrapped.LastIndexOf('#');
            return end > start ? wrapped.Substring(start, end - start) : wrapped.Substring(start);
        }

        // deviceId null => clear the pin: the app follows the Windows default again.
        public static bool SetAppEndpoint(uint pid, EDataFlow flow, string mmDeviceIdOrNull)
        {
            if (!Ensure()) return false;
            IntPtr h = mmDeviceIdOrNull == null ? IntPtr.Zero : Hstr(Wrap(mmDeviceIdOrNull, flow));
            try
            {
                int hr1 = apcNew != null
                    ? apcNew.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eConsole, h)
                    : apcOld.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eConsole, h);
                int hr2 = apcNew != null
                    ? apcNew.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eMultimedia, h)
                    : apcOld.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.eMultimedia, h);
                return hr1 == 0 && hr2 == 0;
            }
            catch { return false; }
            finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
        }

        // Returns the pinned MMDevice id, or null when the app follows the default.
        public static string GetAppEndpoint(uint pid, EDataFlow flow)
        {
            if (!Ensure()) return null;
            IntPtr h = IntPtr.Zero;
            try
            {
                int hr = apcNew != null
                    ? apcNew.GetPersistedDefaultAudioEndpoint(pid, flow, ERole.eMultimedia, out h)
                    : apcOld.GetPersistedDefaultAudioEndpoint(pid, flow, ERole.eMultimedia, out h);
                if (hr != 0) return null;
                string s = FromHstr(h);
                return string.IsNullOrEmpty(s) ? null : Unwrap(s);
            }
            catch { return null; }
            finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
        }
    }
}

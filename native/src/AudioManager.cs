// High-level wrapper over Core Audio: device snapshots, defaults, volumes, meters,
// per-app sessions, endpoint enable/disable. All calls are best-effort; COM failures
// surface as nulls/false rather than crashes.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoundMaster
{
    public class DeviceInfo
    {
        public string Id;
        public string Name;
        public EDataFlow Flow;
        public uint State;              // DeviceState.*
        public bool IsDefault;          // eMultimedia/eConsole default
        public bool IsDefaultComm;      // eCommunications default
        public float Volume;            // 0..1 scalar
        public bool Muted;
        public float Peak;              // live meter 0..1
    }

    public class SessionInfo
    {
        public string DeviceId;         // endpoint this session lives on
        public EDataFlow Flow;          // render session (app playing) or capture session (app recording)
        public uint Pid;
        public string Name;             // process name or display name
        public float Volume;
        public bool Muted;
        public float Peak;
        public int State;               // 0 inactive, 1 active, 2 expired
        public string InstanceId;
    }

    public class AudioSnapshot
    {
        public List<DeviceInfo> Playback = new List<DeviceInfo>();
        public List<DeviceInfo> Recording = new List<DeviceInfo>();
        public List<SessionInfo> Sessions = new List<SessionInfo>();
    }

    public class AudioManager
    {
        readonly IMMDeviceEnumerator enumerator;
        static Guid eventCtx = new Guid("9a3e3f6e-2f74-4b6b-9a51-5b0b7a3e5d10");

        // Cached activated interfaces per device id so meters/volume don't re-activate each poll.
        readonly Dictionary<string, IAudioEndpointVolume> volCache = new Dictionary<string, IAudioEndpointVolume>();
        readonly Dictionary<string, IAudioMeterInformation> meterCache = new Dictionary<string, IAudioMeterInformation>();

        public AudioManager()
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public static bool DebugLog = Environment.GetEnvironmentVariable("SM_DEBUG") == "1";

        // Marshal.ReleaseComObject can throw NullReferenceException on this machine's CLR
        // for property-store RCWs. Releasing is best-effort housekeeping — never let it
        // destroy a result.
        static void Release(object o)
        {
            if (o == null) return;
            try { Marshal.ReleaseComObject(o); } catch { /* RCW already detached */ }
        }

        static string GetName(IMMDevice dev)
        {
            try
            {
                IPropertyStore store;
                int hr = dev.OpenPropertyStore(0 /*STGM_READ*/, out store);
                if (hr != 0 || store == null)
                {
                    if (DebugLog) Console.WriteLine("[GetName] OpenPropertyStore hr=0x" + hr.ToString("X8"));
                    return "(unknown)";
                }
                PropVariant pv;
                PropertyKey key = PKey.DeviceFriendlyName;
                string name = null;
                hr = store.GetValue(ref key, out pv);
                if (hr == 0)
                {
                    name = pv.GetString();
                    Ole32.PropVariantClear(ref pv);
                }
                else if (DebugLog) Console.WriteLine("[GetName] GetValue hr=0x" + hr.ToString("X8"));
                Release(store);
                return string.IsNullOrEmpty(name) ? "(unknown)" : name;
            }
            catch (Exception e)
            {
                if (DebugLog) Console.WriteLine("[GetName] EXC " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return "(unknown)";
            }
        }

        string DefaultId(EDataFlow flow, ERole role)
        {
            try
            {
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return null;
                string id;
                dev.GetId(out id);
                Release(dev);
                return id;
            }
            catch { return null; }
        }

        IAudioEndpointVolume Vol(IMMDevice dev, string id)
        {
            IAudioEndpointVolume v;
            if (volCache.TryGetValue(id, out v)) return v;
            try
            {
                object o;
                Guid iid = Iid.IAudioEndpointVolume;
                if (dev.Activate(ref iid, Iid.CLSCTX_ALL, IntPtr.Zero, out o) != 0) return null;
                v = (IAudioEndpointVolume)o;
                volCache[id] = v;
                return v;
            }
            catch { return null; }
        }

        IAudioMeterInformation Meter(IMMDevice dev, string id)
        {
            IAudioMeterInformation m;
            if (meterCache.TryGetValue(id, out m)) return m;
            try
            {
                object o;
                Guid iid = Iid.IAudioMeterInformation;
                if (dev.Activate(ref iid, Iid.CLSCTX_ALL, IntPtr.Zero, out o) != 0) return null;
                m = (IAudioMeterInformation)o;
                meterCache[id] = m;
                return m;
            }
            catch { return null; }
        }

        void DropCaches(string id)
        {
            IAudioEndpointVolume v;
            if (volCache.TryGetValue(id, out v)) { try { Release(v); } catch { } volCache.Remove(id); }
            IAudioMeterInformation m;
            if (meterCache.TryGetValue(id, out m)) { try { Release(m); } catch { } meterCache.Remove(id); }
        }

        public AudioSnapshot Snapshot(bool includeSessions, bool includeMeters)
        {
            AudioSnapshot snap = new AudioSnapshot();
            CollectDevices(EDataFlow.eRender, snap.Playback, includeMeters);
            CollectDevices(EDataFlow.eCapture, snap.Recording, includeMeters);
            if (includeSessions)
            {
                CollectSessions(EDataFlow.eRender, snap);
                CollectSessions(EDataFlow.eCapture, snap);
            }
            return snap;
        }

        void CollectDevices(EDataFlow flow, List<DeviceInfo> into, bool meters)
        {
            string defId = DefaultId(flow, ERole.eMultimedia);
            string commId = DefaultId(flow, ERole.eCommunications);
            IMMDeviceCollection coll = null;
            try
            {
                if (enumerator.EnumAudioEndpoints(flow, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged, out coll) != 0 || coll == null)
                    return;
            }
            catch { return; }
            uint count = 0;
            try { coll.GetCount(out count); } catch { Release(coll); return; }
            for (uint i = 0; i < count; i++)
            {
                IMMDevice dev = null;
                DeviceInfo d = new DeviceInfo();
                try
                {
                    if (coll.Item(i, out dev) != 0 || dev == null) continue;
                    dev.GetId(out d.Id);
                    dev.GetState(out d.State);
                }
                catch { Release(dev); continue; }
                d.Name = GetName(dev);
                d.Flow = flow;
                d.IsDefault = d.Id == defId;
                d.IsDefaultComm = d.Id == commId;
                if (d.State == DeviceState.Active)
                {
                    try
                    {
                        IAudioEndpointVolume v = Vol(dev, d.Id);
                        if (v != null)
                        {
                            float lvl; bool mute;
                            int hr = v.GetMasterVolumeLevelScalar(out lvl);
                            if (hr == 0) { d.Volume = lvl; }
                            else
                            {
                                // Stale interface (replug / Audiosrv restart): poison the cache
                                // and re-activate once from the device we already hold.
                                DropCaches(d.Id);
                                v = Vol(dev, d.Id);
                                if (v != null && v.GetMasterVolumeLevelScalar(out lvl) == 0) d.Volume = lvl;
                            }
                            if (v != null && v.GetMute(out mute) == 0) d.Muted = mute;
                        }
                        if (meters)
                        {
                            IAudioMeterInformation m = Meter(dev, d.Id);
                            float peak;
                            if (m != null && m.GetPeakValue(out peak) == 0) d.Peak = peak;
                            else if (m != null) DropCaches(d.Id);
                        }
                    }
                    catch { DropCaches(d.Id); }
                }
                else
                {
                    DropCaches(d.Id);   // interfaces activated on a now-inactive endpoint are dead
                }
                into.Add(d);
                Release(dev);
            }
            Release(coll);
        }

        void CollectSessions(EDataFlow flow, AudioSnapshot snap)
        {
            IMMDeviceCollection coll = null;
            try
            {
                if (enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out coll) != 0 || coll == null) return;
            }
            catch { return; }
            uint count = 0;
            try { coll.GetCount(out count); } catch { Release(coll); return; }
            for (uint i = 0; i < count; i++)
            {
                IMMDevice dev = null;
                string devId = null;
                try
                {
                    if (coll.Item(i, out dev) != 0 || dev == null) continue;
                    dev.GetId(out devId);
                }
                catch { Release(dev); continue; }
                try
                {
                    object o;
                    Guid iid = Iid.IAudioSessionManager2;
                    if (dev.Activate(ref iid, Iid.CLSCTX_ALL, IntPtr.Zero, out o) == 0)
                    {
                        IAudioSessionManager2 mgr = (IAudioSessionManager2)o;
                        IAudioSessionEnumerator sessions;
                        if (mgr.GetSessionEnumerator(out sessions) == 0 && sessions != null)
                        {
                            int n;
                            sessions.GetCount(out n);
                            for (int s = 0; s < n; s++)
                            {
                                IAudioSessionControl ctl;
                                if (sessions.GetSession(s, out ctl) != 0 || ctl == null) continue;
                                SessionInfo si = ReadSession(ctl, devId, flow);
                                if (si != null) snap.Sessions.Add(si);
                                Release(ctl);
                            }
                            Release(sessions);
                        }
                        Release(mgr);
                    }
                }
                catch { }
                Release(dev);
            }
            Release(coll);
        }

        SessionInfo ReadSession(IAudioSessionControl ctl, string devId, EDataFlow flow)
        {
            try
            {
                IAudioSessionControl2 c2 = ctl as IAudioSessionControl2;
                if (c2 == null) return null;
                SessionInfo si = new SessionInfo();
                si.DeviceId = devId;
                si.Flow = flow;
                c2.GetProcessId(out si.Pid);
                c2.GetState(out si.State);
                if (si.State == 2) return null;                     // expired
                c2.GetSessionInstanceIdentifier(out si.InstanceId);
                string disp;
                c2.GetDisplayName(out disp);
                si.Name = si.Pid == 0 ? "System sounds"
                    : (string.IsNullOrEmpty(disp) ? ProcessName(si.Pid) : disp);
                ISimpleAudioVolume sv = ctl as ISimpleAudioVolume;
                if (sv != null)
                {
                    float lvl; bool mute;
                    if (sv.GetMasterVolume(out lvl) == 0) si.Volume = lvl;
                    if (sv.GetMute(out mute) == 0) si.Muted = mute;
                }
                IAudioMeterInformation mi = ctl as IAudioMeterInformation;
                if (mi != null)
                {
                    float peak;
                    if (mi.GetPeakValue(out peak) == 0) si.Peak = peak;
                }
                return si;
            }
            catch { return null; }
        }

        static readonly Dictionary<uint, string> nameCache = new Dictionary<uint, string>();
        static readonly object nameLock = new object();
        static string ProcessName(uint pid)
        {
            if (pid == 0) return "System";
            string n;
            lock (nameLock) { if (nameCache.TryGetValue(pid, out n)) return n; }
            try { n = Process.GetProcessById((int)pid).ProcessName; }
            catch { n = "PID " + pid; }
            lock (nameLock)
            {
                if (nameCache.Count > 512) nameCache.Clear();   // pids recycle; keep it bounded
                nameCache[pid] = n;
            }
            return n;
        }

        // ---- actions ----

        // This LTSC build registers only the Vista PolicyConfig class. Try the full
        // interface first (other builds), then fall back to the Vista one.
        static object CreatePolicyConfig()
        {
            try { return new PolicyConfigComObject(); } catch { }
            try { return new PolicyConfigVistaComObject(); } catch { }
            return null;
        }

        static int PcSetDefault(string id, ERole role)
        {
            object pc = CreatePolicyConfig();
            if (pc == null) return -1;
            try
            {
                IPolicyConfig full = pc as IPolicyConfig;
                if (full != null) return full.SetDefaultEndpoint(id, role);
                IPolicyConfigVista vista = pc as IPolicyConfigVista;
                if (vista != null) return vista.SetDefaultEndpoint(id, role);
                return -1;
            }
            finally { try { Release(pc); } catch { } }
        }

        public bool SetDefaultDevice(string id, bool communicationsToo)
        {
            int hr = PcSetDefault(id, ERole.eConsole);
            PcSetDefault(id, ERole.eMultimedia);
            if (communicationsToo) PcSetDefault(id, ERole.eCommunications);
            return hr == 0;
        }

        public bool SetDefaultCommunications(string id)
        {
            return PcSetDefault(id, ERole.eCommunications) == 0;
        }

        // Disable/enable an endpoint, same end state as the Sound control panel.
        // SetEndpointVisibility is unavailable here (Vista PolicyConfig only), so this
        // edits the endpoint's DeviceState in the registry — which requires admin — via
        // an elevated helper process (one UAC prompt per toggle).
        public bool SetDeviceEnabled(string id, bool enabled)
        {
            try
            {
                // id looks like {0.0.0.00000000}.{g
                // uid}; the registry key uses just the {guid} part, under Render/Capture.
                int brace = id.LastIndexOf('{');
                if (brace < 0) return false;
                string guid = id.Substring(brace);
                string flowKey = id.StartsWith("{0.0.1.") ? "Capture" : "Render";
                string keyPath = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\MMDevices\\Audio\\" + flowKey + "\\" + guid;
                // 0x10000001 = disabled-by-user, 0x1 = active. Restarting Audiosrv is not
                // needed — the endpoint builder watches these keys.
                string value = enabled ? "1" : "268435457";
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "reg.exe";
                psi.Arguments = "add \"" + keyPath + "\" /v DeviceState /t REG_DWORD /d " + value + " /f";
                psi.Verb = "runas";           // UAC prompt
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000)) return false;   // still running: report unknown as failure
                    DropCaches(id);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }   // user cancelled UAC, or access denied
        }

        public bool SetDeviceVolume(string id, float scalar)
        {
            try
            {
                IMMDevice dev;
                if (enumerator.GetDevice(id, out dev) != 0 || dev == null) return false;
                IAudioEndpointVolume v = Vol(dev, id);
                Release(dev);
                if (v == null) return false;
                return v.SetMasterVolumeLevelScalar(Clamp01(scalar), ref eventCtx) == 0;
            }
            catch { return false; }
        }

        public bool SetDeviceMute(string id, bool mute)
        {
            try
            {
                IMMDevice dev;
                if (enumerator.GetDevice(id, out dev) != 0 || dev == null) return false;
                IAudioEndpointVolume v = Vol(dev, id);
                Release(dev);
                if (v == null) return false;
                return v.SetMute(mute, ref eventCtx) == 0;
            }
            catch { return false; }
        }

        public bool SetSessionVolume(string deviceId, string instanceId, float scalar, bool? mute)
        {
            try
            {
                IMMDevice dev;
                if (enumerator.GetDevice(deviceId, out dev) != 0 || dev == null) return false;
                object o;
                Guid iid = Iid.IAudioSessionManager2;
                bool ok = false;
                if (dev.Activate(ref iid, Iid.CLSCTX_ALL, IntPtr.Zero, out o) == 0)
                {
                    IAudioSessionManager2 mgr = (IAudioSessionManager2)o;
                    IAudioSessionEnumerator sessions;
                    if (mgr.GetSessionEnumerator(out sessions) == 0 && sessions != null)
                    {
                        int n;
                        sessions.GetCount(out n);
                        for (int s = 0; s < n; s++)
                        {
                            IAudioSessionControl ctl;
                            if (sessions.GetSession(s, out ctl) != 0 || ctl == null) continue;
                            IAudioSessionControl2 c2 = ctl as IAudioSessionControl2;
                            string inst = null;
                            if (c2 != null) c2.GetSessionInstanceIdentifier(out inst);
                            if (inst == instanceId)
                            {
                                ISimpleAudioVolume sv = ctl as ISimpleAudioVolume;
                                if (sv != null)
                                {
                                    ok = true;
                                    if (scalar >= 0) ok &= sv.SetMasterVolume(Clamp01(scalar), ref eventCtx) == 0;
                                    if (mute.HasValue) ok &= sv.SetMute(mute.Value, ref eventCtx) == 0;
                                }
                            }
                            Release(ctl);
                            if (ok) break;
                        }
                        Release(sessions);
                    }
                    Release(mgr);
                }
                Release(dev);
                return ok;
            }
            catch { return false; }
        }

        // Route ONE app's audio to a specific device (null = follow the Windows default
        // again). Live streams migrate immediately, same as the Settings page.
        public bool SetAppRoute(uint pid, EDataFlow flow, string deviceIdOrNull)
        {
            try { return AudioPolicyConfig.SetAppEndpoint(pid, flow, deviceIdOrNull); }
            catch { return false; }
        }

        public string GetAppRoute(uint pid, EDataFlow flow)
        {
            try { return AudioPolicyConfig.GetAppEndpoint(pid, flow); }
            catch { return null; }
        }

        static float Clamp01(float f) { return f < 0f ? 0f : (f > 1f ? 1f : f); }
    }
}

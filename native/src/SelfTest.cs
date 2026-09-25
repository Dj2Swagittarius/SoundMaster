// Read-only interop validation. Exercises every interface; the only "write" is
// setting the default device to the device that is already default (a no-op).
using System;
using System.Globalization;

namespace SoundMaster
{
    public static class SelfTest
    {
        public static int Run()
        {
            int failures = 0;
            AudioManager am = null;
            try
            {
                am = new AudioManager();
                Console.WriteLine("[ok] MMDeviceEnumerator created");
            }
            catch (Exception e)
            {
                Console.WriteLine("[FAIL] enumerator: " + e.Message);
                return 1;
            }

            AudioSnapshot snap = null;
            try
            {
                snap = am.Snapshot(true, true);
                Console.WriteLine("[ok] snapshot: " + snap.Playback.Count + " playback, "
                    + snap.Recording.Count + " recording, " + snap.Sessions.Count + " sessions");
            }
            catch (Exception e)
            {
                Console.WriteLine("[FAIL] snapshot: " + e);
                return 1;
            }

            string defaultPlayback = null;
            foreach (DeviceInfo d in snap.Playback)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  render  {0} vol={1:P0} mute={2} state={3}{4}{5}  {6}",
                    d.IsDefault ? "*" : " ", d.Volume, d.Muted, d.State,
                    d.IsDefaultComm ? " comm" : "", d.Peak > 0 ? " peak=" + d.Peak.ToString("F3", CultureInfo.InvariantCulture) : "",
                    d.Name));
                if (d.IsDefault) defaultPlayback = d.Id;
            }
            foreach (DeviceInfo d in snap.Recording)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  capture {0} vol={1:P0} mute={2} state={3}  {4}",
                    d.IsDefault ? "*" : " ", d.Volume, d.Muted, d.State, d.Name));
            foreach (SessionInfo s in snap.Sessions)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  session [{0}] pid={1} vol={2:P0} mute={3} state={4} dev={5}...",
                    s.Flow == EDataFlow.eRender ? "app" : "rec", s.Pid, s.Volume, s.Muted, s.State,
                    s.DeviceId != null && s.DeviceId.Length > 24 ? s.DeviceId.Substring(0, 24) : s.DeviceId));

            if (snap.Playback.Count == 0) { Console.WriteLine("[FAIL] no playback devices found"); failures++; }

            // PolicyConfig: set default to the CURRENT default — verifies the interface
            // + vtable position without changing anything.
            if (defaultPlayback != null)
            {
                bool ok = am.SetDefaultDevice(defaultPlayback, false);
                Console.WriteLine(ok ? "[ok] IPolicyConfig.SetDefaultEndpoint (no-op self-set)"
                                     : "[FAIL] IPolicyConfig.SetDefaultEndpoint returned error");
                if (!ok) failures++;
            }

            // Volume read-back check: set volume to its current value (no audible change).
            if (defaultPlayback != null)
            {
                DeviceInfo def = null;
                foreach (DeviceInfo d in snap.Playback) if (d.IsDefault) def = d;
                if (def != null)
                {
                    bool ok = am.SetDeviceVolume(def.Id, def.Volume);
                    Console.WriteLine(ok ? "[ok] IAudioEndpointVolume.SetMasterVolumeLevelScalar (no-op self-set)"
                                         : "[FAIL] SetMasterVolumeLevelScalar");
                    if (!ok) failures++;
                }
            }

            // Per-app routing interface (read-only probe): activate the policy factory and
            // read the persisted endpoint of a real session pid. No state is written.
            Console.WriteLine("[..] AudioPolicyConfig: " + (AudioPolicyConfig.Available
                ? "available (" + AudioPolicyConfig.VariantName + ")" : "NOT AVAILABLE"));
            if (AudioPolicyConfig.Available)
            {
                bool probed = false;
                foreach (SessionInfo s in snap.Sessions)
                {
                    if (s.Pid == 0) continue;
                    string pinned = am.GetAppRoute(s.Pid, s.Flow);
                    Console.WriteLine("[ok] GetPersistedDefaultAudioEndpoint pid=" + s.Pid + " ("
                        + s.Name + ", " + (s.Flow == EDataFlow.eRender ? "render" : "capture") + "): "
                        + (pinned == null ? "follows default" : "pinned -> " + pinned));
                    probed = true;
                    break;
                }
                if (!probed) Console.WriteLine("[..] no app session to probe");
            }
            else failures++;

            Console.WriteLine(failures == 0 ? "SELFTEST PASS" : "SELFTEST FAIL (" + failures + ")");
            return failures == 0 ? 0 : 1;
        }
    }
}

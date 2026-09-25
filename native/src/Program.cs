using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundMaster
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
            {
                if (!AttachConsole(-1)) AllocConsole();   // winexe: attach parent console or open one
                Console.WriteLine();
                int rc = SelfTest.Run();
                // winexe means callers may not see the exit code — leave a verdict on disk too.
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"),
                        DateTime.Now + "  " + (rc == 0 ? "SELFTEST PASS" : "SELFTEST FAIL") + Environment.NewLine);
                }
                catch { }
                return rc;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // One flaky COM call must never take the app down with the default crash dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundmaster-errors.log"),
                        DateTime.Now + "  " + e.Exception + Environment.NewLine);
                }
                catch { }
            };
            Application.Run(new MainForm());
            return 0;
        }
    }
}

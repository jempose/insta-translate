using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>
    /// 真机取词联调：起一个记事本 → 强制切前台 → 全选 → 走真实的"模拟 Ctrl+C + 剪贴板"链路读回来。
    /// 这一步能在没有人工点击的情况下验证"跨进程取词"是否可用。
    /// 用法：InstaTranslate.exe --live-test   结果写到 %TEMP%\InstaTranslate\livetest.txt
    /// </summary>
    internal static class LiveTest
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int SW_RESTORE = 9;

        public static void Run()
        {
            var sb = new StringBuilder();
            int exit = 1;
            try
            {
                string text = "The quick brown fox jumps over the lazy dog " + DateTime.Now.Ticks % 100000;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string file = Path.Combine(baseDir, "livetest-data.txt");
                File.WriteAllText(file, text, Encoding.UTF8);

                sb.AppendLine("权限级别：" + Native.SelfIntegrity());
                sb.AppendLine("被测文本：" + text);

                var psi = new ProcessStartInfo("notepad.exe", "\"" + file + "\"");
                psi.UseShellExecute = true;
                using (var np = Process.Start(psi))
                {
                    IntPtr hwnd = IntPtr.Zero;
                    for (int i = 0; i < 40 && hwnd == IntPtr.Zero; i++)
                    {
                        Thread.Sleep(100);
                        np.Refresh();
                        hwnd = np.MainWindowHandle;
                    }
                    if (hwnd == IntPtr.Zero) { sb.AppendLine("✗ 记事本没起来"); return; }
                    sb.AppendLine("记事本窗口：" + hwnd);
                    Thread.Sleep(400);

                    bool front = false;
                    for (int i = 0; i < 6 && !front; i++)
                    {
                        ForceForeground(hwnd);
                        Thread.Sleep(350);
                        front = Native.Foreground() == hwnd;
                    }
                    sb.AppendLine("前台窗口是不是记事本：" + front);
                    if (!front)
                    {
                        sb.AppendLine("结论：· 跳过（当前前台被别的窗口占着，请关掉正在用的窗口后重试）");
                        try { np.Kill(); } catch { }
                        return;
                    }

                    // 全选（Ctrl+A）
                    SendCtrl(0x41);
                    Thread.Sleep(250);

                    var sw = Stopwatch.StartNew();
                    string got = Lang.Normalize(ClipboardCapture.Capture(1200));
                    sw.Stop();
                    sb.AppendLine("取词耗时：" + sw.ElapsedMilliseconds + "ms");
                    sb.AppendLine("取到：" + (string.IsNullOrEmpty(got) ? "(空)" : got));
                    sb.AppendLine("取词错误信息：" + (string.IsNullOrEmpty(ClipboardCapture.LastError) ? "(无)" : ClipboardCapture.LastError));

                    if (got == text) { sb.AppendLine("结论：✓ 跨进程取词可用"); exit = 0; }
                    else if (got.Length > 0) { sb.AppendLine("结论：△ 取到了内容但不完全一致（可能记事本有换行差异）"); exit = 0; }
                    else { sb.AppendLine("结论：✗ 跨进程取词失败"); }

                    try { np.Kill(); } catch { }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("异常：" + ex);
            }
            finally
            {
                try
                {
                    string outFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livetest-result.txt");
                    File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
                    Log.Write("LIVETEST " + sb.ToString().Replace(Environment.NewLine, " | "));
                }
                catch { }
                Environment.Exit(exit);
            }
        }

        private static void ForceForeground(IntPtr hwnd)
        {
            try
            {
                ShowWindow(hwnd, SW_RESTORE);
                uint pid;
                uint targetThread = GetWindowThreadProcessId(hwnd, out pid);
                uint self = GetCurrentThreadId();
                AttachThreadInput(self, targetThread, true);
                SetForegroundWindow(hwnd);
                Thread.Sleep(120);
                AttachThreadInput(self, targetThread, false);
            }
            catch (Exception ex)
            {
                Log.Write("切前台失败：" + ex.Message);
            }
        }

        private static void SendCtrl(byte vk)
        {
            Native.SendKeyCombo(0x11, vk);
        }
    }
}

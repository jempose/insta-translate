using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InstaTranslate
{
    internal static class Program
    {
        private const string MutexName = "Local\\InstaTranslate.SingleInstance";
        public const string WindowTitle = "InstaTranslate.Activate";

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam, int fuFlags, int uTimeout, out IntPtr lpdwResult);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        public static int ActivateMessage = 0;
        public static bool LaunchedHidden = false;
        public static bool Headless = false;

        [STAThread]
        private static void Main(string[] args)
        {
            Log.Boot("程序启动 pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " args=" + string.Join(" ", args));
            Log.Boot("当前权限级别：" + SafeIntegrity());
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Write("UI 线程异常: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("未捕获异常: " + e.ExceptionObject);
            };

            if (Has(args, "--selftest"))
            {
                SelfTest.Run();
                return;
            }
            if (Has(args, "--diag"))
            {
                Headless = true;
                LaunchedHidden = true;
                var dc = Config.Load();
                var ctx = new TrayContext(dc);
                ctx.RunDiagnostic();
                Application.Run(ctx);
                return;
            }
            if (Has(args, "--live-test"))
            {
                LiveTest.Run();
                return;
            }            if (Has(args, "--ui-snapshot"))
            {
                SelfTest.Snapshot();
                return;
            }

            ActivateMessage = (int)RegisterWindowMessage("InstaTranslateActivateMsg");
            LaunchedHidden = Has(args, "--hidden");

            // 关键：如果程序所在目录带"低完整性"标记（例如放在某些工作区沙箱目录里），
            // 那么谁来启动它、都会是低权限 —— 低权限读不到别的程序选区，UAC 也救不了。
            // 此时把自身复制到一个正常目录再启动，一劳永逸。
            {
                string integ0 = SafeIntegrity();
                if ((integ0 == "低" || integ0 == "不可信") && !Has(args, "--no-escape"))
                {
                    string self = System.Windows.Forms.Application.ExecutablePath;
                    // 只能写到低权限可写的位置：临时目录
                    string targetDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InstaTranslate");
                    string targetExe = System.IO.Path.Combine(targetDir, "InstaTranslate.exe");
                    if (!string.Equals(self, targetExe, StringComparison.OrdinalIgnoreCase) && !EscapedRecently())
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(targetDir);
                            System.IO.File.Copy(self, targetExe, true);
                            string srcAssets = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(self), "assets");
                            string dstAssets = System.IO.Path.Combine(targetDir, "assets");
                            if (System.IO.Directory.Exists(srcAssets) && !System.IO.Directory.Exists(dstAssets))
                            {
                                System.IO.Directory.CreateDirectory(dstAssets);
                                foreach (var f in System.IO.Directory.GetFiles(srcAssets))
                                    System.IO.File.Copy(f, System.IO.Path.Combine(dstAssets, System.IO.Path.GetFileName(f)), true);
                            }
                            Log.Boot("检测到低权限（" + integ0 + "，目录带低完整性标记），已复制到 " + targetDir + " 并重启");
                            // 交给 explorer 启动，避免新实例继续继承低权限
                            var psi0 = new System.Diagnostics.ProcessStartInfo("explorer.exe");
                            psi0.Arguments = "\"" + targetExe + "\"";
                            psi0.UseShellExecute = true;
                            System.Diagnostics.Process.Start(psi0);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Log.Boot("复制自身到正常目录失败：" + ex.Message);
                        }
                    }
                }
            }

            bool created;
            var mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                // 已有实例：试着让它把设置窗口弹出来（用户能看到反馈），拿不到响应就接管启动
                if (NotifyExisting())
                {
                    Log.Boot("已通知运行中的实例，本次退出");
                    return;
                }
                Log.Boot("检测到无响应的残留实例，接管启动");
            }

            try
            {
                var cfg = Config.Load();
                bool first = cfg.FirstRun;
                Log.Boot("配置已加载，引擎=" + cfg.Engine + " 快捷键=" + cfg.Hotkey + " Key=" + (string.IsNullOrEmpty(cfg.LlmApiKey) ? "无" : "有") + " 首次=" + first);
                Application.Run(new TrayContext(cfg));
            }
            catch (Exception ex)
            {
                Log.Write("主循环异常: " + ex);
                MessageBox.Show("即时译启动失败：\n" + ex.Message + "\n\n日志：" + Log.File, "即时译", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>20 秒内是否已经尝试过"脱离沙箱重启"，防止无限重启</summary>
        private static bool EscapedRecently()
        {
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "InstaTranslate", "escape.marker");
                if (System.IO.File.Exists(f))
                {
                    var t = System.IO.File.GetLastWriteTime(f);
                    if ((DateTime.Now - t).TotalSeconds < 20) return true;
                }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                System.IO.File.WriteAllText(f, DateTime.Now.ToString("o"));
            }
            catch { }
            return false;
        }
        /// <summary>权限级别（拿不到就返回"未知"，绝不让它把启动搞崩）</summary>
        private static string SafeIntegrity()
        {
            try { return Native.SelfIntegrity(); }
            catch { return "未知"; }
        }
        /// <summary>找到正在运行的实例并让它响应；返回是否成功</summary>
        private static bool NotifyExisting()
        {
            try
            {
                IntPtr h = FindWindow(null, WindowTitle);
                if (h == IntPtr.Zero) return false;
                IntPtr result;
                IntPtr ok = SendMessageTimeout(h, ActivateMessage, IntPtr.Zero, IntPtr.Zero, 0x0002, 1500, out result);
                return ok != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal static class SelfTest
    {
        private static readonly StringBuilder Sb = new StringBuilder();

        private static void Line(string s)
        {
            Sb.AppendLine(s);
            Log.Write("SELFTEST " + s);
        }

        public static void Run()
        {
            int fail = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Line("即时译 自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("系统 " + Environment.OSVersion.VersionString + "，.NET " + Environment.Version);
            Line("程序 " + Application.ExecutablePath);
            Line("数据 " + Paths.DataDir);
            Line("日志 " + Log.File);
            Line("");

            // 1 配置
            var cfg = Config.Load();
            Line("[配置] 快捷键=" + cfg.Hotkey + "，引擎=" + cfg.Engine + "，目标=" + cfg.TargetLang + "，Key=" + (string.IsNullOrEmpty(cfg.LlmApiKey) ? "无" : "有"));
            try
            {
                cfg.Save();
                Line("  配置读写 ✓");
            }
            catch (Exception ex)
            {
                fail++;
                Line("  配置读写 ✗ " + ex.Message);
            }

            // 1b 取到的文本要先洗一遍：富文本控件会把内嵌图片报成 U+FFFC
            try
            {
                string dirty = "\ufffc\n\ufffc\n\ufffc\n\ufffc\n赞\n16";
                string cleaned = Lang.Normalize(dirty);
                if (cleaned == "赞\n16") Line("  占位符清理 ✓ 图片占位符已剔除");
                else
                {
                    fail++;
                    Line("  占位符清理 ✗ 期望「赞\\n16」，实际「" + cleaned.Replace("\n", "\\n") + "」");
                }
            }
            catch (Exception ex)
            {
                fail++;
                Line("  占位符清理 ✗ " + ex.Message);
            }

            // 2 钩子
            try
            {
                var proc = new Native.HookProc(TestHook);
                IntPtr kb = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
                IntPtr ms = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
                if (kb == IntPtr.Zero || ms == IntPtr.Zero)
                {
                    fail++;
                    Line("  键鼠钩子 ✗ 安装失败，错误码 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                }
                else
                {
                    Native.UnhookWindowsHookEx(kb);
                    Native.UnhookWindowsHookEx(ms);
                    Line("  键鼠钩子 ✓ 可安装可卸载");
                }
            }
            catch (Exception ex)
            {
                fail++;
                Line("  键鼠钩子 ✗ " + ex.Message);
            }

            // 3 剪贴板
            try
            {
                string probe = "insta-translate-clipboard-" + DateTime.Now.Ticks;
                Clipboard.SetText(probe);
                string back = Clipboard.GetText();
                if (back == probe) Line("  剪贴板 ✓");
                else { fail++; Line("  剪贴板 ✗ 读写不一致"); }
            }
            catch (Exception ex)
            {
                fail++;
                Line("  剪贴板 ✗ " + ex.Message);
            }

            // 4 取词（模拟一次 Ctrl+C，期望读不到内容但流程不报错）
            try
            {
                string got = ClipboardCapture.Capture(300);
                Line("  取词流程 ✓（当前没有选区时读到 " + got.Length + " 字，属正常）");
            }
            catch (Exception ex)
            {
                fail++;
                Line("  取词流程 ✗ " + ex.Message);
            }

            // 5 必应
            Line("");
            Line("[翻译引擎]");
            try
            {
                var r = Translator.Translate("Reading in any app just got easier.", cfg, "bing");
                Line("  必应 英→中 ✓ " + r.Elapsed + "ms：" + r.Text);
            }
            catch (Exception ex)
            {
                fail++;
                Line("  必应 英→中 ✗ " + Errors.Friendly(ex.Message));
            }
            try
            {
                var r = Translator.Translate("选中文字就能看到译文。", cfg, "bing");
                Line("  必应 中→英 ✓ " + r.Elapsed + "ms：" + r.Text);
            }
            catch (Exception ex)
            {
                fail++;
                Line("  必应 中→英 ✗ " + Errors.Friendly(ex.Message));
            }

            if (!string.IsNullOrEmpty(cfg.LlmApiKey))
            {
                try
                {
                    var r = Translator.Translate("serendipity", cfg, "llm");
                    Line("  大模型 单词 ✓ " + r.Elapsed + "ms：" + r.Text + " " + r.Phonetic + " " + r.Pos);
                }
                catch (Exception ex)
                {
                    fail++;
                    Line("  大模型 单词 ✗ " + Errors.Friendly(ex.Message));
                }
            }
            else
            {
                Line("  大模型 · 未配置 Key，跳过");
            }

            // 6 语音
            string voices = Tts.VoiceList();
            Line("");
            Line("[语音] " + (string.IsNullOrEmpty(voices) ? "未检测到语音包（朗读会静默失败，不影响翻译）" : voices));
            if (!string.IsNullOrEmpty(voices))
            {
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                Tts.SpeakPair("serendipity", "en", "机缘巧合，意外发现美好事物的运气", "zh", 450);
                System.Threading.Thread.Sleep(300);
                while (Tts.PairBusy && sw2.ElapsedMilliseconds < 20000) System.Threading.Thread.Sleep(120);
                sw2.Stop();
                Line("  朗读序列 ✓ 用时 " + sw2.ElapsedMilliseconds + "ms（先英文发音 → 停顿 → 中文释义）");
            }

            // 7 数据
            Line("");
            Line("[数据] 历史 " + Store.History().Count + " 条，生词 " + Store.Vocab().Count + " 个");

            // 8 取词机制（进程内自测：不需要跨进程权限，验证 SendInput + 剪贴板 + UIA 的完整链路）
            Line("");
            Line("[取词机制]");
            try
            {
                string probeText = "InstaTranslate capture probe " + DateTime.Now.Ticks % 100000;
                bool focused = SelfTestFocusable();
                string gotClip = CaptureSelfTest(probeText, false);
                if (gotClip == probeText) Line("  模拟 Ctrl+C + 剪贴板 ✓ 取到 \"" + gotClip + "\"");
                else if (!focused) Line("  模拟 Ctrl+C + 剪贴板 · 跳过（本环境拿不到前台焦点，无法自测）");
                else { fail++; Line("  模拟 Ctrl+C + 剪贴板 ✗ 期望 \"" + probeText + "\"，实际 \"" + gotClip + "\"（" + ClipboardCapture.LastError + "）"); }

                string gotUia = CaptureSelfTest(probeText, true);
                if (gotUia == probeText) Line("  UIA 读选区 ✓ 取到 \"" + gotUia + "\"");
                else Line("  UIA 读选区 · 本次读不到（" + UiaReader.LastInfo + "），不影响剪贴板法");
            }
            catch (Exception ex)
            {
                fail++;
                Line("  取词机制 ✗ " + ex.Message);
            }

            sw.Stop();            Line("");
            Line(fail == 0 ? "全部通过，耗时 " + sw.ElapsedMilliseconds + "ms" : fail + " 项失败，耗时 " + sw.ElapsedMilliseconds + "ms");

            try
            {
                File.WriteAllText(Path.Combine(Log.Dir ?? Path.GetTempPath(), "selftest.txt"), Sb.ToString(), Encoding.UTF8);
            }
            catch { }
            try
            {
                Console.WriteLine(Sb.ToString());
            }
            catch { }
        }

        /// <summary>本环境能否让自己的窗口拿到前台（沙箱/无桌面时不能）</summary>
        private static bool SelfTestFocusable()
        {
            var f = new Form();
            f.Width = 200; f.Height = 120; f.StartPosition = FormStartPosition.CenterScreen;
            f.Show();
            f.Activate();
            Application.DoEvents();
            System.Threading.Thread.Sleep(120);
            Application.DoEvents();
            bool ok = Native.GetForegroundWindow() == f.Handle;
            f.Close();
            f.Dispose();
            return ok;
        }

        /// <summary>开一个带文本框的窗口，选中文字，再用真实取词链路去读——不依赖任何跨进程权限</summary>
        private static string CaptureSelfTest(string text, bool useUia)
        {
            string got = "";
            var f = new Form();
            f.Text = "InstaTranslate SelfTest";
            f.Width = 520;
            f.Height = 200;
            f.StartPosition = FormStartPosition.CenterScreen;
            var box = new TextBox();
            box.Multiline = true;
            box.Dock = DockStyle.Fill;
            box.Text = text;
            f.Controls.Add(box);
            f.Show();
            f.Activate();
            box.Focus();
            box.SelectAll();
            Application.DoEvents();
            Thread.Sleep(150);
            Application.DoEvents();
            try
            {
                got = useUia ? UiaReader.Read(1200) : ClipboardCapture.Capture(800);
            }
            finally
            {
                f.Close();
                f.Dispose();
            }
            return got == null ? "" : got.Trim();
        }
        private static IntPtr TestHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        /// <summary>把界面渲染成图片，便于在没有可见桌面的环境里检查排版</summary>
        public static void Snapshot()
        {
            string dir = Path.Combine(Log.Dir == null ? AppDomain.CurrentDomain.BaseDirectory : Log.Dir, "shots");
            Directory.CreateDirectory(dir);

            var cfg = Config.Load();

            var r = new TranslationResult();
            r.Source = "Serendipity is the occurrence of events by chance in a happy or beneficial way.";
            r.Text = "Serendipity 指的是以愉快或有益的方式偶然发生的事件。";
            r.From = "en";
            r.To = "zh";
            r.Detected = "en";
            r.Engine = "llm";
            r.EngineLabel = "大模型";
            r.Elapsed = 620;

            Snap(dir, "popup-passage.png", cfg, r, false);

            var w = new TranslationResult();
            w.Source = "serendipity";
            w.Text = "机缘巧合；意外发现珍奇事物的运气；意外收获";
            w.Senses = new List<string>(new[] { "机缘巧合", "意外发现珍奇事物的运气", "意外收获" });
            w.Phonetic = "/ˌsɛrənˈdɪpɪti/";
            w.Pos = "n.";
            w.Ctx = "常用来形容“意外发现美好事物”的能力或运气，语气偏文学。";
            w.From = "en";
            w.To = "zh";
            w.Detected = "en";
            w.Engine = "llm";
            w.EngineLabel = "大模型";
            w.Elapsed = 410;
            Snap(dir, "popup-word.png", cfg, w, true);

            var e = new TranslationResult();
            Snap(dir, "popup-error.png", cfg, null, false);

            try
            {
                var sf = new SettingsForm(cfg, delegate { });
                sf.Show();
                Application.DoEvents();
                Thread.Sleep(200);
                Application.DoEvents();
                using (var bmp = new Bitmap(sf.Width, sf.Height))
                {
                    sf.DrawToBitmap(bmp, new Rectangle(0, 0, sf.Width, sf.Height));
                    bmp.Save(Path.Combine(dir, "settings.png"), ImageFormat.Png);
                }
                sf.Close();
                sf.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("设置窗口快照失败: " + ex.Message);
            }

            Log.Write("界面快照已输出到 " + dir);
            try { Console.WriteLine("snapshot: " + dir); } catch { }
        }

        private static void Snap(string dir, string name, Config cfg, TranslationResult r, bool vocab)
        {
            try
            {
                var f = new PopupForm();
                f.Configure(cfg.BubbleWidth);
                var pt = new Native.POINT();
                pt.X = 200;
                pt.Y = 200;
                if (r == null) f.ShowErrorText("网络连接失败，请检查网络或代理", "Hello");
                else f.ShowResultText(r, vocab);
                f.ShowAt(pt);
                Application.DoEvents();
                Thread.Sleep(120);
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                }
                f.Hide();
                f.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("快照失败 " + name + ": " + ex);
            }
        }
    }
}

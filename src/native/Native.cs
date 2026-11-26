using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>Win32 互操作：低级键鼠钩子、模拟按键、剪贴板、窗口/屏幕</summary>
    public static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // 注意：INPUT 必须和 Win32 完全一致（x64 下 40 字节）。
        // 早先写成 explicit { int type; KEYBDINPUT ki; } 只有 32 字节，
        // SendInput 会返回 0 / ERROR_INVALID_PARAMETER，一个按键都发不出去。
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static POINT CursorPos()
        {
            POINT p;
            GetCursorPos(out p);
            return p;
        }

        public static string ClassNameAt(POINT p)
        {
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            GetClassNameW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static int WindowPidAt(POINT p)
        {
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return (int)pid;
        }

        public static string TitleAt(POINT p)
        {
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ForegroundClass()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            GetClassNameW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static IntPtr Foreground()
        {
            return GetForegroundWindow();
        }

        // ---- 进程完整性级别：判断"为什么模拟按键被静默丢弃" ----
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr hProcess, uint desiredAccess, out IntPtr tokenHandle);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr tokenHandle, int infoClass, IntPtr info, int infoLen, out int returnLen);
        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")]
        static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int pid);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        private const uint TOKEN_QUERY = 0x0008;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>返回 "系统/高/中/低/未知"</summary>
        public static string IntegrityLevel(int pid)
        {
            IntPtr proc = IntPtr.Zero;
            IntPtr token = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                proc = pid <= 0 ? GetCurrentProcess() : OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (proc == IntPtr.Zero) return "未知(打不开进程)";
                if (!OpenProcessToken(proc, TOKEN_QUERY, out token)) return "未知(打不开令牌)";

                int len = 0;
                GetTokenInformation(token, 25 /*TokenIntegrityLevel*/, IntPtr.Zero, 0, out len);
                if (len <= 0) return "未知(长度)";
                buf = Marshal.AllocHGlobal(len);
                int got;
                if (!GetTokenInformation(token, 25, buf, len, out got)) return "未知(读令牌)";

                IntPtr sid = Marshal.ReadIntPtr(buf); // TOKEN_MANDATORY_LABEL.Label.Sid
                if (sid == IntPtr.Zero) return "未知(空 SID)";
                int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count <= 0) return "未知(空 SID 计数)";
                int rid = Marshal.ReadInt32(GetSidSubAuthority(sid, count - 1));
                switch (rid)
                {
                    case 0x0000: return "不可信";
                    case 0x1000: return "低";
                    case 0x2000: return "中";
                    case 0x3000: return "高(管理员)";
                    case 0x4000: return "系统";
                    default: return "0x" + rid.ToString("X");
                }
            }
            catch (Exception ex)
            {
                return "未知(" + ex.Message + ")";
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (token != IntPtr.Zero) CloseHandle(token);
                if (pid > 0 && proc != IntPtr.Zero) CloseHandle(proc);
            }
        }

        public static string SelfIntegrity()
        {
            return IntegrityLevel(0);
        }

        public static int ForegroundPid()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return (int)pid;
        }
        public static string ForegroundTitle()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "(无)";
            var sb = new StringBuilder(256);
            GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.Length == 0)
            {
                var c = new StringBuilder(256);
                GetClassNameW(h, c, c.Capacity);
                t = c.ToString();
            }
            return t;
        }

        /// <summary>等用户把修饰键松开：否则热键残留的 Alt 会让 Ctrl+C 变成 Alt+Ctrl+C</summary>
        public static string WaitModifiersReleased(int timeoutMs)
        {
            var names = new string[] { "Shift", "Ctrl", "Alt", "Win" };
            int[] vks = new int[] { 0x10, 0x11, 0x12, 0x5B };
            var down = new System.Collections.Generic.List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                down.Clear();
                for (int i = 0; i < vks.Length; i++)
                {
                    if ((GetAsyncKeyState(vks[i]) & 0x8000) != 0) down.Add(names[i]);
                }
                if ((GetAsyncKeyState(0x5C) & 0x8000) != 0) down.Add("WinR");
                if (down.Count == 0) return "";
                Thread.Sleep(15);
            }
            return "仍有按键未松开：" + string.Join("+", down.ToArray());
        }

        /// <summary>发送 Ctrl + 某键（例如 Ctrl+A 全选）</summary>
        public static bool SendKeyCombo(byte modifierVk, byte vk)
        {
            INPUT[] inputs = new INPUT[4];
            for (int i = 0; i < 4; i++) { inputs[i] = new INPUT(); inputs[i].type = 1; }
            inputs[0].u.ki.wVk = modifierVk;
            inputs[1].u.ki.wVk = vk;
            inputs[2].u.ki.wVk = vk;
            inputs[2].u.ki.dwFlags = 0x0002;
            inputs[3].u.ki.wVk = modifierVk;
            inputs[3].u.ki.dwFlags = 0x0002;
            uint sent = SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == 4;
        }

        public static string LastSendError = "";

        public static bool SendCtrlC()
        {
            INPUT[] inputs = new INPUT[4];
            for (int i = 0; i < 4; i++)
            {
                inputs[i] = new INPUT();
                inputs[i].type = 1; // INPUT_KEYBOARD
            }
            inputs[0].u.ki.wVk = 0x11; // Ctrl down
            inputs[1].u.ki.wVk = 0x43; // C down
            inputs[2].u.ki.wVk = 0x43; // C up
            inputs[2].u.ki.dwFlags = 0x0002;
            inputs[3].u.ki.wVk = 0x11; // Ctrl up
            inputs[3].u.ki.dwFlags = 0x0002;
            inputs[0].u.ki.time = 0;

            uint sent = SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != 4)
            {
                int err = Marshal.GetLastWin32Error();
                LastSendError = "SendInput 返回 " + sent + "，错误码 " + err;
                // 退路：老接口，参数简单，几乎总能工作
                try
                {
                    keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    keybd_event(0x43, 0, 0, UIntPtr.Zero);
                    keybd_event(0x43, 0, 2, UIntPtr.Zero);
                    keybd_event(0x11, 0, 2, UIntPtr.Zero);
                    LastSendError += "；已改用 keybd_event 兜底";
                }
                catch (Exception ex)
                {
                    LastSendError += "；keybd_event 也失败：" + ex.Message;
                }
                return false;
            }
            LastSendError = "";
            return true;
        }

        /// <summary>屏幕工作区（不含任务栏），坐标按 96 DPI 逻辑像素</summary>
        public static RECT WorkAreaFor(POINT p)
        {
            Screen s = Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
            var r = s.WorkingArea;
            RECT o;
            o.Left = r.Left;
            o.Top = r.Top;
            o.Right = r.Right;
            o.Bottom = r.Bottom;
            return o;
        }
    }

    /// <summary>把剪贴板当临时通道：备份 → 清空 → Ctrl+C → 读取 → 还原</summary>
    internal static class ClipboardCapture
    {
        public static string LastError = "";

        /// <summary>日志里只显示前 30 个字符，避免刷屏</summary>
        private static string Snippet(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length <= 30 ? t : t.Substring(0, 30) + "…";
        }
        public static string Capture(int timeoutMs)
        {
            LastError = "";
            // 先试不需要按键的 UIA；不行再走剪贴板
            string uia = UiaReader.Read(3000);
            Log.Write("UIA 结果：" + (string.IsNullOrEmpty(uia) ? "无" : uia.Length + " 字 \"" + Snippet(uia) + "\"") + "｜" + UiaReader.LastInfo);

            // 只有 1~2 个字符时很可能是"光标处的退化选区"而不是真的选中，
            // 这种先当候选，让剪贴板法再确认一次；剪贴板也失败才退回它。
            string uiaWeak = null;
            if (!string.IsNullOrEmpty(uia) && Lang.LooksTranslatable(uia))
            {
                if (uia.Trim().Length >= 2)
                {
                    LastError = "UIA 取到";
                    return uia;
                }
                uiaWeak = uia;
                Log.Write("UIA 只读到 " + uia.Trim().Length + " 个字符，疑似光标位置，改用剪贴板法确认");
            }

            string backup = "";
            bool hadText = false;
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    backup = System.Windows.Forms.Clipboard.GetText();
                    hadText = true;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            try
            {
                System.Windows.Forms.Clipboard.Clear();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            string stuck = Native.WaitModifiersReleased(900);
            if (!string.IsNullOrEmpty(stuck)) LastError = stuck;
            Thread.Sleep(25);
            bool sent = Native.SendCtrlC();
            LastError = sent ? "" : Native.LastSendError;

            string picked = "";
            int waited = 0;
            int limit = timeoutMs <= 0 ? 400 : timeoutMs;
            while (waited < limit)
            {
                Thread.Sleep(20);
                waited += 20;
                try
                {
                    if (System.Windows.Forms.Clipboard.ContainsText())
                    {
                        picked = System.Windows.Forms.Clipboard.GetText();
                        if (!string.IsNullOrEmpty(picked)) break;
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }

            if (string.IsNullOrEmpty(picked) && Native.SendCtrlC())
            {
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(20);
                    try
                    {
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            picked = System.Windows.Forms.Clipboard.GetText();
                            if (!string.IsNullOrEmpty(picked)) break;
                        }
                    }
                    catch { }
                }
            }

            // 还原
            try
            {
                if (hadText && !string.IsNullOrEmpty(backup)) System.Windows.Forms.Clipboard.SetText(backup);
                else System.Windows.Forms.Clipboard.Clear();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            if (picked.Length > 0)
            {
                LastError = "剪贴板法成功";
                return picked.Trim();
            }

            // 剪贴板也没读到，才退回 UIA 的短结果
            if (uiaWeak != null)
            {
                LastError = "UIA 取到（短）";
                return uiaWeak;
            }
            return "";
        }

        public static void WriteText(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetText(text ?? "");
                    return;
                }
                catch
                {
                    Thread.Sleep(40);
                }
            }
        }
    }

    /// <summary>系统语音朗读</summary>
    internal static class Tts
    {
        private static System.Speech.Synthesis.SpeechSynthesizer _synth;
        private static readonly object Lock = new object();
        private static DateTime _lastUse = DateTime.MinValue;

        private static System.Speech.Synthesis.SpeechSynthesizer Synth()
        {
            lock (Lock)
            {
                if (_synth == null)
                {
                    _synth = new System.Speech.Synthesis.SpeechSynthesizer();
                    _synth.Rate = 0;
                }
                return _synth;
            }
        }

        public static void Speak(string text, string lang)
        {
            if (string.IsNullOrEmpty(text)) return;
            string t = text.Length > 600 ? text.Substring(0, 600) : text;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var s = Synth();
                    lock (Lock)
                    {
                        try { s.SpeakAsyncCancelAll(); } catch { }
                        try { SelectVoice(s, MapLang(lang)); } catch { }
                        try { s.SpeakAsync(t); } catch { }
                        _lastUse = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("TTS 失败: " + ex.Message);
                }
            });
        }

        private static string MapLang(string lang)
        {
            if (lang == "zh") return "zh";
            if (lang == "ja") return "ja";
            if (lang == "ko") return "ko";
            if (lang == "fr") return "fr";
            if (lang == "de") return "de";
            if (lang == "es") return "es";
            if (lang == "ru") return "ru";
            return "en";
        }

        /// <summary>
        /// 连续朗读两段：先读原文（例如单词发音），停顿后再读译文。
        /// 两段可以选不同语言的嗓音（英语词用英文嗓子，释义用中文嗓子）。
        /// </summary>
        public static void SpeakPair(string first, string firstLang, string second, string secondLang, int pauseMs)
        {
            string a = Trim(first, 400);
            string b = Trim(second, 600);
            if (a.Length == 0 && b.Length == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var s = Synth();
                    lock (Lock)
                    {
                        try { s.SpeakAsyncCancelAll(); } catch { }
                        try { SelectVoice(s, MapLang(firstLang)); } catch { }
                        if (a.Length == 0)
                        {
                            try { SelectVoice(s, MapLang(secondLang)); } catch { }
                            try { s.SpeakAsync(b); } catch { }
                            return;
                        }
                        if (b.Length == 0)
                        {
                            try { s.SpeakAsync(a); } catch { }
                            return;
                        }
                        // 读完第一段再读第二段：用 SpeakCompleted 串起来，避免靠定时器猜时长
                        EventHandler<System.Speech.Synthesis.SpeakCompletedEventArgs> onDone = null;
                        onDone = delegate(object sender, System.Speech.Synthesis.SpeakCompletedEventArgs e)
                        {
                            try { s.SpeakCompleted -= onDone; } catch { }
                            try
                            {
                                Thread.Sleep(pauseMs <= 0 ? 420 : pauseMs);
                                lock (Lock) { SelectVoice(s, MapLang(secondLang)); }
                                s.SpeakAsync(b);
                            }
                            catch { }
                        };
                        s.SpeakCompleted += onDone;
                        try { s.SpeakAsync(a); } catch { }
                        _lastUse = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("TTS 失败: " + ex.Message);
                }
            });
        }

        /// <summary>停止朗读</summary>
        public static void Stop()
        {
            try
            {
                lock (Lock)
                {
                    var s = Synth();
                    s.SpeakAsyncCancelAll();
                }
            }
            catch { }
        }

        public static bool IsSpeaking()
        {
            try
            {
                lock (Lock) { return _synth != null && _synth.State == System.Speech.Synthesis.SynthesizerState.Speaking; }
            }
            catch { return false; }
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }

        private static void SelectVoice(System.Speech.Synthesis.SpeechSynthesizer s, string lang)
        {
            foreach (var v in s.GetInstalledVoices())
            {
                if (!v.Enabled) continue;
                string c = v.VoiceInfo.Culture.ThreeLetterWindowsLanguageName.ToLowerInvariant();
                string n = v.VoiceInfo.Name.ToLowerInvariant();
                bool match = false;
                if (lang == "zh") match = c.StartsWith("chs") || c.StartsWith("cht") || n.Contains("chinese") || n.Contains("huihui") || n.Contains("xiaoxiao") || n.Contains("kangkang");
                else if (lang == "en") match = c.StartsWith("eng") || n.Contains("english") || n.Contains("zira") || n.Contains("david");
                else if (lang == "ja") match = c.StartsWith("jpn") || n.Contains("japanese") || n.Contains("haruka");
                else if (lang == "ko") match = c.StartsWith("kor") || n.Contains("korean");
                else if (lang == "fr") match = c.StartsWith("fra") || n.Contains("french");
                else if (lang == "de") match = c.StartsWith("deu") || n.Contains("german");
                else if (lang == "es") match = c.StartsWith("spa") || n.Contains("spanish");
                else if (lang == "ru") match = c.StartsWith("rus") || n.Contains("russian");
                if (match)
                {
                    s.SelectVoice(v.VoiceInfo.Name);
                    return;
                }
            }
        }

        public static string VoiceList()
        {
            var sb = new StringBuilder();
            try
            {
                var s = Synth();
                foreach (var v in s.GetInstalledVoices())
                {
                    if (!v.Enabled) continue;
                    sb.Append(v.VoiceInfo.Name).Append(" (").Append(v.VoiceInfo.Culture.Name).Append("); ");
                }
            }
            catch (Exception ex)
            {
                return "读取语音失败: " + ex.Message;
            }
            return sb.ToString();
        }
    }
}

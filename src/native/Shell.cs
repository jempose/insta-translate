using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InstaTranslate
{
    internal static class HotkeyUtil
    {
        /// <summary>把 "Ctrl+Alt+T" 解析成修饰键与主键</summary>
        public static bool Parse(string combo, out Keys mods, out Keys key)
        {
            mods = Keys.None;
            key = Keys.None;
            if (string.IsNullOrEmpty(combo)) return false;
            string[] parts = combo.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (string.Equals(p, "Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= Keys.Control;
                else if (string.Equals(p, "Alt", StringComparison.OrdinalIgnoreCase)) mods |= Keys.Alt;
                else if (string.Equals(p, "Shift", StringComparison.OrdinalIgnoreCase)) mods |= Keys.Shift;
                else if (string.Equals(p, "Win", StringComparison.OrdinalIgnoreCase)) mods |= Keys.LWin;
                else key = NameToKey(p);
            }
            return key != Keys.None;
        }

        public static Keys NameToKey(string name)
        {
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') return (Keys)(int)c;
                if (c >= '0' && c <= '9') return (Keys)(int)c;
                switch (name[0])
                {
                    case '-': return Keys.OemMinus;
                    case '=': return Keys.Oemplus;
                    case '[': return Keys.OemOpenBrackets;
                    case ']': return Keys.OemCloseBrackets;
                    case ';': return Keys.OemSemicolon;
                    case '\'': return Keys.OemQuotes;
                    case '`': return Keys.Oemtilde;
                    case '\\': return Keys.OemPipe;
                    case ',': return Keys.Oemcomma;
                    case '.': return Keys.OemPeriod;
                    case '/': return Keys.OemQuestion;
                }
            }
            if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(name.Substring(1), out n) && n >= 1 && n <= 12) return (Keys)((int)Keys.F1 + (n - 1));
            }
            switch (name.ToLowerInvariant())
            {
                case "space": return Keys.Space;
                case "enter": return Keys.Enter;
                case "tab": return Keys.Tab;
                case "esc":
                case "escape": return Keys.Escape;
                case "home": return Keys.Home;
                case "end": return Keys.End;
                case "pageup": return Keys.PageUp;
                case "pagedown": return Keys.PageDown;
                case "insert": return Keys.Insert;
                case "delete": return Keys.Delete;
                case "up": return Keys.Up;
                case "down": return Keys.Down;
                case "left": return Keys.Left;
                case "right": return Keys.Right;
            }
            return Keys.None;
        }

        public static string KeyName(Keys k)
        {
            int v = (int)k;
            if (v >= 65 && v <= 90) return ((char)v).ToString();
            if (v >= 48 && v <= 57) return ((char)v).ToString();
            if (k >= Keys.F1 && k <= Keys.F12) return "F" + (v - (int)Keys.F1 + 1);
            switch (k)
            {
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemtilde: return "`";
                case Keys.OemPipe: return "\\";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Space: return "Space";
                case Keys.Enter: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Escape: return "Esc";
            }
            return k.ToString();
        }
    }

    /// <summary>
    /// 开机自启：本程序要求管理员权限，用注册表 Run 键会在每次登录时弹 UAC，
    /// 所以改用"登录时运行 + 最高权限"的计划任务，静默启动。
    /// </summary>
    internal static class AutoStart
    {
        private const string TaskName = "InstaTranslate";

        private static string Run(string args, int timeoutMs)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null) return "";
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(timeoutMs <= 0 ? 8000 : timeoutMs);
                    return output;
                }
            }
            catch (Exception ex)
            {
                Log.Write("schtasks 调用失败: " + ex.Message);
                return "";
            }
        }

        public static bool IsEnabled()
        {
            string output = Run("/query /tn " + TaskName, 5000);
            return output.IndexOf(TaskName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Apply(bool on)
        {
            if (on)
            {
                string tr = "\"" + Application.ExecutablePath + "\" --hidden";
                Run("/create /tn " + TaskName + " /tr " + tr + " /sc onlogon /rl HIGHEST /f", 10000);
                Log.Write("已创建开机自启计划任务：" + TaskName);
            }
            else
            {
                Run("/delete /tn " + TaskName + " /f", 8000);
                Log.Write("已删除开机自启计划任务：" + TaskName);
            }
        }
    }

    internal static class AppIcon
    {
        private static Icon _icon;

        public static Icon Get()
        {
            if (_icon != null) return _icon;
            try
            {
                string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
                if (File.Exists(ico))
                {
                    _icon = new Icon(ico);
                    return _icon;
                }
                // 内嵌兜底：从当前 exe 取
                _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (_icon != null) return _icon;
            }
            catch { }
            _icon = SystemIcons.Application;
            return _icon;
        }
    }

    internal static class AppVersion
    {
        public static string Value
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.0"; }
            }
        }
    }
}

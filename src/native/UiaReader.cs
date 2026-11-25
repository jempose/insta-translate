using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace InstaTranslate
{
    /// <summary>
    /// 用 UI Automation 直接读"前台窗口里选中的文字"，不需要模拟按键。
    /// 浏览器（Chrome/Edge）、Office、WPF、部分 Electron 应用支持；
    /// 读不到就返回 null，由剪贴板法兜底。
    /// </summary>
    internal static class UiaReader
    {
        public static string LastInfo = "";

        /// <summary>预热：只查一次焦点元素，触发 Chromium 打开无障碍树</summary>
        public static void WarmUp()
        {
            try
            {
                var t = new Thread(delegate()
                {
                    try { var el = AutomationElement.FocusedElement; if (el != null) { var n = el.Current.Name; } }
                    catch { }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
            }
            catch { }
        }

        public static string Read(int timeoutMs)
        {
            string result = null;
            var t = new Thread(delegate()
            {
                try { result = ReadCore(); }
                catch (Exception ex) { LastInfo = "UIA 异常：" + ex.Message; result = null; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(timeoutMs <= 0 ? 1500 : timeoutMs))
            {
                LastInfo = "UIA 超时";
                return null;
            }
            return result;
        }

        private static string ReadCore()
        {
            // Chromium / Electron 的辅助功能树是"按需打开"的：第一次查询往往只有一个空壳，
            // 索引建好要几百毫秒，所以这里重试几次。
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string hit = TryOnce();
                if (!string.IsNullOrEmpty(hit))
                {
                    if (attempt > 0) LastInfo += "（第 " + (attempt + 1) + " 次尝试）";
                    return hit;
                }
                Thread.Sleep(250);
            }
            return null;
        }

        private static string TryOnce()
        {
            // 1) 先看焦点元素
            AutomationElement focused = AutomationElement.FocusedElement;
            string text = focused == null ? null : ReadElement(focused);
            if (!string.IsNullOrEmpty(text)) { LastInfo = "UIA 焦点元素"; return text; }

            // 2) 从前台窗口往下找 Document / Edit
            IntPtr fg = Native.Foreground();
            if (fg == IntPtr.Zero) { LastInfo = "UIA：没有前台窗口"; return null; }
            AutomationElement root = null;
            try { root = AutomationElement.FromHandle(fg); } catch { }
            if (root == null) { LastInfo = "UIA：取窗口元素失败"; return null; }

            text = ReadElement(root);
            if (!string.IsNullOrEmpty(text)) { LastInfo = "UIA 窗口根元素"; return text; }

            text = FindInSubtree(root, ControlType.Document);
            if (!string.IsNullOrEmpty(text)) { LastInfo = "UIA Document"; return text; }

            text = FindInSubtree(root, ControlType.Edit);
            if (!string.IsNullOrEmpty(text)) { LastInfo = "UIA Edit"; return text; }

            LastInfo = "UIA：没有可读选区（" + SafeName(root) + "）";
            return null;
        }

        private static string SafeName(AutomationElement e)
        {
            try { return e.Current.ControlType.ProgrammaticName + "/" + e.Current.Name; }
            catch { return "?"; }
        }

        private static string FindInSubtree(AutomationElement root, ControlType type)
        {
            AutomationElementCollection all;
            try
            {
                all = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            }
            catch
            {
                return null;
            }
            if (all == null) return null;
            int n = Math.Min(all.Count, 20);
            for (int i = 0; i < n; i++)
            {
                string text = ReadElement(all[i]);
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static string ReadElement(AutomationElement el)
        {
            object p;
            try
            {
                if (!el.TryGetCurrentPattern(TextPattern.Pattern, out p)) return null;
            }
            catch { return null; }
            try
            {
                var tp = (TextPattern)p;
                var ranges = tp.GetSelection();
                if (ranges == null || ranges.Length == 0) return null;
                var sb = new StringBuilder();
                foreach (var r in ranges)
                {
                    string s = r.GetText(8192);
                    if (!string.IsNullOrEmpty(s)) sb.Append(s);
                }
                string result = sb.ToString().Trim();
                return result.Length == 0 ? null : result;
            }
            catch
            {
                return null;
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>隐藏的顶层窗口：接收"第二次双击"通知与全局热键（WM_HOTKEY）</summary>
    public class MessageWindow : NativeWindow
    {
        private int _msg;
        private Action _onActivate;
        private Action _onHotkey;

        public void Create(string title, int message, Action onActivate, Action onHotkey)
        {
            _msg = message;
            _onActivate = onActivate;
            _onHotkey = onHotkey;
            var cp = new CreateParams();
            cp.Caption = title;
            cp.X = -32000;
            cp.Y = -32000;
            cp.Width = 1;
            cp.Height = 1;
            cp.Style = 0; // 无边框、不可见
            cp.ExStyle = 0x00000080; // WS_EX_TOOLWINDOW
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY
            {
                try { if (_onHotkey != null) _onHotkey(); }
                catch (Exception ex) { Log.Write("热键处理失败: " + ex.Message); }
                return;
            }
            if (_msg != 0 && m.Msg == _msg)
            {
                try { if (_onActivate != null) _onActivate(); }
                catch (Exception ex) { Log.Write("激活消息处理失败: " + ex.Message); }
                return;
            }
            base.WndProc(ref m);
        }

        public void Destroy()
        {
            try { DestroyHandle(); } catch { }
        }
    }
}

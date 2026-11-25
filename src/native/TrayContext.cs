using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>托盘主循环：钩子 → 取词 → 翻译 → 气泡</summary>
    public class TrayContext : ApplicationContext
    {
        private readonly Config _cfg;
        private readonly PopupForm _popup;
        private readonly NotifyIcon _tray;
        private readonly SynchronizationContext _ui;

        private System.Windows.Forms.Timer _poll;
        private bool _btnDown;
        private System.Windows.Forms.Timer _dragTimer;
        private System.Windows.Forms.Timer _hideTimer;
        private System.Windows.Forms.Timer _showSettingsTimer;
        private Native.POINT _pendingPt;
        private System.Drawing.Rectangle? _pendingRect;

        private Native.POINT _dragStart;
        private DateTime _dragStartAt = DateTime.MinValue;
        private bool _dragging;
        private System.Drawing.Rectangle _selRect = System.Drawing.Rectangle.Empty;
        private DateTime _selAt = DateTime.MinValue;

        private SettingsForm _settings;
        private bool _busy;
        private MessageWindow _msgWindow;

        public TrayContext(Config cfg)
        {
            _cfg = cfg;
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _popup = new PopupForm();
            _popup.Configure(cfg.BubbleWidth);
            WirePopup();

            var menu = BuildMenu();
            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.Get();
            _tray.Text = "即时译 · " + cfg.Hotkey + " 选中即译";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowSettings(); };

            // 隐藏窗口：接收"又来点了一次"的通知，把设置窗口弹出来
            _msgWindow = new MessageWindow();
            _msgWindow.Create(Program.WindowTitle, Program.ActivateMessage, delegate { ShowSettings(); ShowRunningBalloon(); }, OnHotkeyPressed);

            ReloadHotkey();
            // 预热辅助功能：Chromium 只有在有 UIA 客户端查询时才会打开无障碍树，
            // 提前摸一下可以让用户按快捷键那一刻就有树可读。
            var warm = new System.Windows.Forms.Timer();
            warm.Interval = 5000;
            warm.Tick += delegate { UiaReader.WarmUp(); };
            warm.Start();
            _hideTimer = new System.Windows.Forms.Timer();
            _hideTimer.Interval = 6000;
            _hideTimer.Tick += delegate
            {
                if (!_popup.Visible || _popup.Pinned) { _hideTimer.Stop(); return; }
                Native.POINT cp = Native.CursorPos();
                if (_popup.ContainsScreenPoint(cp))
                {
                    // 鼠标还停在气泡上，多给 3 秒
                    _hideTimer.Interval = 3000;
                    _hideTimer.Stop();
                    _hideTimer.Start();
                    return;
                }
                _hideTimer.Stop();
                _popup.HideSoft();
            };

            _dragTimer = new System.Windows.Forms.Timer();
            _dragTimer.Tick += delegate
            {
                _dragTimer.Stop();
                TranslateAt(_pendingPt, false, _pendingRect);
            };

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 60;
            _poll.Tick += OnPoll;
            _poll.Start();
            Log.Write("托盘已启动，快捷键 " + cfg.Hotkey + "，引擎 " + Translator.PickEngine(cfg, null));

            ShowRunningBalloon();

            if (cfg.FirstRun)
            {
                cfg.FirstRun = false;
                cfg.Save();
            }
            if (!Program.LaunchedHidden)
            {
                _showSettingsTimer = new System.Windows.Forms.Timer();
                _showSettingsTimer.Interval = 400;
                _showSettingsTimer.Tick += delegate
                {
                    _showSettingsTimer.Stop();
                    ShowSettings();
                };
                _showSettingsTimer.Start();
            }
        }

        private void ShowRunningBalloon()
        {
            try
            {
                _tray.ShowBalloonTip(2600, "即时译已启动", "选中文字后按 " + _cfg.Hotkey + " 翻译；右键托盘图标可设置", ToolTipIcon.Info);
            }
            catch { }
        }

        // ---------------- 托盘菜单 ----------------

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = Theme.Ui(9f);

            var title = new ToolStripMenuItem("即时译 · " + _cfg.Hotkey + " 选中即译");
            title.Enabled = false;
            m.Items.Add(title);
            m.Items.Add(new ToolStripSeparator());

            var tClip = new ToolStripMenuItem("翻译剪贴板内容");
            tClip.Click += delegate { TranslateClipboard(); };
            m.Items.Add(tClip);

            var tInput = new ToolStripMenuItem("输入翻译…");
            tInput.Click += delegate { ShowInputDialog(); };
            m.Items.Add(tInput);

            var tAuto = new ToolStripMenuItem("划词即译（拖选自动翻译）");
            tAuto.CheckOnClick = false;
            tAuto.Checked = _cfg.AutoTranslate;
            tAuto.Click += delegate
            {
                _cfg.AutoTranslate = !_cfg.AutoTranslate;
                _cfg.Save();
                tAuto.Checked = _cfg.AutoTranslate;
                if (_settings != null && !_settings.IsDisposed) _settings.RefreshLists();
            };
            m.Items.Add(tAuto);

            m.Items.Add(new ToolStripSeparator());
            var tSet = new ToolStripMenuItem("设置…");
            tSet.Click += delegate { ShowSettings(); };
            m.Items.Add(tSet);

            var tShow = new ToolStripMenuItem("生词本 / 历史");
            tShow.Click += delegate { ShowSettings(); };
            m.Items.Add(tShow);

            m.Items.Add(new ToolStripSeparator());
            var tAutoStart = new ToolStripMenuItem("开机自启动");
            tAutoStart.Checked = AutoStart.IsEnabled();
            tAutoStart.Click += delegate
            {
                bool on = !AutoStart.IsEnabled();
                AutoStart.Apply(on);
                tAutoStart.Checked = on;
                _cfg.AutoStart = on;
                _cfg.Save();
            };
            m.Items.Add(tAutoStart);

            var tAdmin = new ToolStripMenuItem("以管理员身份重启（取不到词时用这个）");
            tAdmin.Click += delegate { RestartAsAdmin(); };
            m.Items.Add(tAdmin);

            var tDiag = new ToolStripMenuItem("取词诊断（3 秒后读当前选区）");
            tDiag.Click += delegate { RunDiagnostic(); };
            m.Items.Add(tDiag);

            m.Items.Add(new ToolStripSeparator());
            var tExit = new ToolStripMenuItem("退出");
            tExit.Click += delegate { ExitApp(); };
            m.Items.Add(tExit);

            return m;
        }

        // ---------------- 全局快捷键 + 鼠标轮询 ----------------

        private const int HotkeyId = 0x1A3B;

        private string _registered = "";

        private void RegisterHotkey()
        {
            if (_registered == _cfg.Hotkey && _msgWindow != null && _msgWindow.Handle != IntPtr.Zero) return;
            UnregisterHotkey();
            Keys mods, key;
            if (!HotkeyUtil.Parse(_cfg.Hotkey, out mods, out key) || key == Keys.None)
            {
                Log.Write("快捷键解析失败：" + _cfg.Hotkey);
                return;
            }
            uint m = 0;
            if ((mods & Keys.Control) != 0) m |= 0x0002;
            if ((mods & Keys.Alt) != 0) m |= 0x0001;
            if ((mods & Keys.Shift) != 0) m |= 0x0004;
            if ((mods & Keys.LWin) != 0) m |= 0x0008;
            m |= 0x4000; // MOD_NOREPEAT：按住不放不重复触发
            IntPtr hwnd = _msgWindow == null ? IntPtr.Zero : _msgWindow.Handle;
            bool ok = Native.RegisterHotKey(hwnd, HotkeyId, m, (uint)key);
            if (!ok)
            {
                Log.Write("全局快捷键注册失败（可能被其他软件占用）：" + _cfg.Hotkey);
                try { _tray.ShowBalloonTip(4000, "快捷键被占用", _cfg.Hotkey + " 注册失败，请在设置里换一个组合键", ToolTipIcon.Warning); } catch { }
            }
            else
            {
                _registered = _cfg.Hotkey;
                Log.Write("全局快捷键已注册：" + _cfg.Hotkey);
            }
        }

        private void UnregisterHotkey()
        {
            try
            {
                if (_msgWindow != null && _msgWindow.Handle != IntPtr.Zero) Native.UnregisterHotKey(_msgWindow.Handle, HotkeyId);
            }
            catch { }
        }

        public void ReloadHotkey()
        {
            RegisterHotkey();
            if (_tray != null) _tray.Text = "即时译 · " + _cfg.Hotkey + " 选中即译";
        }

        /// <summary>60ms 轮询鼠标：判断"按住拖动 → 松开"就是一次划词</summary>
        private void OnPoll(object sender, EventArgs e)
        {
            try
            {
                Native.POINT pt = Native.CursorPos();
                bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;


                if (down && !_btnDown)
                {
                    _btnDown = true;
                    _dragStart = pt;
                    _dragStartAt = DateTime.Now;
                    if (_popup.Visible && !_popup.Pinned && !_popup.ContainsScreenPoint(pt))
                    {
                        _popup.HideSoft();
                        CancelAutoHide();
                    }
                    return;
                }

                if (!down && _btnDown)
                {
                    _btnDown = false;
                    double dist = Math.Sqrt(Math.Pow(pt.X - _dragStart.X, 2) + Math.Pow(pt.Y - _dragStart.Y, 2));
                    double ms = (DateTime.Now - _dragStartAt).TotalMilliseconds;
                    if (!_cfg.AutoTranslate) return;
                    if (dist < 8 || ms < 80 || ms > 20000) return;

                    _selRect = System.Drawing.Rectangle.FromLTRB(
                        Math.Min(_dragStart.X, pt.X), Math.Min(_dragStart.Y, pt.Y),
                        Math.Max(_dragStart.X, pt.X), Math.Max(_dragStart.Y, pt.Y));
                    _selAt = DateTime.Now;
                    Log.Write("拖选结束 dist=" + (int)dist + " ms=" + (int)ms + "，准备翻译");

                    if (_popup.Visible && _popup.ContainsScreenPoint(pt)) return;
                    _pendingPt = pt;
                    _pendingRect = _selRect;
                    _dragTimer.Interval = Math.Max(120, Math.Min(1500, _cfg.AutoDelay));
                    _dragTimer.Stop();
                    _dragTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Log.Write("轮询异常: " + ex.Message);
            }
        }

        /// <summary>由隐藏窗口在收到 WM_HOTKEY 时调用</summary>
        public void OnHotkeyPressed()
        {
            Log.Write("快捷键触发");
            Native.POINT pt = Native.CursorPos();
            System.Drawing.Rectangle? rect = null;
            if (!_selRect.IsEmpty && (DateTime.Now - _selAt).TotalSeconds < 90)
            {
                var r = _selRect;
                r.Inflate(24, 24);
                if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom) rect = _selRect;
            }
            TranslateAt(pt, _cfg.HotkeyTts, rect);
        }

        // ---------------- 触发 ----------------

        private void TriggerHotkey()
        {
            Log.Write("快捷键触发");
            Native.POINT pt = Native.CursorPos();
            System.Drawing.Rectangle? rect = null;
            if (!_selRect.IsEmpty && (DateTime.Now - _selAt).TotalSeconds < 90)
            {
                var r = _selRect;
                r.Inflate(24, 24);
                if (pt.X >= r.Left && pt.X <= r.Right && pt.Y >= r.Top && pt.Y <= r.Bottom) rect = _selRect;
            }
            TranslateAt(pt, _cfg.HotkeyTts, rect);
        }

        private bool IsExcluded(string hay)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(_cfg.ExcludedApps)) return false;
            string[] lines = _cfg.ExcludedApps.Split('\n');
            string h = hay.ToLowerInvariant();
            foreach (var raw in lines)
            {
                string k = raw.Trim().ToLowerInvariant();
                if (k.Length == 0) continue;
                if (h.Contains(k)) return true;
            }
            return false;
        }

        /// <summary>把气泡放到选区旁边（有选区）或鼠标旁边（无选区）</summary>
        /// <summary>气泡显示后 6 秒自动收起；鼠标停在上面则顺延</summary>
        private void ArmAutoHide()
        {
            _hideTimer.Interval = 6000;
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void CancelAutoHide()
        {
            try { _hideTimer.Stop(); } catch { }
        }
        /// <summary>取不到词时给一句有用的提示（区分权限问题和其他原因）</summary>
        private string CaptureFailureHint()
        {
            string self = Native.SelfIntegrity();
            if (self == "低" || self == "不可信")
                return "取词失败：当前程序权限过低（" + self + "），无法读取其它程序。请右键托盘图标 →「以管理员身份重启」。";
            int tpid = Native.ForegroundPid();
            string target = Native.IntegrityLevel(tpid);
            if (target == "高(管理员)" || target == "系统")
                return "取词失败：目标程序以管理员权限运行，普通权限读不到。请右键托盘图标 →「以管理员身份重启」。";
            return "没有取到选中的文字。请先选中文字，再按快捷键。";
        }
        private void PlacePopup(Native.POINT pt, System.Drawing.Rectangle? rect)
        {
            if (rect.HasValue && !rect.Value.IsEmpty && rect.Value.Width >= 0)
            {
                var r = rect.Value;
                _popup.ShowNearRect(r.Left, r.Top, r.Right, r.Bottom);
            }
            else
            {
                _popup.ShowAt(pt);
            }
        }

        private void TranslateAt(Native.POINT pt, bool speak, System.Drawing.Rectangle? rect)
        {
            if (_busy) return;

            if (Native.WindowPidAt(pt) == System.Diagnostics.Process.GetCurrentProcess().Id) return;
            string cls = Native.ClassNameAt(pt);
            string title = Native.TitleAt(pt);
            if (IsExcluded(cls + " " + title)) return;

            _busy = true;
            try
            {
                _popup.Configure(_cfg.BubbleWidth);
                _popup.ShowBusy("正在读取选区…");
                Application.DoEvents();

                Log.Write("开始取词 点=(" + pt.X + "," + pt.Y + ") 前台=" + Native.ForegroundTitle()
                    + "｜自身完整性=" + Native.SelfIntegrity()
                    + "，目标完整性=" + Native.IntegrityLevel(Native.ForegroundPid()));
                string text = Lang.Normalize(ClipboardCapture.Capture(450));
                Log.Write("取到 " + text.Length + " 字" + (string.IsNullOrEmpty(ClipboardCapture.LastError) ? "" : "（" + ClipboardCapture.LastError + "）"));
                if (text.Length == 0 || !Lang.LooksTranslatable(text))
                {
                    _ui.Post(delegate
                    {
                        _popup.ShowErrorText(CaptureFailureHint(), "");
                        PlacePopup(pt, rect);
                        ArmAutoHide();
                    }, null);
                    return;
                }
                if (text.Length > 8000) text = text.Substring(0, 8000);
                string captured = text;

                _ui.Post(delegate
                {
                    _popup.Configure(_cfg.BubbleWidth);
                    _popup.ShowLoadingText(captured);
                    PlacePopup(pt, rect);
                }, null);

                RunTranslation(captured, pt, null, speak, rect);
            }
            catch (Exception ex)
            {
                Log.Write("取词失败: " + ex.Message);
                _ui.Post(delegate { _popup.ShowErrorText(Errors.Friendly(ex.Message), ""); }, null);
            }
            finally
            {
                _busy = false;
            }
        }

        private void RunTranslation(string text, Native.POINT pt, string engineOverride, bool speak, System.Drawing.Rectangle? rect)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var r = Translator.Translate(text, _cfg, engineOverride);
                    Store.AddHistory(r);
                    bool isVocab = Store.IsVocab(r.Source);
                    _ui.Post(delegate
                    {
                        _popup.ShowResultText(r, isVocab);
                        PlacePopup(pt, rect);
                        ArmAutoHide();
                    }, null);
                    if (speak && _cfg.Tts) Tts.Speak(r.Source, r.Detected);
                }
                catch (Exception ex)
                {
                    string msg = Errors.Friendly(ex.Message);
                    _ui.Post(delegate
                    {
                        _popup.ShowErrorText(msg, text);
                        PlacePopup(pt, rect);
                        ArmAutoHide();
                    }, null);
                }
            });
        }

        private void TranslateClipboard()
        {
            string text = "";
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                Log.Write("读取剪贴板失败: " + ex.Message);
            }
            text = Lang.Normalize(text);
            Native.POINT pt = Native.CursorPos();
            if (text.Length == 0)
            {
                _popup.ShowErrorText("剪贴板里没有文字。先复制一段文字，或打开「划词即译」。", "");
                _popup.ShowAt(pt);
                return;
            }
            _popup.ShowLoadingText(text);
            _popup.ShowAt(pt);
            RunTranslation(text, pt, null, false, null);
        }

        // ---------------- 气泡事件 ----------------

        private void WirePopup()
        {
            _popup.CopyRequested += delegate
            {
                if (_popup.Current == null) return;
                string t = _popup.Current.Text;
                if (_popup.Current.Senses != null && _popup.Current.Senses.Count > 0)
                    t = string.Join("；", _popup.Current.Senses.ToArray());
                ClipboardCapture.WriteText(t);
            };

            _popup.VocabRequested += delegate
            {
                if (_popup.Current == null) return;
                if (Store.IsVocab(_popup.Current.Source)) Store.RemoveVocab(_popup.Current.Source);
                else Store.AddVocab(_popup.Current);
                bool v = Store.IsVocab(_popup.Current.Source);
                _popup.ShowResultText(_popup.Current, v);
            };

            _popup.RetranslateRequested += delegate
            {
                if (_popup.Current == null) return;
                string alt = _popup.Current.Engine == "llm" ? "bing" : (string.IsNullOrEmpty(_cfg.LlmApiKey) ? "bing" : "llm");
                Native.POINT pt = Native.CursorPos();
                _popup.ShowLoadingText(_popup.Current.Source);
                _popup.ShowAt(pt);
                RunTranslation(_popup.Current.Source, pt, alt, false, null);
            };

            _popup.SpeakTranslationRequested += delegate
            {
                if (_popup.Current == null) return;
                if (!_cfg.Tts) return;
                string text = _popup.Current.Text;
                if (_popup.Current.Senses != null && _popup.Current.Senses.Count > 0)
                    text = string.Join("；", _popup.Current.Senses.ToArray());
                Tts.Speak(text, _popup.Current.To);
            };

            _popup.ClosedByUser += delegate { };
        }

        /// <summary>从托盘菜单打开「输入翻译」小窗</summary>
        private void ShowInputDialog()
        {
            using (var dlg = new InputBoxForm())
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                string text = (dlg.Value ?? "").Trim();
                if (text.Length == 0) return;
                Native.POINT pt = Native.CursorPos();
                _popup.Configure(_cfg.BubbleWidth);
                _popup.ShowLoadingText(text);
                _popup.ShowAt(pt);
                RunTranslation(text, pt, null, false, null);
            }
        }

        /// <summary>取词诊断：3 秒后对当前选区依次尝试 UIA / Ctrl+C / WM_COPY，把结果写进日志并弹窗</summary>
        public void RunDiagnostic()
        {
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                _ui.Post(delegate
                {
                    var sb = new System.Text.StringBuilder();
                    try
                    {
                        Native.POINT pt = Native.CursorPos();
                        sb.AppendLine("前台窗口：" + Native.ForegroundTitle());
                        sb.AppendLine("光标位置：" + pt.X + "," + pt.Y);

                        string uia = UiaReader.Read(1500);
                        sb.AppendLine("UIA：" + (string.IsNullOrEmpty(uia) ? "（读不到）" : "取到 " + uia.Length + " 字 → " + uia));
                        sb.AppendLine("    " + UiaReader.LastInfo);

                        bool altDown = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
                        bool ctrlDown = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
                        sb.AppendLine("触发时修饰键：Alt=" + altDown + " Ctrl=" + ctrlDown);
                        string clip = ClipboardCapture.Capture(700);
                        sb.AppendLine("剪贴板法：" + (string.IsNullOrEmpty(clip) ? "（读不到）" : "取到 " + clip.Length + " 字 → " + clip));
                        sb.AppendLine("    " + (string.IsNullOrEmpty(ClipboardCapture.LastError) ? "无错误信息" : ClipboardCapture.LastError));

                        Log.Write("诊断：" + sb.ToString().Replace(Environment.NewLine, " | "));
                        string msg = sb.ToString();
                        if (!Program.Headless) MessageBox.Show(msg, "即时译 · 取词诊断", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("诊断失败：" + ex);
                    }
                }, null);
            };
            timer.Start();
        }
        /// <summary>以管理员身份重启：目标程序是管理员权限时，普通权限的模拟按键会被系统丢弃</summary>
        private void RestartAsAdmin()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                Log.Write("提权重启失败（可能取消了 UAC）：" + ex.Message);
            }
        }
        private void ShowSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.ShowFront();
                return;
            }
            _settings = new SettingsForm(_cfg, delegate
            {
                ReloadHotkey();
                _popup.Configure(_cfg.BubbleWidth);
            });
            _settings.TestCaptureRequested += delegate
            {
                var tt = new System.Windows.Forms.Timer();
                tt.Interval = 2600;
                tt.Tick += delegate
                {
                    tt.Stop();
                    tt.Dispose();
                    Native.POINT cp = Native.CursorPos();
                    TranslateAt(cp, false, null);
                };
                tt.Start();
            };
            _settings.HiddenToTray += delegate
            {
                try { _tray.ShowBalloonTip(2000, "即时译仍在后台", "关闭窗口不会退出；右键托盘图标可再次打开设置", ToolTipIcon.Info); } catch { }
            };
            _settings.FormClosed += delegate { _settings = null; };
            _settings.ShowFront();
        }

        public void ExitApp()
        {
            try { if (_settings != null && !_settings.IsDisposed) { _settings.ForceClose = true; _settings.Close(); } } catch { }
            try { if (_poll != null) { _poll.Stop(); _poll.Dispose(); } } catch { }
            UnregisterHotkey();
            try { if (_msgWindow != null) _msgWindow.Destroy(); } catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _popup.Close(); } catch { }
            ExitThread();
        }
    }

    public class ManualTextEventArgs : EventArgs
    {
        public string Text;
        public ManualTextEventArgs(string text) { Text = text; }
    }
}

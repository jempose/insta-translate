using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>设置窗口：常规 / 引擎 / 生词本 / 历史 / 帮助</summary>
    public class SettingsForm : Form
    {
        private readonly Config _cfg;
        private readonly Action _onChanged;

        private Button _hotkey;
        private bool _recording;
        private CheckBox _auto;
        private NumericUpDown _delay;
        private NumericUpDown _width;
        private CheckBox _tts;
        private CheckBox _hotkeyTts;
        private CheckBox _autostart;
        private TextBox _excluded;

        private ComboBox _engine;
        private ComboBox _target;
        private TextBox _baseUrl;
        private TextBox _apiKey;
        private TextBox _model;
        private Label _testResult;
        private Button _testBtn;

        private ListView _vocabList;
        private ListView _historyList;
        private TextBox _search;

        private TabControl _tabs;

        public bool ForceClose;

        public SettingsForm(Config cfg, Action onChanged)
        {
            _cfg = cfg;
            _onChanged = onChanged;

            Text = "即时译 · 设置";
            ShowInTaskbar = true;
            MinimizeBox = false;
            Width = 860;
            Height = 640;
            MinimumSize = new Size(720, 540);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Ui(9f);
            Icon = AppIcon.Get();
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildHeader();
            BuildTabs();
            LoadValues();
        }

        private void BuildHeader()
        {
            var header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 54;
            header.BackColor = Color.FromArgb(32, 38, 48);
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, header.Width), header.Height),
                    Theme.Accent, Theme.Accent2, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(b, 0, 0, header.Width, 3);
                }
                using (var f = Theme.UiBold(13f))
                using (var white = new SolidBrush(Color.White))
                using (var dim = new SolidBrush(Color.FromArgb(180, 190, 205)))
                {
                    e.Graphics.DrawString("即时译", f, white, 18, 12);
                    e.Graphics.DrawString("选中文字即译 · 中英互译", Theme.Ui(9f), dim, 92, 18);
                }
            };
            Controls.Add(header);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Padding = new Point(14, 6);
            Controls.Add(_tabs);

            _tabs.TabPages.Add(BuildGeneral());
            _tabs.TabPages.Add(BuildEngine());
            _tabs.TabPages.Add(BuildVocab());
            _tabs.TabPages.Add(BuildHistory());
            _tabs.TabPages.Add(BuildHelp());
        }

        private TabPage NewPage(string title)
        {
            var p = new TabPage(title);
            p.BackColor = Color.FromArgb(248, 249, 251);
            p.Padding = new Padding(18, 16, 18, 16);
            return p;
        }

        private Label Caption(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.ForeColor = Color.FromArgb(40, 48, 60);
            return l;
        }

        private Label Hint(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 34);
            l.ForeColor = Color.FromArgb(120, 130, 145);
            l.Font = Theme.Ui(8.5f);
            return l;
        }

        // ---------------- 常规 ----------------

        private TabPage BuildGeneral()
        {
            var p = NewPage("常规");

            p.Controls.Add(Caption("翻译快捷键", 18, 20));
            p.Controls.Add(Hint("选中文字后按下即可翻译。点右侧按钮后直接按新的组合键。", 140, 18, 380));

            _hotkey = new Button();
            _hotkey.Text = _cfg.Hotkey;
            _hotkey.Location = new Point(560, 14);
            _hotkey.Size = new Size(190, 30);
            _hotkey.Click += delegate
            {
                _recording = true;
                _hotkey.Text = "请按下组合键…";
                _hotkey.Focus();
            };
            p.Controls.Add(_hotkey);

            _auto = new CheckBox();
            _auto.Text = "开启划词即译（拖选松开鼠标即翻译）";
            _auto.Location = new Point(18, 62);
            _auto.Size = new Size(420, 24);
            _auto.CheckedChanged += delegate
            {
                _cfg.AutoTranslate = _auto.Checked;
                Changed();
            };
            p.Controls.Add(_auto);

            p.Controls.Add(Caption("松开鼠标后延迟（毫秒）", 18, 100));
            _delay = new NumericUpDown();
            _delay.Minimum = 100;
            _delay.Maximum = 1500;
            _delay.Increment = 20;
            _delay.Location = new Point(240, 98);
            _delay.Size = new Size(90, 26);
            _delay.ValueChanged += delegate
            {
                _cfg.AutoDelay = (int)_delay.Value;
                Changed();
            };
            p.Controls.Add(_delay);
            p.Controls.Add(Hint("太短容易打断拖选，太长会显得慢，建议 300–600。", 344, 96, 420));

            p.Controls.Add(Caption("气泡宽度", 18, 138));
            _width = new NumericUpDown();
            _width.Minimum = 320;
            _width.Maximum = 720;
            _width.Increment = 10;
            _width.Location = new Point(240, 136);
            _width.Size = new Size(90, 26);
            _width.ValueChanged += delegate
            {
                _cfg.BubbleWidth = (int)_width.Value;
                Changed();
            };
            p.Controls.Add(_width);

            _tts = new CheckBox();
            _tts.Text = "允许朗读（气泡里的「朗读」按钮）";
            _tts.Location = new Point(18, 178);
            _tts.Size = new Size(360, 24);
            _tts.CheckedChanged += delegate
            {
                _cfg.Tts = _tts.Checked;
                Changed();
            };
            p.Controls.Add(_tts);

            _hotkeyTts = new CheckBox();
            _hotkeyTts.Text = "按快捷键翻译时自动朗读原文";
            _hotkeyTts.Location = new Point(18, 208);
            _hotkeyTts.Size = new Size(360, 24);
            _hotkeyTts.CheckedChanged += delegate
            {
                _cfg.HotkeyTts = _hotkeyTts.Checked;
                Changed();
            };
            p.Controls.Add(_hotkeyTts);

            _autostart = new CheckBox();
            _autostart.Text = "开机自动启动（登录后静默驻留托盘）";
            _autostart.Location = new Point(18, 238);
            _autostart.Size = new Size(360, 24);
            _autostart.CheckedChanged += delegate
            {
                _cfg.AutoStart = _autostart.Checked;
                AutoStart.Apply(_autostart.Checked);
                Changed();
            };
            p.Controls.Add(_autostart);

            p.Controls.Add(Caption("排除的应用（每行一个窗口类名或标题关键字）", 18, 276));
            _excluded = new TextBox();
            _excluded.Multiline = true;
            _excluded.ScrollBars = ScrollBars.Vertical;
            _excluded.Location = new Point(18, 300);
            _excluded.Size = new Size(732, 110);
            _excluded.Font = Theme.Mono(9f);
            _excluded.TextChanged += delegate
            {
                _cfg.ExcludedApps = _excluded.Text;
                Changed();
            };
            p.Controls.Add(_excluded);
            p.Controls.Add(Hint("在这些窗口里划词不会触发翻译（例如某些游戏、密码管理器）。", 18, 416, 700));

            var testBtn = new Button();
            testBtn.Text = "测试取词（3 秒内选中一段文字）";
            testBtn.Location = new Point(18, 452);
            testBtn.Size = new Size(240, 32);
            testBtn.Click += delegate
            {
                Hide();
                if (TestCaptureRequested != null) TestCaptureRequested(this, EventArgs.Empty);
            };
            p.Controls.Add(testBtn);

            return p;
        }

        // ---------------- 引擎 ----------------

        private TabPage BuildEngine()
        {
            var p = NewPage("引擎");

            p.Controls.Add(Caption("翻译引擎", 18, 20));
            _engine = new ComboBox();
            _engine.DropDownStyle = ComboBoxStyle.DropDownList;
            _engine.Location = new Point(160, 16);
            _engine.Size = new Size(220, 26);
            _engine.Items.Add("自动（有 Key 用大模型，否则必应）");
            _engine.Items.Add("大模型（DeepSeek 兼容接口）");
            _engine.Items.Add("必应（免 Key）");
            _engine.SelectedIndexChanged += delegate
            {
                if (_engine.SelectedIndex == 0) _cfg.Engine = "auto";
                else if (_engine.SelectedIndex == 1) _cfg.Engine = "llm";
                else _cfg.Engine = "bing";
                Changed();
            };
            p.Controls.Add(_engine);

            p.Controls.Add(Caption("目标语言", 18, 60));
            _target = new ComboBox();
            _target.DropDownStyle = ComboBoxStyle.DropDownList;
            _target.Location = new Point(160, 56);
            _target.Size = new Size(220, 26);
            _target.Items.Add("自动（中→英 / 英→中）");
            _target.Items.Add("中文");
            _target.Items.Add("英语");
            _target.Items.Add("日语");
            _target.Items.Add("韩语");
            _target.SelectedIndexChanged += delegate
            {
                switch (_target.SelectedIndex)
                {
                    case 1: _cfg.TargetLang = "zh"; break;
                    case 2: _cfg.TargetLang = "en"; break;
                    case 3: _cfg.TargetLang = "ja"; break;
                    case 4: _cfg.TargetLang = "ko"; break;
                    default: _cfg.TargetLang = "auto"; break;
                }
                Changed();
            };
            p.Controls.Add(_target);

            var box = new GroupBox();
            box.Text = "大模型设置（OpenAI 兼容接口）";
            box.Location = new Point(18, 100);
            box.Size = new Size(732, 210);
            p.Controls.Add(box);

            box.Controls.Add(Caption("API 地址", 16, 30));
            _baseUrl = new TextBox();
            _baseUrl.Location = new Point(110, 26);
            _baseUrl.Size = new Size(380, 26);
            _baseUrl.TextChanged += delegate { _cfg.LlmBaseUrl = _baseUrl.Text.Trim(); Changed(); };
            box.Controls.Add(_baseUrl);
            box.Controls.Add(Hint("默认 https://api.deepseek.com，也可填任何兼容 /chat/completions 的地址。", 110, 54, 560));

            box.Controls.Add(Caption("API Key", 16, 92));
            _apiKey = new TextBox();
            _apiKey.Location = new Point(110, 88);
            _apiKey.Size = new Size(380, 26);
            _apiKey.UseSystemPasswordChar = true;
            _apiKey.TextChanged += delegate { _cfg.LlmApiKey = _apiKey.Text.Trim(); Changed(); };
            box.Controls.Add(_apiKey);

            box.Controls.Add(Caption("模型", 16, 132));
            _model = new TextBox();
            _model.Location = new Point(110, 128);
            _model.Size = new Size(200, 26);
            _model.TextChanged += delegate { _cfg.LlmModel = _model.Text.Trim(); Changed(); };
            box.Controls.Add(_model);

            _testBtn = new Button();
            _testBtn.Text = "测试当前引擎";
            _testBtn.Location = new Point(110, 166);
            _testBtn.Size = new Size(120, 30);
            _testBtn.Click += delegate { TestEngine(); };
            box.Controls.Add(_testBtn);

            _testResult = new Label();
            _testResult.Location = new Point(240, 168);
            _testResult.Size = new Size(470, 40);
            _testResult.ForeColor = Color.FromArgb(90, 100, 115);
            box.Controls.Add(_testResult);

            return p;
        }

        private void TestEngine()
        {
            _testBtn.Enabled = false;
            _testResult.ForeColor = Color.FromArgb(90, 100, 115);
            _testResult.Text = "测试中…";
            string sample = "Hello, world";
            var cfg = _cfg;
            var btn = _testBtn;
            var lbl = _testResult;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string text;
                bool ok;
                try
                {
                    var r = Translator.Translate(sample, cfg, null);
                    text = "✓ " + r.EngineLabel + " 可用：" + r.Text + "（" + r.Elapsed + "ms）";
                    ok = true;
                }
                catch (Exception ex)
                {
                    text = "✗ " + ex.Message;
                    ok = false;
                }
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        lbl.Text = text;
                        lbl.ForeColor = ok ? Color.FromArgb(30, 150, 100) : Color.FromArgb(200, 60, 60);
                        btn.Enabled = true;
                    });
                }
                catch { }
            });
        }

        // ---------------- 生词本 ----------------

        private TabPage BuildVocab()
        {
            var p = NewPage("生词本");
            _vocabList = MakeList(18, 16, 732, 420, new[] { "原文", "译文", "音标", "时间" }, new[] { 200, 300, 110, 110 });
            p.Controls.Add(_vocabList);

            var del = new Button();
            del.Text = "移除选中";
            del.Location = new Point(18, 448);
            del.Size = new Size(100, 30);
            del.Click += delegate
            {
                if (_vocabList.SelectedItems.Count == 0) return;
                Store.RemoveVocab(_vocabList.SelectedItems[0].Text);
                RefreshVocab();
                Changed();
            };
            p.Controls.Add(del);

            var exp = new Button();
            exp.Text = "导出 CSV";
            exp.Location = new Point(126, 448);
            exp.Size = new Size(100, 30);
            exp.Click += delegate { Export(true); };
            p.Controls.Add(exp);

            return p;
        }

        private ListView MakeList(int x, int y, int w, int h, string[] cols, int[] widths)
        {
            var lv = new ListView();
            lv.Location = new Point(x, y);
            lv.Size = new Size(w, h);
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.MultiSelect = false;
            lv.Font = Theme.Ui(9f);
            for (int i = 0; i < cols.Length; i++)
            {
                lv.Columns.Add(cols[i], widths[i]);
            }
            return lv;
        }

        private void RefreshVocab()
        {
            _vocabList.Items.Clear();
            foreach (var v in Store.Vocab())
            {
                var it = new ListViewItem(v.Source ?? "");
                it.SubItems.Add(v.Translation ?? "");
                it.SubItems.Add(v.Phonetic ?? "");
                it.SubItems.Add(v.CreatedAt ?? "");
                _vocabList.Items.Add(it);
            }
        }

        // ---------------- 历史 ----------------

        private TabPage BuildHistory()
        {
            var p = NewPage("历史");
            _search = new TextBox();
            _search.Location = new Point(18, 16);
            _search.Size = new Size(300, 26);
            _search.TextChanged += delegate { RefreshHistory(); };
            p.Controls.Add(_search);

            var hint = new Label();
            hint.Text = "按原文或译文搜索";
            hint.Location = new Point(326, 20);
            hint.AutoSize = true;
            hint.ForeColor = Color.FromArgb(120, 130, 145);
            p.Controls.Add(hint);

            _historyList = MakeList(18, 54, 732, 380, new[] { "原文", "译文", "方向", "时间" }, new[] { 260, 300, 80, 130 });
            p.Controls.Add(_historyList);

            var clear = new Button();
            clear.Text = "清空历史";
            clear.Location = new Point(18, 446);
            clear.Size = new Size(100, 30);
            clear.Click += delegate
            {
                if (MessageBox.Show(this, "确定清空全部翻译历史吗？生词本不受影响。", "即时译", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                Store.ClearHistory();
                RefreshHistory();
            };
            p.Controls.Add(clear);

            var exp = new Button();
            exp.Text = "导出 CSV";
            exp.Location = new Point(126, 446);
            exp.Size = new Size(100, 30);
            exp.Click += delegate { Export(false); };
            p.Controls.Add(exp);

            return p;
        }

        private void RefreshHistory()
        {
            _historyList.Items.Clear();
            string q = (_search.Text ?? "").Trim().ToLowerInvariant();
            int n = 0;
            foreach (var h in Store.History())
            {
                if (q.Length > 0)
                {
                    bool hit = (h.Source ?? "").ToLowerInvariant().Contains(q) || (h.Translation ?? "").ToLowerInvariant().Contains(q);
                    if (!hit) continue;
                }
                var it = new ListViewItem(h.Source ?? "");
                it.SubItems.Add(h.Translation ?? "");
                it.SubItems.Add((h.From ?? "") + "→" + (h.To ?? ""));
                it.SubItems.Add(h.CreatedAt ?? "");
                _historyList.Items.Add(it);
                if (++n > 400) break;
            }
        }

        private void Export(bool vocab)
        {
            var dlg = new SaveFileDialog();
            dlg.Filter = "CSV 表格|*.csv";
            dlg.FileName = vocab ? "即时译-生词本.csv" : "即时译-翻译历史.csv";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Store.Export(dlg.FileName, vocab);
                MessageBox.Show(this, "已导出到：\n" + dlg.FileName, "即时译", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "即时译", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------- 帮助 ----------------

        private TabPage BuildHelp()
        {
            var p = NewPage("帮助");
            var tb = new TextBox();
            tb.Multiline = true;
            tb.ReadOnly = true;
            tb.ScrollBars = ScrollBars.Vertical;
            tb.BorderStyle = BorderStyle.None;
            tb.BackColor = Color.FromArgb(248, 249, 251);
            tb.Location = new Point(18, 16);
            tb.Size = new Size(732, 440);
            tb.Font = Theme.Ui(9.5f);
            tb.Text =
                "怎么用\r\n" +
                "  1. 在任何软件里选中中文或英文文字\r\n" +
                "  2. 按快捷键（默认 Alt+T），或打开「划词即译」后拖选松开鼠标\r\n" +
                "  3. 鼠标旁弹出译文；点气泡外任意处收起\r\n\r\n" +
                "气泡上的按钮\r\n" +
                "  复制：复制译文   朗读：读原文/译文   生词本：收藏这一条   重译：换引擎重翻\r\n" +
                "  固定：点别处不消失（方便抄写）\r\n\r\n" +
                "取词原理\r\n" +
                "  模拟 Ctrl+C 读取选区。程序会先备份剪贴板，读完立即还原，你原来复制的内容不会丢。\r\n" +
                "  以管理员权限运行的窗口无法被普通程序模拟按键，这是 Windows 的限制。\r\n\r\n" +
                "数据位置\r\n" +
                "  " + Paths.DataDir + "\r\n" +
                "  settings.json 设置    history.json 历史    vocab.json 生词本\r\n" +
                "  日志：" + Log.File + "\r\n\r\n" +
                "版本 " + AppVersion.Value + "（原生实现，不依赖浏览器内核）\r\n";
            p.Controls.Add(tb);
            return p;
        }

        // ---------------- 数据绑定 ----------------

        private void LoadValues()
        {
            _hotkey.Text = _cfg.Hotkey;
            _auto.Checked = _cfg.AutoTranslate;
            _delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, _cfg.AutoDelay));
            _width.Value = Math.Max(_width.Minimum, Math.Min(_width.Maximum, _cfg.BubbleWidth));
            _tts.Checked = _cfg.Tts;
            _hotkeyTts.Checked = _cfg.HotkeyTts;
            _autostart.Checked = AutoStart.IsEnabled();
            _excluded.Text = _cfg.ExcludedApps ?? "";

            if (_cfg.Engine == "llm") _engine.SelectedIndex = 1;
            else if (_cfg.Engine == "bing") _engine.SelectedIndex = 2;
            else _engine.SelectedIndex = 0;

            switch (_cfg.TargetLang)
            {
                case "zh": _target.SelectedIndex = 1; break;
                case "en": _target.SelectedIndex = 2; break;
                case "ja": _target.SelectedIndex = 3; break;
                case "ko": _target.SelectedIndex = 4; break;
                default: _target.SelectedIndex = 0; break;
            }

            _baseUrl.Text = _cfg.LlmBaseUrl;
            _apiKey.Text = _cfg.LlmApiKey;
            _model.Text = _cfg.LlmModel;

            RefreshVocab();
            RefreshHistory();
        }

        public void RefreshLists()
        {
            RefreshVocab();
            RefreshHistory();
        }

        private void Changed()
        {
            _cfg.Save();
            if (_onChanged != null) _onChanged();
        }

        // ---------------- 快捷键录制 ----------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!_recording) return base.ProcessCmdKey(ref msg, keyData);

            Keys k = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;

            if (k == Keys.Escape)
            {
                _recording = false;
                _hotkey.Text = _cfg.Hotkey;
                return true;
            }
            if (k == Keys.Control || k == Keys.Shift || k == Keys.Alt || k == Keys.Menu)
            {
                return true;
            }

            bool isFn = k >= Keys.F1 && k <= Keys.F12;
            if (mods == Keys.None && !isFn)
            {
                _hotkey.Text = "请加上 Ctrl / Alt / Shift，或使用 F1~F12";
                return true;
            }

            var sb = new System.Text.StringBuilder();
            if ((mods & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((mods & Keys.Alt) != 0) sb.Append("Alt+");
            if ((mods & Keys.Shift) != 0) sb.Append("Shift+");
            sb.Append(HotkeyUtil.KeyName(k));

            _cfg.Hotkey = sb.ToString();
            _hotkey.Text = _cfg.Hotkey;
            _recording = false;
            Changed();
            return true;
        }

        public void ShowFront()
        {
            try
            {
                if (!Visible) Show();
                // 先临时置顶抢到前景，再放开，避免被全屏窗口挡住
                TopMost = true;
                BringToFront();
                Activate();
                Native.SetForegroundWindow(Handle);
                var t = new System.Windows.Forms.Timer();
                t.Interval = 600;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    TopMost = false;
                };
                t.Start();
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshVocab();
            RefreshHistory();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // 点 X 只是收到托盘，不退出程序
            if (!ForceClose)
            {
                e.Cancel = true;
                Hide();
                if (HiddenToTray != null) HiddenToTray(this, EventArgs.Empty);
            }
        }

        public event EventHandler HiddenToTray;
        public event EventHandler TestCaptureRequested;
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>翻译气泡：无边框、置顶、不抢焦点、随内容自适应高度</summary>
    public class PopupForm : Form
    {
        public event EventHandler CopyRequested;
        public event EventHandler SpeakSourceRequested;
        public event EventHandler SpeakTranslationRequested;
        public event EventHandler VocabRequested;
        public event EventHandler RetranslateRequested;
        public event EventHandler ClosedByUser;
        public event EventHandler PinnedChanged;
        public event EventHandler OpenSettingsRequested;

        public TranslationResult Current;
        public bool Pinned;
        public bool IsVocab;

        private int _width = 430;
        private float _scale = 1f;

        private Label _logo;
        private Label _dir;
        private Label _engine;
        private Button _btnPin;
        private Button _btnCopy;
        private Button _btnClose;
        private Label _src;
        private Panel _sep;
        private Label _trans;
        private Label _phon;
        private Label _senses;
        private Label _ctx;
        private Label _notice;
        private bool _isError;
        private readonly ToolTip _tip = new ToolTip();

        private Button _btnVocab;
        private Button _btnRetranslate;
        private Button _btnSpeak;
        private Label _meta;
        private Label _hint;

        private const int RowGap = 7;

        public PopupForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Padding = new Padding(0);
            KeyPreview = true;

            BuildControls();
            
            using (var g = CreateGraphics())
            {
                _scale = g.DpiX / 96f;
                if (_scale <= 0) _scale = 1f;
            }
            Visible = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private int S(int v)
        {
            return (int)Math.Round(v * _scale);
        }

        private void BuildControls()
        {
            _logo = MakeLabel("译", Theme.UiBold(8.5f), Color.White);
            _logo.TextAlign = ContentAlignment.MiddleCenter;
            _logo.BackColor = Theme.Accent;

            _dir = MakeLabel("", Theme.Ui(8.5f), Theme.Dim);
            _engine = MakeLabel("", Theme.Ui(8f), Theme.Faint);

            _btnPin = Theme.Link("固定", OnPinClick);
            _btnCopy = Theme.Link("复制", OnCopyClick);
            _btnClose = Theme.Link("✕", OnCloseClick);

            _src = MakeLabel("", Theme.Ui(9f), Theme.Dim);

            _sep = new Panel();
            _sep.BackColor = Theme.Line;
            _sep.Height = 1;

            _trans = MakeLabel("", Theme.UiBold(11.5f), Theme.Fg);

            _phon = MakeLabel("", Theme.Ui(9f), Theme.Faint);
            _senses = MakeLabel("", Theme.Ui(9f), Theme.Dim);
            _ctx = MakeLabel("", Theme.Ui(8.5f), Theme.Dim);
            _notice = MakeLabel("", Theme.Ui(8.5f), Theme.Warn);


            _btnVocab = Theme.Flat("♡ 生词本", OnVocabClick);
            _btnRetranslate = Theme.Flat("↻ 重译", OnRetranslateClick);
            _btnSpeak = Theme.Flat("🔊 朗读", OnSpeakClick);
            _meta = MakeLabel("", Theme.Ui(8f), Theme.Faint);
            _meta.TextAlign = ContentAlignment.MiddleRight;

            _hint = MakeLabel("", Theme.Ui(8.5f), Theme.Faint);

            Controls.Add(_logo);
            Controls.Add(_dir);
            Controls.Add(_engine);
            Controls.Add(_btnPin);
            Controls.Add(_btnCopy);
            Controls.Add(_btnClose);
            Controls.Add(_src);
            Controls.Add(_sep);
            Controls.Add(_trans);
            Controls.Add(_phon);
            Controls.Add(_senses);
            Controls.Add(_ctx);
            Controls.Add(_notice);

            Controls.Add(_btnVocab);
            Controls.Add(_btnRetranslate);
            Controls.Add(_btnSpeak);
            Controls.Add(_meta);
            Controls.Add(_hint);
        }

        private Label MakeLabel(string text, Font font, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            l.UseMnemonic = false;
            return l;
        }

        // ---------------- 交互 ----------------

        private void OnPinClick(object s, EventArgs e)
        {
            Pinned = !Pinned;
            _btnPin.Text = Pinned ? "已固定" : "固定";
            _btnPin.ForeColor = Pinned ? Theme.Accent : Theme.Faint;
            if (PinnedChanged != null) PinnedChanged(this, EventArgs.Empty);
        }

        private void OnCopyClick(object s, EventArgs e)
        {
            if (CopyRequested != null) CopyRequested(this, EventArgs.Empty);
            _btnCopy.Text = "已复制";
            var t = new Timer();
            t.Interval = 900;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                _btnCopy.Text = "复制";
            };
            t.Start();
        }

        private void OnCloseClick(object s, EventArgs e)
        {
            Hide();
            if (ClosedByUser != null) ClosedByUser(this, EventArgs.Empty);
        }

        private void OnVocabClick(object s, EventArgs e)
        {
            if (VocabRequested != null) VocabRequested(this, EventArgs.Empty);
        }

        private void OnSpeakClick(object s, EventArgs e)
        {
            if (SpeakTranslationRequested != null) SpeakTranslationRequested(this, EventArgs.Empty);
        }

        private void OnRetranslateClick(object s, EventArgs e)
        {
            if (RetranslateRequested != null) RetranslateRequested(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                if (ClosedByUser != null) ClosedByUser(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            // 顶部一条渐变高光
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, Width), S(3)), Color.FromArgb(90, Theme.Accent), Color.FromArgb(0, Theme.Accent), LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(b, 0, 0, Width, S(2));
            }
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (var path = Theme.RoundRect(new Rectangle(0, 0, Width, Height), S(8)))
                {
                    var old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        // ---------------- 内容与布局 ----------------

        public void Configure(int width)
        {
            if (width >= 300 && width <= 760) _width = width;
        }

        private int Measure(string text, Font font, int width, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int lineH = font.Height;
            int max = lineH * maxLines;
            return Math.Min(size.Height + S(2), max);
        }

        private void ResetContent()
        {
            _src.Text = "";
            _trans.Text = "";
            _phon.Text = "";
            _senses.Text = "";
            _ctx.Text = "";
            _notice.Text = "";
            _dir.Text = "";
            _engine.Text = "";
            _meta.Text = "";
            _hint.Text = "";
            _btnVocab.Text = "♡ 生词本";
            _btnVocab.ForeColor = Theme.Fg;
            Pinned = false;
            _btnPin.Text = "固定";
            _btnPin.ForeColor = Theme.Faint;
        }

        public void ShowLoadingText(string source)
        {
            ResetContent();
            Current = null;
            _src.Text = OneLine(source, 160);
            _trans.Text = "翻译中…";
            _trans.ForeColor = Theme.Dim;
            LayoutAll(true);
            ShowAtCursor();
        }

        public void ShowResultText(TranslationResult r, bool isVocab)
        {
            ResetContent();
            Current = r;
            IsVocab = isVocab;
            _trans.ForeColor = Theme.Fg;

            _src.Text = OneLine(r.Source, 200);
            _dir.Text = Lang.NameOf(r.From == "auto" ? (string.IsNullOrEmpty(r.Detected) ? "auto" : r.Detected) : r.From) + " → " + Lang.NameOf(r.To);
            _engine.Text = r.EngineLabel ?? "";
            _meta.Text = r.Elapsed > 0 ? (r.Elapsed + " ms") : "";

            bool isWord = r.Senses != null && r.Senses.Count > 0;
            _trans.Font = isWord ? Theme.UiBold(12f) : Theme.Ui(10.5f);
            _trans.Text = isWord ? r.Senses[0] : (r.Text ?? "");
            if (string.IsNullOrEmpty(_trans.Text)) _trans.Text = "（无译文）";

            if (!string.IsNullOrEmpty(r.Pos)) _phon.Text = r.Pos + (string.IsNullOrEmpty(r.Phonetic) ? "" : "  " + WrapPhonetic(r.Phonetic));
            else if (!string.IsNullOrEmpty(r.Phonetic)) _phon.Text = WrapPhonetic(r.Phonetic);

            if (isWord && r.Senses.Count > 1)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 1; i < r.Senses.Count && i <= 4; i++)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("· ").Append(r.Senses[i]);
                }
                _senses.Text = sb.ToString();
            }
            _ctx.Text = r.Ctx ?? "";
            _notice.Text = r.Degraded ?? "";

            try
            {
                _tip.SetToolTip(_btnSpeak, isWord ? "先读单词发音，再读释义" : "朗读译文");
                _btnSpeak.Text = isWord ? "🔊 发音" : "🔊 朗读";
            }
            catch { }

            _btnVocab.Text = isVocab ? "♥ 已收藏" : "♡ 生词本";
            _btnVocab.ForeColor = isVocab ? Theme.Ok : Theme.Fg;
            _isError = false;
            _btnVocab.Visible = true;
            _btnSpeak.Visible = true;

            LayoutAll(false);
            ShowAtCursor();
        }

        /// <summary>先弹一个"正在读取选区"的占位，避免取词慢时界面像死了</summary>
        public void ShowBusy(string hint)
        {
            ResetContent();
            _trans.ForeColor = Theme.Dim;
            _trans.Text = hint == null ? "正在读取选区…" : hint;
            _src.Text = "";
            LayoutAll(true);
            ShowAtCursor();
        }

        public void ShowErrorText(string message, string source)
        {
            ResetContent();
            Current = null;
            _src.Text = OneLine(source, 160);
            _trans.Text = "翻译失败";
            _trans.ForeColor = Theme.Danger;
            _hint.Text = message ?? "";
            _hint.ForeColor = Theme.Dim;
            _isError = true;
            _btnVocab.Visible = false;
            _btnSpeak.Visible = false;
            LayoutAll(true);
            ShowAtCursor();
        }

        private string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length > max) t = t.Substring(0, max) + "…";
            return t;
        }

        private string WrapPhonetic(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string t = p.Trim().Trim('/', '[', ']', ' ');
            if (t.Length == 0) return "";
            return "/" + t + "/";
        }

        private void LayoutAll(bool loading)
        {
            SuspendLayout();
            int padX = S(13);
            int padY = S(11);
            int inner = _width - padX * 2;
            int y = padY;

            // 头部
            int hh = S(20);
            _logo.SetBounds(padX, y, S(20), S(20));
            _logo.Visible = true;
            _btnClose.SetBounds(_width - padX - S(24), y, S(24), hh);
            _btnCopy.SetBounds(_btnClose.Left - S(44), y, S(44), hh);
            _btnPin.SetBounds(_btnCopy.Left - S(44), y, S(44), hh);
            int textLeft = _logo.Right + S(8);
            _engine.SetBounds(_btnPin.Left - S(70), y + S(2), S(68), S(16));
            _engine.TextAlign = ContentAlignment.MiddleRight;
            _dir.SetBounds(textLeft, y + S(2), Math.Max(0, _engine.Left - textLeft - S(6)), S(16));
            y += hh + S(6);

            // 原文
            int srcH = Measure(_src.Text, _src.Font, inner, 3);
            _src.SetBounds(padX, y, inner, srcH);
            y += srcH + S(6);

            // 分隔线
            _sep.SetBounds(padX, y, inner, 1);
            y += 1 + S(7);

            // 译文
            int transH = Measure(_trans.Text, _trans.Font, inner, 12);
            _trans.SetBounds(padX, y, inner, transH);
            y += transH + S(2);

            // 音标 / 词性
            if (_phon.Text.Length > 0)
            {
                _phon.Visible = true;
                _phon.SetBounds(padX, y, inner, S(16));
                y += S(17);
            }
            else _phon.Visible = false;

            // 义项
            if (_senses.Text.Length > 0)
            {
                _senses.Visible = true;
                int h = Measure(_senses.Text, _senses.Font, inner, 5);
                _senses.SetBounds(padX, y + S(2), inner, h);
                y += h + S(4);
            }
            else _senses.Visible = false;

            // 语境说明
            if (_ctx.Text.Length > 0)
            {
                _ctx.Visible = true;
                int h = Measure(_ctx.Text, _ctx.Font, inner - S(8), 3);
                _ctx.SetBounds(padX + S(8), y + S(2), inner - S(8), h);
                y += h + S(4);
            }
            else _ctx.Visible = false;

            // 降级提示
            if (_notice.Text.Length > 0)
            {
                _notice.Visible = true;
                int h = Measure(_notice.Text, _notice.Font, inner, 2);
                _notice.SetBounds(padX, y + S(2), inner, h);
                y += h + S(4);
            }
            else _notice.Visible = false;

            // 错误详情
            if (_hint.Text.Length > 0)
            {
                _hint.Visible = true;
                int h = Measure(_hint.Text, _hint.Font, inner, 4);
                _hint.SetBounds(padX, y + S(2), inner, h);
                y += h + S(4);
            }
            else _hint.Visible = false;



            // 底栏（出错时只留「重试」）
            int footH = S(24);
            int bx = padX;
            _btnVocab.Visible = !_isError && Current != null;
            _btnSpeak.Visible = !_isError && Current != null;
            _btnRetranslate.Text = _isError ? "↻ 重试" : "↻ 重译";
            if (_btnVocab.Visible)
            {
                _btnVocab.SetBounds(bx, y, S(84), footH);
                bx += S(88);
            }
            _btnRetranslate.SetBounds(bx, y, S(64), footH);
            bx += S(68);
            if (_btnSpeak.Visible) _btnSpeak.SetBounds(bx, y, S(72), footH);
            _meta.SetBounds(_width - padX - S(80), y, S(80), footH);
            y += footH + padY;

            Height = y;
            Width = _width;
            ResumeLayout(true);
            ApplyRegion();
        }

        /// <summary>定位到光标附近，自动避屏</summary>
        public void ShowAtCursor()
        {
            Native.POINT p = Native.CursorPos();
            ShowAt(p);
        }

        /// <summary>贴着选区文字显示：优先放在选区下方、与选区左端对齐；放不下就翻到上方</summary>
        public void ShowNearRect(int left, int top, int right, int bottom)
        {
            if (right < left) { int t = left; left = right; right = t; }
            if (bottom < top) { int t = top; top = bottom; bottom = t; }

            var mid = new Native.POINT();
            mid.X = left;
            mid.Y = bottom;
            var wa = Native.WorkAreaFor(mid);

            int gap = S(6);
            int x = left;
            int y = bottom + gap;

            if (y + Height > wa.Bottom - S(4)) y = top - Height - gap;
            if (y < wa.Top + S(4)) y = wa.Top + S(4);
            if (x + Width > wa.Right - S(8)) x = wa.Right - S(8) - Width;
            if (x < wa.Left + S(8)) x = wa.Left + S(8);

            Location = new Point(x, y);
            if (!Visible) Show();
            else BringToFront();
            Invalidate();
        }

        public void ShowAt(Native.POINT p)
        {
            var wa = Native.WorkAreaFor(p);
            int gap = S(14);
            int x = p.X + gap;
            int y = p.Y + gap;
            if (x + Width > wa.Right - S(4)) x = p.X - Width - gap;
            if (x < wa.Left + S(4)) x = wa.Left + S(4);
            if (y + Height > wa.Bottom - S(4)) y = p.Y - Height - gap;
            if (y < wa.Top + S(4)) y = wa.Top + S(4);
            Location = new Point(x, y);
            if (!Visible) Show();
            else BringToFront();
            Invalidate();
        }

        public bool ContainsScreenPoint(Native.POINT p)
        {
            if (!Visible) return false;
            var b = Bounds;
            return p.X >= b.Left && p.X <= b.Right && p.Y >= b.Top && p.Y <= b.Bottom;
        }

        public void HideSoft()
        {
            if (Visible) Hide();
            Pinned = false;
        }
    }
}

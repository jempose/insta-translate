using System;
using System.Drawing;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>简单输入框：托盘里的「输入翻译」</summary>
    public class InputBoxForm : Form
    {
        private TextBox _box;
        public string Value;

        public InputBoxForm()
        {
            Text = "输入翻译";
            Width = 560;
            Height = 180;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Font = Theme.Ui(9.5f);
            Icon = AppIcon.Get();

            var lbl = new Label();
            lbl.Text = "输入中文或英文，回车翻译（Esc 取消）";
            lbl.Location = new Point(18, 16);
            lbl.AutoSize = true;
            lbl.ForeColor = Color.FromArgb(90, 100, 115);
            Controls.Add(lbl);

            _box = new TextBox();
            _box.Location = new Point(18, 46);
            _box.Size = new Size(506, 30);
            _box.Font = Theme.Ui(11f);
            _box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Value = _box.Text;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            Controls.Add(_box);

            var ok = new Button();
            ok.Text = "翻译";
            ok.Location = new Point(316, 88);
            ok.Size = new Size(96, 32);
            ok.Click += delegate
            {
                Value = _box.Text;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(420, 88);
            cancel.Size = new Size(96, 32);
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += delegate { _box.Focus(); };
        }
    }
}

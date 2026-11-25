using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InstaTranslate
{
    /// <summary>配色、字体与常用控件工厂</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(27, 31, 39);
        public static readonly Color BgSoft = Color.FromArgb(35, 40, 50);
        public static readonly Color Fg = Color.FromArgb(242, 244, 248);
        public static readonly Color Dim = Color.FromArgb(163, 170, 185);
        public static readonly Color Faint = Color.FromArgb(120, 128, 142);
        public static readonly Color Accent = Color.FromArgb(91, 157, 255);
        public static readonly Color Accent2 = Color.FromArgb(138, 116, 255);
        public static readonly Color Ok = Color.FromArgb(70, 211, 154);
        public static readonly Color Danger = Color.FromArgb(255, 112, 112);
        public static readonly Color Warn = Color.FromArgb(240, 198, 116);
        public static readonly Color Line = Color.FromArgb(48, 54, 66);

        public static Font Ui(float size)
        {
            return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font UiBold(float size)
        {
            return new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font Mono(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Button Flat(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Line;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 54, 66);
            b.BackColor = Color.FromArgb(40, 46, 57);
            b.ForeColor = Fg;
            b.Font = Ui(8.5f);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Button Link(string text, EventHandler onClick)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 51, 63);
            b.BackColor = Color.Transparent;
            b.ForeColor = Faint;
            b.Font = Ui(8.5f);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            if (onClick != null) b.Click += onClick;
            return b;
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InstaTranslateSetup
{
    internal static class Program
    {
        internal const string AppName = "即时译";
        private const string AppId = "InstaTranslate";
        internal const string Version = "1.0.2";
        internal const string Publisher = "郑铮培";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false, silent = false;
            foreach (var a in args)
            {
                if (string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                if (string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
            }

            if (uninstall)
            {
                Uninstaller.Run(silent);
                return;
            }
            if (silent)
            {
                SilentInstall();
                return;
            }
            Application.Run(new SetupForm());
        }

        // ---------------- 公共 ----------------

        internal static string DefaultDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppId);
            }
        }

        internal static string InstallDir
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId, false))
                    {
                        if (k != null)
                        {
                            object v = k.GetValue("InstallLocation");
                            if (v != null && !string.IsNullOrEmpty(v.ToString())) return v.ToString();
                        }
                    }
                }
                catch { }
                return DefaultDir;
            }
        }

        internal static string StartMenuDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName); }
        }

        internal static string ShortcutPath(string folder)
        {
            return Path.Combine(folder, AppName + ".lnk");
        }

        internal static void CreateShortcut(string lnkPath, string target, string args)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                if (!string.IsNullOrEmpty(args)) lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { AppName + " - 选中即译" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex)
            {
                Log("创建快捷方式失败 " + lnkPath + "：" + ex.Message);
            }
        }

        internal static void Log(string line)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "InstaTranslateSetup");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "setup.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>静默安装（供自动化测试用）</summary>
        internal static void SilentInstall()
        {
            try
            {
                string dir = DefaultDir;
                Extract(dir);
                string self = Application.ExecutablePath;
                string uninst = Path.Combine(dir, "uninstall.exe");
                if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase)) File.Copy(self, uninst, true);
                string exe = Path.Combine(dir, "InstaTranslate.exe");
                CreateShortcut(ShortcutPath(StartMenuDir), exe, "");
                CreateShortcut(ShortcutPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), exe, "");
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", Publisher);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                Log("静默安装完成：" + dir);
            }
            catch (Exception ex)
            {
                Log("静默安装失败：" + ex);
            }
        }

        /// <summary>从自身资源里还原安装文件</summary>
        internal static void Extract(string destDir)
        {
            Directory.CreateDirectory(destDir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames())
            {
                string rel = res;
                if (rel.StartsWith("payload.", StringComparison.Ordinal)) rel = rel.Substring(8);
                rel = rel.Replace("__", "\\");
                string outPath = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                using (var s = asm.GetManifestResourceStream(res))
                using (var f = File.Create(outPath))
                {
                    byte[] buf = new byte[64 * 1024];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) f.Write(buf, 0, n);
                }
            }
        }
    }

    internal class SetupForm : Form
    {
        private TextBox _dir;
        private CheckBox _desktop;
        private CheckBox _launch;
        private Button _install;
        private Label _status;
        private ProgressBar _bar;

        public SetupForm()
        {
            Text = "即时译 · 安装";
            Width = 560;
            Height = 330;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(248, 249, 251);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 72;
            head.BackColor = Color.FromArgb(32, 38, 48);
            head.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, head.Width), 4), Color.FromArgb(91, 157, 255), Color.FromArgb(138, 116, 255), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b, 0, 0, head.Width, 4);
                using (var f1 = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold))
                using (var f2 = new Font("Microsoft YaHei UI", 9f))
                using (var w = new SolidBrush(Color.White))
                using (var d = new SolidBrush(Color.FromArgb(180, 190, 205)))
                {
                    e.Graphics.DrawString("即时译", f1, w, 20, 14);
                    e.Graphics.DrawString("选中即译 · 中英互译　v" + "1.0.0", f2, d, 104, 26);
                }
            };
            Controls.Add(head);

            var lbl = new Label();
            lbl.Text = "安装到：";
            lbl.AutoSize = true;
            lbl.Location = new Point(22, 96);
            Controls.Add(lbl);

            _dir = new TextBox();
            _dir.Location = new Point(88, 92);
            _dir.Width = 430;
            _dir.Text = Program.DefaultDir;
            Controls.Add(_dir);

            _desktop = new CheckBox();
            _desktop.Text = "在桌面创建快捷方式";
            _desktop.Checked = true;
            _desktop.Location = new Point(88, 132);
            _desktop.AutoSize = true;
            Controls.Add(_desktop);

            _launch = new CheckBox();
            _launch.Text = "安装完成后立即启动";
            _launch.Checked = true;
            _launch.Location = new Point(88, 160);
            _launch.AutoSize = true;
            Controls.Add(_launch);

            _bar = new ProgressBar();
            _bar.Location = new Point(22, 200);
            _bar.Width = 496;
            _bar.Height = 8;
            _bar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_bar);

            _status = new Label();
            _status.Location = new Point(22, 214);
            _status.Size = new Size(496, 40);
            _status.ForeColor = Color.FromArgb(90, 100, 115);
            _status.Text = "点「开始安装」。不需要管理员权限，装到当前用户目录。";
            Controls.Add(_status);

            _install = new Button();
            _install.Text = "开始安装";
            _install.Location = new Point(360, 258);
            _install.Size = new Size(158, 34);
            _install.Click += delegate { DoInstall(); };
            Controls.Add(_install);

            var cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(268, 258);
            cancel.Size = new Size(84, 34);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
        }

        private void DoInstall()
        {
            _install.Enabled = false;
            string dir = _dir.Text.Trim();
            try
            {
                Directory.CreateDirectory(dir);
                Program.Log("安装到 " + dir);
                _status.Text = "正在释放文件…";
                Application.DoEvents();
                Program.Extract(dir);

                // 卸载程序 = 安装包自身的一份拷贝
                string self = Application.ExecutablePath;
                string uninst = Path.Combine(dir, "uninstall.exe");
                if (!string.Equals(self, uninst, StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, uninst, true);

                string exe = Path.Combine(dir, "InstaTranslate.exe");
                _status.Text = "正在创建快捷方式…";
                Application.DoEvents();
                Program.CreateShortcut(Program.ShortcutPath(Program.StartMenuDir), exe, "");
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string dlnk = Program.ShortcutPath(desktop);
                if (_desktop.Checked) Program.CreateShortcut(dlnk, exe, "");
                else if (File.Exists(dlnk)) File.Delete(dlnk);

                _status.Text = "正在注册卸载信息…";
                Application.DoEvents();
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstaTranslate"))
                {
                    k.SetValue("DisplayName", "即时译");
                    k.SetValue("DisplayVersion", Program.Version);
                    k.SetValue("Publisher", Program.Publisher);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }

                _bar.Value = 100;
                _status.Text = "安装完成。";
                if (_launch.Checked)
                {
                    try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir }); } catch { }
                }
                MessageBox.Show(this,
                    "安装完成！\n\n程序位置：" + dir +
                    "\n启动方式：开始菜单 → 即时译；或双击桌面快捷方式" +
                    "\n卸载方式：设置 → 应用 → 即时译 → 卸载\n\n" +
                    "首次启动会在右下角托盘出现图标（可能在 ^ 展开区里）。",
                    "即时译", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _install.Enabled = true;
                _status.Text = "安装失败：" + ex.Message;
                MessageBox.Show(this, "安装失败：\n" + ex.Message, "即时译", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class Uninstaller
    {
        public static void Run(bool silent)
        {
            string dir = Program.InstallDir;
            if (!silent)
            {
                var r = MessageBox.Show(
                    "确定卸载「即时译」吗？\n\n程序目录：" + dir + "\n（翻译历史与生词本保留在 %APPDATA%\\InstaTranslate，不会删除）",
                    "卸载 即时译", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            try
            {
                // 先结束正在运行的实例
                foreach (var p in Process.GetProcessesByName("InstaTranslate"))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }

                // 快捷方式
                try { File.Delete(Program.ShortcutPath(Program.StartMenuDir)); } catch { }
                try { Directory.Delete(Program.StartMenuDir, true); } catch { }
                try { File.Delete(Program.ShortcutPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))); } catch { }

                // 注册表
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\InstaTranslate", false); } catch { }

                // 开机自启（如果开过）
                try
                {
                    var psi = new ProcessStartInfo("schtasks.exe", "/delete /tn InstaTranslate /f");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch { }

                // 删目录（自身可能在里面，交给 cmd 延迟删除）
                string self = Application.ExecutablePath;
                string cmd = "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"";
                var psi2 = new ProcessStartInfo("cmd.exe", cmd);
                psi2.CreateNoWindow = true;
                psi2.UseShellExecute = false;
                Process.Start(psi2);

                Program.Log("已卸载：" + dir);
                if (!silent)
                    MessageBox.Show("已卸载。\n\n如果目录里还有残留文件，等几秒后会自动删除。",
                        "即时译", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载时出错：" + ex.Message, "即时译", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

using System.Diagnostics;

namespace super_toolbox
{
    public partial class AboutForm : Form
    {
        private string browserSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser.txt");

        public AboutForm()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "关于-超级工具箱";
            this.Size = new Size(420, 350);

            SetupAboutContent();
        }

        private void SetupAboutContent()
        {
            var lblTitle = new Label
            {
                Text = "超级工具箱",
                Font = new Font("微软雅黑", 16, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Location = new Point(25, 15),
                AutoSize = true
            };
            Controls.Add(lblTitle);

            var lblVersion = new Label
            {
                Text = $"版本:{Application.ProductVersion}",
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray,
                Location = new Point(25, 50),
                AutoSize = true
            };
            Controls.Add(lblVersion);

            var lblDescription = new Label
            {
                Text = "一个功能强大的游戏解包工具箱和管理器\r\n\r\n" +
                       "包含上百种提取器、转换器、压缩/解压器和打包器",
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Black,
                Location = new Point(25, 80),
                Size = new Size(350, 80)
            };
            Controls.Add(lblDescription);

            var btnBrowserSetting = new Button
            {
                Text = "设置默认浏览器",
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(76, 175, 80),
                Location = new Point(25, 170),
                Size = new Size(150, 30),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnBrowserSetting.FlatAppearance.BorderSize = 0;
            btnBrowserSetting.Click += BtnBrowserSetting_Click;
            Controls.Add(btnBrowserSetting);

            var lblBrowserInfo = new Label
            {
                Text = GetBrowserDisplayText(),
                Font = new Font("微软雅黑", 8),
                ForeColor = Color.Gray,
                Location = new Point(180, 176),
                AutoSize = true
            };
            Controls.Add(lblBrowserInfo);

            var btnDocs = new Button
            {
                Text = "查看详细说明文档",
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.Blue,
                BackColor = Color.LightBlue,
                Location = new Point(25, 210),
                Size = new Size(350, 40),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnDocs.FlatAppearance.BorderColor = Color.Blue;
            btnDocs.FlatAppearance.BorderSize = 1;
            btnDocs.Click += (s, e) => OpenUrlWithBrowser("https://www.bilibili.com/opus/1173419824498343952#reply46226415", "详细说明文档");
            Controls.Add(btnDocs);

            var lblAuthor = new Label
            {
                Text = "超级工具箱-持续更新中",
                Font = new Font("微软雅黑", 8),
                ForeColor = Color.DarkGray,
                Location = new Point(25, 270),
                AutoSize = true
            };
            Controls.Add(lblAuthor);
        }

        private string? GetSavedBrowserPath()
        {
            try
            {
                if (File.Exists(browserSettingsFile))
                {
                    return File.ReadAllText(browserSettingsFile).Trim();
                }
            }
            catch { }
            return null;
        }

        private string GetBrowserDisplayText()
        {
            string? browserPath = GetSavedBrowserPath();
            if (!string.IsNullOrEmpty(browserPath) && File.Exists(browserPath))
            {
                return $"当前浏览器:{Path.GetFileNameWithoutExtension(browserPath)}";
            }
            return "当前浏览器:系统默认";
        }

        private void SaveBrowserPath(string browserPath)
        {
            try
            {
                File.WriteAllText(browserSettingsFile, browserPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存浏览器设置失败:{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectAndSaveBrowser()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择默认浏览器";
                dialog.Filter = "浏览器程序|*.exe";
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    SaveBrowserPath(dialog.FileName);
                    MessageBox.Show($"已设置默认浏览器为:{Path.GetFileNameWithoutExtension(dialog.FileName)}",
                        "设置成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    foreach (Control ctrl in Controls)
                    {
                        if (ctrl is Label && ctrl.Location.Y == 176 && ctrl.Location.X == 180)
                        {
                            ctrl.Text = GetBrowserDisplayText();
                            break;
                        }
                    }
                }
            }
        }

        private void BtnBrowserSetting_Click(object? sender, EventArgs e)
        {
            SelectAndSaveBrowser();
        }

        private void OpenUrlWithBrowser(string url, string resourceName)
        {
            try
            {
                string? browserPath = GetSavedBrowserPath();

                if (!string.IsNullOrEmpty(browserPath) && File.Exists(browserPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(
                    $"打开浏览器失败:{ex.Message}\n\n是否要重新设置浏览器?",
                    "错误",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error);

                if (result == DialogResult.Yes)
                {
                    SelectAndSaveBrowser();
                    OpenUrlWithBrowser(url, resourceName);
                }
            }
        }
    }
}
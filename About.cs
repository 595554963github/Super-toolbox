using System.Diagnostics;

namespace super_toolbox
{
    public partial class AboutForm : Form
    {
        private string browserSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser.txt");

        public AboutForm()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.Sizable; 
            this.MaximizeBox = true;  
            this.MinimizeBox = true;   
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "关于-超级工具箱";
            this.Size = new Size(420, 410);
            this.MinimumSize = new Size(400, 380);

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
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left 
            };
            Controls.Add(lblTitle);

            var lblVersion = new Label
            {
                Text = $"版本:{Application.ProductVersion}",
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Gray,
                Location = new Point(25, 50),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            Controls.Add(lblVersion);

            var lblDescription = new Label
            {
                Text = "一个功能强大的游戏解包工具箱和管理器\r\n\r\n" +
                       "包含上百种提取器、转换器、压缩/解压器和打包器",
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Black,
                Location = new Point(25, 80),
                Size = new Size(350, 80),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right 
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
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
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
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
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
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnDocs.FlatAppearance.BorderColor = Color.Blue;
            btnDocs.FlatAppearance.BorderSize = 1;
            btnDocs.Click += (s, e) => OpenUrlWithBrowser("https://www.bilibili.com/opus/1173419824498343952#reply46226415", "详细说明文档");
            Controls.Add(btnDocs);

            var lblDonorsTitle = new Label
            {
                Text = "❤️感谢以下朋友的赞助支持,排名不分先后,有遗漏的请私信通知我❤️",
                Font = new Font("微软雅黑", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 128, 0),
                Location = new Point(25, 265),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            Controls.Add(lblDonorsTitle);

            string[] donors = new string[]
            {"御坂银蛇","azoa9","卡柯卡基","紅い瞳に","一年小舞","紧差菊I吴克","水落清秋","明天就不开始","次元狸","苏子瑜o","-梦幻的月夜-","木木木木木酱","叶子三分青","枝江与卿长存我心","苏格拉没UD","帅气逼人小鱼干","煤洋洋","紫林旧主","Encore_Requiem","春是哈鲁",
            };

            var listDonors = new ListBox
            {
                Location = new Point(25, 290),
                Size = new Size(350, 70),
                Font = new Font("微软雅黑", 8),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(250, 250, 250),
                SelectionMode = SelectionMode.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right 
            };

            foreach (var donor in donors)
            {
                listDonors.Items.Add("❤" + donor);
            }
            Controls.Add(listDonors);

            var lblAuthor = new Label
            {
                Text = "超级工具箱-持续更新中",
                Font = new Font("微软雅黑", 8),
                ForeColor = Color.DarkGray,
                Location = new Point(25, 370),
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
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

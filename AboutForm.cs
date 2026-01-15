namespace super_toolbox
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "关于-超级工具箱";

            AddDescriptionText();
            ShowThanksMessage();
        }

        private void AddDescriptionText()
        {
            var lblDescription = new Label
            {
                Text = "超级工具箱是我开发的一个工具箱,可解包、打包、压缩、解压、提取,该项目会长期坚持更新,所有工具均是本人测试和添加,无旁人协助,说是倾尽心血也不为过,不管是汉化软件还是做解包工具,up本人都是凭良心做事,如果你看好此项目可以小小的支持一下,兜里没钱的可以无视,up绝不强求。",
                AutoSize = false,
                Size = new Size(300, 120),
                Font = new Font("微软雅黑", 9),
                ForeColor = Color.Black,
                Location = new Point(18, 12),
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(lblDescription);
        }

        private void ShowThanksMessage()
        {
            var lblThanks = new Label
            {
                Text = "如果你看好本工具,可以打赏支持一下,up会感激不尽～",
                AutoSize = true,
                Font = new Font("微软雅黑", 8, FontStyle.Bold),
                ForeColor = Color.DarkRed,
                Location = new Point((Width - 300) / 2, pictureBox1.Bottom + 20)
            };
            Controls.Add(lblThanks);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }
    }
}
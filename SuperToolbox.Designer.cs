namespace super_toolbox
{
    partial class SuperToolbox
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            treeView1 = new TreeView();
            treeViewContextMenu = new ContextMenuStrip(components);
            addNewCategoryMenuItem = new ToolStripMenuItem();
            renameCategoryMenuItem = new ToolStripMenuItem();
            deleteCategoryMenuItem = new ToolStripMenuItem();
            moveToCategoryMenuItem = new ToolStripMenuItem();
            txtFolderPath = new TextBox();
            btnExtract = new Button();
            richTextBox1 = new RichTextBox();
            btnClear = new Button();
            toolTip1 = new ToolTip(components);
            treeViewContextMenu.SuspendLayout();
            SuspendLayout();
            
            treeView1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            treeView1.ContextMenuStrip = treeViewContextMenu;
            treeView1.HideSelection = false;
            treeView1.Location = new Point(12, 12);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(265, 535);
            treeView1.TabIndex = 0;
            toolTip1.SetToolTip(treeView1, "每个工具都有不同的作用，具体使用方法请查看使用手册");
            treeView1.AfterSelect += treeView1_AfterSelect;
            
            treeViewContextMenu.ImageScalingSize = new Size(20, 20);
            treeViewContextMenu.Items.AddRange(new ToolStripItem[] { addNewCategoryMenuItem, renameCategoryMenuItem, deleteCategoryMenuItem, moveToCategoryMenuItem });
            treeViewContextMenu.Name = "treeViewContextMenu";
            treeViewContextMenu.Size = new Size(137, 92);
            treeViewContextMenu.Opening += treeViewContextMenu_Opening;
            
            addNewCategoryMenuItem.Name = "addNewCategoryMenuItem";
            addNewCategoryMenuItem.Size = new Size(136, 22);
            addNewCategoryMenuItem.Text = "添加新分组";
            addNewCategoryMenuItem.Click += addNewCategoryMenuItem_Click;
            
            renameCategoryMenuItem.Name = "renameCategoryMenuItem";
            renameCategoryMenuItem.Size = new Size(136, 22);
            renameCategoryMenuItem.Text = "编辑分组";
            renameCategoryMenuItem.Click += renameCategoryMenuItem_Click;
            
            deleteCategoryMenuItem.Name = "deleteCategoryMenuItem";
            deleteCategoryMenuItem.Size = new Size(136, 22);
            deleteCategoryMenuItem.Text = "删除分组";
            deleteCategoryMenuItem.Click += deleteCategoryMenuItem_Click;
            
            moveToCategoryMenuItem.Name = "moveToCategoryMenuItem";
            moveToCategoryMenuItem.Size = new Size(136, 22);
            moveToCategoryMenuItem.Text = "移动到分组";
            
            txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtFolderPath.ForeColor = SystemColors.ActiveCaptionText;
            txtFolderPath.Location = new Point(283, 12);
            txtFolderPath.Name = "txtFolderPath";
            txtFolderPath.Size = new Size(499, 23);
            txtFolderPath.TabIndex = 1;
            toolTip1.SetToolTip(txtFolderPath, "拖放一个文件夹到此");
            
            btnExtract.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExtract.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
            btnExtract.ForeColor = Color.SpringGreen;
            btnExtract.Location = new Point(788, 12);
            btnExtract.Name = "btnExtract";
            btnExtract.Size = new Size(88, 62);
            btnExtract.TabIndex = 3;
            btnExtract.Text = "开始";
            toolTip1.SetToolTip(btnExtract, "点击此按钮开始提取、转换、压缩、解压、打包");
            btnExtract.UseVisualStyleBackColor = true;
            btnExtract.Click += btnExtract_Click;
            
            richTextBox1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            richTextBox1.ForeColor = SystemColors.ActiveCaptionText;
            richTextBox1.Location = new Point(283, 80);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(593, 467);
            richTextBox1.TabIndex = 4;
            richTextBox1.Text = "";
            toolTip1.SetToolTip(richTextBox1, "你可以在此窗口查看提取出来的所有文件信息");
            
            btnClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClear.Font = new Font("Microsoft YaHei UI", 12F);
            btnClear.Location = new Point(694, 46);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(88, 28);
            btnClear.TabIndex = 5;
            btnClear.Text = "清空日志";
            toolTip1.SetToolTip(btnClear, "点击此按钮清空所有提取出来的文件信息");
            btnClear.UseVisualStyleBackColor = true;
            btnClear.Click += btnClear_Click;
            
            toolTip1.AutoPopDelay = 5000;
            toolTip1.InitialDelay = 500;
            toolTip1.ReshowDelay = 100;
            toolTip1.ShowAlways = true;
            
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(888, 571);
            Controls.Add(btnClear);
            Controls.Add(richTextBox1);
            Controls.Add(btnExtract);
            Controls.Add(txtFolderPath);
            Controls.Add(treeView1);
            MinimumSize = new Size(800, 600);
            Name = "SuperToolbox";
            Text = "超级工具箱";
            FormClosing += MainForm_FormClosing;
            treeViewContextMenu.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.TextBox txtFolderPath;
        private System.Windows.Forms.Button btnExtract;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.ContextMenuStrip treeViewContextMenu;
        private System.Windows.Forms.ToolStripMenuItem addNewCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem renameCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem deleteCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem moveToCategoryMenuItem;
        private ToolTip toolTip1;
    }
}

namespace super_toolbox
{
    partial class SuperToolbox
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
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
            btnSelectFolder = new Button();
            btnExtract = new Button();
            richTextBox1 = new RichTextBox();
            btnClear = new Button();
            treeViewContextMenu.SuspendLayout();
            SuspendLayout();
            // 
            // treeView1
            // 
            treeView1.ContextMenuStrip = treeViewContextMenu;
            treeView1.HideSelection = false;
            treeView1.Location = new Point(1, 14);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(272, 591);
            treeView1.TabIndex = 0;
            treeView1.AfterSelect += treeView1_AfterSelect;
            // 
            // treeViewContextMenu
            // 
            treeViewContextMenu.ImageScalingSize = new Size(20, 20);
            treeViewContextMenu.Items.AddRange(new ToolStripItem[] { addNewCategoryMenuItem, renameCategoryMenuItem, deleteCategoryMenuItem, moveToCategoryMenuItem });
            treeViewContextMenu.Name = "treeViewContextMenu";
            treeViewContextMenu.Size = new Size(137, 92);
            treeViewContextMenu.Opening += treeViewContextMenu_Opening;
            // 
            // addNewCategoryMenuItem
            // 
            addNewCategoryMenuItem.Name = "addNewCategoryMenuItem";
            addNewCategoryMenuItem.Size = new Size(136, 22);
            addNewCategoryMenuItem.Text = "添加新分组";
            addNewCategoryMenuItem.Click += addNewCategoryMenuItem_Click;
            // 
            // renameCategoryMenuItem
            // 
            renameCategoryMenuItem.Name = "renameCategoryMenuItem";
            renameCategoryMenuItem.Size = new Size(136, 22);
            renameCategoryMenuItem.Text = "编辑分组";
            renameCategoryMenuItem.Click += renameCategoryMenuItem_Click;
            // 
            // deleteCategoryMenuItem
            // 
            deleteCategoryMenuItem.Name = "deleteCategoryMenuItem";
            deleteCategoryMenuItem.Size = new Size(136, 22);
            deleteCategoryMenuItem.Text = "删除分组";
            deleteCategoryMenuItem.Click += deleteCategoryMenuItem_Click;
            // 
            // moveToCategoryMenuItem
            // 
            moveToCategoryMenuItem.Name = "moveToCategoryMenuItem";
            moveToCategoryMenuItem.Size = new Size(136, 22);
            moveToCategoryMenuItem.Text = "移动到分组";
            // 
            // txtFolderPath
            // 
            txtFolderPath.ForeColor = SystemColors.ActiveCaptionText;
            txtFolderPath.Location = new Point(279, 17);
            txtFolderPath.Name = "txtFolderPath";
            txtFolderPath.Size = new Size(411, 23);
            txtFolderPath.TabIndex = 1;
            // 
            // btnSelectFolder
            // 
            btnSelectFolder.Location = new Point(704, 12);
            btnSelectFolder.Name = "btnSelectFolder";
            btnSelectFolder.Size = new Size(88, 28);
            btnSelectFolder.TabIndex = 2;
            btnSelectFolder.Text = "选择文件夹";
            btnSelectFolder.UseVisualStyleBackColor = true;
            btnSelectFolder.Click += btnSelectFolder_Click;
            // 
            // btnExtract
            // 
            btnExtract.ForeColor = Color.SpringGreen;
            btnExtract.Location = new Point(704, 49);
            btnExtract.Name = "btnExtract";
            btnExtract.Size = new Size(88, 28);
            btnExtract.TabIndex = 3;
            btnExtract.Text = "开始";
            btnExtract.UseVisualStyleBackColor = true;
            btnExtract.Click += btnExtract_Click;
            // 
            // richTextBox1
            // 
            richTextBox1.ForeColor = SystemColors.ActiveCaptionText;
            richTextBox1.Location = new Point(279, 84);
            richTextBox1.Name = "richTextBox1";
            richTextBox1.Size = new Size(514, 521);
            richTextBox1.TabIndex = 4;
            richTextBox1.Text = "";
            // 
            // btnClear
            // 
            btnClear.Location = new Point(279, 49);
            btnClear.Name = "btnClear";
            btnClear.Size = new Size(88, 28);
            btnClear.TabIndex = 5;
            btnClear.Text = "清空日志";
            btnClear.UseVisualStyleBackColor = true;
            btnClear.Click += btnClear_Click;
            // 
            // SuperToolbox
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(815, 644);
            Controls.Add(btnClear);
            Controls.Add(richTextBox1);
            Controls.Add(btnExtract);
            Controls.Add(btnSelectFolder);
            Controls.Add(txtFolderPath);
            Controls.Add(treeView1);
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
        private System.Windows.Forms.Button btnSelectFolder;
        private System.Windows.Forms.Button btnExtract;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.ContextMenuStrip treeViewContextMenu;
        private System.Windows.Forms.ToolStripMenuItem addNewCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem renameCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem deleteCategoryMenuItem;
        private System.Windows.Forms.ToolStripMenuItem moveToCategoryMenuItem;
    }
}

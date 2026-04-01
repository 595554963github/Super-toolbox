using System.ComponentModel;
using System.Text;
namespace super_toolbox
{
    public partial class SuperToolbox : Form
    {
        private int totalFileCount;
        private int totalConvertedCount;
        private Dictionary<string, TreeNode> formatNodes = new Dictionary<string, TreeNode>();
        private Dictionary<string, TreeNode> categoryNodes = new Dictionary<string, TreeNode>();
        private readonly List<string>[] messageBuffers = new List<string>[2];
        private readonly object[] bufferLocks = { new object(), new object() };
        private int activeBufferIndex;
        private bool isUpdatingUI;
        private System.Windows.Forms.Timer updateTimer;
        private CancellationTokenSource extractionCancellationTokenSource;
        private const int UpdateInterval = 100;
        private const int MaxMessagesPerUpdate = 20;
        private bool isExtracting;
        private StatusStrip? statusStrip1;
        private ToolStripStatusLabel lblStatus;
        private ToolStripStatusLabel lblFileCount;
        private Preferences preferences;
        private Dictionary<string, bool> categoryExpansionState = new Dictionary<string, bool>();
        public SuperToolbox()
        {
            InitializeComponent();
            txtFolderPath.AllowDrop = true;
            txtFolderPath.DragEnter += TxtFolderPath_DragEnter;
            txtFolderPath.DragDrop += TxtFolderPath_DragDrop;
            txtFolderPath.DragLeave += TxtFolderPath_DragLeave;
            btnAbout.Click += BtnAbout_Click;
            preferences = Preferences.Load();
            statusStrip1 = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "就绪" };
            lblFileCount = new ToolStripStatusLabel { Text = "已提取:0个文件" };
            statusStrip1.Items.Add(lblStatus);
            statusStrip1.Items.Add(lblFileCount);
            statusStrip1.Dock = DockStyle.Bottom;
            this.Controls.Add(statusStrip1);
            InitializeTreeView();
            messageBuffers[0] = new List<string>(MaxMessagesPerUpdate);
            messageBuffers[1] = new List<string>(MaxMessagesPerUpdate);
            updateTimer = new System.Windows.Forms.Timer { Interval = UpdateInterval };
            updateTimer.Tick += UpdateUITimerTick;
            updateTimer.Start();
            extractionCancellationTokenSource = new CancellationTokenSource();

            treeView1.MouseMove += TreeView1_MouseMove;
            treeView1.AfterExpand += TreeView1_AfterExpand;
            treeView1.AfterCollapse += TreeView1_AfterCollapse;
        }
        private void InitializeTreeView()
        {
            foreach (string category in ExtractorRegistry.DefaultCategories.Values.Select(x => x.category).Distinct())
            {
                AddCategory(category);
            }

            var sortedExtractors = ExtractorRegistry.DefaultCategories
                .OrderBy(x => x.Key)
                .ToList();

            foreach (var item in sortedExtractors)
            {
                string extractorName = item.Key;
                string categoryName = item.Value.category;

                if (categoryNodes.TryGetValue(categoryName, out TreeNode? categoryNode))
                {
                    TreeNode extractorNode = categoryNode.Nodes.Add(extractorName);
                    formatNodes[extractorName] = extractorNode;
                    extractorNode.Tag = extractorName;
                }
            }

            foreach (TreeNode categoryNode in treeView1.Nodes)
            {
                var sortedChildren = categoryNode.Nodes
                    .Cast<TreeNode>()
                    .OrderBy(node => node.Text)
                    .ToList();

                categoryNode.Nodes.Clear();
                categoryNode.Nodes.AddRange(sortedChildren.ToArray());
                string categoryName = categoryNode.Text;
                bool shouldExpand = preferences.ExpandedCategories.ContainsKey(categoryName)
                    ? preferences.ExpandedCategories[categoryName]
                    : false;

                if (shouldExpand)
                {
                    categoryNode.Expand();
                }
                else
                {
                    categoryNode.Collapse();
                }

                categoryExpansionState[categoryName] = shouldExpand;
            }
        }
        private TreeNode AddCategory(string categoryName)
        {
            if (categoryNodes.ContainsKey(categoryName)) return categoryNodes[categoryName];
            TreeNode categoryNode = treeView1.Nodes.Add(categoryName);
            categoryNode.Tag = "category";
            categoryNodes[categoryName] = categoryNode;
            return categoryNode;
        }
        private bool IsConverter(string formatName) => ExtractorFactory.IsConverter(formatName);
        private BaseExtractor? CreateExtractor(string formatName)
        {
            try
            {
                return ExtractorFactory.Create(formatName);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private async void btnExtract_Click(object sender, EventArgs e)
        {
            if (isExtracting)
            {
                EnqueueMessage("正在进行操作,请等待...");
                return;
            }
            string dirPath = txtFolderPath.Text;
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                EnqueueMessage($"错误:{dirPath}不是一个有效的目录。");
                return;
            }
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string == "category")
            {
                EnqueueMessage("请选择你的操作");
                return;
            }
            string formatName = selectedNode.Text;
            bool isConverter = IsConverter(formatName);
            bool isPacker = formatName.EndsWith("打包器") ||
                           formatName.Contains("_pack") ||
                           formatName.Contains("_repack") ||
                           formatName.Contains("packer") ||
                           formatName.Contains("repacker");
            bool isCompressor = formatName.EndsWith("_compress") ||
                               formatName.Contains("压缩") ||
                               formatName.Contains("Compressor");
            bool isDecompressor = formatName.EndsWith("_decompress") ||
                                 formatName.Contains("解压") ||
                                 formatName.Contains("Decompressor");
            if (isConverter)
            {
                totalConvertedCount = 0;
            }
            else
            {
                totalFileCount = 0;
            }
            isExtracting = true;
            UpdateUIState(true);
            try
            {
                var extractor = CreateExtractor(formatName);
                if (extractor == null)
                {
                    EnqueueMessage($"错误:不支持{formatName}");
                    isExtracting = false;
                    UpdateUIState(false);
                    return;
                }
                string operationType = "提取";
                if (isConverter) operationType = "转换";
                else if (isPacker) operationType = "打包";
                else if (isCompressor) operationType = "压缩";
                else if (isDecompressor) operationType = "解压";
                EnqueueMessage($"开始{operationType}{formatName}...");
                SubscribeToExtractorEvents(extractor);
                await Task.Run(async () =>
                {
                    try
                    {
                        await extractor.ExtractAsync(dirPath, extractionCancellationTokenSource.Token);
                        this.Invoke(new Action(() =>
                        {
                            UpdateFileCountDisplay();
                            int count = isConverter ? totalConvertedCount : totalFileCount;
                            EnqueueMessage($"{operationType}操作完成,总共{operationType}了{count}个文件");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage($"{operationType}操作已取消");
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            EnqueueMessage($"{operationType}过程中出现错误:{ex.Message}");
                        }));
                    }
                    finally
                    {
                        this.Invoke(new Action(() =>
                        {
                            isExtracting = false;
                            UpdateUIState(false);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                EnqueueMessage($"操作初始化失败:{ex.Message}");
                isExtracting = false;
                UpdateUIState(false);
            }
        }
        private void SubscribeToExtractorEvents(BaseExtractor extractor)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            string selectedText = selectedNode?.Text ?? "";
            bool isConverter = IsConverter(selectedText);
            bool isPacker = selectedText.EndsWith("打包器") || selectedText.Contains("_repack");
            bool isCompressor = selectedText.EndsWith("_compress") || selectedText.Contains("压缩");
            bool isDecompressor = selectedText.EndsWith("_decompress") || selectedText.Contains("解压");
            if (isConverter)
            {
                extractor.FileConverted += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalConvertedCount);
                    EnqueueMessage($"已转换:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.ConversionCompleted += (s, count) =>
                {
                    EnqueueMessage($"转换完成,共转换{count}个文件");
                };
                extractor.ConversionFailed += (s, error) =>
                {
                    EnqueueMessage($"转换失败:{error}");
                };
            }
            else if (isPacker)
            {
                extractor.FilePacked += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已打包:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.PackingCompleted += (s, count) =>
                {
                    EnqueueMessage($"打包完成,共打包{count}个文件");
                };
                extractor.PackingFailed += (s, error) =>
                {
                    EnqueueMessage($"打包失败:{error}");
                };
            }
            else if (isCompressor)
            {
                extractor.FileCompressed += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已压缩:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.CompressionCompleted += (s, count) =>
                {
                    EnqueueMessage($"压缩完成,共压缩{count}个文件");
                };

                extractor.CompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"压缩失败:{error}");
                };
            }
            else if (isDecompressor)
            {
                extractor.FileDecompressed += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已解压:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.DecompressionCompleted += (s, count) =>
                {
                    EnqueueMessage($"解压完成,共解压{count}个文件");
                };
                extractor.DecompressionFailed += (s, error) =>
                {
                    EnqueueMessage($"解压失败:{error}");
                };
            }
            else
            {
                extractor.FileExtracted += (s, fileName) =>
                {
                    Interlocked.Increment(ref totalFileCount);
                    EnqueueMessage($"已提取:{Path.GetFileName(fileName)}");
                    UpdateFileCountDisplay();
                };
                extractor.ExtractionCompleted += (s, count) =>
                {
                };
                extractor.ExtractionFailed += (s, error) =>
                {
                    EnqueueMessage($"提取失败:{error}");
                };
            }
            extractor.ProgressUpdated += (s, progress) => { };
            var type = extractor.GetType();
            BindDynamicEvent(type, extractor, "ExtractionStarted", "提取开始");
            BindDynamicEvent(type, extractor, "ExtractionProgress", "提取进度");
            BindDynamicEvent(type, extractor, "ConversionStarted", "转换开始");
            BindDynamicEvent(type, extractor, "ConversionProgress", "转换进度");
            BindDynamicEvent(type, extractor, "PackingStarted", "打包开始");
            BindDynamicEvent(type, extractor, "PackingProgress", "打包进度");
            BindDynamicEvent(type, extractor, "CompressionStarted", "压缩开始");
            BindDynamicEvent(type, extractor, "CompressionProgress", "压缩进度");
            BindDynamicEvent(type, extractor, "DecompressionStarted", "解压开始");
            BindDynamicEvent(type, extractor, "DecompressionProgress", "解压进度");
            BindDynamicEvent(type, extractor, "ExtractionError", "错误", true);
            BindDynamicEvent(type, extractor, "ConversionError", "错误", true);
            BindDynamicEvent(type, extractor, "PackingError", "错误", true);
            BindDynamicEvent(type, extractor, "CompressionError", "错误", true);
            BindDynamicEvent(type, extractor, "DecompressionError", "错误", true);
        }
        private void BindDynamicEvent(Type type, BaseExtractor extractor, string eventName, string prefix, bool isError = false)
        {
            var eventInfo = type.GetEvent(eventName);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(extractor, new EventHandler<string>((s, message) =>
                {
                    string formattedMessage = isError ? $"错误:{message}" : $"{prefix}: {message}";
                    EnqueueMessage(formattedMessage);
                }));
            }
        }
        private void btnClear_Click(object sender, EventArgs e)
        {
            lock (bufferLocks[0]) { messageBuffers[0].Clear(); }
            lock (bufferLocks[1]) { messageBuffers[1].Clear(); richTextBox1.Clear(); }
            totalFileCount = 0;
            totalConvertedCount = 0;
            UpdateFileCountDisplay();
        }
        private void EnqueueMessage(string message)
        {
            int bufferIndex = activeBufferIndex;
            lock (bufferLocks[bufferIndex])
            {
                if (messageBuffers[bufferIndex].Count >= MaxMessagesPerUpdate && !isUpdatingUI)
                {
                    activeBufferIndex = (activeBufferIndex + 1) % 2;
                    bufferIndex = activeBufferIndex;
                }
                messageBuffers[bufferIndex].Add(message);
            }
        }
        private void UpdateUITimerTick(object? sender, EventArgs e)
        {
            if (isUpdatingUI) return;
            int inactiveBufferIndex = (activeBufferIndex + 1) % 2;
            object bufferLock = bufferLocks[inactiveBufferIndex];
            List<string>? messagesToUpdate = null;
            lock (bufferLock)
            {
                if (messageBuffers[inactiveBufferIndex].Count > 0)
                {
                    isUpdatingUI = true;
                    messagesToUpdate = new List<string>(messageBuffers[inactiveBufferIndex]);
                    messageBuffers[inactiveBufferIndex].Clear();
                }
            }
            if (messagesToUpdate != null && messagesToUpdate.Count > 0) UpdateRichTextBox(messagesToUpdate);
            else isUpdatingUI = false;
        }
        private void UpdateRichTextBox(List<string> messages)
        {
            if (richTextBox1.IsDisposed || richTextBox1.Disposing) { isUpdatingUI = false; return; }
            if (richTextBox1.InvokeRequired)
            {
                try { richTextBox1.Invoke(new Action(() => UpdateRichTextBoxInternal(messages))); }
                catch { isUpdatingUI = false; return; }
            }
            else UpdateRichTextBoxInternal(messages);
        }
        private void UpdateRichTextBoxInternal(List<string> messages)
        {
            if (statusStrip1 == null || lblFileCount == null || richTextBox1.IsDisposed)
            {
                isUpdatingUI = false;
                return;
            }

            try
            {
                richTextBox1.SuspendLayout();

                bool isAtBottom = IsRichTextBoxAtBottom();
                int firstVisibleIndex = richTextBox1.GetCharIndexFromPosition(new Point(0, 0));

                StringBuilder sb = new StringBuilder();
                foreach (string message in messages)
                {
                    sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
                }
                richTextBox1.AppendText(sb.ToString());

                richTextBox1.PerformLayout();

                if (isAtBottom)
                {
                    richTextBox1.BeginInvoke(new Action(() =>
                    {
                        richTextBox1.SelectionStart = richTextBox1.TextLength;
                        richTextBox1.ScrollToCaret();
                    }));
                }
                else
                {
                    richTextBox1.BeginInvoke(new Action(() =>
                    {
                        richTextBox1.SelectionStart = firstVisibleIndex;
                        richTextBox1.ScrollToCaret();
                    }));
                }

                richTextBox1.ResumeLayout();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateRichTextBoxInternal错误:{ex.Message}");
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        private bool IsRichTextBoxAtBottom()
        {
            if (richTextBox1.TextLength == 0) return true;

            int lastCharIndex = richTextBox1.TextLength - 1;
            Point lastCharPos = richTextBox1.GetPositionFromCharIndex(lastCharIndex);
            int clientBottom = richTextBox1.ClientSize.Height;

            return lastCharPos.Y <= clientBottom + 10;
        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node != null && lblStatus != null)
            {
                lblStatus.Text = e.Node.Tag as string == "category"
                    ? $"已选择:{e.Node.Text} (分组)"
                    : $"已选择:{e.Node.Text}";
            }
        }
        private void TreeView1_MouseMove(object? sender, MouseEventArgs e)
        {
            TreeNode? node = treeView1.GetNodeAt(e.Location);
            if (node != null && node.Tag as string != "category")
            {
                if (ExtractorRegistry.DefaultCategories.ContainsKey(node.Text))
                {
                    string description = ExtractorRegistry.DefaultCategories[node.Text].description;
                    toolTip1.SetToolTip(treeView1, description);
                }
                else
                {
                    toolTip1.SetToolTip(treeView1, "该工具的具体说明");
                }
            }
            else
            {
                toolTip1.SetToolTip(treeView1, "每个工具都有不同的作用");
            }
        }
        private void treeViewContextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (treeView1.SelectedNode == null)
            {
                e.Cancel = false;
                moveToCategoryMenuItem.Visible = false;
                renameCategoryMenuItem.Visible = false;
                deleteCategoryMenuItem.Visible = false;
                addNewCategoryMenuItem.Visible = true;
                return;
            }
            bool isCategory = treeView1.SelectedNode.Tag as string == "category";
            moveToCategoryMenuItem.Visible = !isCategory;
            renameCategoryMenuItem.Visible = isCategory;
            deleteCategoryMenuItem.Visible = isCategory && treeView1.SelectedNode.Nodes.Count == 0 &&
                !ExtractorRegistry.DefaultCategories.Values.Select(x => x.category).Contains(treeView1.SelectedNode.Text);
            addNewCategoryMenuItem.Visible = true;
            moveToCategoryMenuItem.DropDownItems.Clear();
            if (!isCategory)
            {
                foreach (string category in categoryNodes.Keys)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(category);
                    item.Click += (s, args) => MoveSelectedNodeToCategory(category);
                    moveToCategoryMenuItem.DropDownItems.Add(item);
                }
            }
        }
        private void MoveSelectedNodeToCategory(string category)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Parent == null || selectedNode.Tag as string == "category")
                return;

            TreeNode? targetCategory = categoryNodes.ContainsKey(category) ? categoryNodes[category] : null;
            if (targetCategory == null || selectedNode.Parent == targetCategory)
                return;
            selectedNode.Remove();
            targetCategory.Nodes.Add(selectedNode);
            var sortedChildren = targetCategory.Nodes
                .Cast<TreeNode>()
                .OrderBy(node => node.Text)
                .ToList();
            targetCategory.Nodes.Clear();
            targetCategory.Nodes.AddRange(sortedChildren.ToArray());
            var oldParent = selectedNode.Parent;
            if (oldParent != null)
            {
                var oldSortedChildren = oldParent.Nodes
                    .Cast<TreeNode>()
                    .OrderBy(node => node.Text)
                    .ToList();
                oldParent.Nodes.Clear();
                oldParent.Nodes.AddRange(oldSortedChildren.ToArray());
            }
            treeView1.SelectedNode = selectedNode;
            EnqueueMessage($"已将{selectedNode.Text}移动到{category}分组");
        }
        private void UpdateUIState(bool isExtracting)
        {
            btnExtract.Enabled = !isExtracting;
            treeView1.Enabled = !isExtracting;
            if (lblStatus != null) lblStatus.Text = isExtracting ? "正在处理..." : "就绪";
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
                extractionCancellationTokenSource?.Cancel();
                extractionCancellationTokenSource?.Dispose();
                preferences?.Save();
            }
            catch { }
            base.OnFormClosing(e);
        }
        private void UpdateFileCountDisplay()
        {
            if (lblFileCount != null)
            {
                TreeNode selectedNode = treeView1.SelectedNode;
                if (selectedNode != null)
                {
                    string selectedText = selectedNode.Text;
                    bool isConverter = IsConverter(selectedText);
                    bool isPacker = selectedText.EndsWith("打包器") ||
                                   selectedText.Contains("_pack") ||
                                   selectedText.Contains("_repack") ||
                                   selectedText.Contains("packer") ||
                                   selectedText.Contains("repacker");
                    bool isCompressor = selectedText.EndsWith("_compress") ||
                                       selectedText.Contains("压缩") ||
                                       selectedText.Contains("Compressor");
                    bool isDecompressor = selectedText.EndsWith("_decompress") ||
                                         selectedText.Contains("解压") ||
                                         selectedText.Contains("Decompressor");
                    if (isConverter)
                    {
                        lblFileCount.Text = $"已转换:{totalConvertedCount}个文件";
                    }
                    else if (isPacker)
                    {
                        lblFileCount.Text = $"已打包:{totalFileCount}个文件";
                    }
                    else if (isCompressor)
                    {
                        lblFileCount.Text = $"已压缩:{totalFileCount}个文件";
                    }
                    else if (isDecompressor)
                    {
                        lblFileCount.Text = $"已解压:{totalFileCount}个文件";
                    }
                    else
                    {
                        lblFileCount.Text = $"已提取出:{totalFileCount}个文件";
                    }
                }
                else
                {
                    lblFileCount.Text = $"已提取出:{totalFileCount}个文件";
                }
            }
        }
        private string ShowInputDialog(string title, string prompt, string initialValue = "")
        {
            string result = string.Empty;

            Form dialog = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(330, 130)
            };
            Label label = new Label
            {
                Text = prompt,
                Location = new Point(20, 20),
                AutoSize = true
            };
            TextBox textBox = new TextBox
            {
                Text = initialValue,
                Location = new Point(20, 45),
                Size = new Size(285, 23),
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            Button okButton = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(140, 80),
                Size = new Size(75, 23)
            };
            Button cancelButton = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(230, 80),
                Size = new Size(75, 23)
            };
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                result = textBox.Text ?? string.Empty;
            }
            return result;
        }
        private void addNewCategoryMenuItem_Click(object sender, EventArgs e)
        {
            string categoryName = ShowInputDialog("添加新分组", "请输入分组名称:");
            if (!string.IsNullOrEmpty(categoryName))
            {
                if (string.IsNullOrEmpty(categoryName.Trim()))
                {
                    MessageBox.Show("分组名称不能为空!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(categoryName))
                {
                    MessageBox.Show($"分组'{categoryName}'已存在!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TreeNode newCategory = AddCategory(categoryName);
                treeView1.SelectedNode = newCategory;
                treeView1.ExpandAll();
                EnqueueMessage($"已添加新分组:{categoryName}");
            }
        }
        private void renameCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行编辑!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (ExtractorRegistry.DefaultCategories.Values.Select(x => x.category).Contains(selectedNode.Text))
            {
                MessageBox.Show("不能编辑默认分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string newName = ShowInputDialog("编辑分组", "请输入新的分组名称:", selectedNode.Text);
            if (!string.IsNullOrEmpty(newName))
            {
                if (string.IsNullOrEmpty(newName.Trim()))
                {
                    MessageBox.Show("分组名称不能为空!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (categoryNodes.ContainsKey(newName))
                {
                    MessageBox.Show($"分组'{newName}'已存在!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string oldName = selectedNode.Text;
                categoryNodes.Remove(oldName);
                selectedNode.Text = newName;
                categoryNodes[newName] = selectedNode;
                EnqueueMessage($"已将分组'{oldName}'重命名为:{newName}");
            }
        }
        private void deleteCategoryMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeView1.SelectedNode;
            if (selectedNode == null || selectedNode.Tag as string != "category")
            {
                MessageBox.Show("请选择一个分组进行删除!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selectedNode.Nodes.Count > 0)
            {
                MessageBox.Show("无法删除非空分组,请先将其中的提取器移至其他分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (ExtractorRegistry.DefaultCategories.Values.Select(x => x.category).Contains(selectedNode.Text))
            {
                MessageBox.Show("不能删除默认分组!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (MessageBox.Show($"确定要删除分组'{selectedNode.Text}'吗?", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string categoryName = selectedNode.Text;
                selectedNode.Remove();
                categoryNodes.Remove(categoryName);
                EnqueueMessage($"已删除分组:{categoryName}");
            }
        }
        private void TxtFolderPath_DragEnter(object? sender, DragEventArgs e)
        {
            if (txtFolderPath == null) return;

            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                {
                    e.Effect = DragDropEffects.Copy;
                    txtFolderPath.BackColor = Color.Green;
                }
                else
                {
                    e.Effect = DragDropEffects.None;
                }
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void TxtFolderPath_DragDrop(object? sender, DragEventArgs e)
        {
            if (txtFolderPath == null) return;

            txtFolderPath.BackColor = SystemColors.Window;

            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];

                if (files != null && files.Length == 1 && Directory.Exists(files[0]))
                {
                    txtFolderPath.Text = files[0];
                    EnqueueMessage($"已通过拖放选择文件夹:{files[0]}");
                }
                else
                {
                    EnqueueMessage("错误:请拖放单个文件夹");
                }
            }
        }
        private void TxtFolderPath_DragLeave(object? sender, EventArgs e)
        {
            if (txtFolderPath == null) return;

            txtFolderPath.BackColor = SystemColors.Window;
        }
        private void BtnAbout_Click(object? sender, EventArgs e)
        {
            try
            {
                using (var aboutForm = new AboutForm())
                {
                    aboutForm.StartPosition = FormStartPosition.CenterParent;
                    aboutForm.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开关于窗口:{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
        private void TreeView1_AfterExpand(object? sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag as string == "category")
            {
                string categoryName = e.Node.Text;
                categoryExpansionState[categoryName] = true;
                preferences.ExpandedCategories[categoryName] = true;
                preferences.Save();
            }
        }

        private void TreeView1_AfterCollapse(object? sender, TreeViewEventArgs e)
        {
            if (e.Node != null && e.Node.Tag as string == "category")
            {
                string categoryName = e.Node.Text;
                categoryExpansionState[categoryName] = false;
                preferences.ExpandedCategories[categoryName] = false;
                preferences.Save();
            }
        }
        private void BtnAudioPlayer_Click(object? sender, EventArgs e)
        {
            var playerForm = new AudioPlayerForm();
            playerForm.StartPosition = FormStartPosition.CenterParent;
            playerForm.Show(this); 
            playerForm.FormClosed += (s, args) => playerForm.Dispose();
        }
    }
}
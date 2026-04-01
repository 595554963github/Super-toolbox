using System.Media;
using VGAudio.Containers.Adx;
using VGAudio.Containers.Dsp;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Hps;
using VGAudio.Containers.Idsp;
using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;
using VGAudio.Formats;

namespace super_toolbox
{
    public class AudioPlayerForm : Form
    {
        private ListView listViewFiles = null!;
        private Label lblFile = null!;
        private Label lblStatus = null!;
        private Label lblTime = null!;
        private ProgressBar progressBar = null!;
        private ComboBox cboFormat = null!;
        private System.Windows.Forms.Timer updateTimer = null!;
        private ToolTip toolTip = null!;
        private Button btnClearList = null!;
        private Button btnPrev = null!;
        private Button btnPlay = null!;
        private Button btnPause = null!;
        private Button btnStop = null!;
        private Button btnNext = null!;
        private ColumnHeader colIndex = null!;
        private ColumnHeader colFileName = null!;
        private ColumnHeader colDuration = null!;
        private Panel topPanel = null!;
        private Panel controlPanel = null!;
        private Panel infoPanel = null!;
        private TableLayoutPanel mainLayout = null!;

        private CancellationTokenSource? _cancellation;
        private string? _tempFile;
        private List<string> _playlist = new List<string>();
        private List<TimeSpan> _durations = new List<TimeSpan>();
        private int _currentIndex = -1;
        private bool _isPlaying;
        private bool _isPaused;
        private DateTime _playStartTime;
        private TimeSpan _totalDuration;
        private TimeSpan _pausedElapsed;
        private MemoryStream? _currentWavStream;
        private SoundPlayer? _soundPlayer;

        public AudioPlayerForm()
        {
            InitializeComponent();
            FormClosing += AudioPlayerForm_FormClosing;
            AllowDrop = true;
            DragEnter += AudioPlayerForm_DragEnter;
            DragDrop += AudioPlayerForm_DragDrop;
            this.Resize += AudioPlayerForm_Resize;
        }

        private void InitializeComponent()
        {
            Text = "音频播放器";
            Size = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 500);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(230, 230, 230);

            toolTip = new ToolTip();

            mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            topPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 140,
                BackColor = Color.FromArgb(45, 45, 48),
                Margin = new Padding(0, 0, 0, 1)
            };

            controlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.FromArgb(45, 45, 48)
            };

            string[] buttonTexts = { "⏮", "▶", "⏸", "■", "⏭" };
            string[] buttonToolTips = { "上一首", "播放", "暂停", "停止", "下一首" };

            int buttonWidth = 55;
            int buttonHeight = 45;
            int buttonSpacing = 10;
            int totalWidth = buttonWidth * 5 + buttonSpacing * 4;

            int startX = (controlPanel.Width - totalWidth) / 2;
            if (startX < 10) startX = 10;

            for (int i = 0; i < 5; i++)
            {
                var btn = new Button
                {
                    Text = buttonTexts[i],
                    Size = new Size(buttonWidth, buttonHeight),
                    Font = new Font("Segoe UI", i == 1 ? 14F : 12F),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = i == 1 ? Color.FromArgb(0, 120, 215) : Color.FromArgb(63, 63, 70),
                    ForeColor = Color.White,
                    Enabled = false,
                    Tag = i
                };

                btn.Location = new Point(startX + i * (buttonWidth + buttonSpacing), (controlPanel.Height - buttonHeight) / 2);

                toolTip.SetToolTip(btn, buttonToolTips[i]);
                controlPanel.Controls.Add(btn);

                if (i == 0) btnPrev = btn;
                else if (i == 1) btnPlay = btn;
                else if (i == 2) btnPause = btn;
                else if (i == 3) btnStop = btn;
                else if (i == 4) btnNext = btn;
            }

            controlPanel.Resize += (s, e) => {
                int newStartX = (controlPanel.Width - totalWidth) / 2;
                if (newStartX < 10) newStartX = 10;
                for (int i = 0; i < controlPanel.Controls.Count; i++)
                {
                    var btn = controlPanel.Controls[i];
                    btn.Location = new Point(newStartX + i * (buttonWidth + buttonSpacing), (controlPanel.Height - buttonHeight) / 2);
                }
            };

            btnPrev.Click += BtnPrev_Click;
            btnPlay.Click += BtnPlay_Click;
            btnPause.Click += BtnPause_Click;
            btnStop.Click += BtnStop_Click;
            btnNext.Click += BtnNext_Click;

            infoPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(20, 10, 20, 10)
            };

            lblFile = new Label
            {
                Text = "未选择文件",
                Location = new Point(20, 15),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 120, 215),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(20, 45),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(160, 160, 160),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            lblTime = new Label
            {
                Text = "00:00 / 00:00",
                Location = new Point(infoPanel.Width - 150, 25),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 120, 215),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            progressBar = new ProgressBar
            {
                Location = new Point(20, 80),
                Width = infoPanel.Width - 40,
                Height = 6,
                Style = ProgressBarStyle.Continuous,
                ForeColor = Color.FromArgb(0, 120, 215),
                BackColor = Color.FromArgb(63, 63, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            infoPanel.Controls.AddRange(new Control[] { lblFile, lblStatus, lblTime, progressBar });
            infoPanel.Resize += (s, e) => {
                lblTime.Left = infoPanel.Width - lblTime.Width - 20;
                progressBar.Width = infoPanel.Width - 40;
            };

            topPanel.Controls.Add(controlPanel);
            topPanel.Controls.Add(infoPanel);
            controlPanel.Dock = DockStyle.Top;
            infoPanel.Dock = DockStyle.Fill;

            colIndex = new ColumnHeader { Text = "序号", Width = 50, TextAlign = HorizontalAlignment.Center };
            colFileName = new ColumnHeader { Text = "音频文件", Width = 0 };
            colDuration = new ColumnHeader { Text = "音频时长", Width = 80, TextAlign = HorizontalAlignment.Right };

            listViewFiles = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Segoe UI", 9F),
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listViewFiles.Columns.AddRange(new ColumnHeader[] { colIndex, colFileName, colDuration });
            listViewFiles.Resize += ListViewFiles_Resize;
            listViewFiles.SelectedIndexChanged += ListViewFiles_SelectedIndexChanged;
            listViewFiles.DoubleClick += ListViewFiles_DoubleClick;

            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(12, 10, 12, 10)
            };

            btnClearList = new Button
            {
                Text = "清空列表",
                Location = new Point(12, 12),
                Size = new Size(90, 27),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };
            btnClearList.Click += BtnClearList_Click;

            cboFormat = new ComboBox
            {
                Location = new Point(110, 12),
                Size = new Size(180, 27),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Segoe UI", 9F),
                FlatStyle = FlatStyle.Flat
            };
            cboFormat.Items.AddRange(new string[]
            {
                "目前支持的所有格式",
                "aiff","adx","ahx","at3","at9","bcstm","bcwav","bfstm","bfwav",
                "binka","brstm","brwav","dsp","flac","hca","hps","idsp","lopus",
                "mdsp","msf","mtaf","opus","qoa","rada","swav","tta","vag","wav",
                "wem","xma","xwma"
            });
            cboFormat.SelectedIndex = 0;

            bottomPanel.Controls.AddRange(new Control[] { btnClearList, cboFormat });

            Panel listPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 12, 12, 12)
            };
            listPanel.Controls.Add(listViewFiles);

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(listPanel, 0, 1);

            Controls.Add(mainLayout);
            Controls.Add(bottomPanel);

            updateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void AudioPlayerForm_Resize(object? sender, EventArgs e)
        {
            if (infoPanel != null)
            {
                lblTime.Left = infoPanel.Width - lblTime.Width - 20;
                progressBar.Width = infoPanel.Width - 40;
            }
        }

        private void ListViewFiles_Resize(object? sender, EventArgs e)
        {
            if (listViewFiles.Columns.Count >= 3 && listViewFiles.Width > 0)
            {
                int totalWidth = listViewFiles.ClientSize.Width;
                colIndex.Width = 50;
                colDuration.Width = 80;
                colFileName.Width = totalWidth - colIndex.Width - colDuration.Width - 4;
                if (colFileName.Width < 100) colFileName.Width = 100;
            }
        }

        private void BtnClearList_Click(object? sender, EventArgs e)
        {
            BtnStop_Click(null, EventArgs.Empty);
            _playlist.Clear();
            _durations.Clear();
            listViewFiles.Items.Clear();
            _currentIndex = -1;
            UpdateButtonStates();
        }

        private void AudioPlayerForm_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void AudioPlayerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (!(e?.Data?.GetDataPresent(DataFormats.FileDrop) ?? false)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 0) return;

            if (Directory.Exists(files[0]))
            {
                LoadFolder(files[0]);
            }
            else
            {
                List<string> audioFiles = new List<string>();
                foreach (var f in files)
                    if (IsSupportedFile(f)) audioFiles.Add(f);
                if (audioFiles.Count > 0)
                {
                    _playlist = audioFiles;
                    _durations = new List<TimeSpan>(new TimeSpan[audioFiles.Count]);
                    RefreshPlaylist();
                    UpdateButtonStates();
                }
            }
        }

        private bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower().TrimStart('.');
            string[] supported =
            {
                "adx", "ahx", "aiff", "at3", "at9",
                "bcstm", "bcwav", "bfstm", "bfwav", "binka", "brstm", "brwav",
                "dsp",
                "flac",
                "hca", "hps",
                "idsp",
                "lopus",
                "mdsp", "msf", "mtaf",
                "opus",
                "qoa",
                "rada",
                "swav",
                "tta",
                "vag",
                "wav", "wem",
                "xma", "xwma"
            };
            return supported.Contains(ext);
        }
        private void LoadFolder(string folderPath)
        {
            _playlist.Clear();
            _durations.Clear();
            string[] exts =
            {
                "*.adx", "*.ahx", "*.aiff", "*.at3", "*.at9",
                "*.bcstm", "*.bcwav", "*.bfstm", "*.bfwav", "*.binka", "*.brstm", "*.brwav",
                "*.dsp",
                "*.flac",
                "*.hca", "*.hps",
                "*.idsp",
                "*.lopus",
                "*.mdsp", "*.msf", "*.mtaf",
                "*.opus",
                "*.qoa",
                "*.rada",
                "*.swav",
                "*.tta",
                "*.vag",
                "*.wav", "*.wem",
                "*.xma", "*.xwma"
            };

            foreach (var ext in exts)
            {
                try
                {
                    _playlist.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.AllDirectories));
                }
                catch
                {
                }
            }

            _durations = new List<TimeSpan>(new TimeSpan[_playlist.Count]);
            RefreshPlaylist();
            lblStatus.Text = _playlist.Count > 0 ? $"已加载{_playlist.Count}个音频" : "未找到已支持的音频文件";
            UpdateButtonStates();
        }

        private void RefreshPlaylist()
        {
            listViewFiles.Items.Clear();
            for (int i = 0; i < _playlist.Count; i++)
            {
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(Path.GetFileName(_playlist[i]));
                bool hasDuration = _durations[i] != TimeSpan.Zero;
                item.SubItems.Add(hasDuration ? $"{_durations[i]:mm\\:ss}" : "未知");
                if (hasDuration)
                {
                    item.ForeColor = Color.LightGreen;
                }
                else
                {
                    item.ForeColor = Color.FromArgb(230, 230, 230);
                }

                listViewFiles.Items.Add(item);
            }

            if (listViewFiles.Columns.Count >= 3)
            {
                int totalWidth = listViewFiles.ClientSize.Width;
                colIndex.Width = 50;
                colDuration.Width = 80;
                colFileName.Width = totalWidth - colIndex.Width - colDuration.Width - 4;
                if (colFileName.Width < 100) colFileName.Width = 100;
            }
        }

        private void UpdateDuration(int index, TimeSpan duration)
        {
            if (index >= 0 && index < _durations.Count && _durations[index] == TimeSpan.Zero)
            {
                _durations[index] = duration;
                if (index < listViewFiles.Items.Count)
                {
                    listViewFiles.Items[index].SubItems[2].Text = $"{duration:mm\\:ss}";
                    listViewFiles.Items[index].ForeColor = Color.LightGreen;
                }
            }

            if (index == _currentIndex)
            {
                if (InvokeRequired)
                {
                    Invoke(() => {
                        lblTime.Text = $"00:00 / {duration:mm\\:ss}";
                        _totalDuration = duration;
                    });
                }
                else
                {
                    lblTime.Text = $"00:00 / {duration:mm\\:ss}";
                    _totalDuration = duration;
                }
            }
        }

        private void ListViewFiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (listViewFiles.SelectedIndices.Count > 0)
            {
                _currentIndex = listViewFiles.SelectedIndices[0];
                lblFile.Text = Path.GetFileName(_playlist[_currentIndex]);
                toolTip.SetToolTip(lblFile, _playlist[_currentIndex]);

                if (_durations[_currentIndex] != TimeSpan.Zero)
                {
                    lblTime.Text = $"00:00 / {_durations[_currentIndex]:mm\\:ss}";
                }
                else
                {
                    lblTime.Text = "00:00 / 未知";
                }

                progressBar.Value = 0;
            }
            UpdateButtonStates();
        }

        private void ListViewFiles_DoubleClick(object? sender, EventArgs e)
        {
            if (_currentIndex >= 0) PlayCurrentFile();
        }

        private async void PlayCurrentFile()
        {
            if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;
            lblStatus.Text = "解码中...";
            progressBar.Value = 0;

            if (_durations[_currentIndex] != TimeSpan.Zero)
            {
                lblTime.Text = $"00:00 / {_durations[_currentIndex]:mm\\:ss}";
            }
            else
            {
                lblTime.Text = "00:00 / 解码中...";
            }

            _cancellation?.Cancel();
            _cancellation = new CancellationTokenSource();
            try
            {
                await PlayAsync(_playlist[_currentIndex], _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "已停止";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"播放失败:{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "播放失败";
            }
            finally
            {
                UpdateButtonStates();
            }
        }

        private void BtnPrev_Click(object? sender, EventArgs e)
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                listViewFiles.SelectedIndices.Clear();
                listViewFiles.Items[_currentIndex].Selected = true;
                PlayCurrentFile();
            }
        }

        private async void BtnPlay_Click(object? sender, EventArgs e)
        {
            if (_isPaused)
            {
                _isPaused = false; _isPlaying = true;
                _playStartTime = DateTime.Now - _pausedElapsed;
                updateTimer.Start(); _soundPlayer?.Play();
                lblStatus.Text = "播放中..."; UpdateButtonStates();
                return;
            }
            if (_currentIndex >= 0 && _currentIndex < _playlist.Count) PlayCurrentFile();
            else if (_playlist.Count > 0) { _currentIndex = 0; listViewFiles.Items[0].Selected = true; PlayCurrentFile(); }
        }

        private void BtnPause_Click(object? sender, EventArgs e)
        {
            if (!_isPlaying) return;
            _isPaused = true; _isPlaying = false;
            _pausedElapsed = DateTime.Now - _playStartTime;
            updateTimer.Stop(); _soundPlayer?.Stop();
            lblStatus.Text = "已暂停"; UpdateButtonStates();
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            _cancellation?.Cancel();
            _isPlaying = false; _isPaused = false;
            updateTimer.Stop(); _soundPlayer?.Stop();
            lblStatus.Text = "已停止"; progressBar.Value = 0;

            if (_currentIndex >= 0 && _currentIndex < _durations.Count && _durations[_currentIndex] != TimeSpan.Zero)
            {
                lblTime.Text = $"00:00 / {_durations[_currentIndex]:mm\\:ss}";
            }
            else
            {
                lblTime.Text = "00:00 / 00:00";
            }

            UpdateButtonStates();
        }

        private void BtnNext_Click(object? sender, EventArgs e)
        {
            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
                listViewFiles.SelectedIndices.Clear();
                listViewFiles.Items[_currentIndex].Selected = true;
                PlayCurrentFile();
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying) return;
            TimeSpan elapsed = DateTime.Now - _playStartTime;
            if (elapsed > _totalDuration) elapsed = _totalDuration;
            if (_totalDuration.TotalSeconds > 0)
            {
                int pct = (int)(elapsed.TotalSeconds / _totalDuration.TotalSeconds * 100);
                progressBar.Value = Math.Clamp(pct, 0, 100);
            }
            lblTime.Text = $"{elapsed:mm\\:ss} / {_totalDuration:mm\\:ss}";

            if (elapsed >= _totalDuration && _isPlaying)
            {
                _isPlaying = false;
                updateTimer.Stop();
                _soundPlayer?.Stop();
                lblStatus.Text = "播放结束";

                if (_currentIndex < _playlist.Count - 1)
                {
                    _currentIndex++;
                    listViewFiles.SelectedIndices.Clear();
                    listViewFiles.Items[_currentIndex].Selected = true;
                    PlayCurrentFile();
                }
                else
                {
                    progressBar.Value = 0;
                    if (_currentIndex >= 0 && _currentIndex < _durations.Count && _durations[_currentIndex] != TimeSpan.Zero)
                    {
                        lblTime.Text = $"00:00 / {_durations[_currentIndex]:mm\\:ss}";
                    }
                    else
                    {
                        lblTime.Text = "00:00 / 00:00";
                    }
                    UpdateButtonStates();
                }
            }
        }

        private async Task PlayAsync(string filePath, CancellationToken ct)
        {
            string sel = cboFormat.SelectedItem?.ToString() ?? "";
            string fmt = sel == "目前支持的所有格式"
                ? Path.GetExtension(filePath).TrimStart('.').ToLower() : sel.ToLower();
            if (string.IsNullOrEmpty(fmt)) throw new Exception("无法识别的格式");

            _currentWavStream?.Dispose();
            _soundPlayer?.Dispose();
            string tmpWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            MemoryStream? msStream = null;
            try
            {
                var result = await DecodeToWavStreamWithDuration(filePath, fmt, ct);
                msStream = result.Stream;
                _totalDuration = result.Duration;

                if (InvokeRequired)
                {
                    Invoke(() => UpdateDuration(_currentIndex, _totalDuration));
                }
                else
                {
                    UpdateDuration(_currentIndex, _totalDuration);
                }

                msStream.Position = 0;
                using (FileStream fs = new FileStream(tmpWav, FileMode.Create, FileAccess.Write))
                    msStream.WriteTo(fs);

                _currentWavStream = msStream;
                _soundPlayer = new SoundPlayer(tmpWav);

                if (InvokeRequired)
                {
                    Invoke(() => {
                        lblStatus.Text = "播放中...";
                        _playStartTime = DateTime.Now;
                        _isPlaying = true;
                        _isPaused = false;
                        updateTimer.Start();
                        UpdateButtonStates();
                    });
                }
                else
                {
                    lblStatus.Text = "播放中...";
                    _playStartTime = DateTime.Now;
                    _isPlaying = true;
                    _isPaused = false;
                    updateTimer.Start();
                    UpdateButtonStates();
                }

                _soundPlayer.Play();

                while (_isPlaying && !ct.IsCancellationRequested)
                {
                    await Task.Delay(100, ct);
                }
            }
            catch (OperationCanceledException)
            {
                if (InvokeRequired)
                    Invoke(() => lblStatus.Text = "已停止");
                else
                    lblStatus.Text = "已停止";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"播放失败:{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (InvokeRequired)
                    Invoke(() => lblStatus.Text = "播放失败");
                else
                    lblStatus.Text = "播放失败";
            }
            finally
            {
                if (File.Exists(tmpWav))
                {
                    try { File.Delete(tmpWav); } catch { }
                }
                if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile))
                {
                    try { File.Delete(_tempFile); } catch { }
                    _tempFile = null;
                }
            }
        }

        private async Task<(MemoryStream Stream, TimeSpan Duration)> DecodeToWavStreamWithDuration(string path, string fmt, CancellationToken ct)
        {
            if (fmt == "wav")
            {
                var ms = new MemoryStream();
                using (var fs = File.OpenRead(path))
                {
                    await fs.CopyToAsync(ms, ct);
                }
                ms.Position = 0;
                return (ms, GetWavDuration(ms));
            }

            switch (fmt)
            {
                case "hca":
                case "adx":
                case "bcstm":
                case "bfstm":
                case "brstm":
                case "dsp":
                case "hps":
                case "idsp":
                case "mdsp":
                    return await DecodeWithVGAudio(path, fmt, ct);
                default:
                    return await DecodeWithTempFile(path, fmt, ct);
            }
        }

        private async Task<(MemoryStream Stream, TimeSpan Duration)> DecodeWithVGAudio(string path, string fmt, CancellationToken ct)
        {
            return await Task.Run(() => {
                AudioData? ad = null;
                using (var fs = File.OpenRead(path))
                {
                    switch (fmt)
                    {
                        case "hca": ad = new HcaReader().Read(fs); break;
                        case "adx": ad = new AdxReader().Read(fs); break;
                        case "bcstm": case "bfstm": ad = new BCFstmReader().Read(fs); break;
                        case "brstm": ad = new BrstmReader().Read(fs); break;
                        case "dsp": case "mdsp": ad = new DspReader().Read(fs); break;
                        case "hps": ad = new HpsReader().Read(fs); break;
                        case "idsp": ad = new IdspReader().Read(fs); break;
                    }
                }
                if (ad == null) throw new Exception("读取音频数据失败");

                var pcmFormat = ad.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>();
                TimeSpan duration = TimeSpan.FromSeconds((double)pcmFormat.SampleCount / pcmFormat.SampleRate);

                MemoryStream ms = new MemoryStream();
                new WaveWriter().WriteToStream(ad, ms);
                ms.Position = 0;
                return (ms, duration);
            }, ct);
        }


        private async Task<(MemoryStream Stream, TimeSpan Duration)> DecodeWithTempFile(string path, string fmt, CancellationToken ct)
        {
            if (fmt == "binka")
            {
                byte[] header = new byte[4];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 4) throw new Exception("无效的binka文件");
                    fs.Read(header, 0, 4);
                }
                if (!header.SequenceEqual(new byte[] { 0x41, 0x42, 0x45, 0x55 }))
                {
                    throw new Exception("该binka格式不支持解码,请使用vgmstream或foobar2000");
                }
            }
            if (fmt == "xma")
            {
                byte[] sig = new byte[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length >= 0x16)
                    {
                        fs.Seek(0x10, SeekOrigin.Begin);
                        fs.Read(sig, 0, 6);
                    }
                }
                if (sig.SequenceEqual(new byte[] { 0x00, 0x90, 0x01, 0x00, 0x2C, 0x00 }))
                {
                    throw new Exception("该xma格式不支持解码,请使用vgmstream或foobar2000");
                }
            }

            if (fmt == "wem")
            {
                byte[] sig = new byte[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length >= 0x16)
                    {
                        fs.Seek(0x10, SeekOrigin.Begin);
                        fs.Read(sig, 0, 6);
                    }
                }
                if (!sig.SequenceEqual(new byte[] { 0x18, 0x00, 0x00, 0x00, 0xFE, 0xFF }))
                {
                    throw new Exception("该wem格式不支持解码,请使用vgmstream或foobar2000");
                }
            }
            if (fmt == "at3")
            {
                byte[] sig = new byte[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length >= 0x16)
                    {
                        fs.Seek(0x10, SeekOrigin.Begin);
                        fs.Read(sig, 0, 6);
                    }
                    else
                    {
                        throw new Exception("无效的AT3文件");
                    }
                }
                if (!sig.SequenceEqual(new byte[] { 0x34, 0x00, 0x00, 0x00, 0xFE, 0xFF }))
                {
                    throw new Exception("该at3格式不支持解码,请使用vgmstream或foobar2000");
                }
            }

            _tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            await Task.Run(() => {
                BaseExtractor? ext = null;
                if (fmt == "xma")
                {
                    ext = TryXmaDecoders(path, ct);
                }
                else
                {
                    ext = CreateExtractorByFormat(fmt);
                }
                if (ext == null) throw new Exception("无对应解码器");

                string tmpDir = Path.GetTempPath();
                string sf = Path.Combine(tmpDir, Guid.NewGuid() + Path.GetExtension(path));
                File.Copy(path, sf, true);

                try
                {
                    var task = ext.ExtractAsync(tmpDir, ct);
                    task.Wait(ct);

                    string wavOut = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(sf) + ".wav");
                    if (File.Exists(wavOut))
                    {
                        wavOut = ConvertToPcmWav(wavOut, tmpDir);

                        if (File.Exists(_tempFile)) File.Delete(_tempFile);
                        File.Move(wavOut, _tempFile);
                    }
                }
                finally
                {
                    try { File.Delete(sf); } catch { }
                }
            }, ct);

            if (!File.Exists(_tempFile)) throw new Exception("未生成wav");
            MemoryStream st = new MemoryStream();
            using (FileStream rd = File.OpenRead(_tempFile)) rd.CopyTo(st);
            st.Position = 0;
            TimeSpan duration = GetWavDuration(st);
            st.Position = 0;
            return (st, duration);
        }
        private TimeSpan GetWavDuration(MemoryStream wavStream)
        {
            try
            {
                long pos = wavStream.Position;
                wavStream.Position = 0;

                byte[] header = new byte[44];
                wavStream.Read(header, 0, 44);

                int channels = BitConverter.ToInt16(header, 22);
                int sampleRate = BitConverter.ToInt32(header, 24);
                int bitsPerSample = BitConverter.ToInt16(header, 34);

                wavStream.Position = 12;
                long dataSize = 0;
                while (wavStream.Position < wavStream.Length - 8)
                {
                    byte[] chunkId = new byte[4];
                    wavStream.Read(chunkId, 0, 4);
                    int chunkSize = BitConverter.ToInt32(new byte[4] {
                (byte)wavStream.ReadByte(), (byte)wavStream.ReadByte(),
                (byte)wavStream.ReadByte(), (byte)wavStream.ReadByte() }, 0);

                    if (System.Text.Encoding.ASCII.GetString(chunkId) == "data")
                    {
                        dataSize = chunkSize;
                        break;
                    }
                    wavStream.Position += chunkSize;
                }

                wavStream.Position = pos;

                if (dataSize > 0 && sampleRate > 0 && channels > 0 && bitsPerSample > 0)
                {
                    double bytesPerSecond = sampleRate * channels * (bitsPerSample / 8.0);
                    return TimeSpan.FromSeconds(dataSize / bytesPerSecond);
                }
            }
            catch { }

            return TimeSpan.FromSeconds(30);
        }
        private string ConvertToPcmWav(string inputWav, string outputDir)
        {
            string outputPath = Path.Combine(outputDir, Guid.NewGuid() + "_pcm.wav");

            var reader = new WaveReader();
            using (var fs = File.OpenRead(inputWav))
            {
                var audioData = reader.Read(fs);
                if (audioData == null) throw new Exception("无法读取音频数据");

                using (var ms = new MemoryStream())
                {
                    new WaveWriter().WriteToStream(audioData, ms);
                    ms.Position = 0;
                    using (var outFs = new FileStream(outputPath, FileMode.Create))
                    {
                        ms.WriteTo(outFs);
                    }
                }
            }

            try { File.Delete(inputWav); } catch { }
            return outputPath;
        }

        private BaseExtractor? TryXmaDecoders(string path, CancellationToken ct)
        {
            byte[] sig = new byte[6];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length < 0x16) return null;
                fs.Seek(0x10, SeekOrigin.Begin);
                fs.Read(sig, 0, 6);
            }

            if (sig.SequenceEqual(new byte[] { 0x20, 0x00, 0x00, 0x00, 0x65, 0x01 }) ||
                sig.SequenceEqual(new byte[] { 0x34, 0x00, 0x00, 0x00, 0x66, 0x01 }))
                return new Xma2wav1_Converter();

            if (sig.SequenceEqual(new byte[] { 0x14, 0x00, 0x00, 0x00, 0x69, 0x00 }))
                return new Xma2wav2_Converter();

            if (sig.SequenceEqual(new byte[] { 0x14, 0x00, 0x00, 0x00, 0x11, 0x00 }))
                return new Xma2wav3_Converter();

            if (sig.SequenceEqual(new byte[] { 0x32, 0x00, 0x00, 0x00, 0x02, 0x00 }))
                return new Xma2wav4_Converter();

            return null;
        }

        private BaseExtractor? CreateExtractorByFormat(string fmt)
        {
            return fmt switch
            {
                "aiff" => new Aiff2wav_Converter(),
                "ahx" => new Ahx2wav_Converter(),
                "at3" => new At32wav_Converter(),
                "at9" => new At92wav_Converter(),
                "bcwav" => new Bcwav2wav_Converter(),
                "bfwav" => new Bfwav2wav_Converter(),
                "brwav" => new Brwav2wav_Converter(),
                "flac" => new Flac2wav_Converter(),
                "wem" => new Wem2wav_Converter(),
                "opus" => new Opus2wav_Converter(),
                "lopus" => new Lopus2wav_Converter(),
                "binka" => new Binka2wav_Converter(),
                "rada" => new Rada2wav_Converter(),
                "xwma" => new Xwma2wav_Converter(),
                "msf" => new Msf2wav_Converter(),
                "mtaf" => new Mtaf2wav_Converter(),
                "qoa" => new Qoa2wav_Converter(),
                "swav" => new Swav2wav_Converter(),
                "tta" => new Tta2wav_Converter(),
                "vag" => new Vag2wav_Converter(),
                _ => null
            };
        }

        private void UpdateButtonStates()
        {
            bool hasList = _playlist.Count > 0;
            bool hasSel = _currentIndex >= 0;
            bool ok = hasList && hasSel;
            btnPrev.Enabled = hasSel && _currentIndex > 0;
            btnNext.Enabled = hasSel && _currentIndex < _playlist.Count - 1;
            btnPlay.Enabled = ok && !_isPlaying;
            btnPause.Enabled = _isPlaying;
            btnStop.Enabled = ok && (_isPlaying || _isPaused);
        }

        private void AudioPlayerForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _cancellation?.Cancel(); updateTimer?.Stop();
            _soundPlayer?.Stop(); _soundPlayer?.Dispose();
            _currentWavStream?.Dispose();
            if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile)) try { File.Delete(_tempFile); } catch { }
        }
    }
}
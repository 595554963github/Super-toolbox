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
        private ListBox listBoxFiles = null!;
        private Label lblFile = null!;
        private Label lblStatus = null!;
        private Label lblTime = null!;
        private ProgressBar progressBar = null!;
        private ComboBox cboFormat = null!;
        private System.Windows.Forms.Timer updateTimer = null!;
        private FolderBrowserDialog folderBrowserDialog = null!;
        private ToolTip toolTip = null!;
        private Button btnSelectFolder = null!;
        private Button btnClearList = null!;
        private Button btnPrev = null!;
        private Button btnPlay = null!;
        private Button btnPause = null!;
        private Button btnStop = null!;
        private Button btnNext = null!;

        private CancellationTokenSource? _cancellation;
        private string? _tempFile;
        private List<string> _playlist = new List<string>();
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
        }

        private void InitializeComponent()
        {
            Text = "音频播放器";
            Size = new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 400);

            toolTip = new ToolTip();
            folderBrowserDialog = new FolderBrowserDialog();

            listBoxFiles = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(250, 370),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            listBoxFiles.SelectedIndexChanged += ListBoxFiles_SelectedIndexChanged;
            listBoxFiles.DoubleClick += ListBoxFiles_DoubleClick;

            btnSelectFolder = new Button
            {
                Text = "加载文件夹",
                Location = new Point(12, 390),
                Size = new Size(110, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnSelectFolder.Click += BtnSelectFolder_Click;
            toolTip.SetToolTip(btnSelectFolder, "选择包含音频文件的文件夹或拖放音频文件到此窗口");
            btnClearList = new Button
            {
                Text = "清空列表",
                Location = new Point(130, 390),
                Size = new Size(110, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnClearList.Click += BtnClearList_Click;
            toolTip.SetToolTip(btnClearList, "点击此按钮清空播放列表");
            cboFormat = new ComboBox
            {
                Location = new Point(12, 425),
                Size = new Size(228, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            cboFormat.Items.AddRange(new string[]
            {
                "目前支持的所有格式",
                "adx","ahx","at3","at9","bcstm","bcwav","bfstm","bfwav",
                "binka","brstm","brwav","dsp","hca","hps","idsp","lopus",
                "mdsp","msf","mtaf","opus","qoa","rada","swav","vag",
                "wem","xma","xwma"
            });
            cboFormat.SelectedIndex = 0;

            lblFile = new Label
            {
                Text = "未选择文件",
                Location = new Point(280, 12),
                Size = new Size(480, 25),
                AutoSize = false
            };

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(280, 45),
                Size = new Size(200, 25),
                AutoSize = false
            };

            lblTime = new Label
            {
                Text = "00:00 / 00:00",
                Location = new Point(620, 45),
                Size = new Size(140, 25),
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false
            };

            progressBar = new ProgressBar
            {
                Location = new Point(280, 75),
                Size = new Size(480, 10),
                Style = ProgressBarStyle.Continuous
            };

            btnPrev = new Button
            {
                Text = "⏮",
                Location = new Point(320, 100),
                Size = new Size(60, 40),
                Font = new Font("Segoe UI", 12F),
                Enabled = false
            };
            btnPrev.Click += BtnPrev_Click;

            btnPlay = new Button
            {
                Text = "▶",
                Location = new Point(390, 100),
                Size = new Size(60, 40),
                Font = new Font("Segoe UI", 14F),
                Enabled = false
            };
            btnPlay.Click += BtnPlay_Click;

            btnPause = new Button
            {
                Text = "⏸",
                Location = new Point(460, 100),
                Size = new Size(60, 40),
                Font = new Font("Segoe UI", 12F),
                Enabled = false
            };
            btnPause.Click += BtnPause_Click;

            btnStop = new Button
            {
                Text = "■",
                Location = new Point(530, 100),
                Size = new Size(60, 40),
                Font = new Font("Segoe UI", 12F),
                Enabled = false
            };
            btnStop.Click += BtnStop_Click;

            btnNext = new Button
            {
                Text = "⏭",
                Location = new Point(600, 100),
                Size = new Size(60, 40),
                Font = new Font("Segoe UI", 12F),
                Enabled = false
            };
            btnNext.Click += BtnNext_Click;

            updateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            updateTimer.Tick += UpdateTimer_Tick;

            Controls.AddRange(new Control[]
            {
                listBoxFiles,btnSelectFolder,btnClearList,cboFormat,
                lblFile,lblStatus,lblTime,progressBar,
                btnPrev,btnPlay,btnPause,btnStop,btnNext
            });
        }

        private void BtnClearList_Click(object? sender, EventArgs e)
        {
            BtnStop_Click(null, EventArgs.Empty);
            _playlist.Clear();
            listBoxFiles.Items.Clear();
            _currentIndex = -1;
            lblFile.Text = "未选择文件";
            lblStatus.Text = "列表已清空";
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
                    RefreshPlaylist();
                    UpdateButtonStates();
                }
            }
        }

        private bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower().TrimStart('.');
            string[] supported = {"hca","adx","bcstm","bfstm","brstm","dsp","hps","idsp","mdsp",
        "bcwav","bfwav","brwav","wem","xma","ahx","at3","at9","opus","lopus","binka","rada",
        "xwma","msf","mtaf","qoa","swav","vag"};
            return supported.Contains(ext);
        }

        private void BtnSelectFolder_Click(object? sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                LoadFolder(folderBrowserDialog.SelectedPath);
        }

        private void LoadFolder(string folderPath)
        {
            _playlist.Clear();
            string[] exts = {"*.hca","*.adx","*.bcstm","*.bfstm","*.brstm","*.dsp","*.hps","*.idsp","*.mdsp",
        "*.bcwav","*.bfwav","*.brwav","*.wem","*.xma","*.ahx","*.at3","*.at9","*.opus","*.lopus","*.binka","*.rada",
        "*.xwma","*.msf","*.mtaf","*.qoa","*.swav","*.vag"};
            foreach (var ext in exts)
            {
                try { _playlist.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.AllDirectories)); } catch { }
            }
            RefreshPlaylist();
            lblStatus.Text = _playlist.Count > 0 ? $"已加载{_playlist.Count}个音频":"未找到已支持的音频文件";
            UpdateButtonStates();
        }

        private void RefreshPlaylist()
        {
            listBoxFiles.Items.Clear();
            foreach (var f in _playlist) listBoxFiles.Items.Add(Path.GetFileName(f));
        }

        private void ListBoxFiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (listBoxFiles.SelectedIndex >= 0 && listBoxFiles.SelectedIndex < _playlist.Count)
            {
                _currentIndex = listBoxFiles.SelectedIndex;
                lblFile.Text = Path.GetFileName(_playlist[_currentIndex]);
                toolTip.SetToolTip(lblFile, _playlist[_currentIndex]);
            }
            UpdateButtonStates();
        }

        private void ListBoxFiles_DoubleClick(object? sender, EventArgs e)
        {
            if (_currentIndex >= 0) PlayCurrentFile();
        }

        private async void PlayCurrentFile()
        {
            if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;
            lblStatus.Text = "解码中...";
            progressBar.Value = 0;
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
                listBoxFiles.SelectedIndex = _currentIndex;
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
            else if (_playlist.Count > 0) { _currentIndex = 0; listBoxFiles.SelectedIndex = 0; PlayCurrentFile(); }
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
            lblTime.Text = "00:00 / 00:00";
            UpdateButtonStates();
        }

        private void BtnNext_Click(object? sender, EventArgs e)
        {
            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
                listBoxFiles.SelectedIndex = _currentIndex;
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
            if (elapsed >= _totalDuration) { BtnStop_Click(null, EventArgs.Empty); lblStatus.Text = "播放结束"; }
        }

        private async Task PlayAsync(string filePath, CancellationToken ct)
        {
            string sel = cboFormat.SelectedItem?.ToString() ?? "";
            string fmt = sel == "目前支持的所有格式"
                ? Path.GetExtension(filePath).TrimStart('.').ToLower() : sel.ToLower();
            if (string.IsNullOrEmpty(fmt)) throw new Exception("无法识别的格式");

            _currentWavStream?.Dispose(); _soundPlayer?.Dispose();
            string tmpWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            MemoryStream? msStream = null;
            try
            {
                msStream = await DecodeToWavStream(filePath, fmt, ct);
                msStream.Position = 0;
                using (FileStream fs = new FileStream(tmpWav, FileMode.Create, FileAccess.Write))
                    msStream.WriteTo(fs);
                _totalDuration = EstimateDuration(msStream);
                _currentWavStream = msStream;
                _soundPlayer = new SoundPlayer(tmpWav);
                Invoke(() => {
                    lblStatus.Text = "播放中..."; _playStartTime = DateTime.Now;
                    _isPlaying = true; _isPaused = false; updateTimer.Start();
                    UpdateButtonStates();
                });
                _soundPlayer.Play();
                await Task.Delay((int)_totalDuration.TotalMilliseconds, ct);
            }
            catch { throw; }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    _isPlaying = false; updateTimer.Stop();
                    Invoke(() => { lblStatus.Text = "播放结束"; progressBar.Value = 0; lblTime.Text = "00:00 / 00:00"; UpdateButtonStates(); });
                }
                if (File.Exists(tmpWav)) try { File.Delete(tmpWav); } catch { }
                if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile)) { try { File.Delete(_tempFile); } catch { } _tempFile = null; }
            }
        }

        private TimeSpan EstimateDuration(MemoryStream s)
        {
            try
            {
                long pos = s.Position; s.Position = 0;
                byte[] h = new byte[44]; int r = s.Read(h, 0, 44);
                if (r < 44) { s.Position = pos; return TimeSpan.FromSeconds(30); }
                int sr = BitConverter.ToInt32(h, 24); short bps = BitConverter.ToInt16(h, 34);
                int dataLen = BitConverter.ToInt32(h, 40);
                double sec = (double)dataLen / (sr * (bps / 8));
                s.Position = pos; return TimeSpan.FromSeconds(sec);
            }
            catch { return TimeSpan.FromSeconds(30); }
        }

        private async Task<MemoryStream> DecodeToWavStream(string path, string fmt, CancellationToken ct)
        {
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

        private async Task<MemoryStream> DecodeWithVGAudio(string path, string fmt, CancellationToken ct)
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
                MemoryStream ms = new MemoryStream();
                new WaveWriter().WriteToStream(ad, ms);
                ms.Position = 0; return ms;
            }, ct);
        }

        private async Task<MemoryStream> DecodeWithTempFile(string path, string fmt, CancellationToken ct)
        {
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
            return st;
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
                "ahx" => new Ahx2wav_Converter(),
                "at3" => new At32wav_Converter(),
                "at9" => new At92wav_Converter(),
                "bcwav" => new Bcwav2wav_Converter(),
                "bfwav" => new Bfwav2wav_Converter(),
                "brwav" => new Brwav2wav_Converter(),
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
using System.Text.RegularExpressions;
using CSCore;
using CSCore.Codecs.WAV;
using CSCore.SoundOut;
using VGAudio.Containers.Adx;
using VGAudio.Containers.Dsp;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Hps;
using VGAudio.Containers.Idsp;
using VGAudio.Containers.NintendoWare;
using VGAudio.Formats;
using VGAudioWaveWriter = VGAudio.Containers.Wave.WaveWriter;
namespace super_toolbox
{
    public class AudioPlayerForm : Form
    {
        private ListView listViewFiles = null!;
        private Label lblFile = null!, lblStatus = null!, lblTime = null!;
        private ProgressBar progressBar = null!;
        private ComboBox cboFormat = null!;
        private TrackBar volumeTrackBar = null!;
        private System.Windows.Forms.Timer updateTimer = null!;
        private Button btnClearList = null!, btnPrev = null!, btnPlay = null!, btnPause = null!, btnStop = null!, btnNext = null!;
        private ColumnHeader colIndex = null!, colFileName = null!, colDuration = null!;

        private CancellationTokenSource? _cancellation;
        private string? _tempFile;
        private List<string> _playlist = new List<string>();
        private List<TimeSpan> _durations = new List<TimeSpan>();
        private int _currentIndex = -1;
        private bool _isPlaying, _isPaused;
        private DateTime _playStartTime;
        private TimeSpan _totalDuration, _pausedElapsed;
        private MemoryStream? _currentWavStream;
        private ISoundOut? _soundOut;
        private IWaveSource? _waveSource;
        private bool _isAutoDecoding = false;
        private static readonly string[] AudioExtensions = new[]
        {
            "adx","ahx","aifc","aiff","apex","asf","ast","at3","at9","bcstm","bcwav","bfstm","bfwav","binka","brstm","brwav","cv3","dsp","flac","hca","hps","idsp","kvs","lopus","mdsp","msf","mtaf","nwa","ogg","opus","pcm","qoa","rada","raw","rf64","snr","swav","tta","vag","w64","wav","wem","wma","xa","xma","xwma"
        };

        public AudioPlayerForm()
        {
            InitializeComponent();
            FormClosing += AudioPlayerForm_FormClosing;
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
            DragDrop += AudioPlayerForm_DragDrop;
            new ToolTip().SetToolTip(listViewFiles, "请拖放音频文件到此窗口进行播放");
        }

        private void InitializeComponent()
        {
            Text = "音频播放器";
            Size = new Size(1000, 700);
            MinimumSize = new Size(800, 500);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(230, 230, 230);

            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var topPanel = new Panel { Dock = DockStyle.Fill, Height = 85, BackColor = Color.FromArgb(45, 45, 48) };
            var controlPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.FromArgb(45, 45, 48) };

            string[] buttonTexts = { "⏮", "▶", "⏸", "■", "⏭" };
            int buttonWidth = 50, buttonHeight = 38, buttonSpacing = 8;
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
                    Enabled = false
                };
                btn.Location = new Point(startX + i * (buttonWidth + buttonSpacing), (controlPanel.Height - buttonHeight) / 2);
                controlPanel.Controls.Add(btn);
                if (i == 0) btnPrev = btn;
                else if (i == 1) btnPlay = btn;
                else if (i == 2) btnPause = btn;
                else if (i == 3) btnStop = btn;
                else if (i == 4) btnNext = btn;
            }

            volumeTrackBar = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 80,
                TickFrequency = 10,
                Width = 100,
                Location = new Point(startX + totalWidth + 35, (controlPanel.Height - 30) / 2),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(0, 120, 215)
            };
            volumeTrackBar.ValueChanged += (s, e) => { if (_soundOut != null) _soundOut.Volume = volumeTrackBar.Value / 100f; };
            controlPanel.Controls.Add(volumeTrackBar);

            var volumeLabel = new Label
            {
                Text = "音量",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(startX + totalWidth + 5, (controlPanel.Height - 20) / 2),
                Width = 30
            };
            controlPanel.Controls.Add(volumeLabel);

            controlPanel.Resize += (s, e) => {
                int newStartX = (controlPanel.Width - totalWidth) / 2;
                if (newStartX < 10) newStartX = 10;
                for (int i = 0; i < 5; i++)
                    controlPanel.Controls[i].Location = new Point(newStartX + i * (buttonWidth + buttonSpacing), (controlPanel.Height - buttonHeight) / 2);
                volumeLabel.Location = new Point(newStartX + totalWidth + 5, (controlPanel.Height - 20) / 2);
                volumeTrackBar.Location = new Point(newStartX + totalWidth + 35, (controlPanel.Height - 30) / 2);
            };

            btnPrev.Click += (s, e) => { if (_currentIndex > 0) { _currentIndex--; SelectAndPlay(); } };
            btnPlay.Click += BtnPlay_Click;
            btnPause.Click += (s, e) => { if (_isPlaying) { _isPaused = true; _isPlaying = false; _pausedElapsed = DateTime.Now - _playStartTime; updateTimer.Stop(); _soundOut?.Pause(); lblStatus.Text = "已暂停"; UpdateButtonStates(); } };
            btnStop.Click += (s, e) => { _cancellation?.Cancel(); _isPlaying = _isPaused = false; updateTimer.Stop(); _soundOut?.Stop(); lblStatus.Text = "已停止"; progressBar.Value = 0; if (_currentIndex >= 0 && _currentIndex < _durations.Count && _durations[_currentIndex] != TimeSpan.Zero) lblTime.Text = $"00:00 / {_durations[_currentIndex]:mm\\:ss}"; else lblTime.Text = "00:00 / 00:00"; UpdateButtonStates(); };
            btnNext.Click += (s, e) => { if (_currentIndex < _playlist.Count - 1) { _currentIndex++; SelectAndPlay(); } };

            var infoPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48), Padding = new Padding(20, 5, 20, 5) };
            lblFile = new Label { Text = "未选择文件", AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215) };
            lblStatus = new Label { Text = "就绪", AutoSize = true, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(160, 160, 160) };
            lblTime = new Label { Text = "00:00 / 00:00", AutoSize = true, Font = new Font("Segoe UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 120, 215) };
            progressBar = new ProgressBar { Height = 6, Style = ProgressBarStyle.Continuous, ForeColor = Color.FromArgb(0, 120, 215), BackColor = Color.FromArgb(63, 63, 70) };

            infoPanel.Controls.AddRange(new Control[] { lblFile, lblStatus, lblTime, progressBar });
            infoPanel.Resize += (s, e) => { lblTime.Left = infoPanel.Width - lblTime.Width - 20; progressBar.Width = infoPanel.Width - 40; };
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
            listViewFiles.Resize += (s, e) => { if (listViewFiles.Columns.Count >= 3 && listViewFiles.Width > 0) { int totalWidth = listViewFiles.ClientSize.Width; colIndex.Width = 50; colDuration.Width = 80; colFileName.Width = totalWidth - colIndex.Width - colDuration.Width - 4; if (colFileName.Width < 100) colFileName.Width = 100; } };
            listViewFiles.SelectedIndexChanged += (s, e) => { if (listViewFiles.SelectedIndices.Count > 0) { _currentIndex = listViewFiles.SelectedIndices[0]; lblFile.Text = Path.GetFileName(_playlist[_currentIndex]); lblTime.Text = _durations[_currentIndex] != TimeSpan.Zero ? $"00:00 / {_durations[_currentIndex]:mm\\:ss}" : "00:00 / 未知"; progressBar.Value = 0; UpdateButtonStates(); } };
            listViewFiles.DoubleClick += (s, e) => { if (_currentIndex >= 0) PlayCurrentFile(); };

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = Color.FromArgb(45, 45, 48), Padding = new Padding(12, 0, 12, 0) };
            btnClearList = new Button { Text = "清空列表", Size = new Size(90, 27), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(63, 63, 70), ForeColor = Color.White, Location = new Point(12, 9) };
            btnClearList.Click += (s, e) => { BtnStop_Click(null, EventArgs.Empty); _playlist.Clear(); _durations.Clear(); listViewFiles.Items.Clear(); _currentIndex = -1; UpdateButtonStates(); };

            cboFormat = new ComboBox { Size = new Size(180, 27), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(230, 230, 230), FlatStyle = FlatStyle.Flat, Location = new Point(110, 9) };
            cboFormat.Items.AddRange(new string[] { "目前支持的所有格式" }.Concat(AudioExtensions).ToArray());
            cboFormat.SelectedIndex = 0;

            bottomPanel.Controls.AddRange(new Control[] { btnClearList, cboFormat });
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 12) };
            listPanel.Controls.Add(listViewFiles);

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(listPanel, 0, 1);
            Controls.Add(mainLayout);
            Controls.Add(bottomPanel);

            updateTimer = new System.Windows.Forms.Timer { Interval = 100 };
            updateTimer.Tick += (s, e) => {
                if (!_isPlaying || _waveSource == null) return;
                TimeSpan elapsed = DateTime.Now - _playStartTime;
                if (elapsed >= _totalDuration && _isPlaying)
                {
                    _isPlaying = false;
                    updateTimer.Stop();
                    _soundOut?.Stop();
                    if (_currentIndex < _playlist.Count - 1)
                    {
                        _currentIndex++;
                        SelectAndPlay();
                    }
                    else
                    {
                        progressBar.Value = 0;
                        if (_totalDuration.TotalMilliseconds < 500)
                        {
                            lblTime.Text = $"{_totalDuration:mm\\:ss\\:ff} / {_totalDuration:mm\\:ss\\:ff}";
                        }
                        else
                        {
                            lblTime.Text = _currentIndex >= 0 && _durations[_currentIndex] != TimeSpan.Zero ? $"00:00 / {_durations[_currentIndex]:mm\\:ss}" : "00:00 / 00:00";
                        }
                        UpdateButtonStates();
                    }
                    return;
                }
                if (_totalDuration.TotalSeconds > 0)
                    progressBar.Value = (int)(elapsed.TotalSeconds / _totalDuration.TotalSeconds * 100);
                if (_totalDuration.TotalMilliseconds < 500)
                {
                    lblTime.Text = $"{elapsed:mm\\:ss\\:ff} / {_totalDuration:mm\\:ss\\:ff}";
                }
                else
                {
                    lblTime.Text = $"{elapsed:mm\\:ss} / {_totalDuration:mm\\:ss}";
                }
            };
        }

        private bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower().TrimStart('.');
            return AudioExtensions.Contains(ext);
        }

        private void UpdateDuration(int index, TimeSpan duration)
        {
            if (index < listViewFiles.Items.Count)
            {
                string durationText;
                if (duration.TotalMilliseconds < 1000)
                    durationText = $"{duration:ss\\:fff}";
                else
                    durationText = $"{duration:mm\\:ss}";
                listViewFiles.Items[index].SubItems[2].Text = durationText;
                listViewFiles.Items[index].ForeColor = Color.LightGreen;
            }

            if (index == _currentIndex && InvokeRequired)
            {
                Invoke(() => lblTime.Text = duration.TotalMilliseconds < 1000 ? $"00:00 / {duration:ss\\:fff}" : $"00:00 / {duration:mm\\:ss}");
            }
            else if (index == _currentIndex)
            {
                lblTime.Text = duration.TotalMilliseconds < 1000 ? $"00:00 / {duration:ss\\:fff}" : $"00:00 / {duration:mm\\:ss}";
            }
        }

        private void SelectAndPlay()
        {
            listViewFiles.SelectedIndices.Clear();
            listViewFiles.Items[_currentIndex].Selected = true;
            PlayCurrentFile();
        }

        private void RefreshPlaylist()
        {
            listViewFiles.Items.Clear();
            for (int i = 0; i < _playlist.Count; i++)
            {
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(Path.GetFileName(_playlist[i]));

                string durationText;
                if (_durations[i] != TimeSpan.Zero)
                {
                    if (_durations[i].TotalMilliseconds < 1000)
                        durationText = $"{_durations[i]:ss\\:fff}";
                    else
                        durationText = $"{_durations[i]:mm\\:ss}";
                }
                else
                {
                    durationText = "未知";
                }
                item.SubItems.Add(durationText);
                item.ForeColor = _durations[i] != TimeSpan.Zero ? Color.LightGreen : Color.FromArgb(230, 230, 230);
                listViewFiles.Items.Add(item);
            }
        }

        private void UpdateDurationDisplay(int index, TimeSpan duration)
        {
            if (index < listViewFiles.Items.Count)
            {
                string durationText;
                if (duration.TotalMilliseconds < 1000)
                    durationText = $"{duration:ss\\:fff}";
                else
                    durationText = $"{duration:mm\\:ss}";
                listViewFiles.Items[index].SubItems[2].Text = durationText;
                listViewFiles.Items[index].ForeColor = Color.LightGreen;
            }
        }

        private void AudioPlayerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (!(e?.Data?.GetDataPresent(DataFormats.FileDrop) ?? false)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 0) return;

            _playlist.Clear();
            _durations.Clear();

            List<string> audioFiles = new List<string>();
            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    foreach (var ext in GetSupportedExtensions())
                    {
                        try
                        {
                            audioFiles.AddRange(Directory.GetFiles(file, ext, SearchOption.AllDirectories));
                        }
                        catch { }
                    }
                }
                else if (IsSupportedFile(file))
                {
                    audioFiles.Add(file);
                }
            }

            if (audioFiles.Count > 0)
            {
                _playlist = audioFiles
                    .OrderBy(f =>
                    {
                        string fileName = Path.GetFileNameWithoutExtension(f);
                        var match = Regex.Match(fileName, @"_(\d+)$");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                            return num;
                        return int.MaxValue;
                    })
                    .ThenBy(f => Path.GetFileNameWithoutExtension(f))
                    .ToList();
                _durations = new List<TimeSpan>(new TimeSpan[_playlist.Count]);
                RefreshPlaylist();
                lblStatus.Text = $"已加载{_playlist.Count}个音频，开始解码...";
                UpdateButtonStates();

                _ = AutoDecodePlaylistAsync();
            }
            else
            {
                lblStatus.Text = "未找到已支持的音频文件";
            }
        }

        private string[] GetSupportedExtensions()
        {
            return new string[]
            {
                "*.adx", "*.ahx", "*.aifc", "*.aiff", "*.apex", "*.asf", "*.ast", "*.at3", "*.at9",
                "*.bcstm", "*.bcwav", "*.bfstm", "*.bfwav", "*.binka", "*.brstm", "*.brwav",
                "*.cv3",
                "*.dsp", "*.flac", "*.hca", "*.hps", "*.idsp", "*.lopus", "*.kvs",
                "*.mdsp", "*.msf", "*.mtaf", "*.nwa", "*.ogg", "*.opus", "*.pcm", "*.qoa",
                "*.rada", "*.raw", "*.rf64", "*.snr", "*.swav", "*.tta", "*.vag",
                "*.w64", "*.wav", "*.wem", "*.wma", "*.xa", "*.xma", "*.xwma"
            };
        }

        private async Task AutoDecodePlaylistAsync()
        {
            if (_isAutoDecoding) return;
            _isAutoDecoding = true;

            try
            {
                for (int i = 0; i < _playlist.Count; i++)
                {
                    if (_durations[i] != TimeSpan.Zero) continue;

                    if (InvokeRequired)
                        Invoke(() => lblStatus.Text = $"解码中({i + 1}/{_playlist.Count}): {Path.GetFileName(_playlist[i])}");
                    else
                        lblStatus.Text = $"解码中({i + 1}/{_playlist.Count}): {Path.GetFileName(_playlist[i])}";

                    try
                    {
                        var duration = await GetAudioDurationAsync(_playlist[i]);
                        _durations[i] = duration;

                        if (InvokeRequired)
                            Invoke(() => UpdateDuration(i, duration));
                        else
                            UpdateDuration(i, duration);

                        await Task.Delay(50);
                    }
                    catch (Exception)
                    {
                        _durations[i] = TimeSpan.FromSeconds(30);
                        if (InvokeRequired)
                            Invoke(() => UpdateDuration(i, TimeSpan.FromSeconds(30)));
                        else
                            UpdateDuration(i, TimeSpan.FromSeconds(30));
                    }
                }

                if (InvokeRequired)
                    Invoke(() => lblStatus.Text = _playlist.Count > 0 ? $"已加载{_playlist.Count}个音频" : "未找到已支持的音频文件");
                else
                    lblStatus.Text = _playlist.Count > 0 ? $"已加载{_playlist.Count}个音频" : "未找到已支持的音频文件";
            }
            finally
            {
                _isAutoDecoding = false;
            }
        }

        private async void PlayCurrentFile()
        {
            if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;

            if (_durations[_currentIndex] == TimeSpan.Zero)
            {
                lblStatus.Text = "解码中...";
                try
                {
                    var duration = await GetAudioDurationAsync(_playlist[_currentIndex]);
                    _durations[_currentIndex] = duration;
                    UpdateDurationDisplay(_currentIndex, duration);
                    lblTime.Text = duration.TotalMilliseconds < 1000 ? $"00:00 / {duration:ss\\:fff}" : $"00:00 / {duration:mm\\:ss}";
                }
                catch { lblStatus.Text = "解码失败"; return; }
            }

            _cancellation?.Cancel();
            _cancellation = new CancellationTokenSource();
            try { await PlayAsync(_playlist[_currentIndex], _cancellation.Token); }
            catch (OperationCanceledException) { lblStatus.Text = "已停止"; }
            catch (Exception ex) { MessageBox.Show($"播放失败:{ex.Message}", "错误"); lblStatus.Text = "播放失败"; }
            finally { UpdateButtonStates(); }
        }

        private async Task PlayAsync(string filePath, CancellationToken ct)
        {
            string fmt = cboFormat.SelectedItem?.ToString() == "目前支持的所有格式" ? Path.GetExtension(filePath).TrimStart('.').ToLower() : cboFormat.SelectedItem?.ToString()?.ToLower() ?? "";
            if (string.IsNullOrEmpty(fmt)) throw new Exception("无法识别的格式");

            _soundOut?.Stop();
            updateTimer.Stop();

            await Task.Delay(50, ct);

            _soundOut?.Dispose();
            _waveSource?.Dispose();
            _currentWavStream?.Dispose();
            _soundOut = null;
            _waveSource = null;
            _currentWavStream = null;

            if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile))
            {
                try { File.Delete(_tempFile); } catch { }
                _tempFile = null;
            }

            string tmpWav = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{DateTime.Now.Ticks}.wav");
            _tempFile = tmpWav;

            var result = await DecodeToWavStreamWithDuration(filePath, fmt, ct);
            _totalDuration = result.Duration;

            if (_durations[_currentIndex] == TimeSpan.Zero)
            {
                _durations[_currentIndex] = _totalDuration;
                UpdateDurationDisplay(_currentIndex, _totalDuration);
            }

            result.Stream.Position = 0;
            using (FileStream fs = new FileStream(tmpWav, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                result.Stream.WriteTo(fs);
            }
            _currentWavStream = result.Stream;

            _waveSource = new WaveFileReader(tmpWav);
            _soundOut = new WasapiOut();
            _soundOut.Initialize(_waveSource);
            _soundOut.Volume = volumeTrackBar.Value / 100f;

            _playStartTime = DateTime.Now;
            _isPlaying = true;
            _isPaused = false;

            updateTimer.Interval = _totalDuration.TotalMilliseconds < 500 ? 50 : 100;
            updateTimer.Start();
            UpdateButtonStates();

            _soundOut.Play();

            while (_isPlaying && !ct.IsCancellationRequested)
                await Task.Delay(100, ct);
        }

        private async void BtnPlay_Click(object? sender, EventArgs e)
        {
            if (_isPaused)
            {
                _isPaused = false; _isPlaying = true;
                _playStartTime = DateTime.Now - _pausedElapsed;
                updateTimer.Start(); _soundOut?.Resume();
                lblStatus.Text = "播放中..."; UpdateButtonStates();
                return;
            }
            if (_currentIndex >= 0)
            {
                _cancellation?.Cancel();
                _cancellation = new CancellationTokenSource();
                PlayCurrentFile();
            }
            else if (_playlist.Count > 0)
            {
                _currentIndex = 0;
                SelectAndPlay();
            }
        }

        private async Task<TimeSpan> GetAudioDurationAsync(string filePath)
        {
            string fmt = cboFormat.SelectedItem?.ToString() == "目前支持的所有格式" ? Path.GetExtension(filePath).TrimStart('.').ToLower() : cboFormat.SelectedItem?.ToString()?.ToLower() ?? "";
            if (string.IsNullOrEmpty(fmt)) throw new Exception("无法识别的格式");

            if (fmt == "wav")
            {
                using var fs = File.OpenRead(filePath);
                var ms = new MemoryStream();
                await fs.CopyToAsync(ms);
                ms.Position = 0;
                return GetWavDuration(ms);
            }

            if (new[] { "hca", "adx", "bcstm", "bfstm", "brstm", "dsp", "hps", "idsp", "mdsp" }.Contains(fmt))
                return await GetDurationWithVGAudio(filePath, fmt);

            return await GetDurationWithTempFile(filePath, fmt);
        }

        private Task<TimeSpan> GetDurationWithVGAudio(string path, string fmt) => Task.Run(() =>
        {
            AudioData? ad = null;
            using var fs = File.OpenRead(path);
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
            if (ad == null) throw new Exception("读取音频数据失败");
            var pcmFormat = ad.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>();
            if (pcmFormat == null) throw new Exception("无法获取PCM格式");
            return TimeSpan.FromSeconds((double)pcmFormat.SampleCount / pcmFormat.SampleRate);
        });

        private async Task<TimeSpan> GetDurationWithTempFile(string path, string fmt)
        {
            string tempWav = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            try
            {
                await Task.Run(() => {
                    BaseExtractor? ext = fmt == "xma" ? TryXmaDecoders(path) : CreateExtractorByFormat(fmt);
                    if (ext == null) throw new Exception("无对应解码器");
                    string sf = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(path));
                    File.Copy(path, sf, true);
                    try
                    {
                        ext.ExtractAsync(Path.GetTempPath(), CancellationToken.None).Wait();
                        string wavOut = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(sf) + ".wav");
                        if (File.Exists(wavOut))
                        {
                            if (File.Exists(tempWav)) File.Delete(tempWav);
                            File.Move(wavOut, tempWav);
                        }
                    }
                    finally { try { File.Delete(sf); } catch { } }
                });
                if (!File.Exists(tempWav)) throw new Exception("未生成wav");
                using var fs = File.OpenRead(tempWav);
                var ms = new MemoryStream();
                await fs.CopyToAsync(ms);
                ms.Position = 0;
                return GetWavDuration(ms);
            }
            finally { if (File.Exists(tempWav)) try { File.Delete(tempWav); } catch { } }
        }

        private async Task<(MemoryStream Stream, TimeSpan Duration)> DecodeToWavStreamWithDuration(string path, string fmt, CancellationToken ct)
        {
            if (fmt == "wav")
            {
                var ms = new MemoryStream();
                using var fs = File.OpenRead(path);
                await fs.CopyToAsync(ms, ct);
                ms.Position = 0;
                return (ms, GetWavDuration(ms));
            }

            if (new[] { "hca", "adx", "bcstm", "bfstm", "brstm", "dsp", "hps", "idsp", "mdsp" }.Contains(fmt))
                return await DecodeWithVGAudio(path, fmt, ct);

            return await DecodeWithTempFile(path, fmt, ct);
        }

        private Task<(MemoryStream Stream, TimeSpan Duration)> DecodeWithVGAudio(string path, string fmt, CancellationToken ct) => Task.Run(() =>
        {
            AudioData? ad = null;
            using var fs = File.OpenRead(path);
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
            if (ad == null) throw new Exception("读取音频数据失败");
            var pcmFormat = ad.GetFormat<VGAudio.Formats.Pcm16.Pcm16Format>();
            if (pcmFormat == null) throw new Exception("无法获取PCM格式");
            var duration = TimeSpan.FromSeconds((double)pcmFormat.SampleCount / pcmFormat.SampleRate);
            var ms = new MemoryStream();
            new VGAudioWaveWriter().WriteToStream(ad, ms);
            ms.Position = 0;
            return (ms, duration);
        }, ct);

        private async Task<(MemoryStream Stream, TimeSpan Duration)> DecodeWithTempFile(string path, string fmt, CancellationToken ct)
        {
            _tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
            await Task.Run(() => {
                BaseExtractor? ext = fmt == "xma" ? TryXmaDecoders(path) : CreateExtractorByFormat(fmt);
                if (ext == null) throw new Exception("无对应解码器");
                string sf = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(path));
                File.Copy(path, sf, true);
                try
                {
                    ext.ExtractAsync(Path.GetTempPath(), ct).Wait(ct);
                    string wavOut = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(sf) + ".wav");
                    if (File.Exists(wavOut))
                    {
                        if (File.Exists(_tempFile)) File.Delete(_tempFile);
                        File.Move(wavOut, _tempFile);
                    }
                }
                finally { try { File.Delete(sf); } catch { } }
            }, ct);
            if (!File.Exists(_tempFile)) throw new Exception("未生成wav");
            var st = new MemoryStream();
            using var rd = File.OpenRead(_tempFile);
            rd.CopyTo(st);
            st.Position = 0;
            var duration = GetWavDuration(st);
            st.Position = 0;
            return (st, duration);
        }

        private TimeSpan GetWavDuration(MemoryStream wavStream)
        {
            try
            {
                var pos = wavStream.Position;
                wavStream.Position = 0;

                byte[] header = new byte[44];
                int bytesRead = wavStream.Read(header, 0, 44);
                if (bytesRead < 44) return TimeSpan.Zero;

                short channels = BitConverter.ToInt16(header, 22);
                int sampleRate = BitConverter.ToInt32(header, 24);
                short bitsPerSample = BitConverter.ToInt16(header, 34);

                if (channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                    return TimeSpan.Zero;

                wavStream.Position = 0;

                long dataSize = 0;
                long riffSize = 0;

                wavStream.Position = 4;
                byte[] riffSizeBytes = new byte[4];
                wavStream.Read(riffSizeBytes, 0, 4);
                riffSize = BitConverter.ToUInt32(riffSizeBytes, 0);

                wavStream.Position = 12;

                while (wavStream.Position < wavStream.Length - 8)
                {
                    byte[] chunkId = new byte[4];
                    wavStream.Read(chunkId, 0, 4);
                    string chunkIdStr = System.Text.Encoding.ASCII.GetString(chunkId);

                    byte[] chunkSizeBytes = new byte[4];
                    wavStream.Read(chunkSizeBytes, 0, 4);
                    uint chunkSize = BitConverter.ToUInt32(chunkSizeBytes, 0);

                    if (chunkIdStr == "data")
                    {
                        dataSize = chunkSize;
                        break;
                    }

                    wavStream.Position += chunkSize;
                }

                wavStream.Position = pos;

                if (dataSize == 0)
                {
                    dataSize = riffSize + 8 - 44;
                    if (dataSize <= 0) return TimeSpan.Zero;
                }

                double bytesPerSecond = sampleRate * channels * (bitsPerSample / 8.0);
                if (bytesPerSecond <= 0) return TimeSpan.Zero;

                double seconds = dataSize / bytesPerSecond;

                if (seconds < 0.01 && dataSize > 0 && bytesPerSecond > 0)
                {
                    seconds = Math.Max(0.01, seconds);
                }

                return TimeSpan.FromSeconds(seconds);
            }
            catch
            {
                return TimeSpan.FromSeconds(0.01);
            }
        }

        private BaseExtractor? TryXmaDecoders(string path)
        {
            var sig = new byte[6];
            using var fs = File.OpenRead(path);
            if (fs.Length < 0x16) return null;
            fs.Seek(0x10, SeekOrigin.Begin);
            fs.Read(sig, 0, 6);
            if (sig.SequenceEqual(new byte[] { 0x20, 0x00, 0x00, 0x00, 0x65, 0x01 }) || sig.SequenceEqual(new byte[] { 0x34, 0x00, 0x00, 0x00, 0x66, 0x01 })) return new Xma2wav1_Converter();
            if (sig.SequenceEqual(new byte[] { 0x14, 0x00, 0x00, 0x00, 0x69, 0x00 })) return new Xma2wav2_Converter();
            if (sig.SequenceEqual(new byte[] { 0x14, 0x00, 0x00, 0x00, 0x11, 0x00 })) return new Xma2wav3_Converter();
            if (sig.SequenceEqual(new byte[] { 0x32, 0x00, 0x00, 0x00, 0x02, 0x00 })) return new Xma2wav4_Converter();
            return null;
        }

        private BaseExtractor? CreateExtractorByFormat(string fmt) => fmt switch
        {
            "aifc" => new Aifc2wav_Converter(),
            "aiff" => new Aiff2wav_Converter(),
            "ahx" => new Ahx2wav_Converter(),
            "apex" => new Apex2wav_Converter(),
            "asf" => new Asf2wav_Converter(),
            "ast" => new Ast2wav_Converter(),
            "at3" => new At3plus2wav_Converter(),
            "at9" => new At92wav_Converter(),
            "bcwav" => new Bcwav2wav_Converter(),
            "bfwav" => new Bfwav2wav_Converter(),
            "binka" => new Binka2wav_Converter(),
            "brwav" => new Brwav2wav_Converter(),
            "cv3" => new Cv3_Converter(),
            "flac" => new Flac2wav_Converter(),
            "kvs" => new Kvs2wav_Converter(),
            "lopus" => new Lopus2wav_Converter(),
            "msf" => new Msf2wav_Converter(),
            "mtaf" => new Mtaf2wav_Converter(),
            "nwa" => new Nwa2wav_Converter(),
            "ogg" => new Ogg2wav_Converter(),
            "opus" => new Opus2wav_Converter(),
            "pcm" => new Sony_psxadpcm2wav_Converter(),
            "qoa" => new Qoa2wav_Converter(),
            "rada" => new Rada2wav_Converter(),
            "raw" => new Msu2wav_Converter(),
            "rf64" => new Rf64ToWav_Converter(),
            "snr" => new Snr2wav_Converter(),
            "swav" => new Swav2wav_Converter(),
            "tta" => new Tta2wav_Converter(),
            "vag" => new Vag2wav_Converter(),
            "w64" => new W64ToWav_Converter(),
            "wem" => new Wem2wav_Converter(),
            "wma" => new Wma2wav_Converter(),
            "xa" => new MaxisXa2wav_Converter(),
            "xwma" => new Xwma2wav_Converter(),
            _ => null
        };

        private void UpdateButtonStates()
        {
            bool hasSel = _currentIndex >= 0 && _currentIndex < _playlist.Count;
            btnPrev.Enabled = hasSel && _currentIndex > 0;
            btnNext.Enabled = hasSel && _currentIndex < _playlist.Count - 1;
            btnPlay.Enabled = hasSel && !_isPlaying;
            btnPause.Enabled = _isPlaying;
            btnStop.Enabled = hasSel && (_isPlaying || _isPaused);
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            _cancellation?.Cancel();
            _isPlaying = _isPaused = false;
            updateTimer.Stop();
            _soundOut?.Stop();
            lblStatus.Text = "已停止";
            progressBar.Value = 0;
            if (_currentIndex >= 0 && _currentIndex < _durations.Count && _durations[_currentIndex] != TimeSpan.Zero)
            {
                var duration = _durations[_currentIndex];
                lblTime.Text = duration.TotalMilliseconds < 1000 ? $"00:00 / {duration:ss\\:fff}" : $"00:00 / {duration:mm\\:ss}";
            }
            else
                lblTime.Text = "00:00 / 00:00";
            UpdateButtonStates();
        }

        private void AudioPlayerForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _cancellation?.Cancel();
            updateTimer?.Stop();
            _soundOut?.Stop();
            _soundOut?.Dispose();
            _waveSource?.Dispose();
            _currentWavStream?.Dispose();
            if (!string.IsNullOrEmpty(_tempFile) && File.Exists(_tempFile))
            {
                try { File.Delete(_tempFile); } catch { }
            }
            try
            {
                foreach (var file in Directory.GetFiles(Path.GetTempPath(), "*.wav"))
                {
                    if (Path.GetFileName(file).StartsWith(Guid.Empty.ToString().Substring(0, 8)))
                        try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }
    }
}
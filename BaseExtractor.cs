using System.Reflection;
using System.Runtime.InteropServices;

namespace super_toolbox
{
    public abstract class BaseExtractor
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        protected static extern bool SetDllDirectory(string lpPathName);
        protected static string TempDllDirectory { get; private set; } = string.Empty;
        static BaseExtractor()
        {
            InitializeDllLoading();
        }
        private static void InitializeDllLoading()
        {
            TempDllDirectory = Path.Combine(Path.GetTempPath(), "supertoolbox_temp");
            Directory.CreateDirectory(TempDllDirectory);
            SetDllDirectory(TempDllDirectory);
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try { Directory.Delete(TempDllDirectory, true); } catch { }
            };
        }
        protected static void LoadEmbeddedDll(string embeddedResourceName, string dllFileName)
        {
            string dllPath = Path.Combine(TempDllDirectory, dllFileName);

            if (!File.Exists(dllPath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"嵌入的DLL资源'{embeddedResourceName}'未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(dllPath, buffer);
                }
            }
        }
        protected static string LoadEmbeddedExe(string embeddedResourceName, string exeFileName)
        {
            string exePath = Path.Combine(TempDllDirectory, exeFileName);

            if (!File.Exists(exePath))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"嵌入的EXE资源'{embeddedResourceName}'未找到");

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    File.WriteAllBytes(exePath, buffer);
                }
            }
            return exePath;
        }
#pragma warning disable CS0067
        public event EventHandler<string>? FileExtracted;
        public event EventHandler<int>? ProgressUpdated;
        public event EventHandler<int>? ExtractionCompleted;
        public event EventHandler<string>? ExtractionFailed;
        public event EventHandler<string>? FileConverted;
        public event EventHandler<int>? ConversionCompleted;
        public event EventHandler<string>? ConversionFailed;
        public event EventHandler<string>? FilePacked;
        public event EventHandler<int>? PackingCompleted;
        public event EventHandler<string>? PackingFailed;
        public event EventHandler<string>? FileCompressed;
        public event EventHandler<int>? CompressionCompleted;
        public event EventHandler<string>? CompressionFailed;
        public event EventHandler<string>? FileDecompressed;
        public event EventHandler<int>? DecompressionCompleted;
        public event EventHandler<string>? DecompressionFailed;
        public event EventHandler<string>? ExtractionStarted;
        public event EventHandler<string>? ExtractionProgress;
        public event EventHandler<string>? ExtractionError;
        public event EventHandler<string>? ConversionStarted;
        public event EventHandler<string>? ConversionProgress;
        public event EventHandler<string>? ConversionError;
        public event EventHandler<string>? PackingStarted;
        public event EventHandler<string>? PackingProgress;
        public event EventHandler<string>? PackingError;
        public event EventHandler<string>? CompressionStarted;
        public event EventHandler<string>? CompressionProgress;
        public event EventHandler<string>? CompressionError;
        public event EventHandler<string>? DecompressionStarted;
        public event EventHandler<string>? DecompressionProgress;
        public event EventHandler<string>? DecompressionError;
#pragma warning restore CS0067

        private int _extractedFileCount = 0;
        private int _totalFilesToExtract = 0;
        private bool _isExtractionCompleted = false;
        private readonly object _extractionLock = new object();

        private int _convertedFileCount = 0;
        private int _totalFilesToConvert = 0;
        private bool _isConversionCompleted = false;
        private readonly object _conversionLock = new object();

        private int _packedFileCount = 0;
        private int _totalFilesToPack = 0;
        private bool _isPackingCompleted = false;
        private readonly object _packingLock = new object();

        private int _compressedFileCount = 0;
        private int _totalFilesToCompress = 0;
        private bool _isCompressionCompleted = false;
        private readonly object _compressionLock = new object();

        private int _decompressedFileCount = 0;
        private int _totalFilesToDecompress = 0;
        private bool _isDecompressionCompleted = false;
        private readonly object _decompressionLock = new object();

        public int ExtractedFileCount
        {
            get { lock (_extractionLock) return _extractedFileCount; }
        }

        public int TotalFilesToExtract
        {
            get { lock (_extractionLock) return _totalFilesToExtract; }
            protected set { lock (_extractionLock) _totalFilesToExtract = value; }
        }

        // 转换属性
        public int ConvertedFileCount
        {
            get { lock (_conversionLock) return _convertedFileCount; }
        }

        public int TotalFilesToConvert
        {
            get { lock (_conversionLock) return _totalFilesToConvert; }
            protected set { lock (_conversionLock) _totalFilesToConvert = value; }
        }

        // 打包属性
        public int PackedFileCount
        {
            get { lock (_packingLock) return _packedFileCount; }
        }

        public int TotalFilesToPack
        {
            get { lock (_packingLock) return _totalFilesToPack; }
            protected set { lock (_packingLock) _totalFilesToPack = value; }
        }

        // 压缩属性
        public int CompressedFileCount
        {
            get { lock (_compressionLock) return _compressedFileCount; }
        }

        public int TotalFilesToCompress
        {
            get { lock (_compressionLock) return _totalFilesToCompress; }
            protected set { lock (_compressionLock) _totalFilesToCompress = value; }
        }

        // 解压属性
        public int DecompressedFileCount
        {
            get { lock (_decompressionLock) return _decompressedFileCount; }
        }

        public int TotalFilesToDecompress
        {
            get { lock (_decompressionLock) return _totalFilesToDecompress; }
            protected set { lock (_decompressionLock) _totalFilesToDecompress = value; }
        }

        // 进度百分比属性
        public int ProgressPercentage
        {
            get
            {
                lock (_extractionLock)
                {
                    return _totalFilesToExtract > 0
                        ? (int)((_extractedFileCount / (double)_totalFilesToExtract) * 100)
                        : 0;
                }
            }
        }

        public int ConversionProgressPercentage
        {
            get
            {
                lock (_conversionLock)
                {
                    return _totalFilesToConvert > 0
                        ? (int)((_convertedFileCount / (double)_totalFilesToConvert) * 100)
                        : 0;
                }
            }
        }

        public int PackingProgressPercentage
        {
            get
            {
                lock (_packingLock)
                {
                    return _totalFilesToPack > 0
                        ? (int)((_packedFileCount / (double)_totalFilesToPack) * 100)
                        : 0;
                }
            }
        }

        public int CompressionProgressPercentage
        {
            get
            {
                lock (_compressionLock)
                {
                    return _totalFilesToCompress > 0
                        ? (int)((_compressedFileCount / (double)_totalFilesToCompress) * 100)
                        : 0;
                }
            }
        }

        public int DecompressionProgressPercentage
        {
            get
            {
                lock (_decompressionLock)
                {
                    return _totalFilesToDecompress > 0
                        ? (int)((_decompressedFileCount / (double)_totalFilesToDecompress) * 100)
                        : 0;
                }
            }
        }

        public bool IsCancellationRequested { get; private set; } = false;

        public abstract Task ExtractAsync(string directoryPath, CancellationToken cancellationToken = default);

        public virtual void Extract(string directoryPath)
        {
            ExtractAsync(directoryPath).Wait();
        }

        // 提取相关方法
        protected void OnFileExtracted(string fileName)
        {
            bool shouldTriggerCompleted = false;
            int currentCount;
            int currentTotal;

            lock (_extractionLock)
            {
                _extractedFileCount++;
                currentCount = _extractedFileCount;
                currentTotal = _totalFilesToExtract;
                shouldTriggerCompleted = !_isExtractionCompleted && currentCount == currentTotal;
                if (shouldTriggerCompleted)
                {
                    _isExtractionCompleted = true;
                }
            }

            FileExtracted?.Invoke(this, fileName);
            ProgressUpdated?.Invoke(this, ProgressPercentage);

            if (shouldTriggerCompleted)
            {
                ExtractionCompleted?.Invoke(this, currentCount);
            }
        }

        protected void OnExtractionCompleted()
        {
            lock (_extractionLock)
            {
                if (!_isExtractionCompleted)
                {
                    _isExtractionCompleted = true;
                    ExtractionCompleted?.Invoke(this, _extractedFileCount);
                }
            }
        }

        protected void OnExtractionFailed(string errorMessage)
        {
            ExtractionFailed?.Invoke(this, errorMessage);
        }

        // 转换相关方法
        protected void OnFileConverted(string fileName)
        {
            bool shouldTriggerCompleted = false;
            int currentCount;
            int currentTotal;

            lock (_conversionLock)
            {
                _convertedFileCount++;
                currentCount = _convertedFileCount;
                currentTotal = _totalFilesToConvert;
                shouldTriggerCompleted = !_isConversionCompleted && currentCount == currentTotal;
                if (shouldTriggerCompleted)
                {
                    _isConversionCompleted = true;
                }
            }

            FileConverted?.Invoke(this, fileName);

            if (shouldTriggerCompleted)
            {
                ConversionCompleted?.Invoke(this, currentCount);
            }
        }

        protected void OnConversionCompleted()
        {
            lock (_conversionLock)
            {
                if (!_isConversionCompleted)
                {
                    _isConversionCompleted = true;
                    ConversionCompleted?.Invoke(this, _convertedFileCount);
                }
            }
        }

        protected void OnConversionFailed(string errorMessage)
        {
            ConversionFailed?.Invoke(this, errorMessage);
        }

        // 打包相关方法
        protected void OnFilePacked(string fileName)
        {
            bool shouldTriggerCompleted = false;
            int currentCount;
            int currentTotal;

            lock (_packingLock)
            {
                _packedFileCount++;
                currentCount = _packedFileCount;
                currentTotal = _totalFilesToPack;
                shouldTriggerCompleted = !_isPackingCompleted && currentCount == currentTotal;
                if (shouldTriggerCompleted)
                {
                    _isPackingCompleted = true;
                }
            }

            FilePacked?.Invoke(this, fileName);

            if (shouldTriggerCompleted)
            {
                PackingCompleted?.Invoke(this, currentCount);
            }
        }

        protected void OnPackingCompleted()
        {
            lock (_packingLock)
            {
                if (!_isPackingCompleted)
                {
                    _isPackingCompleted = true;
                    PackingCompleted?.Invoke(this, _packedFileCount);
                }
            }
        }

        protected void OnPackingFailed(string errorMessage)
        {
            PackingFailed?.Invoke(this, errorMessage);
        }

        // 压缩相关方法
        protected void OnFileCompressed(string fileName)
        {
            bool shouldTriggerCompleted = false;
            int currentCount;
            int currentTotal;

            lock (_compressionLock)
            {
                _compressedFileCount++;
                currentCount = _compressedFileCount;
                currentTotal = _totalFilesToCompress;
                shouldTriggerCompleted = !_isCompressionCompleted && currentCount == currentTotal;
                if (shouldTriggerCompleted)
                {
                    _isCompressionCompleted = true;
                }
            }

            FileCompressed?.Invoke(this, fileName);

            if (shouldTriggerCompleted)
            {
                CompressionCompleted?.Invoke(this, currentCount);
            }
        }

        protected void OnCompressionCompleted()
        {
            lock (_compressionLock)
            {
                if (!_isCompressionCompleted)
                {
                    _isCompressionCompleted = true;
                    CompressionCompleted?.Invoke(this, _compressedFileCount);
                }
            }
        }

        protected void OnCompressionFailed(string errorMessage)
        {
            CompressionFailed?.Invoke(this, errorMessage);
        }

        // 解压相关方法
        protected void OnFileDecompressed(string fileName)
        {
            bool shouldTriggerCompleted = false;
            int currentCount;
            int currentTotal;

            lock (_decompressionLock)
            {
                _decompressedFileCount++;
                currentCount = _decompressedFileCount;
                currentTotal = _totalFilesToDecompress;
                shouldTriggerCompleted = !_isDecompressionCompleted && currentCount == currentTotal;
                if (shouldTriggerCompleted)
                {
                    _isDecompressionCompleted = true;
                }
            }

            FileDecompressed?.Invoke(this, fileName);

            if (shouldTriggerCompleted)
            {
                DecompressionCompleted?.Invoke(this, currentCount);
            }
        }

        protected void OnDecompressionCompleted()
        {
            lock (_decompressionLock)
            {
                if (!_isDecompressionCompleted)
                {
                    _isDecompressionCompleted = true;
                    DecompressionCompleted?.Invoke(this, _decompressedFileCount);
                }
            }
        }

        protected void OnDecompressionFailed(string errorMessage)
        {
            DecompressionFailed?.Invoke(this, errorMessage);
        }

        protected void ThrowIfCancellationRequested(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || IsCancellationRequested)
            {
                IsCancellationRequested = true;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
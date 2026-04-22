using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DS1ParamEditor
{
    public enum ScanState { Idle, Scanning, Done, Failed }

    /// <summary>
    /// Central application state. Owns config, param loading, process attachment, and scanning.
    /// UI reads from this class and calls its methods вЂ” no logic lives in the UI layer.
    /// </summary>
    public sealed class EditorState
    {
        // в”Ђв”Ђ Config в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        public AppConfig Config { get; } = new();

        // в”Ђв”Ђ Param loading в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private readonly ParamStore _store = new();

        public bool UseDrawParams { get; private set; } = true;
        public List<ParamFile> ParamFiles { get; private set; } = [];
        public ParamFile? SelectedFile { get; private set; }
        public LoadedParam? SelectedParam { get; private set; }

        // в”Ђв”Ђ LockCamParam вЂ” independent slot в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        // Loaded from DrawParam regardless of the main param mode.
        public LoadedParam? LockCamParam  { get; private set; }
        public ParamFile?   LockCamFile   { get; private set; }

        private volatile int _lockCamScanState = (int)ScanState.Idle;
        public ScanState LockCamScanState
        {
            get => (ScanState)_lockCamScanState;
            private set => _lockCamScanState = (int)value;
        }
        private CancellationTokenSource? _lockCamScanCts;

        // в”Ђв”Ђ Process / scan в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        private ParamHook? _paramHook;      // For param scanning
        private PlayerHook? _playerHook;    // For DSR-Gadget features
        
        public GameProcess? Process { get; private set; }
        public PlayerHook? Player => _playerHook;
        
        /// <summary>Exposes the ParamHook for direct field writes.</summary>
        public ParamHook? GetParamHook() => _paramHook;
        
        public bool IsAttached
        {
            get
            {
                try { return _paramHook?.Hooked == true && Process?.IsAlive == true; }
                catch { return false; }
            }
        }
        
        public bool IsPlayerAttached => _playerHook?.Hooked == true;

        // Maps param name в†’ base address. Written from Task, read from UI thread.
        private readonly Dictionary<string, nint> _hookedAddresses = new();
        private readonly object _addrLock = new();
        public IReadOnlyDictionary<string, nint> HookedAddresses => _hookedAddresses;

        private volatile int _scanState = (int)ScanState.Idle;
        public ScanState ScanState
        {
            get => (ScanState)_scanState;
            private set => _scanState = (int)value;
        }
        private CancellationTokenSource? _scanCts;

        // в”Ђв”Ђ Status в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        // Written from background thread, read from UI thread вЂ” use volatile fields.
        private volatile string _statusMessage = string.Empty;
        private volatile bool   _statusIsError;
        public string StatusMessage => _statusMessage;
        public bool   StatusIsError => _statusIsError;

        // в”Ђв”Ђ Init в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        public EditorState()
        {
            Config.TryToGetGamePath();
        }

        // в”Ђв”Ђ Config actions в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

        public void SelectExe()
        {
            Config.ShowFileDialog();
            // Reset everything when exe changes
            ResetAll();
        }

        // в”Ђв”Ђ Param loading в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

        public void SetDrawParams(bool value)
        {
            if (UseDrawParams == value) return;
            UseDrawParams = value;
            ResetParams();
        }

        public void LoadParams()
        {
            if (!Config.IsReady)
            {
                SetStatus("Paths not configured. Select exe first.", true);
                return;
            }

            ResetParams();

            try
            {
                string dir = UseDrawParams ? Config.SelectedDrawParamPath : Config.SelectedParamPath;
                ParamFiles = _store.Load(Config.SelectedParamDefPath, dir, ScanPatternMode, CustomPatternLength, ForceLoadParams, ScanPatternStart);
                int totalParams = ParamFiles.Sum(f => f.Params.Count);
                int totalSkipped = ParamFiles.Sum(f => f.SkippedCount);
                string skipMsg = totalSkipped > 0 ? $" ({totalSkipped} skipped — enable Force load or open console for details)" : "";
                SetStatus($"Loaded {ParamFiles.Count} files, {totalParams} params.{skipMsg}");
            }
            catch (Exception ex)
            {
                SetStatus($"Load failed: {ex.Message}", true);
            }
        }

        public void SelectFile(ParamFile file)
        {
            if (SelectedFile == file) return;
            SelectedFile = file;
            SelectedParam = null;
        }

        public void SelectParam(LoadedParam param)
        {
            if (SelectedParam == param) return;
            SelectedParam = param;
        }

        public void SaveCurrentFile()
        {
            if (SelectedFile == null) return;
            try
            {
                _store.Save(SelectedFile);
                SetStatus($"Saved '{SelectedFile.FileName}'.");
            }
            catch (Exception ex)
            {
                SetStatus($"Save failed: {ex.Message}", true);
            }
        }

        // в”Ђв”Ђ LockCamParam в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

        public void LoadLockCamParam()
        {
            if (!Config.IsReady)
            {
                SetStatus("Paths not configured. Select exe first.", true);
                return;
            }

            LockCamParam     = null;
            LockCamFile      = null;
            LockCamScanState = ScanState.Idle;

            try
            {
                var files = _store.Load(Config.SelectedParamDefPath, Config.SelectedParamPath, ScanPatternMode, CustomPatternLength, ForceLoadParams, ScanPatternStart);
                foreach (var file in files)
                {
                    var lp = file.Params.FirstOrDefault(
                        p => p.Name.Equals("LockCamParam", StringComparison.OrdinalIgnoreCase));
                    if (lp != null)
                    {
                        LockCamFile  = file;
                        LockCamParam = lp;
                        SetStatus("LockCamParam loaded.");
                        return;
                    }
                }
                SetStatus("LockCamParam not found in GameParam.", true);
            }
            catch (Exception ex)
            {
                SetStatus($"LockCamParam load failed: {ex.Message}", true);
            }
        }

        public void SaveLockCamParam()
        {
            if (LockCamFile == null) return;
            try
            {
                _store.Save(LockCamFile);
                SetStatus("LockCamParam saved.");
            }
            catch (Exception ex)
            {
                SetStatus($"LockCamParam save failed: {ex.Message}", true);
            }
        }

        public void StartLockCamScan()
        {
            if (LockCamParam == null)
            {
                SetStatus("Load LockCamParam first.", true);
                return;
            }
            if (!IsAttached)
            {
                SetStatus("Not attached to game process.", true);
                return;
            }

            // Check if already cached in ParamHook
            var cached = _paramHook?.GetCachedAddress("LockCamParam");
            if (cached.HasValue)
            {
                lock (_addrLock)
                    _hookedAddresses["LockCamParam"] = cached.Value;
                LockCamScanState = ScanState.Done;
                SetStatus($"LockCamParam already cached at 0x{cached.Value:X}");
                return;
            }

            _lockCamScanCts?.Cancel();
            _lockCamScanCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ct = _lockCamScanCts.Token;

            LockCamScanState = ScanState.Scanning;
            SetStatus("Scanning for LockCamParam...");

            var param = LockCamParam;
            var hook = _paramHook!;

            Task.Run(() =>
            {
                try
                {
                    nint baseAddr = hook.ScanAndCache("LockCamParam", param.ScanPattern, param.ScanOffset, ct);
                    if (baseAddr != nint.Zero)
                    {
                        lock (_addrLock)
                            _hookedAddresses["LockCamParam"] = baseAddr;
                        LockCamScanState = ScanState.Done;
                        SetStatus($"LockCamParam hooked at 0x{baseAddr:X}");
                    }
                    else
                    {
                        LockCamScanState = ScanState.Failed;
                        SetStatus("LockCamParam pattern not found.", true);
                    }
                }
                catch (OperationCanceledException)
                {
                    LockCamScanState = ScanState.Idle;
                    SetStatus("LockCamParam scan cancelled.");
                }
                catch (Exception ex)
                {
                    LockCamScanState = ScanState.Failed;
                    SetStatus($"LockCamParam scan error: {ex.Message}", true);
                }
            }, ct);
        }

        public void CancelLockCamScan() => _lockCamScanCts?.Cancel();

        public void ClearLockCamHook()
        {
            lock (_addrLock)
                _hookedAddresses.Remove("LockCamParam");
            LockCamScanState = ScanState.Idle;
        }

        // в”Ђв”Ђ Auto-load params on warp в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
        public bool AutoLoadParamsOnWarp { get; set; } = false;
        public LoadedParam.PatternMode  ScanPatternMode  { get; set; } = LoadedParam.PatternMode.Auto;
        public LoadedParam.PatternStart ScanPatternStart { get; set; } = LoadedParam.PatternStart.FromFileStart;
        public int  CustomPatternLength { get; set; } = 96;
        public bool ForceLoadParams { get; set; } = false;

        // Remembers the anchor address for each map prefix across warps
        // Key: map prefix like "m10", "m15" вЂ” Value: last known anchor address
        private readonly Dictionary<string, nint> _mapAnchorCache = new();

        /// <summary>
        /// Tries to load DrawParam files matching the given map name (e.g. "m15_01").
        /// Looks for files whose FileName contains the map name.
        /// </summary>
        public void LoadParamsByMapName(string mapName)
        {
            if (!Config.IsReady || string.IsNullOrEmpty(mapName)) return;

            try
            {
                string dir = Config.SelectedDrawParamPath;
                var files = _store.Load(Config.SelectedParamDefPath, dir, ScanPatternMode, CustomPatternLength, ForceLoadParams, ScanPatternStart);

                string[] prefixes = GetDrawParamPrefixes(mapName);

                var matching = files.Where(f =>
                    prefixes.Any(p => f.FileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                    f.FileName.StartsWith("default_", StringComparison.OrdinalIgnoreCase)).ToList();

                if (matching.Count == 0)
                {
                    SetStatus($"No DrawParam files found for {mapName}.");
                    return;
                }

                ParamFiles = matching;
                SelectedFile = matching.Count > 0 ? matching[0] : null;
                SelectedParam = null;

                SetStatus($"Loaded {matching.Count} param file(s) for {mapName}. Scanning...");
                Console.WriteLine($"[AutoLoad] {mapName} (prefixes: {string.Join(", ", prefixes)}): {matching.Count} files");

                // Auto-hook all loaded params
                if (IsAttached)
                    ScanAllLoadedParams();
            }
            catch (Exception ex)
            {
                SetStatus($"Auto-load failed for {mapName}: {ex.Message}", true);
            }
        }

        /// <summary>
        /// Scans all currently loaded params in parallel and registers their addresses.
        /// </summary>

        public void ScanAllLoadedParams()
        {
            if (!IsAttached || _paramHook == null) return;

            var allParams = ParamFiles.SelectMany(f => f.Params).ToList();
            if (allParams.Count == 0) return;

            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;
            var hook = _paramHook;

            // Clear only map-specific params вЂ” default_ and GameParam addresses are stable
            foreach (var param in allParams.Where(p => IsMapSpecificParam(p.Name)))
            {
                hook.RemoveFromCache(param.Name);
                lock (_addrLock) _hookedAddresses.Remove(param.Name);
            }

            ScanState = ScanState.Scanning;
            SetStatus($"Scanning {allParams.Count} params...");

            Task.Run(() =>
            {
                int found = 0, failed = 0;
                object countLock = new();

                System.Threading.Tasks.Parallel.ForEach(allParams,
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                    param =>
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            // Stable params: use cache if available, skip scan
                            var cached = hook.GetCachedAddress(param.Name);
                            if (cached.HasValue)
                            {
                                lock (_addrLock) _hookedAddresses[param.Name] = cached.Value;
                                lock (countLock) found++;
                                return;
                            }

                            nint addr = hook.ScanAndCache(param.Name, param.ScanPattern, param.ScanOffset, ct);
                            if (addr != nint.Zero) { lock (_addrLock) _hookedAddresses[param.Name] = addr; lock (countLock) found++; }
                            else lock (countLock) failed++;
                        }
                        catch (OperationCanceledException) { }
                        catch { lock (countLock) failed++; }
                    });

                if (!ct.IsCancellationRequested)
                {
                    ScanState = ScanState.Done;
                    SetStatus($"Hooked {found}/{allParams.Count} params" + (failed > 0 ? $" ({failed} failed)" : "."));
                    Console.WriteLine($"[AutoLoad] Scan complete: {found} hooked, {failed} failed");
                }
            }, ct);
        }

        private static bool IsMapSpecificParam(string name) =>
            !name.StartsWith("default_", StringComparison.OrdinalIgnoreCase) &&
            name.Length > 1 && char.IsDigit(name[1]); // m10_, s10_, m15_, etc.

        private static string[] GetDrawParamPrefixes(string mapName)
        {
            // mapName format: "mAA_BB" e.g. "m10_02", "m15_01"
            // DrawParam files: "aAA_DrawParam.parambnd.dcx" вЂ” prefix "a" + area number, no block
            // e.g. m10_02 -> "a10_", m15_01 -> "a15_"
            var parts = mapName.Split('_');
            if (parts.Length < 1) return new[] { "default_" };

            // Strip leading 'm' and get area number
            string area = parts[0].TrimStart('m'); // "10", "15"
            return new[] { $"a{area}_" };
        }

        public void AttachProcess()
        {
            if (string.IsNullOrEmpty(Config.SelectedExe))
            {
                SetStatus("No exe selected.", true);
                return;
            }

            // Create ParamHook for param scanning
            if (_paramHook == null)
            {
                _paramHook = new ParamHook(1000, 100); // 1s refresh, 100ms min lifetime
                _paramHook.OnHooked += (s, e) =>
                {
                    Process = GameProcess.TryAttach(_paramHook);
                    if (Process != null)
                        SetStatus($"Attached to DSR {_paramHook.Version} (Params)");
                };
                _paramHook.OnUnhooked += (s, e) =>
                {
                    Process?.Dispose();
                    Process = null;
                    SetStatus("Game process exited (Params).");
                };
                _paramHook.Start();
            }
            
            // Create PlayerHook for DSR-Gadget features
            if (_playerHook == null)
            {
                _playerHook = new PlayerHook(1000, 100);
                _playerHook.OnHooked += (s, e) =>
                {
                    SetStatus($"DSR-Gadget ready (version {_playerHook.Version})");
                };
                _playerHook.OnUnhooked += (s, e) =>
                {
                    SetStatus("DSR-Gadget disconnected.");
                };
                _playerHook.Start();
            }

            if (!_paramHook.Hooked && !_playerHook.Hooked)
            {
                SetStatus("Waiting for Dark Souls Remastered...");
            }
            else if (_paramHook.Hooked && _playerHook.Hooked)
            {
                SetStatus($"Already attached (Params + Gadget)");
            }
        }

        // в”Ђв”Ђ Scanning в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

        public void StartScan()
        {
            if (SelectedParam == null || SelectedFile == null)
            {
                SetStatus("Select a param first.", true);
                return;
            }

            if (!IsAttached)
            {
                SetStatus("Not attached to game process.", true);
                return;
            }

            // Check if already cached
            var cached = _paramHook?.GetCachedAddress(SelectedParam.Name);
            if (cached.HasValue)
            {
                lock (_addrLock)
                    _hookedAddresses[SelectedParam.Name] = cached.Value;
                ScanState = ScanState.Done;
                SetStatus($"'{SelectedParam.Name}' already cached at 0x{cached.Value:X}");
                return;
            }

            // Cancel any previous scan still running
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30s timeout
            var ct = _scanCts.Token;

            ScanState = ScanState.Scanning;
            SetStatus($"Scanning for '{SelectedParam.Name}'...");

            var param = SelectedParam;
            var hook = _paramHook!;

            Task.Run(() =>
            {
                try
                {
                    nint baseAddr = hook.ScanAndCache(param.Name, param.ScanPattern, param.ScanOffset, ct);

                    if (baseAddr != nint.Zero)
                    {
                        lock (_addrLock)
                            _hookedAddresses[param.Name] = baseAddr;
                        ScanState = ScanState.Done;
                        SetStatus($"Hooked '{param.Name}' at 0x{baseAddr:X}");
                    }
                    else
                    {
                        ScanState = ScanState.Failed;
                        SetStatus($"Pattern not found for '{param.Name}'.", true);
                    }
                }
                catch (OperationCanceledException)
                {
                    ScanState = ScanState.Idle;
                    SetStatus(ct.IsCancellationRequested && !ct.CanBeCanceled
                        ? $"Scan timed out for '{param.Name}' (30s). Pattern may not match memory."
                        : "Scan cancelled.");
                }
                catch (Exception ex)
                {
                    ScanState = ScanState.Failed;
                    SetStatus($"Scan error: {ex.Message}", true);
                }
            }, ct);
        }

        public void CancelScan()
        {
            _scanCts?.Cancel();
        }

        public void ClearHooks()
        {
            lock (_addrLock)
            {
                // Preserve LockCamParam hook вЂ” it's managed independently
                _hookedAddresses.TryGetValue("LockCamParam", out nint lockCamAddr);
                _hookedAddresses.Clear();
                if (lockCamAddr != default)
                    _hookedAddresses["LockCamParam"] = lockCamAddr;
            }
            
            // Clear ParamHook cache except LockCamParam
            if (_paramHook != null)
            {
                var cached = _paramHook.GetCachedParams();
                foreach (var name in cached)
                {
                    if (name != "LockCamParam")
                        _paramHook.RemoveFromCache(name);
                }
            }
            
            ScanState = ScanState.Idle;
            SetStatus("Hooks cleared.");
        }

        public nint? GetHookedAddress(string paramName)
        {
            lock (_addrLock)
                return _hookedAddresses.TryGetValue(paramName, out var a) ? a : null;
        }

        // в”Ђв”Ђ Helpers в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

        private void ResetAll()
        {
            ResetParams();
            _lockCamScanCts?.Cancel();
            LockCamParam     = null;
            LockCamFile      = null;
            LockCamScanState = ScanState.Idle;
            
            _paramHook?.Stop();
            _paramHook = null;
            _playerHook?.Stop();
            _playerHook = null;
            
            Process?.Dispose();
            Process = null;
        }

        private void ResetParams()
        {
            _scanCts?.Cancel();
            ParamFiles    = [];
            SelectedFile  = null;
            SelectedParam = null;
            lock (_addrLock)
            {
                _hookedAddresses.TryGetValue("LockCamParam", out nint lockCamAddr);
                _hookedAddresses.Clear();
                if (lockCamAddr != default)
                    _hookedAddresses["LockCamParam"] = lockCamAddr;
            }

            // Also clear ParamHook cache (except LockCamParam) so re-hook does a fresh scan
            if (_paramHook != null)
            {
                var cached = _paramHook.GetCachedParams();
                foreach (var name in cached)
                {
                    if (name != "LockCamParam")
                        _paramHook.RemoveFromCache(name);
                }
            }

            ScanState = ScanState.Idle;
        }

        private void SetStatus(string msg, bool isError = false)
        {
            _statusMessage = msg;
            _statusIsError = isError;
        }
    }
}

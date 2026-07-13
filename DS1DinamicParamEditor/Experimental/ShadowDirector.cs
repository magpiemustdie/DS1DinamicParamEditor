using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SoulsFormats;

namespace DS1ParamEditor.Experimental
{
    /// <summary>
    /// Watches the player position relative to SFX events on map m17 (Duke's Archives).
    /// When the player is close to an SFX, rotates the shadow at index 1 in ShadowBank
    /// to point away from the SFX (as if the SFX is a light source casting the shadow).
    ///
    /// Pipeline:
    ///   1. Load m17_XX_00_00.msb → collect SFX events → resolve region positions
    ///   2. Load a17_DrawParam → hook m17_ShadowBank
    ///   3. Each tick: get player pos, find nearest SFX within radius,
    ///      compute direction SFX→player, write shadow azimuth/elevation to row 1
    /// </summary>
    public sealed class ShadowDirector
    {
        private readonly EditorState _state;

        // ── Config ────────────────────────────────────────────────────────────
        public float TriggerRadius { get; set; } = 20f;   // metres
        public float LerpSpeed { get; set; } = 720f;      // degrees per second; 720 = instant (no lerp)
        public float RotateSpeed { get; set; } = 90f;     // degrees per second in rotate mode
        public float AzimuthOffset { get; set; } = 0f;    // degrees added to computed angle (calibration)
        public int   UpdateIntervalMs { get; set; } = 16; // ms between ticks (0 = max rate / Task.Yield)
        public int   MultiWritesPerYield { get; set; } = 100; // writes per source before yielding in multi mode
        public bool  IsRunning => _cts != null;
        public bool  RotateMode { get; set; } = false;    // true = spin mode, false = SFX tracking

        // ── State ─────────────────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        private List<SfxPoint> _sfxPoints = new();
        private LoadedParam? _shadowBank;
        private PARAM.Row? _shadowBankRow;
        private readonly Dictionary<string, float> _sfxDistances = new();

        // Current smoothed values
        private float _currentAzimuth = 0f;
        private float _currentElevation = 45f;
        private SfxPoint? _activeSfx; // locked SFX with hysteresis

        // Shadow field names in ShadowBank (DSR paramdef names)
        // Row 1 = index 1 (second row, 0-based)
        // Fields: degRotX (elevation), degRotY (azimuth) — float32
        private const string SHADOW_PARAM_NAME = "m17_ShadowBank";
        private const string FIELD_ROT_X = "lightDegRotX";
        private const string FIELD_ROT_Y = "lightDegRotY";

        public IReadOnlyList<SfxPoint> SfxPoints => _sfxPoints;
        public IReadOnlyDictionary<string, float> SfxDistances => _sfxDistances;
        public string StatusMessage { get; private set; } = "Idle";
        public string NearestSfxName { get; private set; } = "---";
        public float  NearestSfxDist { get; private set; } = float.MaxValue;

        /// <summary>
        /// Selected SFX sources (up to 10). Tracking uses the nearest among these.
        /// Empty = auto mode (nearest within MAX_SOURCE_DIST).
        /// </summary>
        private readonly HashSet<string> _pinnedSfxNames = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> PinnedSfxNames => _pinnedSfxNames;
        public const int MAX_PINNED = 10;

        public bool IsPinned(string name) => _pinnedSfxNames.Contains(name);

        public bool TogglePin(string name)
        {
            if (_pinnedSfxNames.Contains(name))
            {
                _pinnedSfxNames.Remove(name);
                return false;
            }
            if (_pinnedSfxNames.Count >= MAX_PINNED) return false; // limit reached
            _pinnedSfxNames.Add(name);
            return true;
        }

        public void ClearPins() => _pinnedSfxNames.Clear();

        // ── Logging ───────────────────────────────────────────────────────────
        private bool _logging = false;
        private System.Diagnostics.Stopwatch _logSw = new();
        private double _lastLogMs = 0;
        public bool IsLogging => _logging;
        public const double LOG_INTERVAL_MS = 200.0;

        public void StartLog()
        {
            if (_logging) return;
            _logging = true;
            _logSw.Restart();
            _lastLogMs = 0;
            Console.WriteLine();
            Console.WriteLine("╔══ SHADOW LOG START ══════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║  {"Time(ms)",8} {"PX",8} {"PY",8} {"PZ",8} {"SFX_X",8} {"SFX_Z",8} {"Dist",7} {"Computed°",10} {"LiveY°",8} {"Interval",9} ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");
        }

        public void StopLog()
        {
            if (!_logging) return;
            _logging = false;
            Console.WriteLine("╚══ SHADOW LOG END ════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        private void MaybeLog(Vector3 playerPos, SfxPoint? sfx, float computed, double nowMs)
        {
            if (!_logging) return;
            if (nowMs - _lastLogMs < LOG_INTERVAL_MS) return;
            double interval = nowMs - _lastLogMs;
            _lastLogMs = nowMs;

            float? liveY = ReadShadowRotY();
            float dist = sfx != null ? Vector3.Distance(playerPos, sfx.Position) : float.NaN;
            float sfxX = sfx?.Position.X ?? float.NaN;
            float sfxZ = sfx?.Position.Z ?? float.NaN;
            string liveStr = liveY.HasValue ? $"{liveY.Value,8:F2}" : "     N/A";
            string intStr  = $"{interval,6:F0}ms";

            Console.WriteLine($"║  {nowMs,8:F0} {playerPos.X,8:F2} {playerPos.Y,8:F2} {playerPos.Z,8:F2} {sfxX,8:F2} {sfxZ,8:F2} {dist,7:F2} {computed,10:F2} {liveStr} {intStr} ║");
        }

        public ShadowDirector(EditorState state)
        {
            _state = state;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Loads m17 MSB, extracts SFX positions, loads a17 DrawParam and hooks ShadowBank.
        /// </summary>
        public bool Initialize(string msbPath)
        {
            _shadowBank = null; // reset so it gets re-hooked
            _shadowBankRow = null;
            try
            {
                // 1. Load MSB and collect SFX → region positions
                _sfxPoints = LoadSfxPoints(msbPath);
                Console.WriteLine($"[ShadowDir] Loaded {_sfxPoints.Count} SFX points from {Path.GetFileName(msbPath)}");

                // 2. Load a17 DrawParam and hook only ShadowBank
                if (!_state.Config.IsReady)
                {
                    StatusMessage = "Config not ready";
                    return false;
                }

                _shadowBank = LoadAndHookShadowBank();
                _shadowBankRow = _shadowBank?.Param?.Rows?.FirstOrDefault(r => r.ID == 1);
                if (_shadowBank == null)
                {
                    StatusMessage = "ShadowBank not found or hook failed — check console";
                    return false;
                }

                StatusMessage = $"Initialized: {_sfxPoints.Count} SFX, scanning ShadowBank...";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Init failed: {ex.Message}";
                Console.WriteLine($"[ShadowDir] Init error: {ex}");
                return false;
            }
        }

        /// <summary>Starts the background update loop.</summary>
        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Task.Run(() => UpdateLoop(ct), ct);
            StatusMessage = "Running";
        }

        /// <summary>Stops the background update loop.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            StatusMessage = "Stopped";
        }

        // ── Update loop ───────────────────────────────────────────────────────

        /// <summary>
        /// Multi-source cycle mode: rapidly cycles through all pinned SFX writing each angle
        /// in turn with no delay — relies on GPU persistence to show multiple shadows.
        /// Single-source mode: standard 16ms tick.
        /// </summary>
        private async Task UpdateLoop(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double lastMs = sw.Elapsed.TotalMilliseconds;

            while (!ct.IsCancellationRequested)
            {
                double nowMs = sw.Elapsed.TotalMilliseconds;
                double deltaMs = nowMs - lastMs;
                lastMs = nowMs;

                if (deltaMs < 1.0)   deltaMs = 1.0;
                if (deltaMs > 100.0) deltaMs = 100.0;

                try
                {
                    if (RotateMode)
                    {
                        TickRotate(deltaMs);
                        if (UpdateIntervalMs > 0) await Task.Delay(UpdateIntervalMs); else await Task.Yield();
                    }
                    else if (_pinnedSfxNames.Count > 1)
                    {
                        // Multi-source: cycle through all pinned SFX as fast as possible
                        await TickMultiSfx(ct);
                    }
                    else
                    {
                        TickSfx(deltaMs, nowMs);
                        if (UpdateIntervalMs > 0) await Task.Delay(UpdateIntervalMs); else await Task.Yield();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ShadowDir] Tick error: {ex.Message}");
                    await Task.Delay(16);
                }
            }
        }

        /// <summary>
        /// Cycles through all pinned SFX, writing each angle as fast as possible —
        /// all writes in a tight synchronous loop, no yield between sources.
        /// </summary>
        private async Task TickMultiSfx(CancellationToken ct)
        {
            var player = _state.Player;
            if (player == null || !player.IsValid) return;
            if (!player.GetPosition(out float px, out float py, out float pz, out _)) return;

            var playerPos = new Vector3(px, py, pz);

            // Update distances once
            foreach (var sfx in _sfxPoints)
                _sfxDistances[sfx.Name] = Vector3.Distance(playerPos, sfx.Position);

            // Collect pinned SFX that have valid positions and are within trigger radius
            var active = _sfxPoints
                .Where(s => _pinnedSfxNames.Contains(s.Name) && s.HasRegion
                    && _sfxDistances.GetValueOrDefault(s.Name, float.MaxValue) <= TriggerRadius)
                .ToList();

            if (active.Count == 0) return;

            // Update nearest for display with hysteresis
            var nearest = active.MinBy(s => _sfxDistances.GetValueOrDefault(s.Name, float.MaxValue));
            float nearestDist = _sfxDistances.GetValueOrDefault(nearest.Name, float.MaxValue);
            if (_activeSfx != null && nearest != _activeSfx)
            {
                float currentDist = _sfxDistances.GetValueOrDefault(_activeSfx.Name, float.MaxValue);
                if (nearestDist > currentDist - 5f)
                    nearest = active.FirstOrDefault(s => s.Name == _activeSfx.Name) ?? nearest;
            }
            NearestSfxDist = nearestDist;
            NearestSfxName = nearest.Name;

            // Pre-compute all angles
            var angles = new float[active.Count];
            for (int i = 0; i < active.Count; i++)
            {
                Vector3 toPlayer = playerPos - active[i].Position;
                toPlayer.Y = 0;
                if (toPlayer.Length() < 0.01f) { angles[i] = _currentAzimuth; continue; }
                toPlayer = Vector3.Normalize(toPlayer);
                angles[i] = MathF.Atan2(toPlayer.X, toPlayer.Z) * (180f / MathF.PI) + AzimuthOffset;
            }

            // Tight write loop — configurable writes per source before yielding
            // MultiWritesPerYield=1 → yield between every source (slowest, most "fair")
            // MultiWritesPerYield=100 → 100 writes per source before yield (fastest burst)
            for (int i = 0; i < active.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                int writes = Math.Max(1, MultiWritesPerYield);
                for (int rep = 0; rep < writes; rep++)
                    WriteShadowDirection(angles[i]);
                await Task.Yield(); // yield after each source's burst
            }

            StatusMessage = $"Multi [{active.Count}] ×{MultiWritesPerYield} writes/src";
        }

        private void TickRotate(double deltaMs)
        {
            // RotateSpeed is degrees per second
            _currentAzimuth = (_currentAzimuth + (float)(RotateSpeed * deltaMs / 1000.0)) % 360f;
            WriteShadowDirection(_currentAzimuth);
            StatusMessage = $"Rotating Y → {_currentAzimuth:F1}°";
        }

        private void TickSfx(double deltaMs, double nowMs)
        {
            var player = _state.Player;
            if (player == null || !player.IsValid) return;
            if (!player.GetPosition(out float px, out float py, out float pz, out _)) return;

            // DSR memory: X=X, Y=height, Z=depth — same layout as MSB
            var playerPos = new Vector3(px, py, pz);

            // Update distances for all SFX (always, for display)
            foreach (var sfx in _sfxPoints)
                _sfxDistances[sfx.Name] = Vector3.Distance(playerPos, sfx.Position);

            SfxPoint? nearest;
            float nearestDist;

            if (_pinnedSfxNames.Count > 0)
            {
                // Pinned mode: find nearest among selected SFX only
                nearest = null;
                nearestDist = float.MaxValue;
                foreach (var sfx in _sfxPoints)
                {
                    if (!_pinnedSfxNames.Contains(sfx.Name)) continue;
                    float dist = _sfxDistances.GetValueOrDefault(sfx.Name, float.MaxValue);
                    if (dist < nearestDist) { nearestDist = dist; nearest = sfx; }
                }
                if (_activeSfx != nearest)
                {
                    Console.WriteLine($"[ShadowDir] Pinned nearest: '{nearest?.Name ?? "none"}' (d={nearestDist:F1}m) from {_pinnedSfxNames.Count} selected");
                    _activeSfx = nearest;
                }
            }
            else
            {
                // Auto mode: find nearest within MAX_SOURCE_DIST
                nearest = null;
                nearestDist = float.MaxValue;
                const float MAX_SOURCE_DIST = 5f;

                foreach (var sfx in _sfxPoints)
                {
                    float dist = _sfxDistances.GetValueOrDefault(sfx.Name, float.MaxValue);
                    if (dist > MAX_SOURCE_DIST) continue;
                    if (dist < nearestDist) { nearestDist = dist; nearest = sfx; }
                }

                // Hysteresis: only switch active SFX if new one is significantly closer
                // Use larger hysteresis (5m) to prevent rapid switching between nearby torches
                const float HYSTERESIS = 5f;
                if (nearest != null && nearest != _activeSfx)
                {
                    float activeDist = _activeSfx != null
                        ? _sfxDistances.GetValueOrDefault(_activeSfx.Name, float.MaxValue)
                        : float.MaxValue;
                    if (nearestDist < activeDist - HYSTERESIS || _activeSfx == null)
                    {
                        Console.WriteLine($"[ShadowDir] Active SFX: '{_activeSfx?.Name ?? "none"}' → '{nearest.Name}' (d={nearestDist:F1}m, prev={activeDist:F1}m)");
                        _activeSfx = nearest;
                    }
                }

                // No SFX in range — clear active and stop writing
                if (nearest == null)
                {
                    _activeSfx = null;
                    NearestSfxDist = float.MaxValue;
                    NearestSfxName = "---";
                    StatusMessage = "No SFX in range";
                    return;
                }

                if (nearestDist > TriggerRadius) return;
            }

            NearestSfxDist = nearestDist;
            NearestSfxName = nearest?.Name ?? "---";

            var activeSfx = _activeSfx;
            if (activeSfx == null || activeSfx.Position == Vector3.Zero)
            {
                if (activeSfx != null && !activeSfx.HasRegion)
                    StatusMessage = $"SFX '{activeSfx.Name}' has no region — cannot compute angle";
                return;
            }

            // Compute direction from SFX to player (shadow points away from light)
            Vector3 toPlayer = playerPos - activeSfx.Position;
            toPlayer.Y = 0;

            // Too close — direction is unstable, skip update (only in auto mode)
            const float MIN_DIST = 5f;
            if (_pinnedSfxNames.Count == 0 && toPlayer.Length() < MIN_DIST) return;
            if (toPlayer.Length() < 0.01f) return;

            toPlayer = Vector3.Normalize(toPlayer);

            float targetAzimuth = MathF.Atan2(toPlayer.X, toPlayer.Z) * (180f / MathF.PI);
            targetAzimuth += AzimuthOffset; // calibration offset (accounts for DSR coordinate system rotation)

            // Log for calibration analysis
            MaybeLog(playerPos, activeSfx, targetAzimuth, nowMs);

            if (LerpSpeed >= 720f)
            {
                // Instant mode — write directly, no lerp
                _currentAzimuth = targetAzimuth;
            }
            else
            {
                // Frame-independent lerp: LerpSpeed is degrees per second
                float maxStep = LerpSpeed * (float)(deltaMs / 1000.0);
                _currentAzimuth = LerpAngle(_currentAzimuth, targetAzimuth, maxStep);
            }

            WriteShadowDirection(_currentAzimuth);

            string pin = _pinnedSfxNames.Count > 0 ? $" [{_pinnedSfxNames.Count} pinned]" : "";
            StatusMessage = $"SFX Track{pin} → {_currentAzimuth:F1}° (near {NearestSfxName} d={nearestDist:F1}m)";
        }

        // ── Shadow writing ────────────────────────────────────────────────────

        private LoadedParam? LoadAndHookShadowBank()
        {
            try
            {
                string dir = _state.Config.SelectedDrawParamPath;
                // a17_DrawParam contains m17_ShadowBank
                string[] candidates = { "a17_DrawParam.parambnd.dcx", "a17_DrawParam.parambnd" };
                string? filePath = null;
                foreach (var name in candidates)
                {
                    string p = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(p)) { filePath = p; break; }
                }

                if (filePath == null)
                {
                    Console.WriteLine("[ShadowDir] a17_DrawParam not found in " + dir);
                    return null;
                }

                var store = new ParamStore();
                var file = store.Load(_state.Config.SelectedParamDefPath, dir)
                    .FirstOrDefault(f => f.FileName.StartsWith("a17_", StringComparison.OrdinalIgnoreCase));

                if (file == null) return null;

                var shadowParam = file.Params.FirstOrDefault(p =>
                    p.Name.Equals("m17_ShadowBank", StringComparison.OrdinalIgnoreCase));

                if (shadowParam == null)
                {
                    Console.WriteLine($"[ShadowDir] m17_ShadowBank not found. Available: {string.Join(", ", file.Params.Select(p => p.Name))}");
                    return null;
                }

                // Hook only this param
                var hook = _state.GetParamHook();
                if (hook == null)
                {
                    Console.WriteLine("[ShadowDir] ParamHook is null — connect to game first (Params tab)");
                    return null;
                }
                if (!hook.Hooked)
                {
                    Console.WriteLine("[ShadowDir] ParamHook not hooked to game process");
                    return null;
                }

                Console.WriteLine($"[ShadowDir] Scanning for m17_ShadowBank ({shadowParam.ScanPattern.Length} bytes @ offset 0x{shadowParam.ScanOffset:X})...");
                var cts = new System.Threading.CancellationTokenSource(30000); // 30s timeout
                nint addr = hook.ScanAndCache(shadowParam.Name, shadowParam.ScanPattern, shadowParam.ScanOffset, cts.Token);
                if (addr == nint.Zero)
                {
                    Console.WriteLine("[ShadowDir] Failed to hook m17_ShadowBank");
                    return null;
                }

                Console.WriteLine($"[ShadowDir] Hooked m17_ShadowBank @ 0x{addr:X}");
                _shadowBank = shadowParam;
                _shadowBankRow = shadowParam?.Param?.Rows?.FirstOrDefault(r => r.ID == 1);
                CacheWriteAddress();
                return shadowParam;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowDir] LoadAndHookShadowBank error: {ex.Message}");
                return null;
            }
        }

        // Shadow color/density override
        public int ShadowColR    { get; set; } = 255;  // 0..255 (s16)
        public int ShadowColG    { get; set; } = 0;
        public int ShadowColB    { get; set; } = 0;
        public int ShadowDensity { get; set; } = 100;  // 0..100 (s16, game uses 0..100)
        private const string FIELD_COL_R       = "colR";
        private const string FIELD_COL_G       = "colG";
        private const string FIELD_COL_B       = "colB";
        private const string FIELD_DENSITY     = "densityRatio";

        /// <summary>Writes color and density to row ID==1 once.</summary>
        public void ApplyColorOverride()
        {
            if (_shadowBank == null) { Console.WriteLine("[ShadowDir] ShadowBank not hooked"); return; }

            var param = _shadowBank.Param;
            var row   = _shadowBankRow;
            if (row == null) { Console.WriteLine("[ShadowDir] Row ID==1 not found"); return; }

            var hook = _state.GetParamHook();
            if (hook == null) return;

            // Print all field names and types for diagnostics
            if (param.AppliedParamdef != null)
            {
                Console.WriteLine("[ShadowDir] ShadowBank fields:");
                foreach (var f in param.AppliedParamdef.Fields)
                    Console.WriteLine($"  {f.InternalName} : {f.DisplayType}");
            }

            long rowOff = row.DataOffset;

            void WriteField(string name, int val)
            {
                if (param.AppliedParamdef == null) return;
                long offset = 0;
                foreach (var f in param.AppliedParamdef.Fields)
                {
                    if (f.InternalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        hook.WriteParamField(_shadowBank.Name, rowOff, offset, f.DisplayType, (short)Math.Clamp(val, -32768, 32767));
                        Console.WriteLine($"[ShadowDir] {name} ({f.DisplayType}) = {val}");
                        return;
                    }
                    if (SoulsFormats.ParamUtil.IsArrayType(f.DisplayType))
                        offset += SoulsFormats.ParamUtil.GetValueSize(f.DisplayType) * f.ArrayLength;
                    else
                        offset += SoulsFormats.ParamUtil.GetValueSize(f.DisplayType);
                }
                Console.WriteLine($"[ShadowDir] Field '{name}' not found");
            }

            WriteField(FIELD_DENSITY, ShadowDensity);
            WriteField(FIELD_COL_R,   ShadowColR);
            WriteField(FIELD_COL_G,   ShadowColG);
            WriteField(FIELD_COL_B,   ShadowColB);
        }

        // Cached write target — computed once after hook, used in tight loop
        private nint _cachedWriteAddr = nint.Zero;  // absolute memory address of lightDegRotY

        /// <summary>Pre-computes the absolute memory address for lightDegRotY in row ID==1.</summary>
        private void CacheWriteAddress()
        {
            _cachedWriteAddr = nint.Zero;
            if (_shadowBank == null) return;

            var param = _shadowBank.Param;
            var row = _shadowBankRow;
            if (row == null) return;

            long? rotYOffset = FindFieldOffset(param, FIELD_ROT_Y);
            if (rotYOffset == null) return;

            var hook = _state.GetParamHook();
            if (hook == null) return;

            var baseAddr = hook.GetCachedAddress(_shadowBank.Name);
            if (!baseAddr.HasValue) return;

            _cachedWriteAddr = baseAddr.Value + (nint)row.DataOffset + (nint)rotYOffset.Value;
            Console.WriteLine($"[ShadowDir] Cached write addr: 0x{_cachedWriteAddr:X}");
        }

        private void WriteShadowDirection(float azimuthDeg)
        {
            short val = (short)Math.Clamp((int)azimuthDeg, -32768, 32767);

            // Fast path: use cached address directly
            if (_cachedWriteAddr != nint.Zero)
            {
                var hook = _state.GetParamHook();
                if (hook != null)
                {
                    PropertyHook.Kernel32.WriteInt16(hook.Handle, (IntPtr)_cachedWriteAddr, val);
                    return;
                }
            }

            // Slow path fallback
            if (_shadowBank == null) return;
            var param = _shadowBank.Param;
            var row = _shadowBankRow;
            if (row == null) return;
            long? rotYOffset = FindFieldOffset(param, FIELD_ROT_Y);
            if (rotYOffset == null) return;
            var h = _state.GetParamHook();
            if (h == null) return;
            h.WriteParamField(_shadowBank.Name, row.DataOffset, rotYOffset.Value, PARAMDEF.DefType.s16, val);
        }

        private static long? FindFieldOffset(PARAM param, string fieldName)
        {
            if (param.AppliedParamdef == null) return null;

            // Calculate field offset by summing sizes of preceding fields
            long offset = 0;
            foreach (var field in param.AppliedParamdef.Fields)
            {
                if (field.InternalName.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    return offset;

                // Advance offset by field size
                var type = field.DisplayType;
                if (SoulsFormats.ParamUtil.IsArrayType(type))
                    offset += SoulsFormats.ParamUtil.GetValueSize(type) * field.ArrayLength;
                else
                    offset += SoulsFormats.ParamUtil.GetValueSize(type);
            }
            return null;
        }

        /// <summary>Reads lightDegRotY for ALL rows in ShadowBank and prints to console.</summary>
        public void PrintAllShadowRows()
        {
            if (_shadowBank == null) { Console.WriteLine("[ShadowDir] ShadowBank not hooked"); return; }

            var param = _shadowBank.Param;
            var hook = _state.GetParamHook();
            if (hook == null) return;

            long? rotYOffset = FindFieldOffset(param, FIELD_ROT_Y);
            long? rotXOffset = FindFieldOffset(param, FIELD_ROT_X);
            if (rotYOffset == null) { Console.WriteLine("[ShadowDir] lightDegRotY field not found"); return; }

            Console.WriteLine();
            Console.WriteLine($"╔══ m17_ShadowBank ALL ROWS ({param.Rows.Count} rows) ══════════════════════════╗");
            Console.WriteLine($"║  {"Row ID",6} {"DataOffset",12} {"RotX°",10} {"RotY°",10} ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

            foreach (var row in param.Rows)
            {
                var rotY = hook.ReadParamField(_shadowBank.Name, row.DataOffset, rotYOffset.Value, PARAMDEF.DefType.s16);
                var rotX = rotXOffset.HasValue
                    ? hook.ReadParamField(_shadowBank.Name, row.DataOffset, rotXOffset.Value, PARAMDEF.DefType.s16)
                    : null;

                string rotYStr = rotY is short sy ? $"{sy,10}" : "       N/A";
                string rotXStr = rotX is short sx ? $"{sx,10}" : "       N/A";
                string marker  = row.ID == 1 ? " ← writing here" : "";
                Console.WriteLine($"║  {row.ID,6} {row.DataOffset,12:X} {rotXStr} {rotYStr}{marker} ║");
            }

            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        // ── Calibration ──────────────────────────────────────────────────────

        /// <summary>
        /// Reads the current lightDegRotY from game memory and prints a calibration table
        /// comparing computed angles (SFX→player) vs the live value in the shadow param.
        /// </summary>
        public void PrintCalibrationTable()
        {
            // Read current shadow Y from memory
            float? liveY = ReadShadowRotY();

            // Read player position
            float px = 0, py = 0, pz = 0;
            bool hasPlayer = _state.Player != null
                && _state.Player.IsValid
                && _state.Player.GetPosition(out px, out py, out pz, out _);

            // DSR memory: X=X, Y=height, Z=depth — same layout as MSB
            var playerPos = hasPlayer ? new Vector3(px, py, pz) : Vector3.Zero;

            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                        SHADOW CALIBRATION TABLE                                 ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

            if (hasPlayer)
                Console.WriteLine($"║  Player pos : ({px,8:F2}, {py,8:F2}, {pz,8:F2})                                    ║");
            else
                Console.WriteLine("║  Player pos : N/A (not connected)                                               ║");

            Console.WriteLine($"║  Live rotY  : {(liveY.HasValue ? $"{liveY.Value,8:F2}°" : "N/A (not hooked)")}                                                    ║");
            Console.WriteLine($"║  Last written azimuth : {_currentAzimuth,8:F2}°   AzimuthOffset : {AzimuthOffset,8:F2}°              ║");            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");
            Console.WriteLine($"║  {"SFX Name",-28} {"FX",-6} {"Dist(m)",8} {"Computed°",10} {"Delta°",8} {"Region",-12} ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

            foreach (var sfx in _sfxPoints)
            {
                float dist = hasPlayer ? Vector3.Distance(playerPos, sfx.Position) : float.NaN;
                bool hasPos = sfx.Position != Vector3.Zero;

                float computed = float.NaN;
                if (hasPlayer && hasPos)
                {
                    Vector3 toPlayer = playerPos - sfx.Position;
                    toPlayer.Y = 0;
                    float len = toPlayer.Length();
                    if (len > 0.01f)
                        computed = MathF.Atan2(toPlayer.X / len, toPlayer.Z / len) * (180f / MathF.PI);
                }

                // Delta: difference between computed angle and live shadow angle
                float delta = float.NaN;
                if (!float.IsNaN(computed) && liveY.HasValue)
                {
                    delta = liveY.Value - computed;
                    // Wrap to [-180, 180]
                    while (delta >  180f) delta -= 360f;
                    while (delta < -180f) delta += 360f;
                }

                string distStr    = float.IsNaN(dist)    ? "   N/A  " : $"{dist,8:F2}";
                string computedStr = float.IsNaN(computed) ? "       N/A" : $"{computed,10:F2}";
                string deltaStr   = float.IsNaN(delta)   ? "     N/A" : $"{delta,8:F2}";
                string name       = sfx.Name.Length > 28 ? sfx.Name[..28] : sfx.Name;
                string region     = (sfx.Region.Length > 12 ? sfx.Region[..12] : sfx.Region).PadRight(12);

                Console.WriteLine($"║  {name,-28} {sfx.EffectId,-6} {distStr} {computedStr} {deltaStr} {region} ║");
            }

            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════╣");

            // Summary: if player is close to any SFX, highlight the expected offset
            if (hasPlayer && liveY.HasValue && _sfxPoints.Count > 0)
            {
                var nearest = _sfxPoints
                    .Where(s => s.Position != Vector3.Zero)
                    .OrderBy(s => Vector3.Distance(playerPos, s.Position))
                    .FirstOrDefault();

                if (nearest != null)
                {
                    Vector3 toPlayer = playerPos - nearest.Position;
                    toPlayer.Y = 0;
                    float len = toPlayer.Length();
                    if (len > 0.01f)
                    {
                        float nearestComputed = MathF.Atan2(toPlayer.X / len, toPlayer.Z / len) * (180f / MathF.PI);
                        float nearestDelta = liveY.Value - nearestComputed;
                        while (nearestDelta >  180f) nearestDelta -= 360f;
                        while (nearestDelta < -180f) nearestDelta += 360f;
                        Console.WriteLine($"║  Nearest SFX: {nearest.Name,-20}  computed={nearestComputed,8:F2}°  live={liveY.Value,8:F2}°  ║");
                        Console.WriteLine($"║  → Suggested AzimuthOffset = {nearestDelta,8:F2}°  (add this to computed to match live)  ║");
                    }
                }
            }

            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        /// <summary>Reads the current lightDegRotY value directly from game memory.</summary>
        private float? ReadShadowRotY()
        {
            if (_shadowBank == null) return null;

            var param = _shadowBank.Param;
            var row = _shadowBankRow;
            if (row == null) return null;

            long? rotYOffset = FindFieldOffset(param, FIELD_ROT_Y);
            if (rotYOffset == null) return null;

            var hook = _state.GetParamHook();
            if (hook == null) return null;

            var val = hook.ReadParamField(_shadowBank.Name, row.DataOffset, rotYOffset.Value, PARAMDEF.DefType.s16);
            return val is short s ? (float)s : null;
        }

        // ── MSB loading ───────────────────────────────────────────────────────

        private static List<SfxPoint> LoadSfxPoints(string msbPath)
        {
            var result = new List<SfxPoint>();
            var msb = MSB1.Read(msbPath);

            // Build region lookup: name → position
            // GetEntries() returns all region types (Point, Sphere, Box, Cylinder, etc.)
            var regionPos = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in msb.Regions.GetEntries())
            {
                regionPos[region.Name] = region.Position;
                Console.WriteLine($"[ShadowDir] Region '{region.Name}' @ ({region.Position.X:F2},{region.Position.Y:F2},{region.Position.Z:F2})");
            }

            Console.WriteLine($"[ShadowDir] Total regions: {regionPos.Count}");

            // Collect SFX events
            foreach (var sfx in msb.Events.SFX)
            {
                Vector3 pos = Vector3.Zero;
                bool resolved = false;

                if (!string.IsNullOrEmpty(sfx.RegionName))
                {
                    if (regionPos.TryGetValue(sfx.RegionName, out var rp))
                    {
                        pos = rp;
                        resolved = true;
                    }
                    else
                    {
                        Console.WriteLine($"[ShadowDir] WARNING: SFX '{sfx.Name}' references region '{sfx.RegionName}' which was NOT found in region list");
                    }
                }
                else
                {
                    Console.WriteLine($"[ShadowDir] WARNING: SFX '{sfx.Name}' has no RegionName");
                }

                result.Add(new SfxPoint
                {
                    Name     = sfx.Name,
                    EffectId = sfx.EffectID,
                    Position = pos,
                    Region   = sfx.RegionName ?? "",
                    HasRegion = resolved
                });

                Console.WriteLine($"[ShadowDir] SFX '{sfx.Name}' (fx={sfx.EffectID}) @ ({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) resolved={resolved} region='{sfx.RegionName}'");
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Smoothly interpolates between two angles (degrees), handling ±180 wrap correctly.
        /// Returns current + step toward target, clamped to step size.
        /// </summary>
        private static float LerpAngle(float current, float target, float maxStep)
        {
            float delta = target - current;
            // Wrap delta to [-180, 180]
            while (delta > 180f)  delta -= 360f;
            while (delta < -180f) delta += 360f;

            if (MathF.Abs(delta) <= maxStep)
                return target;

            return current + MathF.Sign(delta) * maxStep;
        }

        // ── Data types ────────────────────────────────────────────────────────

        public sealed class SfxPoint
        {
            public string  Name      { get; set; } = "";
            public int     EffectId  { get; set; }
            public Vector3 Position  { get; set; }
            public string  Region    { get; set; } = "";
            public bool    HasRegion { get; set; } = false;
        }
    }
}

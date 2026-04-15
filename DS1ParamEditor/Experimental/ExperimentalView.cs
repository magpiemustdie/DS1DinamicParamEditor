using System;
using System.IO;
using System.Numerics;
using DS1ParamEditor.Experimental;
using ImGuiNET;

namespace DS1ParamEditor
{
    public sealed class ExperimentalView
    {
        private readonly EditorState _state;
        private MapInfo? _mapInfo;

        private ShadowDirector? _shadowDir;
        private string _msbPath = string.Empty;
        private float _triggerRadius = 20f;
        private float _lerpSpeed = 720f;
        private float _azimuthOffset = -50f;
        private int   _updateIntervalMs = 16;
        private int   _multiWritesPerYield = 100;

        public ExperimentalView(EditorState state)
        {
            _state = state;
        }

        public void Draw()
        {
            if (_mapInfo == null && _state.Player != null)
                _mapInfo = new MapInfo(_state.Player);

            if (ImGui.CollapsingHeader("Map Info", ImGuiTreeNodeFlags.DefaultOpen))
                DrawMapInfo();

            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Shadow Director (m17)", ImGuiTreeNodeFlags.DefaultOpen))
                DrawShadowDirector();
        }

        private void DrawMapInfo()
        {
            if (_state.Player == null || !_state.IsPlayerAttached)
            {
                ImGui.TextDisabled("Connect to game first.");
                return;
            }

            _mapInfo?.Refresh();

            if (_mapInfo == null) { ImGui.TextDisabled("Initializing..."); return; }

            if (!_mapInfo.IsValid)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Map: not loaded");
                return;
            }

            ImGui.Text($"Map ID:  {_mapInfo.MapName}");
            ImGui.Text($"Area:    {_mapInfo.AreaName}");
        }

        private void DrawShadowDirector()
        {
            float w = ImGui.GetContentRegionAvail().X;

            // MSB path
            ImGui.Text("MSB file:");
            ImGui.SetNextItemWidth(w - 70);
            ImGui.InputText("##msbpath", ref _msbPath, 512);
            ImGui.SameLine();
            if (ImGui.Button("Browse##sd", new Vector2(65, 0)))
                BrowseMsb();

            // Auto-fill
            if (string.IsNullOrEmpty(_msbPath) && _state.Config.IsReady)
            {
                string auto = Path.Combine(_state.Config.SelectedGamePath, "map", "MapStudio", "m17_00_00_00.msb.dcx");
                if (File.Exists(auto)) _msbPath = auto;
                else
                {
                    auto = Path.Combine(_state.Config.SelectedGamePath, "map", "MapStudio", "m17_00_00_00.msb");
                    if (File.Exists(auto)) _msbPath = auto;
                }
            }

            ImGui.Text("Trigger radius (m)");
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderFloat("##sdrad", ref _triggerRadius, 1f, 100f, "%.1f"))
                if (_shadowDir != null) _shadowDir.TriggerRadius = _triggerRadius;

            ImGui.Text("Smoothing (deg/sec, 720=instant)");
            ImGui.SetNextItemWidth(w - 60);
            if (ImGui.SliderFloat("##sdlerp", ref _lerpSpeed, 10f, 720f, _lerpSpeed >= 720f ? "Instant" : "%.0f"))
                if (_shadowDir != null) _shadowDir.LerpSpeed = _lerpSpeed;
            ImGui.SameLine();
            if (ImGui.Button("Inst##sdinstant", new Vector2(-1, 0)))
            {
                _lerpSpeed = 720f;
                if (_shadowDir != null) _shadowDir.LerpSpeed = 720f;
            }

            ImGui.Text("Update interval (ms, 0=max)");
            ImGui.SetNextItemWidth(w);
            if (ImGui.SliderInt("##sdinterval", ref _updateIntervalMs, 0, 100,
                _updateIntervalMs == 0 ? "Max rate" : "%d ms"))
                if (_shadowDir != null) _shadowDir.UpdateIntervalMs = _updateIntervalMs;

            if (_shadowDir != null && _shadowDir.PinnedSfxNames.Count > 1)
            {
                ImGui.Text("Multi: writes per source before yield");
                ImGui.SetNextItemWidth(w);
                if (ImGui.SliderInt("##sdmultiw", ref _multiWritesPerYield, 1, 1000,
                    _multiWritesPerYield == 1 ? "1 (yield between each)" : "%d"))
                    _shadowDir.MultiWritesPerYield = _multiWritesPerYield;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Higher = more writes per source per cycle.\nLower = more yields = more OS scheduling between sources.");
            }

            ImGui.Text("Azimuth offset (deg)");
            ImGui.SetNextItemWidth(w - 60);
            if (ImGui.SliderFloat("##sdoffset", ref _azimuthOffset, -180f, 180f, "%.1f"))
                if (_shadowDir != null) _shadowDir.AzimuthOffset = _azimuthOffset;
            ImGui.SameLine();
            if (ImGui.Button("Reset##sdoffreset", new Vector2(-1, 0)))
            {
                _azimuthOffset = 0f;
                if (_shadowDir != null) _shadowDir.AzimuthOffset = 0f;
            }

            ImGui.Spacing();

            // Status
            if (_shadowDir != null)
            {
                var col = _shadowDir.IsRunning
                    ? new Vector4(0.4f, 1f, 0.4f, 1f)
                    : new Vector4(0.7f, 0.7f, 0.7f, 1f);
                ImGui.TextColored(col, _shadowDir.StatusMessage);

                // Show player pos and nearest SFX pos for coordinate verification
                if (_state.Player != null && _state.Player.IsValid
                    && _state.Player.GetPosition(out float px, out float py, out float pz, out _))
                {
                    ImGui.TextDisabled($"Player: ({px:F1}, {py:F1}, {pz:F1})");
                }

                if (_shadowDir.NearestSfxDist < float.MaxValue)
                {
                    ImGui.TextDisabled($"Nearest: {_shadowDir.NearestSfxName}  d={_shadowDir.NearestSfxDist:F1}m");

                    // Show active SFX position if pinned or nearest is known
                    var activeSfx = _shadowDir.SfxPoints.FirstOrDefault(s =>
                        s.Name == _shadowDir.NearestSfxName);
                    if (activeSfx != null && activeSfx.HasRegion)
                    {
                        var p = activeSfx.Position;
                        ImGui.TextDisabled($"SFX pos: ({p.X:F1}, {p.Y:F1}, {p.Z:F1})");
                    }
                }
            }

            ImGui.Spacing();

            bool canInit = !string.IsNullOrEmpty(_msbPath) && File.Exists(_msbPath) && _state.Config.IsReady;

            if (ImGui.Button("Initialize##sdinit", new Vector2(-1, 0)) && canInit)
            {
                _shadowDir ??= new ShadowDirector(_state);
                _shadowDir.TriggerRadius = _triggerRadius;
                _shadowDir.Initialize(_msbPath);
            }
            if (!canInit && ImGui.IsItemHovered())
                ImGui.SetTooltip("Select a valid MSB file and configure game path first.");

            ImGui.Spacing();

            bool running = _shadowDir?.IsRunning == true;

            if (running)
            {
                if (ImGui.Button("Stop##sdstop", new Vector2(-1, 0)))
                {
                    _shadowDir?.Stop();
                    if (_shadowDir != null) _shadowDir.RotateMode = false;
                }
            }
            else
            {
                bool canStart = _shadowDir != null;
                float half = (w - 4) * 0.5f;

                if (ImGui.Button("SFX Track##sdstart", new Vector2(half, 0)) && canStart)
                {
                    _shadowDir!.RotateMode = false;
                    _shadowDir.Start();
                }
                if (!canStart && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Initialize first.");

                ImGui.SameLine();

                if (ImGui.Button("Rotate Y##sdrotate", new Vector2(-1, 0)) && canStart)
                {
                    _shadowDir!.RotateMode = true;
                    _shadowDir.Start();
                }
                if (!canStart && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Initialize first.");
            }

            if (_shadowDir != null)
            {
                ImGui.Text("Rotate speed (deg/sec)");
                ImGui.SetNextItemWidth(w);
                float rotSpeed = _shadowDir.RotateSpeed;
                if (ImGui.SliderFloat("##sdrotspd", ref rotSpeed, 10f, 720f, "%.0f"))
                    _shadowDir.RotateSpeed = rotSpeed;

                ImGui.Spacing();
                float half2 = (w - 4) * 0.5f;
                if (!_shadowDir.IsLogging)
                {
                    if (ImGui.Button("Start Log##sdlog", new Vector2(half2, 0)))
                        _shadowDir.StartLog();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Logs player pos, SFX pos, dist, computed angle and live rotY every {ShadowDirector.LOG_INTERVAL_MS:F0}ms.");
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1f));
                    if (ImGui.Button("Stop Log##sdlogstop", new Vector2(half2, 0)))
                        _shadowDir.StopLog();
                    ImGui.PopStyleColor();
                }
                ImGui.SameLine();
                if (ImGui.Button("Print Calibration Table##sdcalib", new Vector2(-1, 0)))
                    _shadowDir.PrintCalibrationTable();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Snapshot: player pos, all SFX, computed vs live angles.");

                ImGui.Spacing();
                ImGui.SeparatorText("Shadow Color Override");

                float half3 = (w - 4) * 0.5f;

                ImGui.Text("Density (0..100)");
                ImGui.SetNextItemWidth(w);
                int density = _shadowDir.ShadowDensity;
                if (ImGui.SliderInt("##sddensity", ref density, 0, 100))
                    _shadowDir.ShadowDensity = density;

                ImGui.Text("R");
                ImGui.SetNextItemWidth(half3);
                int cr = _shadowDir.ShadowColR;
                if (ImGui.SliderInt("##sdcolr", ref cr, 0, 255)) _shadowDir.ShadowColR = cr;
                ImGui.SameLine();
                ImGui.Text("G");
                ImGui.SetNextItemWidth(-1);
                int cg = _shadowDir.ShadowColG;
                if (ImGui.SliderInt("##sdcolg", ref cg, 0, 255)) _shadowDir.ShadowColG = cg;

                ImGui.Text("B");
                ImGui.SetNextItemWidth(half3);
                int cb = _shadowDir.ShadowColB;
                if (ImGui.SliderInt("##sdcolb", ref cb, 0, 255)) _shadowDir.ShadowColB = cb;
                ImGui.SameLine();
                if (ImGui.Button("Red##sdred", new Vector2(-1, 0)))
                {
                    _shadowDir.ShadowColR = 255; _shadowDir.ShadowColG = 0;
                    _shadowDir.ShadowColB = 0;   _shadowDir.ShadowDensity = 100;
                }

                if (ImGui.Button("Apply Color##sdapplycolor", new Vector2(-1, 0)))
                    _shadowDir.ApplyColorOverride();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Writes R/G/B/Density to ShadowBank row 1 once.");
            }

            // SFX list
            if (_shadowDir != null)
            {
                string hdr = _shadowDir.SfxPoints.Count > 0 ? $"SFX Points ({_shadowDir.SfxPoints.Count})" : "SFX Points";
                if (ImGui.CollapsingHeader(hdr))
                {
                    // "None" option — clear all pins
                    bool noneChecked = _shadowDir.PinnedSfxNames.Count == 0;
                    if (ImGui.Checkbox("Auto (nearest)##sfxpin_none", ref noneChecked) && noneChecked)
                        _shadowDir.ClearPins();
                    ImGui.SameLine();
                    int pinCount = _shadowDir.PinnedSfxNames.Count;
                    var pinCol = pinCount > 0 ? new Vector4(1f, 0.85f, 0.2f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f);
                    ImGui.TextColored(pinCol, $"{pinCount}/{ShadowDirector.MAX_PINNED} selected");

                    ImGui.BeginChild("SfxList", new Vector2(-1, 150), ImGuiChildFlags.Borders);
                    foreach (var sfx in _shadowDir.SfxPoints)
                    {
                        var pos = sfx.Position;
                        bool hasPos = sfx.HasRegion;
                        _shadowDir.SfxDistances.TryGetValue(sfx.Name, out float dist);
                        string distStr = dist > 0 ? $"  d={dist:F1}m" : "";
                        bool inRange = dist > 0 && dist <= 5f;
                        bool isPinned = _shadowDir.IsPinned(sfx.Name);
                        bool isActive = sfx.Name == _shadowDir.NearestSfxName;

                        var col = !hasPos    ? new Vector4(0.5f, 0.5f, 0.5f, 1f)  // grey   = no region
                                : isPinned && isActive ? new Vector4(1f, 0.6f, 0.1f, 1f)  // orange = pinned+active
                                : isPinned  ? new Vector4(1f, 0.85f, 0.2f, 1f)    // yellow = pinned
                                : inRange   ? new Vector4(0.4f, 1f, 0.4f, 1f)     // green  = in range
                                            : new Vector4(1f, 1f, 1f, 1f);         // white  = has pos

                        bool cb = isPinned;
                        if (!hasPos) ImGui.BeginDisabled();
                        // Disable checkbox if at limit and not already pinned
                        bool atLimit = !isPinned && _shadowDir.PinnedSfxNames.Count >= ShadowDirector.MAX_PINNED;
                        if (atLimit) ImGui.BeginDisabled();
                        ImGui.PushStyleColor(ImGuiCol.Text, col);
                        if (ImGui.Checkbox($"##sfxpin_{sfx.Name}", ref cb))
                            _shadowDir.TogglePin(sfx.Name);
                        ImGui.PopStyleColor();
                        if (atLimit) ImGui.EndDisabled();
                        if (!hasPos) ImGui.EndDisabled();

                        ImGui.SameLine();
                        ImGui.TextColored(col, $"[{sfx.EffectId}] {sfx.Name}{distStr}");
                        if (hasPos)
                            ImGui.TextDisabled($"     ({pos.X:F1},{pos.Y:F1},{pos.Z:F1})");
                        else
                            ImGui.TextDisabled($"     no region: '{sfx.Region}'");
                    }
                    ImGui.EndChild();
                }
            }
        }

        private void BrowseMsb()
        {
            string dir = _state.Config.IsReady
                ? Path.Combine(_state.Config.SelectedGamePath, "map", "MapStudio")
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            string? chosen = null;
            var t = new System.Threading.Thread(() =>
            {
                using var dlg = new System.Windows.Forms.OpenFileDialog
                {
                    Filter = "MSB files (*.msb;*.msb.dcx)|*.msb;*.msb.dcx|All files|*.*",
                    InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Title = "Select m17 MSB file"
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    chosen = dlg.FileName;
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (chosen != null) _msbPath = chosen;
        }
    }
}

using System;
using System.Numerics;
using ImGuiNET;

namespace DS1ParamEditor
{
    /// <summary>
    /// Top-level UI.
    /// Layout:
    ///   Left  — game/process controls + param file/param list
    ///   Right — top: LockCamParam (always visible, independent load)
    ///           bottom: selected param generic table
    /// </summary>
    public sealed class MainWindow
    {
        private readonly EditorState    _state       = new();
        private readonly ParamTableView _tableView;
        private readonly LockCamParamView _lockCamView;
        private readonly PlayerView     _playerView;
        private readonly GadgetView          _gadgetView;
        private readonly ExperimentalView    _experimentalView;
        private readonly MsbView             _msbView;

        private const float LEFT_WIDTH    = 380f;

        // Track active left tab for right panel routing
        private enum LeftTab { Teleport, Gadget, Params, Experimental, Msb }
        private LeftTab _activeLeftTab = LeftTab.Teleport;

        // Compact mode - hide right panel and shrink window
        private bool _compactMode = true;

        public MainWindow()
        {
            _tableView        = new ParamTableView(_state);
            _lockCamView      = new LockCamParamView(_state);
            _playerView       = new PlayerView(_state);
            _gadgetView       = new GadgetView(_state);
            _experimentalView = new ExperimentalView(_state);
            _msbView          = new MsbView(_state);
        }

        public void Draw()
        {
            DrawMenuBar();
            DrawStatusBar();
            ImGui.Separator();
            DrawBody();
        }

        // ── Menu bar ──────────────────────────────────────────────────────────

        private void DrawMenuBar()
        {
            if (!ImGui.BeginMenuBar()) return;

            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Select exe..."))
                    _state.SelectExe();

                if (ImGui.MenuItem("Save LockCamParam", _state.LockCamFile != null))
                    _state.SaveLockCamParam();

                if (ImGui.MenuItem("Save current param file", _state.SelectedFile != null))
                    _state.SaveCurrentFile();

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Process"))
            {
                string label = _state.IsAttached
                    ? $"Attached: {_state.Config.SelectedExe}"
                    : "Attach to game";
                if (ImGui.MenuItem(label))
                    _state.AttachProcess();

                if (ImGui.MenuItem("Clear hooks", _state.HookedAddresses.Count > 0))
                    _state.ClearHooks();

                ImGui.Separator();
                string consoleLabel = Program.ConsoleVisible ? "Hide Console" : "Show Console";
                if (ImGui.MenuItem(consoleLabel))
                    Program.ToggleConsole();

                ImGui.EndMenu();
            }

            // Compact mode toggle — right-aligned
            float toggleW = 24f;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - toggleW - ImGui.GetStyle().WindowPadding.X);
            string icon = _compactMode ? ">" : "<";
            if (ImGui.MenuItem(icon))
                ToggleCompactMode();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_compactMode ? "Expand (show right panel)" : "Compact (hide right panel)");

            ImGui.EndMenuBar();
        }

        private void ToggleCompactMode()
        {
            _compactMode = !_compactMode;
            var win = Program.AppWindow;
            int h = win.Height;
            int w = _compactMode ? Program.COMPACT_WIDTH : Program.FULL_WIDTH;
            win.Width  = w;
            win.Height = h;
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(_state.StatusMessage)) return;
            var color = _state.StatusIsError
                ? new Vector4(1f, 0.35f, 0.35f, 1f)
                : new Vector4(0.4f, 1f, 0.4f, 1f);
            ImGui.TextColored(color, _state.StatusMessage);
        }

        // ── Body ──────────────────────────────────────────────────────────────

        private void DrawBody()
        {
            float totalH = ImGui.GetContentRegionAvail().Y;

            float leftW = _compactMode ? -1f : LEFT_WIDTH;

            ImGui.BeginChild("LeftPanel", new Vector2(leftW, totalH), ImGuiChildFlags.Borders);
            DrawLeftPanel();
            ImGui.EndChild();

            if (!_compactMode)
            {
                ImGui.SameLine();
                ImGui.BeginChild("RightPanel", new Vector2(0, totalH), ImGuiChildFlags.Borders);
                DrawRightPanel(totalH);
                ImGui.EndChild();
            }
        }

        // ── Left panel ────────────────────────────────────────────────────────

        private void DrawLeftPanel()
        {
            // Game / process connection status
            ImGui.SeparatorText("Connection");
            
            // Exe path
            ImGui.TextDisabled(string.IsNullOrEmpty(_state.Config.SelectedExe)
                ? "No exe selected" : _state.Config.SelectedExe);

            if (ImGui.Button("Select exe", new Vector2(-1, 0)))
                _state.SelectExe();

            // Connection status
            bool paramsAttached = _state.IsAttached;
            bool gadgetAttached = _state.IsPlayerAttached;
            
            if (paramsAttached && gadgetAttached)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "* Params + Gadget");
            else if (paramsAttached)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "* Params only");
            else if (gadgetAttached)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "* Gadget only");
            else
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "- Not connected");

            // Single attach button
            if (!paramsAttached || !gadgetAttached)
            {
                if (ImGui.Button("Connect to game", new Vector2(-1, 0)))
                    _state.AttachProcess();
            }

            ImGui.Spacing();
            
            // Tabs for DSR-Gadget features
            if (ImGui.BeginTabBar("LeftTabs"))
            {
                if (ImGui.BeginTabItem("Teleport"))
                {
                    _activeLeftTab = LeftTab.Teleport;
                    _playerView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Gadget"))
                {
                    _activeLeftTab = LeftTab.Gadget;
                    _gadgetView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Params"))
                {
                    _activeLeftTab = LeftTab.Params;
                    DrawParamsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Experimental"))
                {
                    _activeLeftTab = LeftTab.Experimental;
                    _experimentalView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("MSB"))
                {
                    _activeLeftTab = LeftTab.Msb;
                    _msbView.DrawLeft();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawParamsTab()
        {
            // LockCamParam — compact, lives in left panel
            DrawLockCamControls();
            if (_state.LockCamParam != null)
            {
                if (ImGui.CollapsingHeader("LockCamParam##lcpleft"))
                {
                    DrawLockCamHookControls();
                    ImGui.Separator();
                    _lockCamView.Draw();
                }
            }

            ImGui.Spacing();

            // Main params
            ImGui.SeparatorText("Param Files");

            bool drawParams = _state.UseDrawParams;
            if (ImGui.Checkbox("Draw params", ref drawParams))
                _state.SetDrawParams(drawParams);

            // Pattern mode selector
            int modeIdx = (int)_state.ScanPatternMode;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##patmode", ref modeIdx, "Auto (96b)\0Custom length\0Full pattern\0\0"))
                _state.ScanPatternMode = (LoadedParam.PatternMode)modeIdx;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pattern used for AOB scan.\nAuto: best 96-byte window.\nCustom: enter byte count below.\nFull: entire row data.");

            // Pattern start selector
            int startIdx = (int)_state.ScanPatternStart;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##patstart", ref startIdx, "From row data start\0Best window (row data)\0From file start\0\0"))
                _state.ScanPatternStart = (LoadedParam.PatternStart)startIdx;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("From row data start: first N bytes of first row.\nBest window: most unique N-byte window in row data.\nFrom file start: first N bytes of file (header with ParamType string) — same as v0.4.1, most reliable.");

            if (_state.ScanPatternMode == LoadedParam.PatternMode.Custom)
            {
                int customLen = _state.CustomPatternLength;
                ImGui.SetNextItemWidth(-1);
                // Update on any change including manual keyboard input
                ImGui.InputInt("##patlen", ref customLen, 8, 32);
                int clamped = Math.Max(8, Math.Min(customLen, 4096));
                if (clamped != _state.CustomPatternLength)
                    _state.CustomPatternLength = clamped;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Number of bytes to use as scan pattern (8..4096). Press Enter or click +/- to apply.");
            }

            bool forceLoad = _state.ForceLoadParams;
            if (ImGui.Checkbox("Force load (bypass paramdef check)", ref forceLoad))
                _state.ForceLoadParams = forceLoad;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Apply paramdef unconditionally even if row size doesn't match.\nMay produce incorrect field values for incompatible params.");

            if (ImGui.Button("Load param files", new Vector2(-1, 0)))
                _state.LoadParams();

            ImGui.Spacing();

            if (_state.ParamFiles.Count > 0)
            {
                ImGui.Text("Files:");
                ImGui.BeginChild("FileList", new Vector2(-1, 120), ImGuiChildFlags.Borders);
                foreach (var file in _state.ParamFiles)
                {
                    bool sel = _state.SelectedFile == file;
                    if (ImGui.Selectable(file.FileName, sel))
                        _state.SelectFile(file);
                }
                ImGui.EndChild();
            }

            if (_state.SelectedFile != null)
            {
                ImGui.Spacing();
                ImGui.Text("Params:");
                ImGui.BeginChild("ParamList", new Vector2(-1, 0), ImGuiChildFlags.Borders);
                foreach (var lp in _state.SelectedFile.Params)
                {
                    bool hooked = _state.HookedAddresses.ContainsKey(lp.Name);
                    bool sel    = _state.SelectedParam == lp;
                    var  col    = hooked ? new Vector4(0.4f, 1f, 0.4f, 1f) : new Vector4(1f, 1f, 1f, 1f);
                    ImGui.PushStyleColor(ImGuiCol.Text, col);
                    if (ImGui.Selectable(hooked ? $"* {lp.Name}" : lp.Name, sel))
                        _state.SelectParam(lp);
                    ImGui.PopStyleColor();
                }
                ImGui.EndChild();
            }
        }

        private void DrawLockCamControls()
        {
            bool loaded = _state.LockCamParam != null;

            if (!loaded)
            {
                if (ImGui.Button("Load LockCamParam", new Vector2(-1, 0)))
                    _state.LoadLockCamParam();
            }
            else
            {
                if (ImGui.SmallButton("Reload##lc"))
                    _state.LoadLockCamParam();
                ImGui.SameLine();
                if (ImGui.SmallButton("Save##lc"))
                    _state.SaveLockCamParam();
            }
        }

        // ── Right panel ───────────────────────────────────────────────────────

        private void DrawRightPanel(float totalH)
        {
            if (_activeLeftTab == LeftTab.Msb)
            {
                _msbView.DrawRight();
                return;
            }

            if (_state.SelectedFile == null)
            {
                ImGui.TextDisabled("Select a param file from the left panel.");
                return;
            }

            DrawSelectedParamPane();
        }

        private void DrawLockCamHookControls()
        {
            var param = _state.LockCamParam;
            if (param == null) return;

            var hookedAddr = _state.GetHookedAddress("LockCamParam");

            if (hookedAddr.HasValue)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Hooked at 0x{hookedAddr.Value:X}");
                ImGui.SameLine();
                if (ImGui.Button("Re-hook##lchook"))
                {
                    _state.ClearLockCamHook();
                    _state.StartLockCamScan();
                }
                ImGui.SameLine();
                if (ImGui.Button("Save##lcsave"))
                    _state.SaveLockCamParam();
            }
            else if (_state.LockCamScanState == ScanState.Scanning)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Scanning...");
                ImGui.SameLine();
                if (ImGui.Button("Cancel##lccancel"))
                    _state.CancelLockCamScan();
            }
            else
            {
                if (ImGui.Button("Hook##lchook"))
                    _state.StartLockCamScan();
                ImGui.SameLine();
                string modeLabel = param.Mode switch {
                    LoadedParam.PatternMode.Custom => $"{param.ScanPattern.Length}b",
                    LoadedParam.PatternMode.Full   => "full",
                    _                              => "auto"
                };
                ImGui.TextDisabled($"Pattern: {param.ScanPattern.Length}b [{modeLabel}] @ +0x{param.ScanOffset:X}");
            }
        }

        private void DrawSelectedParamPane()
        {
            var file = _state.SelectedFile;
            if (file == null)
            {
                ImGui.TextDisabled("Select a param file on the left.");
                return;
            }

            ImGui.BeginChild("MultiParamScroll", Vector2.Zero, ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar);

            foreach (var lp in file.Params)
            {
                bool hooked = _state.HookedAddresses.ContainsKey(lp.Name);
                var hdrCol = hooked ? new Vector4(0.4f, 1f, 0.4f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f);
                string hdrLabel = (hooked ? "* " : "") + lp.Name + $"##ph_{lp.Name}";

                ImGui.PushStyleColor(ImGuiCol.Text, hdrCol);
                bool open = ImGui.CollapsingHeader(hdrLabel);
                ImGui.PopStyleColor();

                if (!open) continue;

                ImGui.Indent();
                DrawInlineHookControls(lp);
                ImGui.Separator();
                _tableView.DrawParam(lp);
                ImGui.Unindent();
                ImGui.Spacing();
            }

            ImGui.EndChild();
        }

        private void DrawInlineHookControls(LoadedParam param)
        {
            var hookedAddr = _state.GetHookedAddress(param.Name);
            string modeLabel = param.Mode switch {
                LoadedParam.PatternMode.Custom => $"{param.ScanPattern.Length}b",
                LoadedParam.PatternMode.Full   => "full",
                _                              => "auto"
            };

            if (hookedAddr.HasValue)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"0x{hookedAddr.Value:X}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Re-hook##{param.Name}"))
                {
                    _state.SelectParam(param);
                    _state.ClearHooks();
                    _state.StartScan();
                }
                ImGui.SameLine();
                if (ImGui.SmallButton($"Save##{param.Name}"))
                    _state.SaveCurrentFile();
            }
            else if (_state.ScanState == ScanState.Scanning && _state.SelectedParam == param)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Scanning...");
                ImGui.SameLine();
                if (ImGui.SmallButton($"Cancel##{param.Name}"))
                    _state.CancelScan();
            }
            else
            {
                if (ImGui.SmallButton($"Hook##{param.Name}"))
                {
                    _state.SelectParam(param);
                    _state.StartScan();
                }
                ImGui.SameLine();
                ImGui.TextDisabled($"{param.ScanPattern.Length}b [{modeLabel}] @ +0x{param.ScanOffset:X}");
            }
        }

        private void DrawHookControls(LoadedParam param)
        {
            var hookedAddr = _state.GetHookedAddress(param.Name);

            if (hookedAddr.HasValue)
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Hooked at 0x{hookedAddr.Value:X}");
                ImGui.SameLine();
                if (ImGui.Button("Re-hook"))
                {
                    _state.ClearHooks();
                    _state.StartScan();
                }
                ImGui.SameLine();
                if (ImGui.Button("Save file"))
                    _state.SaveCurrentFile();
            }
            else if (_state.ScanState == ScanState.Scanning)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Scanning...");
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                    _state.CancelScan();
            }
            else
            {
                if (ImGui.Button("Hook"))
                    _state.StartScan();
                ImGui.SameLine();
                string modeLabel = param.Mode switch {
                    LoadedParam.PatternMode.Custom => $"{param.ScanPattern.Length}b",
                    LoadedParam.PatternMode.Full   => "full",
                    _                              => "auto"
                };
                ImGui.TextDisabled($"Pattern: {param.ScanPattern.Length}b [{modeLabel}] @ +0x{param.ScanOffset:X}");
            }
        }
    }
}

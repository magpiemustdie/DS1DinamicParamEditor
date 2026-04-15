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
        // Fraction of right panel height given to LockCam when both are visible
        private const float LOCK_CAM_FRAC = 0.45f;

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

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
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

            ImGui.BeginChild("LeftPanel", new Vector2(LEFT_WIDTH, totalH), ImGuiChildFlags.Borders);
            DrawLeftPanel();
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("RightPanel", new Vector2(0, totalH), ImGuiChildFlags.Borders);
            DrawRightPanel(totalH);
            ImGui.EndChild();
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
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "● Params + Gadget");
            else if (paramsAttached)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "● Params only");
            else if (gadgetAttached)
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "● Gadget only");
            else
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "○ Not connected");

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
                    _playerView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Gadget"))
                {
                    _gadgetView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Params"))
                {
                    DrawParamsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Experimental"))
                {
                    _experimentalView.Draw();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("MSB"))
                {
                    _msbView.Draw();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawParamsTab()
        {
            // LockCamParam quick-load
            ImGui.SeparatorText("LockCamParam");
            DrawLockCamControls();

            ImGui.Spacing();

            // Main params
            ImGui.SeparatorText("Param Files");

            bool drawParams = _state.UseDrawParams;
            if (ImGui.Checkbox("Draw params", ref drawParams))
                _state.SetDrawParams(drawParams);

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
                    if (ImGui.Selectable(hooked ? $"● {lp.Name}" : lp.Name, sel))
                        _state.SelectParam(lp);
                    ImGui.PopStyleColor();
                }
                ImGui.EndChild();
            }
        }

        private void DrawLockCamControls()
        {
            bool loaded = _state.LockCamParam != null;
            bool hooked = _state.GetHookedAddress("LockCamParam").HasValue;

            if (!loaded)
            {
                if (ImGui.Button("Load LockCamParam", new Vector2(-1, 0)))
                    _state.LoadLockCamParam();
                return;
            }

            // Loaded — show hook state
            if (hooked)
            {
                var a = _state.GetHookedAddress("LockCamParam")!.Value;
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"● 0x{a:X}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Re-hook##lc"))
                {
                    _state.ClearLockCamHook();
                    _state.StartLockCamScan();
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Save##lc"))
                    _state.SaveLockCamParam();
            }
            else if (_state.LockCamScanState == ScanState.Scanning)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Scanning...");
                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel##lc"))
                    _state.CancelLockCamScan();
            }
            else
            {
                if (ImGui.Button("Hook LockCamParam", new Vector2(-1, 0)))
                    _state.StartLockCamScan();
            }
        }

        // ── Right panel ───────────────────────────────────────────────────────

        private void DrawRightPanel(float totalH)
        {
            bool hasLockCam  = _state.LockCamParam != null;
            bool hasSelected = _state.SelectedParam != null;

            if (!hasLockCam && !hasSelected)
            {
                ImGui.TextDisabled("Load LockCamParam or select a param from the left panel.");
                return;
            }

            if (hasLockCam && hasSelected)
            {
                // Split: LockCam on top, selected param below
                float lockH  = totalH * LOCK_CAM_FRAC - 4;
                float paramH = totalH - lockH - 12;

                ImGui.BeginChild("LockCamPane", new Vector2(0, lockH), ImGuiChildFlags.Borders);
                DrawLockCamPane();
                ImGui.EndChild();

                ImGui.BeginChild("ParamPane", new Vector2(0, paramH), ImGuiChildFlags.Borders);
                DrawSelectedParamPane();
                ImGui.EndChild();
            }
            else if (hasLockCam)
            {
                DrawLockCamPane();
            }
            else
            {
                DrawSelectedParamPane();
            }
        }

        private void DrawLockCamPane()
        {
            ImGui.SeparatorText("LockCamParam");
            _lockCamView.Draw();
        }

        private void DrawSelectedParamPane()
        {
            var param = _state.SelectedParam!;
            ImGui.SeparatorText(param.Name);
            DrawHookControls(param);
            ImGui.Separator();
            _tableView.Draw();
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
                ImGui.TextDisabled($"Pattern: {param.ScanPattern.Length} bytes @ +0x{param.ScanOffset:X}");
            }
        }
    }
}

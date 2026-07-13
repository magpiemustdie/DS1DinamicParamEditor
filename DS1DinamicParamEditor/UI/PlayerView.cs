using System;
using System.Numerics;
using ImGuiNET;

namespace DS1ParamEditor
{
    public sealed class PlayerView
    {
        private readonly EditorState _state;

        private float _warpX, _warpY, _warpZ, _warpAngle;

        // Bonfire selector
        private int    _bonfireIdx   = 0;
        private string _areaFilter   = string.Empty;

        public PlayerView(EditorState state)
        {
            _state = state;
        }

        public void Reset()
        {
            _warpX = _warpY = _warpZ = _warpAngle = 0;
            _bonfireIdx = 0;
            _areaFilter = string.Empty;
        }

        public void Draw()
        {
            if (!_state.IsPlayerAttached || _state.Player == null)
            {
                ImGui.TextDisabled("Gadget not connected.");
                ImGui.TextDisabled("Use 'Connect to game' button above.");
                return;
            }

            var player = _state.Player;

            if (!player.Hooked)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Waiting for game...");
                return;
            }

            if (!player.IsValid)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Waiting for world to load...");
                ImGui.TextDisabled(player.GetDiagnostics());
                return;
            }

            // Show game version
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), $"Game {player.Version}");
            ImGui.Separator();

            // ── Live position ─────────────────────────────────────────────────
            if (player.GetPosition(out float x, out float y, out float z, out float angle))
            {
                float fw = ImGui.GetContentRegionAvail().X * 0.5f - 4;

                ImGui.Text("X"); ImGui.SameLine();
                ImGui.SetNextItemWidth(fw - ImGui.CalcTextSize("X").X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.InputFloat("##px", ref x, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                ImGui.Text("Y"); ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat("##py", ref y, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly);

                ImGui.Text("Z"); ImGui.SameLine();
                ImGui.SetNextItemWidth(fw - ImGui.CalcTextSize("Z").X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.InputFloat("##pz", ref z, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                ImGui.Text("A"); ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat("##pa", ref angle, 0, 0, "%.2f", ImGuiInputTextFlags.ReadOnly);

                if (ImGui.Button("Copy Position##cpw", new Vector2(-1, 0)))
                {
                    _warpX = x; _warpY = y; _warpZ = z; _warpAngle = angle;
                }
            }

            // ── Pos warp ──────────────────────────────────────────────────────
            ImGui.SeparatorText("Position Warp");
            {
                float fw2 = ImGui.GetContentRegionAvail().X * 0.5f - 4;
                float lw  = ImGui.CalcTextSize("X").X + ImGui.GetStyle().ItemSpacing.X;

                ImGui.Text("X"); ImGui.SameLine();
                ImGui.SetNextItemWidth(fw2 - lw);
                ImGui.InputFloat("##wx", ref _warpX, 0, 0, "%.2f");
                ImGui.SameLine();
                ImGui.Text("Y"); ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat("##wy", ref _warpY, 0, 0, "%.2f");

                ImGui.Text("Z"); ImGui.SameLine();
                ImGui.SetNextItemWidth(fw2 - lw);
                ImGui.InputFloat("##wz", ref _warpZ, 0, 0, "%.2f");
                ImGui.SameLine();
                ImGui.Text("A"); ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat("##wa", ref _warpAngle, 0, 0, "%.2f");
            }

            if (ImGui.Button("Warp to Position##do", new Vector2(-1, 0)))
            {
                if (player.PosWarp(_warpX, _warpY, _warpZ, _warpAngle))
                    Console.WriteLine($"[PlayerView] Warped to ({_warpX:F2}, {_warpY:F2}, {_warpZ:F2})");
                else
                    Console.WriteLine("[PlayerView] Position warp failed");
            }

            // ── Bonfire warp ──────────────────────────────────────────────────
            ImGui.SeparatorText("Bonfire Warp");

            // Show current LastBonfire
            int current = player.GetLastBonfire();
            if (current != -1)
            {
                string curName = FindBonfireName(current);
                ImGui.PushTextWrapPos(0f);
                ImGui.TextDisabled($"Last bonfire: {curName} ({current})");
                ImGui.PopTextWrapPos();
            }

            // Filter by area
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##areafilter", "Filter by area or name...", ref _areaFilter, 64);

            // Bonfire list
            ImGui.BeginChild("BonfireList", new Vector2(-1, 150), ImGuiChildFlags.Borders);
            for (int i = 0; i < BonfireData.All.Length; i++)
            {
                var b = BonfireData.All[i];
                if (!string.IsNullOrEmpty(_areaFilter) &&
                    !b.Area.Contains(_areaFilter, StringComparison.OrdinalIgnoreCase) &&
                    !b.Name.Contains(_areaFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool sel = _bonfireIdx == i;
                string label = $"[{b.Area}] {b.Name}";
                if (ImGui.Selectable($"{label}##{i}", sel))
                    _bonfireIdx = i;
                if (sel) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndChild();

            bool autoLoad = _state.AutoLoadParamsOnWarp;
            if (ImGui.Checkbox("Auto-load map params after warp", ref autoLoad))
                _state.AutoLoadParamsOnWarp = autoLoad;

            if (ImGui.Button("Warp to Bonfire##bf", new Vector2(-1, 0)))
            {
                if (_bonfireIdx >= 0 && _bonfireIdx < BonfireData.All.Length)
                {
                    int bonfireId = BonfireData.All[_bonfireIdx].Id;
                    if (bonfireId >= 0)
                    {
                        player.LastBonfire = bonfireId;
                        player.BonfireWarp();

                        if (_state.AutoLoadParamsOnWarp)
                        {
                            var mapBytes = player.ReadMapId();
                            if (mapBytes != null && mapBytes.Length >= 2)
                            {
                                string mapName = $"m{mapBytes[0]:X2}_{mapBytes[1]:X2}";
                                _state.LoadParamsByMapName(mapName);
                            }
                        }
                    }
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Instantly teleports to the selected bonfire using DSR-Gadget method.");

            // Diagnostics
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled(player.GetBonfireDiagnostics());
            ImGui.PopTextWrapPos();
        }

        private static string FindBonfireName(int id)
        {
            foreach (var b in BonfireData.All)
                if (b.Id == id) return $"[{b.Area}] {b.Name}";
            return "Unknown";
        }
    }
}

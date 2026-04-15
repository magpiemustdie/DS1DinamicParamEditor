using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DS1ParamEditor.MSBReader;
using ImGuiNET;
using SoulsFormats;

namespace DS1ParamEditor
{
    public sealed class MsbView
    {
        private readonly EditorState _state;
        private readonly MsbStore _store = new();

        // File browser
        private string[] _msbFiles = Array.Empty<string>();
        private int _selectedFileIdx = -1;
        private string _fileFilter = string.Empty;

        // Loaded MSB
        private LoadedMsb? _loaded;

        // Part browser
        private string _partFilter = string.Empty;
        private int _selectedPartIdx = -1;
        private List<MSB1.Part> _filteredParts = new();
        private int _partTypeFilter = 0; // 0=All,1=MapPiece,2=Object,3=Enemy,4=Player

        // Selected part editing
        private Vector3 _editPos, _editRot, _editScale;
        private string _editName = string.Empty;
        private string _editModel = string.Empty;
        private bool _editDirty;

        // Region browser
        private string _regionFilter = string.Empty;
        private int _selectedRegionIdx = -1;
        private List<MSB1.Region> _filteredRegions = new();

        private static readonly string[] PartTypeNames = { "All", "MapPiece", "Object", "Enemy", "Player" };

        public MsbView(EditorState state)
        {
            _state = state;
        }

        public void Draw()
        {
            if (!_state.Config.IsReady)
            {
                ImGui.TextDisabled("Select game exe first.");
                return;
            }

            if (ImGui.BeginTabBar("MsbTabs"))
            {
                if (ImGui.BeginTabItem("Files"))
                {
                    DrawFilesTab();
                    ImGui.EndTabItem();
                }
                if (_loaded != null)
                {
                    if (ImGui.BeginTabItem("Parts"))
                    {
                        DrawPartsTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Regions"))
                    {
                        DrawRegionsTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Events"))
                    {
                        DrawEventsTab();
                        ImGui.EndTabItem();
                    }
                }
                ImGui.EndTabBar();
            }
        }

        // ── Files tab ─────────────────────────────────────────────────────────

        private void DrawFilesTab()
        {
            string msbDir = Path.Combine(_state.Config.SelectedGamePath, "map", "MapStudio");

            if (ImGui.Button("Refresh##msb", new Vector2(-1, 0)))
                RefreshFileList(msbDir);

            if (_msbFiles.Length == 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip(msbDir);

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##msbfilter", "Filter...", ref _fileFilter, 64);

            ImGui.BeginChild("MsbFileList", new Vector2(-1, 200), ImGuiChildFlags.Borders);
            for (int i = 0; i < _msbFiles.Length; i++)
            {
                string name = Path.GetFileName(_msbFiles[i]);
                if (!string.IsNullOrEmpty(_fileFilter) &&
                    !name.Contains(_fileFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool sel = _selectedFileIdx == i;
                if (ImGui.Selectable(name + "##f" + i, sel))
                    _selectedFileIdx = i;
            }
            ImGui.EndChild();

            bool hasFile = _selectedFileIdx >= 0 && _selectedFileIdx < _msbFiles.Length;

            if (ImGui.Button("Load##msbload", new Vector2(-1, 0)) && hasFile)
                LoadSelected();

            if (_loaded != null)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), $"Loaded: {_loaded.FileName}");

                var msb = _loaded.Msb;
                ImGui.TextDisabled($"Parts: {msb.Parts.GetEntries().Count}  " +
                    $"Regions: {msb.Regions.GetEntries().Count}  " +
                    $"Events: {msb.Events.GetEntries().Count}");

                ImGui.Spacing();
                if (ImGui.Button("Save##msbsave", new Vector2(-1, 0)))
                {
                    try { _store.Save(_loaded); ImGui.SetTooltip("Saved!"); }
                    catch (Exception ex) { Console.WriteLine($"[MsbView] Save failed: {ex.Message}"); }
                }
            }

            if (_msbFiles.Length == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("No MSB files found.");
                ImGui.TextDisabled(msbDir);
                if (ImGui.Button("Browse folder##msbdir", new Vector2(-1, 0)))
                    RefreshFileList(msbDir);
            }
        }

        // ── Parts tab ─────────────────────────────────────────────────────────

        private void DrawPartsTab()
        {
            if (_loaded == null) return;

            float w = ImGui.GetContentRegionAvail().X;

            // Type filter
            ImGui.SetNextItemWidth(w);
            if (ImGui.Combo("##parttype", ref _partTypeFilter, PartTypeNames, PartTypeNames.Length))
                RebuildPartList();

            // Name filter
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputTextWithHint("##partfilter", "Filter by name...", ref _partFilter, 64))
                RebuildPartList();

            ImGui.TextDisabled($"{_filteredParts.Count} parts");

            // Part list
            ImGui.BeginChild("PartList", new Vector2(-1, 180), ImGuiChildFlags.Borders);
            for (int i = 0; i < _filteredParts.Count; i++)
            {
                var part = _filteredParts[i];
                string label = $"[{GetPartTypeName(part)}] {part.Name}##p{i}";
                bool sel = _selectedPartIdx == i;
                if (ImGui.Selectable(label, sel))
                {
                    _selectedPartIdx = i;
                    LoadPartForEdit(part);
                }
            }
            ImGui.EndChild();

            // Edit panel
            if (_selectedPartIdx >= 0 && _selectedPartIdx < _filteredParts.Count)
                DrawPartEditor(_filteredParts[_selectedPartIdx]);
        }

        private void DrawPartEditor(MSB1.Part part)
        {
            ImGui.Separator();
            ImGui.Text($"Edit: {part.Name}");

            float w = ImGui.GetContentRegionAvail().X;

            // ── Common fields ─────────────────────────────────────────────────
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputText("Name##pname", ref _editName, 128)) _editDirty = true;

            ImGui.SetNextItemWidth(w);
            if (ImGui.InputText("Model##pmodel", ref _editModel, 64)) _editDirty = true;

            ImGui.Text("Position");
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputFloat3("##ppos", ref _editPos)) _editDirty = true;

            ImGui.Text("Rotation (deg)");
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputFloat3("##prot", ref _editRot)) _editDirty = true;

            ImGui.Text("Scale");
            ImGui.SetNextItemWidth(w);
            if (ImGui.InputFloat3("##pscale", ref _editScale)) _editDirty = true;

            // ── MapPiece-specific ─────────────────────────────────────────────
            if (part is MSB1.Part.MapPiece mp)
                DrawMapPieceFields(mp);

            // ── Collision-specific ────────────────────────────────────────────
            if (part is MSB1.Part.Collision col)
                DrawCollisionFields(col);

            if (_editDirty)
            {
                if (ImGui.Button("Apply##papply", new Vector2(-1, 0)))
                {
                    _store.SetPartPosition(part, _editPos);
                    _store.SetPartRotation(part, _editRot);
                    _store.SetPartScale(part, _editScale);
                    _store.RenamePart(part, _editName);
                    _store.SetPartModel(part, _editModel);
                    _editDirty = false;
                    Console.WriteLine($"[MsbView] Updated part '{part.Name}'");
                }
            }
        }

        private void DrawMapPieceFields(MSB1.Part.MapPiece mp)
        {
            // DrawParam bank IDs are on the base Part class
            DrawPartBankIds(mp);

            ImGui.Spacing();
            ImGui.SeparatorText("Entity");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            int entityId = mp.EntityID;
            if (ImGui.InputInt("EntityID##mpeid", ref entityId)) { mp.EntityID = entityId; _editDirty = true; }
        }

        private void DrawPartBankIds(MSB1.Part part)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("DrawParam Banks");

            float hw = (ImGui.GetContentRegionAvail().X - 8) * 0.5f;

            ImGui.SetNextItemWidth(hw);
            int lightId = part.LightID;
            if (ImGui.InputInt("Light##light", ref lightId)) { part.LightID = (byte)lightId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int fogId = part.FogID;
            if (ImGui.InputInt("Fog##fog", ref fogId)) { part.FogID = (byte)fogId; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            int scatterId = part.ScatterID;
            if (ImGui.InputInt("Scatter##scatter", ref scatterId)) { part.ScatterID = (byte)scatterId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int lensFlareId = part.LensFlareID;
            if (ImGui.InputInt("LensFlare##lf", ref lensFlareId)) { part.LensFlareID = (byte)lensFlareId; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            int shadowId = part.ShadowID;
            if (ImGui.InputInt("Shadow##shadow", ref shadowId)) { part.ShadowID = (byte)shadowId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int dofId = part.DofID;
            if (ImGui.InputInt("Dof##dof", ref dofId)) { part.DofID = (byte)dofId; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            int toneMapId = part.ToneMapID;
            if (ImGui.InputInt("ToneMap##tm", ref toneMapId)) { part.ToneMapID = (byte)toneMapId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int toneCorrectId = part.ToneCorrectID;
            if (ImGui.InputInt("ToneCorrect##tc", ref toneCorrectId)) { part.ToneCorrectID = (byte)toneCorrectId; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            int lanternId = part.LanternID;
            if (ImGui.InputInt("Lantern##lantern", ref lanternId)) { part.LanternID = (byte)lanternId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int lodParamId = part.LodParamID;
            if (ImGui.InputInt("LodParam##lod", ref lodParamId)) { part.LodParamID = (byte)lodParamId; _editDirty = true; }
        }

        private void DrawCollisionFields(MSB1.Part.Collision col)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Collision");

            float w = ImGui.GetContentRegionAvail().X;
            float hw = (w - 8) * 0.5f;

            ImGui.SetNextItemWidth(hw);
            int hitFilterId = col.HitFilterID;
            if (ImGui.InputInt("HitFilter##colhf", ref hitFilterId)) { col.HitFilterID = (byte)hitFilterId; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int soundSpaceType = col.SoundSpaceType;
            if (ImGui.InputInt("SoundSpace##colss", ref soundSpaceType)) { col.SoundSpaceType = (byte)soundSpaceType; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            float reflectPlane = col.ReflectPlaneHeight;
            if (ImGui.InputFloat("ReflectPlane##colrp", ref reflectPlane)) { col.ReflectPlaneHeight = reflectPlane; _editDirty = true; }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(hw);
            int envLightMapSpot = col.EnvLightMapSpotIndex;
            if (ImGui.InputInt("EnvLightMapSpot##colel", ref envLightMapSpot)) { col.EnvLightMapSpotIndex = (short)envLightMapSpot; _editDirty = true; }

            ImGui.SetNextItemWidth(hw);
            int mapNameId = col.MapNameID;
            if (ImGui.InputInt("MapNameID##colmn", ref mapNameId)) { col.MapNameID = (short)mapNameId; _editDirty = true; }

            // DrawParam banks also apply to collision
            DrawPartBankIds(col);

            ImGui.Spacing();
            ImGui.SeparatorText("Entity");
            ImGui.SetNextItemWidth(w);
            int entityId = col.EntityID;
            if (ImGui.InputInt("EntityID##coleid", ref entityId)) { col.EntityID = entityId; _editDirty = true; }
        }

        // ── Regions tab ───────────────────────────────────────────────────────

        private void DrawRegionsTab()
        {
            if (_loaded == null) return;

            float w = ImGui.GetContentRegionAvail().X;

            ImGui.SetNextItemWidth(w);
            if (ImGui.InputTextWithHint("##regfilter", "Filter by name...", ref _regionFilter, 64))
                RebuildRegionList();

            ImGui.TextDisabled($"{_filteredRegions.Count} regions");

            ImGui.BeginChild("RegionList", new Vector2(-1, 200), ImGuiChildFlags.Borders);
            for (int i = 0; i < _filteredRegions.Count; i++)
            {
                var reg = _filteredRegions[i];
                bool sel = _selectedRegionIdx == i;
                if (ImGui.Selectable($"{reg.Name}##r{i}", sel))
                    _selectedRegionIdx = i;
                if (sel)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  ({reg.Position.X:F1}, {reg.Position.Y:F1}, {reg.Position.Z:F1})");
                }
            }
            ImGui.EndChild();

            if (_selectedRegionIdx >= 0 && _selectedRegionIdx < _filteredRegions.Count)
            {
                var reg = _filteredRegions[_selectedRegionIdx];
                ImGui.Separator();
                ImGui.Text($"Region: {reg.Name}");
                var pos = reg.Position;
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputFloat3("Position##rpos", ref pos))
                    _store.SetRegionPosition(reg, pos);
            }
        }

        // ── Events tab ────────────────────────────────────────────────────────

        private void DrawEventsTab()
        {
            if (_loaded == null) return;

            var events = _store.GetEvents(_loaded);
            ImGui.TextDisabled($"{events.Count} events");

            ImGui.BeginChild("EventList", new Vector2(-1, -1), ImGuiChildFlags.Borders);
            foreach (var ev in events)
            {
                string typeName = ev.GetType().Name;
                ImGui.TextDisabled($"[{typeName}] {ev.Name}");
            }
            ImGui.EndChild();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshFileList(string dir)
        {
            if (!Directory.Exists(dir)) { _msbFiles = Array.Empty<string>(); return; }
            _msbFiles = Directory.GetFiles(dir, "*.msb*")
                .OrderBy(f => Path.GetFileName(f))
                .ToArray();
            Console.WriteLine($"[MsbView] Found {_msbFiles.Length} MSB files in {dir}");
        }

        private void LoadSelected()
        {
            try
            {
                _loaded = _store.Load(_msbFiles[_selectedFileIdx]);
                _selectedPartIdx = -1;
                _selectedRegionIdx = -1;
                RebuildPartList();
                RebuildRegionList();
                _store.PrintSummary(_loaded);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MsbView] Load failed: {ex.Message}");
            }
        }

        private void RebuildPartList()
        {
            if (_loaded == null) { _filteredParts.Clear(); return; }

            IEnumerable<MSB1.Part> parts = _partTypeFilter switch
            {
                1 => _loaded.Msb.Parts.MapPieces.Cast<MSB1.Part>(),
                2 => _loaded.Msb.Parts.Objects.Cast<MSB1.Part>(),
                3 => _loaded.Msb.Parts.Enemies.Cast<MSB1.Part>(),
                4 => _loaded.Msb.Parts.Players.Cast<MSB1.Part>(),
                _ => _loaded.Msb.Parts.GetEntries()
            };

            if (!string.IsNullOrEmpty(_partFilter))
                parts = parts.Where(p => p.Name.Contains(_partFilter, StringComparison.OrdinalIgnoreCase));

            _filteredParts = parts.ToList();
            _selectedPartIdx = -1;
        }

        private void RebuildRegionList()
        {
            if (_loaded == null) { _filteredRegions.Clear(); return; }
            var all = _store.GetRegions(_loaded);
            _filteredRegions = string.IsNullOrEmpty(_regionFilter)
                ? all.ToList()
                : all.Where(r => r.Name.Contains(_regionFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            _selectedRegionIdx = -1;
        }

        private void LoadPartForEdit(MSB1.Part part)
        {
            _editPos   = part.Position;
            _editRot   = part.Rotation;
            _editScale = part.Scale;
            _editName  = part.Name;
            _editModel = part.ModelName ?? string.Empty;
            _editDirty = false;
        }

        private static string GetPartTypeName(MSB1.Part part) => part switch
        {
            MSB1.Part.MapPiece => "MapPiece",
            MSB1.Part.Object   => "Object",
            MSB1.Part.Enemy    => "Enemy",
            MSB1.Part.Player   => "Player",
            _                  => "Part"
        };
    }
}

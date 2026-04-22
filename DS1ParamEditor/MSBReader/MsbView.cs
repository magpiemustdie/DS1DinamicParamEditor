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

        /// <summary>Left panel: file picker only.</summary>
        public void DrawLeft()
        {
            if (!_state.Config.IsReady)
            {
                ImGui.TextDisabled("Select game exe first.");
                return;
            }
            DrawFilesTab();
        }

        /// <summary>Right panel: Parts / Regions / Events tabs.</summary>
        public bool HasContent => _loaded != null;

        public void DrawRight()
        {
            if (_loaded == null)
            {
                ImGui.TextDisabled("Load an MSB file from the MSB tab.");
                return;
            }

            ImGui.SeparatorText(_loaded.FileName);

            if (ImGui.BeginTabBar("MsbContentTabs"))
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
            ImGui.Text("Name"); ImGui.SetNextItemWidth(w);
            if (ImGui.InputText("##pname", ref _editName, 128)) _editDirty = true;

            ImGui.Text("Model"); ImGui.SetNextItemWidth(w);
            if (ImGui.InputText("##pmodel", ref _editModel, 64)) _editDirty = true;

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
            if (ImGui.InputInt("##mpeid", ref entityId)) { mp.EntityID = entityId; _editDirty = true; }
        }

        private void DrawPartBankIds(MSB1.Part part)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("DrawParam Banks");

            const float FIELD_W = 80f;
            int v;
            v = part.LightID;       ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##light",   ref v, 0, 0)) { part.LightID      = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Light");
            v = part.FogID;         ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##fog",     ref v, 0, 0)) { part.FogID        = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Fog");
            v = part.ScatterID;     ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##scatter", ref v, 0, 0)) { part.ScatterID    = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Scatter");
            v = part.LensFlareID;   ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##lf",      ref v, 0, 0)) { part.LensFlareID  = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("LensFlare");
            v = part.ShadowID;      ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##shadow",  ref v, 0, 0)) { part.ShadowID     = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Shadow");
            v = part.DofID;         ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##dof",     ref v, 0, 0)) { part.DofID        = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Dof");
            v = part.ToneMapID;     ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##tm",      ref v, 0, 0)) { part.ToneMapID    = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("ToneMap");
            v = part.ToneCorrectID; ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##tc",      ref v, 0, 0)) { part.ToneCorrectID= (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("ToneCorrect");
            v = part.LanternID;     ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##lantern", ref v, 0, 0)) { part.LanternID    = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("Lantern");
            v = part.LodParamID;    ImGui.SetNextItemWidth(FIELD_W); if (ImGui.InputInt("##lod",     ref v, 0, 0)) { part.LodParamID   = (byte)Math.Clamp(v,0,255); _editDirty=true; } ImGui.SameLine(); ImGui.Text("LodParam");
        }

        private void DrawCollisionFields(MSB1.Part.Collision col)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Collision");

            float w = ImGui.GetContentRegionAvail().X;

            ImGui.Text("HitFilter"); ImGui.SetNextItemWidth(w);
            int hitFilterId = col.HitFilterID;
            if (ImGui.InputInt("##colhf", ref hitFilterId)) { col.HitFilterID = (byte)hitFilterId; _editDirty = true; }

            ImGui.Text("SoundSpace"); ImGui.SetNextItemWidth(w);
            int soundSpaceType = col.SoundSpaceType;
            if (ImGui.InputInt("##colss", ref soundSpaceType)) { col.SoundSpaceType = (byte)soundSpaceType; _editDirty = true; }

            ImGui.Text("ReflectPlane"); ImGui.SetNextItemWidth(w);
            float reflectPlane = col.ReflectPlaneHeight;
            if (ImGui.InputFloat("##colrp", ref reflectPlane)) { col.ReflectPlaneHeight = reflectPlane; _editDirty = true; }

            ImGui.Text("EnvLightMapSpot"); ImGui.SetNextItemWidth(w);
            int envLightMapSpot = col.EnvLightMapSpotIndex;
            if (ImGui.InputInt("##colel", ref envLightMapSpot)) { col.EnvLightMapSpotIndex = (short)envLightMapSpot; _editDirty = true; }

            ImGui.Text("MapNameID"); ImGui.SetNextItemWidth(w);
            int mapNameId = col.MapNameID;
            if (ImGui.InputInt("##colmn", ref mapNameId)) { col.MapNameID = (short)mapNameId; _editDirty = true; }

            DrawPartBankIds(col);

            ImGui.Spacing();
            ImGui.SeparatorText("Entity");
            ImGui.SetNextItemWidth(w);
            int entityId = col.EntityID;
            if (ImGui.InputInt("##coleid", ref entityId)) { col.EntityID = entityId; _editDirty = true; }
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

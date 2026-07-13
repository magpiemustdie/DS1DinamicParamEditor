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

        // Unified table
        private List<MsbEntry> _unifiedEntries = new();
        private List<MsbEntry> _filteredUnified = new();
        private string _unifiedFilter = string.Empty;
        private int _unifiedSubtypeFilter = 0;
        private int _unifiedSelected = -1;
        private int _unifiedSortCol = -1;
        private bool _unifiedSortAsc = true;
        private List<float> _cachedColumnWidths = new();
        private List<ColumnDef> _cachedColumns = new();

        // Status feedback
        private string _saveStatus = string.Empty;
        private long _saveStatusTick;

        private static readonly string[] PartTypeNames = { "All", "MapPiece", "Object", "Enemy", "Player", "Collision", "ConnectCollision" };
        private static readonly string[] UnifiedSubtypeNames = {
            "All", "MapPiece", "Object", "Enemy", "Player", "Collision", "ConnectCollision",
            "Region",
            "Treasure", "SpawnPoint", "SFX", "Light", "Sound", "Environment",
            "ObjAct", "Navmesh", "Message", "Generator", "MapOffset",
            "PseudoMultiplayer", "Wind"
        };

        // ── Unified entry ──────────────────────────────────────────────────────
        private sealed class MsbEntry
        {
            public enum KindType { Part, Region, Event }
            public KindType Kind;
            public string Subtype = "";    // "MapPiece", "Collision", "Treasure", etc.
            public string Name = "";
            public Vector3 Position;
            public int EntityID = -1;
            public string Extra = "";      // ModelName for Parts, Shape for Regions, link for Events
            public MSB1.Part? Part;
            public MSB1.Region? Region;
            public MSB1.Event? Event;
        }

        public MsbView(EditorState state)
        {
            _state = state;
        }

        public void Reset()
        {
            _store.Reset();
            _msbFiles = Array.Empty<string>();
            _selectedFileIdx = -1;
            _fileFilter = string.Empty;
            _loaded = null;
            _partFilter = string.Empty;
            _selectedPartIdx = -1;
            _filteredParts = new();
            _partTypeFilter = 0;
            _editPos = _editRot = _editScale = Vector3.Zero;
            _editName = string.Empty;
            _editModel = string.Empty;
            _editDirty = false;
            _regionFilter = string.Empty;
            _selectedRegionIdx = -1;
            _filteredRegions = new();
            _unifiedEntries = new();
            _filteredUnified = new();
            _unifiedFilter = string.Empty;
            _unifiedSubtypeFilter = 0;
            _unifiedSelected = -1;
            _unifiedSortCol = -1;
            _unifiedSortAsc = true;
            _cachedColumnWidths = new();
            _cachedColumns = new();
            _saveStatus = string.Empty;
            _saveStatusTick = 0;
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
                if (ImGui.BeginTabItem("All"))
                {
                    DrawUnifiedTab();
                    ImGui.EndTabItem();
                }
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

            string displayDir = msbDir.Replace('\\', '/');
            if (ImGui.Button("Browse##msbbrowse", new Vector2(-1, 0)))
                RefreshFileList(msbDir);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(displayDir);

            if (ImGui.Button("Refresh##msb", new Vector2(-1, 0)))
                RefreshFileList(msbDir);

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##msbfilter", "Filter...", ref _fileFilter, 64);

            ImGui.BeginChild("MsbFileList", new Vector2(-1, 350), ImGuiChildFlags.Borders);
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
                    try
                    {
                        _store.Save(_loaded);
                        _saveStatus = "Saved!";
                        _saveStatusTick = Environment.TickCount64;
                        Console.WriteLine($"[MsbView] Saved '{_loaded.FileName}'");
                    }
                    catch (Exception ex)
                    {
                        _saveStatus = $"Save failed: {ex.Message}";
                        _saveStatusTick = Environment.TickCount64;
                        Console.WriteLine($"[MsbView] Save failed: {ex.Message}");
                    }
                }
                if (!string.IsNullOrEmpty(_saveStatus) && Environment.TickCount64 - _saveStatusTick < 3000)
                {
                    var col = _saveStatus.StartsWith("Save failed")
                        ? new Vector4(1f, 0.35f, 0.35f, 1f)
                        : new Vector4(0.4f, 1f, 0.4f, 1f);
                    ImGui.TextColored(col, _saveStatus);
                }
                else
                {
                    _saveStatus = string.Empty;
                }
            }

            if (_msbFiles.Length == 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("No MSB files found.");
                ImGui.TextDisabled(displayDir);
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

            // ── ConnectCollision-specific ─────────────────────────────────────
            if (part is MSB1.Part.ConnectCollision cc)
                DrawConnectCollisionFields(cc);

            // ── Object-specific ──────────────────────────────────────────────
            if (part is MSB1.Part.Object obj)
            {
                float fw = ImGui.GetContentRegionAvail().X;
                ImGui.SeparatorText("Object");
                string cn = obj.CollisionName ?? ""; ImGui.SetNextItemWidth(fw); if (ImGui.InputText("CollisionName##pobjcn", ref cn, 64)) { obj.CollisionName = cn; _editDirty = true; }
                int bt = obj.BreakTerm; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("BreakTerm##pobjbt", ref bt)) { obj.BreakTerm = (sbyte)Math.Clamp(bt, -128, 127); _editDirty = true; }
                int ns = obj.NetSyncType; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("NetSyncType##pobjns", ref ns)) { obj.NetSyncType = (sbyte)Math.Clamp(ns, -128, 127); _editDirty = true; }
                int ia = obj.InitAnimID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("InitAnimID##pobjia", ref ia)) { obj.InitAnimID = (short)Math.Clamp(ia, -32768, 32767); _editDirty = true; }
                int ue = obj.UnkT0E; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("UnkT0E##pobjue", ref ue)) { obj.UnkT0E = (short)Math.Clamp(ue, -32768, 32767); _editDirty = true; }
                int u10 = obj.UnkT10; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("UnkT10##pobju10", ref u10)) { obj.UnkT10 = u10; _editDirty = true; }
                DrawPartBankIds(obj);
                int e = obj.EntityID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("EntityID##pobjeid", ref e)) { obj.EntityID = e; _editDirty = true; }
            }

            // ── Enemy-specific ──────────────────────────────────────────────
            if (part is MSB1.Part.Enemy en)
            {
                float fw = ImGui.GetContentRegionAvail().X;
                ImGui.SeparatorText("Enemy");
                int tp = en.ThinkParamID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("ThinkParamID##pentp", ref tp)) { en.ThinkParamID = tp; _editDirty = true; }
                int np = en.NPCParamID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("NPCParamID##pennp", ref np)) { en.NPCParamID = np; _editDirty = true; }
                int tk = en.TalkID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("TalkID##pentk", ref tk)) { en.TalkID = tk; _editDirty = true; }
                int pm = en.PointMoveType; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("PointMoveType##penpm", ref pm)) { en.PointMoveType = (byte)Math.Clamp(pm, 0, 255); _editDirty = true; }
                int pl = en.PlatoonID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("PlatoonID##penpl", ref pl)) { en.PlatoonID = (ushort)Math.Clamp(pl, 0, 65535); _editDirty = true; }
                int ci = en.CharaInitID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("CharaInitID##enci", ref ci)) { en.CharaInitID = ci; _editDirty = true; }
                string cn2 = en.CollisionName ?? ""; ImGui.SetNextItemWidth(fw); if (ImGui.InputText("CollisionName##pencn", ref cn2, 64)) { en.CollisionName = cn2; _editDirty = true; }
                int ia2 = en.InitAnimID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("InitAnimID##penia", ref ia2)) { en.InitAnimID = ia2; _editDirty = true; }
                int da = en.DamageAnimID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("DamageAnimID##penda", ref da)) { en.DamageAnimID = da; _editDirty = true; }
                DrawPartBankIds(en);
                int e2 = en.EntityID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("EntityID##peneid", ref e2)) { en.EntityID = e2; _editDirty = true; }
            }

            // ── Player-specific ─────────────────────────────────────────────
            if (part is MSB1.Part.Player player)
            {
                float fw = ImGui.GetContentRegionAvail().X;
                DrawPartBankIds(player);
                int e3 = player.EntityID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("EntityID##pplayeid", ref e3)) { player.EntityID = e3; _editDirty = true; }
            }

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

            ImGui.Spacing();
            ImGui.SeparatorText("Shadow / Reflection Flags");
            bool b;
            b = part.IsShadowSrc != 0;          if (ImGui.Checkbox("##isshadowsrc", ref b)) { part.IsShadowSrc = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("IsShadowSrc");
            b = part.IsShadowDest != 0;         if (ImGui.Checkbox("##isshadowdest", ref b)) { part.IsShadowDest = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("IsShadowDest");
            b = part.IsShadowOnly != 0;         if (ImGui.Checkbox("##isshadowonly", ref b)) { part.IsShadowOnly = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("IsShadowOnly");
            b = part.DrawByReflectCam != 0;     if (ImGui.Checkbox("##drawbyreflectcam", ref b)) { part.DrawByReflectCam = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("DrawByReflectCam");
            b = part.DrawOnlyReflectCam != 0;   if (ImGui.Checkbox("##drawonlyreflectcam", ref b)) { part.DrawOnlyReflectCam = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("DrawOnlyReflectCam");
            b = part.UseDepthBiasFloat != 0;    if (ImGui.Checkbox("##usedepthbiasfloat", ref b)) { part.UseDepthBiasFloat = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("UseDepthBiasFloat");
            b = part.DisablePointLightEffect != 0; if (ImGui.Checkbox("##disablepointlight", ref b)) { part.DisablePointLightEffect = (byte)(b ? 1 : 0); _editDirty = true; } ImGui.SameLine(); ImGui.Text("DisablePointLight");
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

            ImGui.Text("DisableStart"); 
            bool disableStart = col.DisableStart;
            if (ImGui.Checkbox("##coldisablestart", ref disableStart)) { col.DisableStart = disableStart; _editDirty = true; }

            ImGui.Text("DisableBonfire"); ImGui.SetNextItemWidth(w);
            int disableBonfire = col.DisableBonfireEntityID;
            if (ImGui.InputInt("##coldb", ref disableBonfire)) { col.DisableBonfireEntityID = disableBonfire; _editDirty = true; }

            ImGui.Text("PlayRegion"); ImGui.SetNextItemWidth(w);
            int playRegion = col.PlayRegionID;
            if (ImGui.InputInt("##colpr", ref playRegion)) { col.PlayRegionID = playRegion; _editDirty = true; }

            ImGui.Text("LockCam1"); ImGui.SetNextItemWidth(w);
            int lockCam1 = col.LockCamParamID1;
            if (ImGui.InputInt("##collc1", ref lockCam1)) { col.LockCamParamID1 = (short)lockCam1; _editDirty = true; }

            ImGui.Text("LockCam2"); ImGui.SetNextItemWidth(w);
            int lockCam2 = col.LockCamParamID2;
            if (ImGui.InputInt("##collc2", ref lockCam2)) { col.LockCamParamID2 = (short)lockCam2; _editDirty = true; }

            ImGui.Spacing();
            ImGui.SeparatorText("NvmGroups");
            for (int i = 0; i < 4; i++)
            {
                int nvm = (int)col.NvmGroups[i];
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt($"##colnvm{i}", ref nvm, 0, 0))
                    col.NvmGroups[i] = (uint)nvm;
                if (i < 3) ImGui.SameLine();
            }

            ImGui.Spacing();
            ImGui.SeparatorText("VagrantEntityIDs");
            for (int i = 0; i < 3; i++)
            {
                int vag = col.VagrantEntityIDs[i];
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt($"##colvag{i}", ref vag, 0, 0))
                    col.VagrantEntityIDs[i] = vag;
                if (i < 2) ImGui.SameLine();
            }

            DrawPartBankIds(col);

            ImGui.Spacing();
            ImGui.SeparatorText("Entity");
            ImGui.SetNextItemWidth(w);
            int entityId = col.EntityID;
            if (ImGui.InputInt("##coleid", ref entityId)) { col.EntityID = entityId; _editDirty = true; }
        }

        private void DrawConnectCollisionFields(MSB1.Part.ConnectCollision cc)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("ConnectCollision");

            float w = ImGui.GetContentRegionAvail().X;

            ImGui.Text("CollisionName"); ImGui.SetNextItemWidth(w);
            string colName = cc.CollisionName ?? "";
            if (ImGui.InputText("##cccolname", ref colName, 64)) { cc.CollisionName = colName; _editDirty = true; }

            ImGui.Text("MapID");
            for (int i = 0; i < 4; i++)
            {
                int mapId = cc.MapID[i];
                ImGui.SetNextItemWidth(50);
                if (ImGui.InputInt($"##ccmapid{i}", ref mapId, 0, 0))
                    cc.MapID[i] = (byte)Math.Clamp(mapId, 0, 255);
                if (i < 3) ImGui.SameLine();
            }

            DrawPartBankIds(cc);
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

        // ── Column descriptor for unified table ─────────────────────────────────

        private sealed class ColumnDef
        {
            public string Header;
            public float Width;
            public Action<MsbEntry, string> Draw;
            public Func<MsbEntry, IComparable>? SortKey;
        }

        // ── Unified table ──────────────────────────────────────────────────────

        private List<ColumnDef> GetColumns()
        {
            if (_unifiedSubtypeFilter == 0)
                return GetAllColumns();

            string subtype = UnifiedSubtypeNames[_unifiedSubtypeFilter];
            return subtype switch
            {
                "MapPiece" or "Player" => GetPartColumns(false),
                "Object" => GetObjectColumns(),
                "Enemy" => GetEnemyColumns(),
                "Collision" => GetPartColumns(true),
                "ConnectCollision" => GetConnectCollisionColumns(),
                "Region" => GetRegionColumns(),
                _ => GetEventColumns(subtype),
            };
        }

        private static List<ColumnDef> GetAllColumns()
        {
            return new()
            {
                new() { Header = "Type", Width = 60, Draw = (e, id) => {
                    uint c = e.Kind switch { MsbEntry.KindType.Part => 0xFF6060AA, MsbEntry.KindType.Region => 0xFF60AA60, _ => 0xFFAA6060 };
                    ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(c), e.Subtype);
                }, SortKey = e => e.Subtype },
                new() { Header = "Name", Width = 250, Draw = (e, id) => { ImGui.SetNextItemWidth(-1); ImGui.InputText(id, ref e.Name, 128); if (e.Part != null) e.Part.Name = e.Name; else if (e.Region != null) e.Region.Name = e.Name; else if (e.Event != null) e.Event.Name = e.Name; }, SortKey = e => e.Name },
                new() { Header = "PosX", Width = 55, Draw = (e, id) => { float v = e.Position.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.X = v; if (e.Part != null) e.Part.Position = e.Position; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.X },
                new() { Header = "PosY", Width = 55, Draw = (e, id) => { float v = e.Position.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Y = v; if (e.Part != null) e.Part.Position = e.Position; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.Y },
                new() { Header = "PosZ", Width = 55, Draw = (e, id) => { float v = e.Position.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Z = v; if (e.Part != null) e.Part.Position = e.Position; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.Z },
                new() { Header = "EntID", Width = 40, Draw = (e, id) => { int v = e.EntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) { e.EntityID = v; if (e.Part != null) e.Part.EntityID = v; else if (e.Region != null) e.Region.EntityID = v; else if (e.Event != null) e.Event.EntityID = v; } }, SortKey = e => e.EntityID },
                new() { Header = "Info", Width = 80, Draw = (e, id) => { ImGui.TextUnformatted(e.Extra); } },
            };
        }

        private static List<ColumnDef> GetPartColumns(bool isCollision)
        {
            var cols = new List<ColumnDef>();

            cols.Add(new() { Header = "Name", Width = 250, Draw = (e, id) => { ImGui.SetNextItemWidth(-1); ImGui.InputText(id, ref e.Name, 128); if (e.Part != null) e.Part.Name = e.Name; }, SortKey = e => e.Name });
            cols.Add(new() { Header = "PosX", Width = 50, Draw = (e, id) => { float v = e.Position.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.X = v; if (e.Part != null) e.Part.Position = e.Position; } }, SortKey = e => e.Position.X });
            cols.Add(new() { Header = "PosY", Width = 50, Draw = (e, id) => { float v = e.Position.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Y = v; if (e.Part != null) e.Part.Position = e.Position; } }, SortKey = e => e.Position.Y });
            cols.Add(new() { Header = "PosZ", Width = 50, Draw = (e, id) => { float v = e.Position.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Z = v; if (e.Part != null) e.Part.Position = e.Position; } }, SortKey = e => e.Position.Z });
            cols.Add(new() { Header = "RotX", Width = 50, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Rotation.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Part.Rotation; r.X = v; e.Part.Rotation = r; } }, SortKey = e => e.Part?.Rotation.X ?? 0 });
            cols.Add(new() { Header = "RotY", Width = 50, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Rotation.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Part.Rotation; r.Y = v; e.Part.Rotation = r; } }, SortKey = e => e.Part?.Rotation.Y ?? 0 });
            cols.Add(new() { Header = "RotZ", Width = 50, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Rotation.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Part.Rotation; r.Z = v; e.Part.Rotation = r; } }, SortKey = e => e.Part?.Rotation.Z ?? 0 });
            cols.Add(new() { Header = "SclX", Width = 45, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Scale.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.2f")) { var s = e.Part.Scale; s.X = v; e.Part.Scale = s; } }, SortKey = e => e.Part?.Scale.X ?? 0 });
            cols.Add(new() { Header = "SclY", Width = 45, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Scale.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.2f")) { var s = e.Part.Scale; s.Y = v; e.Part.Scale = s; } }, SortKey = e => e.Part?.Scale.Y ?? 0 });
            cols.Add(new() { Header = "SclZ", Width = 45, Draw = (e, id) => { if (e.Part == null) return; float v = e.Part.Scale.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.2f")) { var s = e.Part.Scale; s.Z = v; e.Part.Scale = s; } }, SortKey = e => e.Part?.Scale.Z ?? 0 });
            cols.Add(new() { Header = "EntID", Width = 50, Draw = (e, id) => { int v = e.EntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) { e.EntityID = v; if (e.Part != null) e.Part.EntityID = v; } }, SortKey = e => e.EntityID });
            cols.Add(new() { Header = "Light", Width = 35, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.LightID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.LightID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.LightID ?? 0 });
            cols.Add(new() { Header = "Fog", Width = 35, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.FogID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.FogID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.FogID ?? 0 });
            cols.Add(new() { Header = "Scatter", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.ScatterID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.ScatterID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.ScatterID ?? 0 });
            cols.Add(new() { Header = "LensFlare", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.LensFlareID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.LensFlareID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.LensFlareID ?? 0 });
            cols.Add(new() { Header = "Shadow", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.ShadowID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.ShadowID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.ShadowID ?? 0 });
            cols.Add(new() { Header = "Dof", Width = 35, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.DofID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.DofID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.DofID ?? 0 });
            cols.Add(new() { Header = "ToneMap", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.ToneMapID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.ToneMapID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.ToneMapID ?? 0 });
            cols.Add(new() { Header = "ToneCorr", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.ToneCorrectID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.ToneCorrectID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.ToneCorrectID ?? 0 });
            cols.Add(new() { Header = "Lantern", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.LanternID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.LanternID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.LanternID ?? 0 });
            cols.Add(new() { Header = "LodParam", Width = 40, Draw = (e, id) => { if (e.Part == null) return; int v = e.Part.LodParamID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) e.Part.LodParamID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => e.Part?.LodParamID ?? 0 });

            cols.Add(new() { Header = "ShdSrc", Width = 40, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.IsShadowSrc != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.IsShadowSrc = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.IsShadowSrc ?? 0 });
            cols.Add(new() { Header = "ShdDst", Width = 40, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.IsShadowDest != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.IsShadowDest = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.IsShadowDest ?? 0 });
            cols.Add(new() { Header = "ShdOnly", Width = 40, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.IsShadowOnly != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.IsShadowOnly = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.IsShadowOnly ?? 0 });
            cols.Add(new() { Header = "ReflCam", Width = 40, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.DrawByReflectCam != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.DrawByReflectCam = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.DrawByReflectCam ?? 0 });
            cols.Add(new() { Header = "ReflOnly", Width = 40, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.DrawOnlyReflectCam != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.DrawOnlyReflectCam = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.DrawOnlyReflectCam ?? 0 });
            cols.Add(new() { Header = "DepthBias", Width = 45, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.UseDepthBiasFloat != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.UseDepthBiasFloat = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.UseDepthBiasFloat ?? 0 });
            cols.Add(new() { Header = "NoPLight", Width = 45, Draw = (e, id) => { if (e.Part == null) return; bool v = e.Part.DisablePointLightEffect != 0; if (ImGui.Checkbox($"##{id}", ref v)) e.Part.DisablePointLightEffect = (byte)(v ? 1 : 0); }, SortKey = e => e.Part?.DisablePointLightEffect ?? 0 });

            if (isCollision)
            {
                cols.Add(new() { Header = "HitFilter", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.HitFilterID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.HitFilterID = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.Collision)?.HitFilterID ?? 0 });
                cols.Add(new() { Header = "SoundSpace", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.SoundSpaceType; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.SoundSpaceType = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.Collision)?.SoundSpaceType ?? 0 });
                cols.Add(new() { Header = "ReflectPlane", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; float v = c.ReflectPlaneHeight; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) c.ReflectPlaneHeight = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.ReflectPlaneHeight ?? 0 });
                cols.Add(new() { Header = "EnvLight", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.EnvLightMapSpotIndex; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.EnvLightMapSpotIndex = (short)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.EnvLightMapSpotIndex ?? 0 });
                cols.Add(new() { Header = "MapNameID", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.MapNameID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.MapNameID = (short)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.MapNameID ?? 0 });
                cols.Add(new() { Header = "DisableStart", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; bool v = c.DisableStart; if (ImGui.Checkbox(id, ref v)) c.DisableStart = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.DisableStart ?? false });
                cols.Add(new() { Header = "DisableBonfire", Width = 60, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.DisableBonfireEntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.DisableBonfireEntityID = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.DisableBonfireEntityID ?? 0 });
                cols.Add(new() { Header = "PlayRegion", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.PlayRegionID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.PlayRegionID = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.PlayRegionID ?? 0 });
                cols.Add(new() { Header = "LockCam1", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.LockCamParamID1; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.LockCamParamID1 = (short)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.LockCamParamID1 ?? 0 });
                cols.Add(new() { Header = "LockCam2", Width = 45, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.LockCamParamID2; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.LockCamParamID2 = (short)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.LockCamParamID2 ?? 0 });
                cols.Add(new() { Header = "NvmG0", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = (int)c.NvmGroups[0]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.NvmGroups[0] = (uint)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.NvmGroups[0] ?? 0 });
                cols.Add(new() { Header = "NvmG1", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = (int)c.NvmGroups[1]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.NvmGroups[1] = (uint)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.NvmGroups[1] ?? 0 });
                cols.Add(new() { Header = "NvmG2", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = (int)c.NvmGroups[2]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.NvmGroups[2] = (uint)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.NvmGroups[2] ?? 0 });
                cols.Add(new() { Header = "NvmG3", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = (int)c.NvmGroups[3]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.NvmGroups[3] = (uint)v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.NvmGroups[3] ?? 0 });
                cols.Add(new() { Header = "Vag0", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.VagrantEntityIDs[0]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.VagrantEntityIDs[0] = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.VagrantEntityIDs[0] ?? 0 });
                cols.Add(new() { Header = "Vag1", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.VagrantEntityIDs[1]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.VagrantEntityIDs[1] = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.VagrantEntityIDs[1] ?? 0 });
                cols.Add(new() { Header = "Vag2", Width = 55, Draw = (e, id) => { var c = e.Part as MSB1.Part.Collision; if (c == null) return; int v = c.VagrantEntityIDs[2]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.VagrantEntityIDs[2] = v; }, SortKey = e => (e.Part as MSB1.Part.Collision)?.VagrantEntityIDs[2] ?? 0 });
            }

            return cols;
        }

        private static List<ColumnDef> GetObjectColumns()
        {
            var cols = GetPartColumns(false);
            cols.Add(new() { Header = "CollisionName", Width = 100, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; string v = o.CollisionName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) o.CollisionName = v; }, SortKey = e => (e.Part as MSB1.Part.Object)?.CollisionName ?? "" });
            cols.Add(new() { Header = "BreakTerm", Width = 45, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; int v = o.BreakTerm; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.BreakTerm = (sbyte)Math.Clamp(v, -128, 127); }, SortKey = e => (e.Part as MSB1.Part.Object)?.BreakTerm ?? 0 });
            cols.Add(new() { Header = "NetSyncType", Width = 45, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; int v = o.NetSyncType; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.NetSyncType = (sbyte)Math.Clamp(v, -128, 127); }, SortKey = e => (e.Part as MSB1.Part.Object)?.NetSyncType ?? 0 });
            cols.Add(new() { Header = "InitAnimID", Width = 45, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; int v = o.InitAnimID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.InitAnimID = (short)Math.Clamp(v, -32768, 32767); }, SortKey = e => (e.Part as MSB1.Part.Object)?.InitAnimID ?? 0 });
            cols.Add(new() { Header = "UnkT0E", Width = 35, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; int v = o.UnkT0E; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.UnkT0E = (short)Math.Clamp(v, -32768, 32767); }, SortKey = e => (e.Part as MSB1.Part.Object)?.UnkT0E ?? 0 });
            cols.Add(new() { Header = "UnkT10", Width = 35, Draw = (e, id) => { var o = e.Part as MSB1.Part.Object; if (o == null) return; int v = o.UnkT10; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.UnkT10 = v; }, SortKey = e => (e.Part as MSB1.Part.Object)?.UnkT10 ?? 0 });
            return cols;
        }

        private static List<ColumnDef> GetEnemyColumns()
        {
            var cols = GetPartColumns(false);
            cols.Add(new() { Header = "ThinkParamID", Width = 55, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.ThinkParamID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.ThinkParamID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.ThinkParamID ?? 0 });
            cols.Add(new() { Header = "NPCParamID", Width = 55, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.NPCParamID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.NPCParamID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.NPCParamID ?? 0 });
            cols.Add(new() { Header = "TalkID", Width = 35, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.TalkID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.TalkID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.TalkID ?? 0 });
            cols.Add(new() { Header = "PlatoonID", Width = 45, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.PlatoonID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.PlatoonID = (ushort)Math.Clamp(v, 0, 65535); }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.PlatoonID ?? 0 });
            cols.Add(new() { Header = "CharaInitID", Width = 55, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.CharaInitID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.CharaInitID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.CharaInitID ?? 0 });
            cols.Add(new() { Header = "ColName", Width = 80, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; string v = en.CollisionName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) en.CollisionName = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.CollisionName ?? "" });
            cols.Add(new() { Header = "InitAnimID", Width = 45, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.InitAnimID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.InitAnimID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.InitAnimID ?? 0 });
            cols.Add(new() { Header = "DamageAnimID", Width = 55, Draw = (e, id) => { var en = e.Part as MSB1.Part.Enemy; if (en == null) return; int v = en.DamageAnimID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) en.DamageAnimID = v; }, SortKey = e => (e.Part as MSB1.Part.Enemy)?.DamageAnimID ?? 0 });
            return cols;
        }

        private static List<ColumnDef> GetConnectCollisionColumns()
        {
            var cols = GetPartColumns(false);
            // Replace the 5 Collision-specific fields with ConnectCollision fields
            // Pop the last 5 (HitFilter, SoundSpace, ReflectPlane, EnvLight, MapNameID)
            // But GetPartColumns(false) doesn't add them - they're only added when isCollision=true.
            // Just add ConnectCollision-specific fields after LodParam.
            cols.Add(new() { Header = "CollisionName", Width = 120, Draw = (e, id) => { var c = e.Part as MSB1.Part.ConnectCollision; if (c == null) return; string v = c.CollisionName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) c.CollisionName = v; }, SortKey = e => (e.Part as MSB1.Part.ConnectCollision)?.CollisionName ?? "" });
            cols.Add(new() { Header = "MapID0", Width = 35, Draw = (e, id) => { var c = e.Part as MSB1.Part.ConnectCollision; if (c == null) return; int v = c.MapID[0]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.MapID[0] = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.ConnectCollision)?.MapID[0] ?? 0 });
            cols.Add(new() { Header = "MapID1", Width = 35, Draw = (e, id) => { var c = e.Part as MSB1.Part.ConnectCollision; if (c == null) return; int v = c.MapID[1]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.MapID[1] = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.ConnectCollision)?.MapID[1] ?? 0 });
            cols.Add(new() { Header = "MapID2", Width = 35, Draw = (e, id) => { var c = e.Part as MSB1.Part.ConnectCollision; if (c == null) return; int v = c.MapID[2]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.MapID[2] = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.ConnectCollision)?.MapID[2] ?? 0 });
            cols.Add(new() { Header = "MapID3", Width = 35, Draw = (e, id) => { var c = e.Part as MSB1.Part.ConnectCollision; if (c == null) return; int v = c.MapID[3]; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) c.MapID[3] = (byte)Math.Clamp(v, 0, 255); }, SortKey = e => (e.Part as MSB1.Part.ConnectCollision)?.MapID[3] ?? 0 });
            return cols;
        }

        private static List<ColumnDef> GetRegionColumns()
        {
            return new()
            {
                new() { Header = "Name", Width = 250, Draw = (e, id) => { ImGui.SetNextItemWidth(-1); ImGui.InputText(id, ref e.Name, 128); if (e.Region != null) e.Region.Name = e.Name; }, SortKey = e => e.Name },
                new() { Header = "Shape", Width = 80, Draw = (e, id) => { ImGui.TextUnformatted(e.Region != null ? DescribeShape(e.Region.Shape) : ""); } },
                new() { Header = "PosX", Width = 50, Draw = (e, id) => { float v = e.Position.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.X = v; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.X },
                new() { Header = "PosY", Width = 50, Draw = (e, id) => { float v = e.Position.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Y = v; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.Y },
                new() { Header = "PosZ", Width = 50, Draw = (e, id) => { float v = e.Position.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { e.Position.Z = v; if (e.Region != null) e.Region.Position = e.Position; } }, SortKey = e => e.Position.Z },
                new() { Header = "RotX", Width = 50, Draw = (e, id) => { if (e.Region == null) return; float v = e.Region.Rotation.X; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Region.Rotation; r.X = v; e.Region.Rotation = r; } }, SortKey = e => e.Region?.Rotation.X ?? 0 },
                new() { Header = "RotY", Width = 50, Draw = (e, id) => { if (e.Region == null) return; float v = e.Region.Rotation.Y; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Region.Rotation; r.Y = v; e.Region.Rotation = r; } }, SortKey = e => e.Region?.Rotation.Y ?? 0 },
                new() { Header = "RotZ", Width = 50, Draw = (e, id) => { if (e.Region == null) return; float v = e.Region.Rotation.Z; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) { var r = e.Region.Rotation; r.Z = v; e.Region.Rotation = r; } }, SortKey = e => e.Region?.Rotation.Z ?? 0 },
                new() { Header = "EntID", Width = 50, Draw = (e, id) => { int v = e.EntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) { e.EntityID = v; if (e.Region != null) e.Region.EntityID = v; } }, SortKey = e => e.EntityID },
            };
        }

        private static List<ColumnDef> GetEventColumns(string subtype)
        {
            var cols = new List<ColumnDef>();
            cols.Add(new() { Header = "Name", Width = 250, Draw = (e, id) => { ImGui.SetNextItemWidth(-1); ImGui.InputText(id, ref e.Name, 128); if (e.Event != null) e.Event.Name = e.Name; }, SortKey = e => e.Name });
            cols.Add(new() { Header = "EntID", Width = 50, Draw = (e, id) => { int v = e.EntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) { e.EntityID = v; if (e.Event != null) e.Event.EntityID = v; } }, SortKey = e => e.EntityID });
            switch (subtype)
            {
                case "Treasure":
                    cols.Add(new() { Header = "TreasurePartName", Width = 120, Draw = (e, id) => { var t = e.Event as MSB1.Event.Treasure; if (t == null) return; string v = t.TreasurePartName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) t.TreasurePartName = v; }, SortKey = e => (e.Event as MSB1.Event.Treasure)?.TreasurePartName ?? "" });
                    cols.Add(new() { Header = "InChest", Width = 40, Draw = (e, id) => { var t = e.Event as MSB1.Event.Treasure; if (t == null) return; bool v = t.InChest; if (ImGui.Checkbox(id, ref v)) t.InChest = v; }, SortKey = e => (e.Event as MSB1.Event.Treasure)?.InChest ?? false });
                    cols.Add(new() { Header = "StartDisabled", Width = 50, Draw = (e, id) => { var t = e.Event as MSB1.Event.Treasure; if (t == null) return; bool v = t.StartDisabled; if (ImGui.Checkbox(id, ref v)) t.StartDisabled = v; }, SortKey = e => (e.Event as MSB1.Event.Treasure)?.StartDisabled ?? false });
                    break;
                case "SpawnPoint":
                    cols.Add(new() { Header = "SpawnPointName", Width = 120, Draw = (e, id) => { var s = e.Event as MSB1.Event.SpawnPoint; if (s == null) return; string v = s.SpawnPointName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) s.SpawnPointName = v; }, SortKey = e => (e.Event as MSB1.Event.SpawnPoint)?.SpawnPointName ?? "" });
                    break;
                case "SFX":
                    cols.Add(new() { Header = "EffectID", Width = 55, Draw = (e, id) => { var s = e.Event as MSB1.Event.SFX; if (s == null) return; int v = s.EffectID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) s.EffectID = v; }, SortKey = e => (e.Event as MSB1.Event.SFX)?.EffectID ?? 0 });
                    break;
                case "Light":
                    cols.Add(new() { Header = "PointLightID", Width = 55, Draw = (e, id) => { var l = e.Event as MSB1.Event.Light; if (l == null) return; int v = l.PointLightID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) l.PointLightID = v; }, SortKey = e => (e.Event as MSB1.Event.Light)?.PointLightID ?? 0 });
                    break;
                case "Sound":
                    cols.Add(new() { Header = "SoundType", Width = 55, Draw = (e, id) => { var s = e.Event as MSB1.Event.Sound; if (s == null) return; int v = s.SoundType; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) s.SoundType = v; }, SortKey = e => (e.Event as MSB1.Event.Sound)?.SoundType ?? 0 });
                    cols.Add(new() { Header = "SoundID", Width = 55, Draw = (e, id) => { var s = e.Event as MSB1.Event.Sound; if (s == null) return; int v = s.SoundID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) s.SoundID = v; }, SortKey = e => (e.Event as MSB1.Event.Sound)?.SoundID ?? 0 });
                    break;
                case "Environment":
                    cols.Add(new() { Header = "UnkT00", Width = 55, Draw = (e, id) => { var env = e.Event as MSB1.Event.Environment; if (env == null) return; int v = env.UnkT00; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) env.UnkT00 = v; }, SortKey = e => (e.Event as MSB1.Event.Environment)?.UnkT00 ?? 0 });
                    break;
                case "ObjAct":
                    cols.Add(new() { Header = "ObjActEntityID", Width = 55, Draw = (e, id) => { var o = e.Event as MSB1.Event.ObjAct; if (o == null) return; int v = o.ObjActEntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.ObjActEntityID = v; }, SortKey = e => (e.Event as MSB1.Event.ObjAct)?.ObjActEntityID ?? 0 });
                    cols.Add(new() { Header = "ObjActPartName", Width = 100, Draw = (e, id) => { var o = e.Event as MSB1.Event.ObjAct; if (o == null) return; string v = o.ObjActPartName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) o.ObjActPartName = v; }, SortKey = e => (e.Event as MSB1.Event.ObjAct)?.ObjActPartName ?? "" });
                    cols.Add(new() { Header = "ObjActParamID", Width = 55, Draw = (e, id) => { var o = e.Event as MSB1.Event.ObjAct; if (o == null) return; int v = o.ObjActParamID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.ObjActParamID = (short)v; }, SortKey = e => (e.Event as MSB1.Event.ObjAct)?.ObjActParamID ?? 0 });
                    cols.Add(new() { Header = "EventFlagID", Width = 55, Draw = (e, id) => { var o = e.Event as MSB1.Event.ObjAct; if (o == null) return; int v = o.EventFlagID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) o.EventFlagID = v; }, SortKey = e => (e.Event as MSB1.Event.ObjAct)?.EventFlagID ?? 0 });
                    break;
                case "Navmesh":
                    cols.Add(new() { Header = "NavmeshRegionName", Width = 120, Draw = (e, id) => { var n = e.Event as MSB1.Event.Navmesh; if (n == null) return; string v = n.NavmeshRegionName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) n.NavmeshRegionName = v; }, SortKey = e => (e.Event as MSB1.Event.Navmesh)?.NavmeshRegionName ?? "" });
                    break;
                case "Message":
                    cols.Add(new() { Header = "MessageID", Width = 55, Draw = (e, id) => { var m = e.Event as MSB1.Event.Message; if (m == null) return; int v = m.MessageID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) m.MessageID = (short)v; }, SortKey = e => (e.Event as MSB1.Event.Message)?.MessageID ?? 0 });
                    cols.Add(new() { Header = "Hidden", Width = 40, Draw = (e, id) => { var m = e.Event as MSB1.Event.Message; if (m == null) return; bool v = m.Hidden; if (ImGui.Checkbox(id, ref v)) m.Hidden = v; }, SortKey = e => (e.Event as MSB1.Event.Message)?.Hidden ?? false });
                    break;
                case "MapOffset":
                    cols.Add(new() { Header = "PosX", Width = 50, Draw = (e, id) => { var m = e.Event as MSB1.Event.MapOffset; if (m == null) return; var v = m.Position; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v.X, 0, 0, "%.1f")) { m.Position = new(v.X, m.Position.Y, m.Position.Z); } }, SortKey = e => (e.Event as MSB1.Event.MapOffset)?.Position.X ?? 0 });
                    cols.Add(new() { Header = "PosY", Width = 50, Draw = (e, id) => { var m = e.Event as MSB1.Event.MapOffset; if (m == null) return; var v = m.Position; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v.Y, 0, 0, "%.1f")) { m.Position = new(m.Position.X, v.Y, m.Position.Z); } }, SortKey = e => (e.Event as MSB1.Event.MapOffset)?.Position.Y ?? 0 });
                    cols.Add(new() { Header = "PosZ", Width = 50, Draw = (e, id) => { var m = e.Event as MSB1.Event.MapOffset; if (m == null) return; var v = m.Position; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v.Z, 0, 0, "%.1f")) { m.Position = new(m.Position.X, m.Position.Y, v.Z); } }, SortKey = e => (e.Event as MSB1.Event.MapOffset)?.Position.Z ?? 0 });
                    cols.Add(new() { Header = "Degree", Width = 50, Draw = (e, id) => { var m = e.Event as MSB1.Event.MapOffset; if (m == null) return; float v = m.Degree; ImGui.SetNextItemWidth(-1); if (ImGui.InputFloat(id, ref v, 0, 0, "%.1f")) m.Degree = v; }, SortKey = e => (e.Event as MSB1.Event.MapOffset)?.Degree ?? 0 });
                    break;
                case "PseudoMultiplayer":
                    cols.Add(new() { Header = "HostEntityID", Width = 55, Draw = (e, id) => { var p = e.Event as MSB1.Event.PseudoMultiplayer; if (p == null) return; int v = p.HostEntityID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) p.HostEntityID = v; }, SortKey = e => (e.Event as MSB1.Event.PseudoMultiplayer)?.HostEntityID ?? 0 });
                    cols.Add(new() { Header = "EventFlagID", Width = 55, Draw = (e, id) => { var p = e.Event as MSB1.Event.PseudoMultiplayer; if (p == null) return; int v = p.EventFlagID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) p.EventFlagID = v; }, SortKey = e => (e.Event as MSB1.Event.PseudoMultiplayer)?.EventFlagID ?? 0 });
                    cols.Add(new() { Header = "ActivateGoodsID", Width = 55, Draw = (e, id) => { var p = e.Event as MSB1.Event.PseudoMultiplayer; if (p == null) return; int v = p.ActivateGoodsID; ImGui.SetNextItemWidth(-1); if (ImGui.InputInt(id, ref v, 0, 0)) p.ActivateGoodsID = v; }, SortKey = e => (e.Event as MSB1.Event.PseudoMultiplayer)?.ActivateGoodsID ?? 0 });
                    break;
                default: // Generator, Wind — just base columns
                    cols.Add(new() { Header = "PartName", Width = 100, Draw = (e, id) => { if (e.Event == null) return; string v = e.Event.PartName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) e.Event.PartName = v; }, SortKey = e => e.Event?.PartName ?? "" });
                    cols.Add(new() { Header = "RegionName", Width = 100, Draw = (e, id) => { if (e.Event == null) return; string v = e.Event.RegionName ?? ""; ImGui.SetNextItemWidth(-1); if (ImGui.InputText(id, ref v, 64)) e.Event.RegionName = v; }, SortKey = e => e.Event?.RegionName ?? "" });
                    break;
            }
            return cols;
        }

        private void RecalcColumnWidths()
        {
            _cachedColumns = GetColumns();
            _cachedColumnWidths.Clear();
            for (int c = 0; c < _cachedColumns.Count; c++)
            {
                string hdr = _cachedColumns[c].Header;
                int headerChars = hdr.Length;
                int dataChars = EstimateDataChars(_cachedColumns[c], hdr);
                float w = Math.Max(headerChars, dataChars) * 7.5f + 36;
                _cachedColumnWidths.Add(w);
            }
        }

        private int EstimateDataChars(ColumnDef col, string header)
        {
            if (header is "Name" or "Info" or "Shape")
            {
                int maxLen = 0;
                for (int i = 0; i < _filteredUnified.Count; i++)
                {
                    string text = header switch
                    {
                        "Name" => _filteredUnified[i].Name,
                        "Shape" => _filteredUnified[i].Region != null ? DescribeShape(_filteredUnified[i].Region.Shape) : "",
                        _ => _filteredUnified[i].Extra,
                    };
                    if (!string.IsNullOrEmpty(text) && text.Length > maxLen)
                        maxLen = text.Length;
                }
                return maxLen;
            }
            if (header is "PartName" or "RegionName" or "CollisionName" or "TreasurePartName"
                or "SpawnPointName" or "ObjActPartName" or "NavmeshRegionName" or "ColName")
            {
                int maxLen = 0;
                for (int i = 0; i < _filteredUnified.Count; i++)
                {
                    string text = header switch
                    {
                        "PartName" => _filteredUnified[i].Event?.PartName ?? "",
                        "RegionName" => _filteredUnified[i].Event?.RegionName ?? "",
                        "CollisionName" => (_filteredUnified[i].Part as MSB1.Part.ConnectCollision)?.CollisionName ?? "",
                        "TreasurePartName" => (_filteredUnified[i].Event as MSB1.Event.Treasure)?.TreasurePartName ?? "",
                        "SpawnPointName" => (_filteredUnified[i].Event as MSB1.Event.SpawnPoint)?.SpawnPointName ?? "",
                        "ObjActPartName" => (_filteredUnified[i].Event as MSB1.Event.ObjAct)?.ObjActPartName ?? "",
                        "NavmeshRegionName" => (_filteredUnified[i].Event as MSB1.Event.Navmesh)?.NavmeshRegionName ?? "",
                        "ColName" => (_filteredUnified[i].Part as MSB1.Part.Enemy)?.CollisionName ?? "",
                        _ => "",
                    };
                    if (!string.IsNullOrEmpty(text) && text.Length > maxLen)
                        maxLen = text.Length;
                }
                return maxLen;
            }
            if (header is "PosX" or "PosY" or "PosZ" or "RotX" or "RotY" or "RotZ" or "ReflectPlane" or "Degree")
                return 8; // "-1234.5"
            if (header is "SclX" or "SclY" or "SclZ")
                return 6; // "123.00"
            if (header is "EntID")
                return 7; // "32767" or "-32768"
            if (header is "HitFilter" or "SoundSpace" or "EnvLight" or "MapNameID" or "LockCam1" or "LockCam2"
                or "PlatoonID" or "InitAnimID" or "UnkT0E" or "MessageID" or "ObjActParamID")
                return 7; // short range
            if (header is "UnkT10" or "BreakTerm" or "NetSyncType")
                return 6;
            if (header is "InChest" or "StartDisabled" or "Hidden")
                return 5; // bool
            if (header is "DisableBonfire" or "PlayRegion" or "Vag0" or "Vag1" or "Vag2"
                or "ThinkParamID" or "NPCParamID" or "TalkID" or "CharaInitID" or "DamageAnimID"
                or "EffectID" or "SoundType" or "SoundID" or "PointLightID" or "UnkT00"
                or "ObjActEntityID" or "EventFlagID" or "HostEntityID" or "ActivateGoodsID")
                return 11; // int32
            if (header is "NvmG0" or "NvmG1" or "NvmG2" or "NvmG3")
                return 10; // uint32 max "4294967295"
            return 4; // byte
        }

        private void DrawUnifiedTab()
        {
            if (_loaded == null) return;

            float w = ImGui.GetContentRegionAvail().X;

            ImGui.SetNextItemWidth(140);
            if (ImGui.Combo("##unifsubtype", ref _unifiedSubtypeFilter, UnifiedSubtypeNames, UnifiedSubtypeNames.Length))
            {
                _unifiedSortCol = -1;
                _unifiedSortAsc = true;
                RebuildFilteredUnified();
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(w - 150);
            if (ImGui.InputTextWithHint("##uniffilter", "Filter by name...", ref _unifiedFilter, 64))
                RebuildFilteredUnified();

            ImGui.TextDisabled($"{_filteredUnified.Count} / {_unifiedEntries.Count} entries");

            var columns = _cachedColumns;
            if (columns.Count == 0) return;

            float tableH = ImGui.GetContentRegionAvail().Y;

            var tableFlags = ImGuiTableFlags.Sortable | ImGuiTableFlags.Resizable |
                ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX;

            string tableId = $"UnifiedTable_{_unifiedSubtypeFilter}_{_unifiedFilter}";
            if (ImGui.BeginTable(tableId, columns.Count, tableFlags, new Vector2(-1, Math.Max(100, tableH))))
            {
                for (int c = 0; c < columns.Count; c++)
                    ImGui.TableSetupColumn(columns[c].Header, ImGuiTableColumnFlags.WidthFixed, _cachedColumnWidths[c]);
                int freezeCols = _unifiedSubtypeFilter == 0 ? 2 : 1;
                ImGui.TableSetupScrollFreeze(freezeCols, 1);
                ImGui.TableHeadersRow();

                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    _unifiedSortCol = sortSpecs.Specs.ColumnIndex;
                    _unifiedSortAsc = sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending;
                    RebuildFilteredUnified();
                    sortSpecs.SpecsDirty = false;
                }

                unsafe
                {
                    ImGuiListClipper clipper;
                    var clipperPtr = new ImGuiListClipperPtr(&clipper);
                    clipperPtr.Begin(_filteredUnified.Count);
                    while (clipperPtr.Step())
                    {
                        for (int i = clipperPtr.DisplayStart; i < clipperPtr.DisplayEnd; i++)
                        {
                            var entry = _filteredUnified[i];
                            ImGui.PushID(i);
                            ImGui.TableNextRow();

                            for (int c = 0; c < columns.Count; c++)
                            {
                                if (!ImGui.TableNextColumn()) continue;
                                if (_unifiedSelected == i)
                                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0x404080FF);
                                columns[c].Draw(entry, c.ToString());
                                if (ImGui.IsItemClicked()) _unifiedSelected = i;
                            }

                            ImGui.PopID();
                        }
                    }
                    clipperPtr.End();
                }

                ImGui.EndTable();
            }

            // Detail panel for selected entry
            if (_unifiedSubtypeFilter == 0 || _unifiedSelected < 0 || _unifiedSelected >= _filteredUnified.Count) return;
            var sel = _filteredUnified[_unifiedSelected];
            ImGui.Separator();
            if (sel.Kind == MsbEntry.KindType.Event && sel.Event != null)
                DrawUnifiedEventInfo(sel.Event);
            else if (sel.Kind == MsbEntry.KindType.Part && sel.Part != null)
                DrawUnifiedPartInfo(sel.Part);
            else if (sel.Kind == MsbEntry.KindType.Region && sel.Region != null)
                DrawUnifiedRegionInfo(sel.Region);
        }

        private void DrawUnifiedPartInfo(MSB1.Part part)
        {
            ImGui.Text($"[{GetPartTypeName(part)}] {part.Name}");
            float w = ImGui.GetContentRegionAvail().X;
            string name = part.Name;
            ImGui.SetNextItemWidth(w * 0.4f); ImGui.Text("Name"); ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.6f); if (ImGui.InputText("##upiname", ref name, 128)) part.Name = name;
            var pos = part.Position;
            ImGui.SetNextItemWidth(w); if (ImGui.InputFloat3("Position##upipos", ref pos)) part.Position = pos;
            var rot = part.Rotation;
            ImGui.SetNextItemWidth(w); if (ImGui.InputFloat3("Rotation##upirot", ref rot)) part.Rotation = rot;
            var scl = part.Scale;
            ImGui.SetNextItemWidth(w); if (ImGui.InputFloat3("Scale##upiscl", ref scl)) part.Scale = scl;
            string model = part.ModelName ?? "";
            ImGui.SetNextItemWidth(w); ImGui.Text("Model"); ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.6f); if (ImGui.InputText("##upimodel", ref model, 64)) part.ModelName = model;
            ImGui.SeparatorText("DrawParam Banks");
            float fw = 70f;
            int v;
            v = part.LightID;       ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##l", ref v,0,0)) { part.LightID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Light");
            v = part.FogID;         ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##f", ref v,0,0)) { part.FogID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Fog");
            v = part.ScatterID;     ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##sc", ref v,0,0)) { part.ScatterID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Scatter");
            v = part.LensFlareID;   ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##lf", ref v,0,0)) { part.LensFlareID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("LensFlare");
            v = part.ShadowID;      ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##sh", ref v,0,0)) { part.ShadowID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Shadow");
            v = part.DofID;         ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##d", ref v,0,0)) { part.DofID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Dof");
            v = part.ToneMapID;     ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##tm", ref v,0,0)) { part.ToneMapID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("ToneMap");
            v = part.ToneCorrectID; ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##tc", ref v,0,0)) { part.ToneCorrectID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("ToneCorr");
            v = part.LanternID;     ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##ln", ref v,0,0)) { part.LanternID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("Lantern");
            v = part.LodParamID;    ImGui.SetNextItemWidth(fw); if (ImGui.InputInt("##lp", ref v,0,0)) { part.LodParamID=(byte)Math.Clamp(v,0,255); } ImGui.SameLine(); ImGui.Text("LodParam");
            bool b;
            b = part.IsShadowSrc != 0;          ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upiss", ref b)) { part.IsShadowSrc = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("ShdSrc");
            b = part.IsShadowDest != 0;         ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upisd", ref b)) { part.IsShadowDest = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("ShdDst");
            b = part.IsShadowOnly != 0;         ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upiso", ref b)) { part.IsShadowOnly = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("ShdOnly");
            b = part.DrawByReflectCam != 0;     ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upibrc", ref b)) { part.DrawByReflectCam = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("ReflCam");
            b = part.DrawOnlyReflectCam != 0;   ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upiorc", ref b)) { part.DrawOnlyReflectCam = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("ReflOnly");
            b = part.UseDepthBiasFloat != 0;    ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upidbf", ref b)) { part.UseDepthBiasFloat = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("DepthBias");
            b = part.DisablePointLightEffect != 0; ImGui.SetNextItemWidth(fw); if (ImGui.Checkbox("##upidpl", ref b)) { part.DisablePointLightEffect = (byte)(b ? 1 : 0); } ImGui.SameLine(); ImGui.Text("NoPLight");
            int eid = part.EntityID;
            ImGui.SetNextItemWidth(w); if (ImGui.InputInt("EntityID##upieid", ref eid)) part.EntityID = eid;
            if (part is MSB1.Part.Object obj)
            {
                ImGui.SeparatorText("Object");
                string cn = obj.CollisionName ?? ""; ImGui.SetNextItemWidth(w); if (ImGui.InputText("CollisionName##upicn", ref cn, 64)) obj.CollisionName = cn;
                int bt = obj.BreakTerm; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("BreakTerm##upibt", ref bt)) obj.BreakTerm = (sbyte)Math.Clamp(bt, -128, 127);
                int ns = obj.NetSyncType; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("NetSyncType##upins", ref ns)) obj.NetSyncType = (sbyte)Math.Clamp(ns, -128, 127);
                int ia = obj.InitAnimID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("InitAnimID##upiia", ref ia)) obj.InitAnimID = (short)Math.Clamp(ia, -32768, 32767);
                int ue = obj.UnkT0E; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("UnkT0E##upiue", ref ue)) obj.UnkT0E = (short)Math.Clamp(ue, -32768, 32767);
                int u10 = obj.UnkT10; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("UnkT10##upiu10", ref u10)) obj.UnkT10 = u10;
            }
            if (part is MSB1.Part.Enemy en)
            {
                ImGui.SeparatorText("Enemy");
                int tp = en.ThinkParamID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("ThinkParamID##upitp", ref tp)) en.ThinkParamID = tp;
                int np = en.NPCParamID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("NPCParamID##upinp", ref np)) en.NPCParamID = np;
                int tk = en.TalkID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("TalkID##upitk", ref tk)) en.TalkID = tk;
                int pm = en.PointMoveType; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("PointMoveType##upipm", ref pm)) en.PointMoveType = (byte)Math.Clamp(pm, 0, 255);
                int pl = en.PlatoonID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("PlatoonID##upipl", ref pl)) en.PlatoonID = (ushort)Math.Clamp(pl, 0, 65535);
                int ci = en.CharaInitID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("CharaInitID##upici", ref ci)) en.CharaInitID = ci;
                string cn2 = en.CollisionName ?? ""; ImGui.SetNextItemWidth(w); if (ImGui.InputText("CollisionName##upicn2", ref cn2, 64)) en.CollisionName = cn2;
                int ia2 = en.InitAnimID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("InitAnimID##upiia2", ref ia2)) en.InitAnimID = ia2;
                int da = en.DamageAnimID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("DamageAnimID##upida", ref da)) en.DamageAnimID = da;
            }
            if (part is MSB1.Part.Collision col)
            {
                ImGui.SeparatorText("Collision");
                int hf = col.HitFilterID; ImGui.SetNextItemWidth(w*0.4f); if (ImGui.InputInt("HitFilter##upichf", ref hf)) col.HitFilterID = (byte)Math.Clamp(hf,0,255);
                int ss = col.SoundSpaceType; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("SoundSpace##upiss", ref ss)) col.SoundSpaceType = (byte)Math.Clamp(ss,0,255);
                float rp = col.ReflectPlaneHeight; ImGui.SetNextItemWidth(w); if (ImGui.InputFloat("ReflectPlane##upirp", ref rp)) col.ReflectPlaneHeight = rp;
                int el = col.EnvLightMapSpotIndex; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("EnvLight##upiel", ref el)) col.EnvLightMapSpotIndex = (short)el;
                int mn = col.MapNameID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("MapNameID##upimn", ref mn)) col.MapNameID = (short)mn;
                bool ds = col.DisableStart; if (ImGui.Checkbox("DisableStart##upids", ref ds)) col.DisableStart = ds;
                int db = col.DisableBonfireEntityID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("DisableBonfire##upidb", ref db)) col.DisableBonfireEntityID = db;
                int pr = col.PlayRegionID; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("PlayRegion##upipr", ref pr)) col.PlayRegionID = pr;
                int lc1 = col.LockCamParamID1; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("LockCam1##upilc1", ref lc1)) col.LockCamParamID1 = (short)lc1;
                int lc2 = col.LockCamParamID2; ImGui.SetNextItemWidth(w); if (ImGui.InputInt("LockCam2##upilc2", ref lc2)) col.LockCamParamID2 = (short)lc2;
                ImGui.SeparatorText("NvmGroups");
                for (int i = 0; i < 4; i++) { int nv = (int)col.NvmGroups[i]; ImGui.SetNextItemWidth(80); if (ImGui.InputInt($"##upinvm{i}", ref nv,0,0)) col.NvmGroups[i]=(uint)nv; if (i<3) ImGui.SameLine(); }
                ImGui.SeparatorText("VagrantEntityIDs");
                for (int i = 0; i < 3; i++) { int vg = col.VagrantEntityIDs[i]; ImGui.SetNextItemWidth(80); if (ImGui.InputInt($"##upivag{i}", ref vg,0,0)) col.VagrantEntityIDs[i]=vg; if (i<2) ImGui.SameLine(); }
            }
            if (part is MSB1.Part.ConnectCollision cc)
            {
                ImGui.SeparatorText("ConnectCollision");
                string cn3 = cc.CollisionName ?? ""; ImGui.SetNextItemWidth(w); if (ImGui.InputText("CollisionName##upicn3", ref cn3, 64)) cc.CollisionName = cn3;
                ImGui.Text("MapID"); ImGui.SameLine();
                for (int i = 0; i < 4; i++) { int mid = cc.MapID[i]; ImGui.SetNextItemWidth(50); if (ImGui.InputInt($"##upimid{i}", ref mid,0,0)) cc.MapID[i]=(byte)Math.Clamp(mid,0,255); if (i<3) ImGui.SameLine(); }
            }
        }

        private void DrawUnifiedRegionInfo(MSB1.Region region)
        {
            ImGui.Text($"Region: {region.Name}");
            float w = ImGui.GetContentRegionAvail().X;
            string name = region.Name;
            ImGui.SetNextItemWidth(w); if (ImGui.InputText("Name##urina", ref name, 128)) region.Name = name;
            var pos = region.Position;
            ImGui.SetNextItemWidth(w); if (ImGui.InputFloat3("Position##uripos", ref pos)) region.Position = pos;
            var rot = region.Rotation;
            ImGui.SetNextItemWidth(w); if (ImGui.InputFloat3("Rotation##urirot", ref rot)) region.Rotation = rot;
            int eid = region.EntityID;
            ImGui.SetNextItemWidth(w); if (ImGui.InputInt("EntityID##urieid", ref eid)) region.EntityID = eid;
            ImGui.TextDisabled($"Shape: {DescribeShape(region.Shape)}");
        }

        private void DrawUnifiedEventInfo(MSB1.Event ev)
        {
            ImGui.Text($"[{ev.GetType().Name}] {ev.Name}");
            float w = ImGui.GetContentRegionAvail().X;

            if (ev is MSB1.Event.Treasure tr)
            {
                ImGui.SeparatorText("Treasure");
                string trePart = tr.TreasurePartName ?? "";
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputText("TreasurePart##treas", ref trePart, 64))
                    tr.TreasurePartName = trePart;
                string lots = string.Join(",", tr.ItemLots);
                ImGui.TextDisabled($"ItemLots: [{lots}]");
                for (int k = 0; k < tr.ItemLots.Length; k++)
                {
                    int lot = tr.ItemLots[k];
                    ImGui.SetNextItemWidth(w);
                    if (ImGui.InputInt($"Lot{k}##treaslot", ref lot))
                        tr.ItemLots[k] = lot;
                }
                bool chest = tr.InChest;
                if (ImGui.Checkbox("InChest##treas", ref chest))
                    tr.InChest = chest;
                bool startDis = tr.StartDisabled;
                if (ImGui.Checkbox("StartDisabled##treas", ref startDis))
                    tr.StartDisabled = startDis;
            }
            else if (ev is MSB1.Event.SpawnPoint sp)
            {
                ImGui.SeparatorText("SpawnPoint");
                string spName = sp.SpawnPointName ?? "";
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputText("SpawnPointName##spawn", ref spName, 64))
                    sp.SpawnPointName = spName;
            }
            else if (ev is MSB1.Event.SFX sfx)
            {
                ImGui.SeparatorText("SFX");
                int fx = sfx.EffectID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("EffectID##sfx", ref fx))
                    sfx.EffectID = fx;
            }
            else if (ev is MSB1.Event.Light lt)
            {
                ImGui.SeparatorText("Light");
                int ltId = lt.PointLightID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("PointLightID##evlight", ref ltId))
                    lt.PointLightID = ltId;
            }
            else if (ev is MSB1.Event.Sound snd)
            {
                ImGui.SeparatorText("Sound");
                int st = snd.SoundType;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("SoundType##snd", ref st))
                    snd.SoundType = st;
                int si = snd.SoundID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("SoundID##snd", ref si))
                    snd.SoundID = si;
            }
            else if (ev is MSB1.Event.Environment env)
            {
                ImGui.SeparatorText("Environment");
                int u0 = env.UnkT00;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("UnkT00##env", ref u0))
                    env.UnkT00 = u0;
            }
            else if (ev is MSB1.Event.ObjAct oa)
            {
                ImGui.SeparatorText("ObjAct");
                int oid = oa.ObjActEntityID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("ObjActEntityID##oa", ref oid))
                    oa.ObjActEntityID = oid;
                string oaPart = oa.ObjActPartName ?? "";
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputText("ObjActPart##oa", ref oaPart, 64))
                    oa.ObjActPartName = oaPart;
                int oaParam = oa.ObjActParamID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("ObjActParamID##oa", ref oaParam))
                    oa.ObjActParamID = (short)oaParam;
                int oaSt = (int)oa.ObjActState;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("ObjActState##oast", ref oaSt))
                    oa.ObjActState = (MSB1.Event.ObjAct.StateType)Math.Clamp(oaSt, 0, 2);
                int oaEf = oa.EventFlagID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("EventFlagID##oaef", ref oaEf))
                    oa.EventFlagID = oaEf;
            }
            else if (ev is MSB1.Event.Navmesh nm)
            {
                ImGui.SeparatorText("Navmesh");
                string navReg = nm.NavmeshRegionName ?? "";
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputText("NavmeshRegion##nav", ref navReg, 64))
                    nm.NavmeshRegionName = navReg;
            }
            else if (ev is MSB1.Event.Message msg)
            {
                ImGui.SeparatorText("Message");
                int mid = msg.MessageID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("MessageID##msg", ref mid))
                    msg.MessageID = (short)mid;
                int u02 = msg.UnkT02;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("UnkT02##msgu02", ref u02))
                    msg.UnkT02 = (short)u02;
                bool hd = msg.Hidden;
                if (ImGui.Checkbox("Hidden##msghid", ref hd))
                    msg.Hidden = hd;
            }
            else if (ev is MSB1.Event.Generator gen)
            {
                ImGui.SeparatorText("Generator");
                ImGui.TextDisabled($"MaxNum:{gen.MaxNum} GenType:{gen.GenType} Limit:{gen.LimitNum} Initial:{gen.InitialSpawnCount}");
            }
            else if (ev is MSB1.Event.MapOffset mo)
            {
                ImGui.SeparatorText("MapOffset");
                var mpos = mo.Position;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputFloat3("OffsetPos##mo", ref mpos))
                    mo.Position = mpos;
                float deg = mo.Degree;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputFloat("Degree##mo", ref deg))
                    mo.Degree = deg;
            }
            else if (ev is MSB1.Event.PseudoMultiplayer pm)
            {
                ImGui.SeparatorText("PseudoMultiplayer");
                int heid = pm.HostEntityID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("HostEntityID##pm", ref heid))
                    pm.HostEntityID = heid;
                int efid = pm.EventFlagID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("EventFlagID##pm", ref efid))
                    pm.EventFlagID = efid;
                int agid = pm.ActivateGoodsID;
                ImGui.SetNextItemWidth(w);
                if (ImGui.InputInt("ActivateGoodsID##pmag", ref agid))
                    pm.ActivateGoodsID = agid;
            }
            else if (ev is MSB1.Event.Wind wnd)
            {
                ImGui.SeparatorText("Wind");
                ImGui.TextDisabled($"Min:{wnd.WindVecMin} Max:{wnd.WindVecMax}");
            }
        }

        private static string DescribeShape(SoulsFormats.MSB.Shape? shape)
        {
            if (shape == null) return "None";
            return shape switch
            {
                SoulsFormats.MSB.Shape.Box b       => $"Box {b.Width:F1}x{b.Depth:F1}x{b.Height:F1}",
                SoulsFormats.MSB.Shape.Rectangle r  => $"Rect {r.Width:F1}x{r.Depth:F1}",
                SoulsFormats.MSB.Shape.Sphere s     => $"Sphere r={s.Radius:F1}",
                SoulsFormats.MSB.Shape.Circle c     => $"Circle r={c.Radius:F1}",
                SoulsFormats.MSB.Shape.Cylinder c   => $"Cyl r={c.Radius:F1} h={c.Height:F1}",
                SoulsFormats.MSB.Shape.Point        => "Point",
                _                                   => shape.GetType().Name
            };
        }

        private void RebuildUnifiedList()
        {
            _unifiedEntries.Clear();
            if (_loaded == null) return;

            foreach (var part in _loaded.Msb.Parts.GetEntries())
            {
                _unifiedEntries.Add(new MsbEntry
                {
                    Kind = MsbEntry.KindType.Part,
                    Subtype = GetPartTypeName(part),
                    Name = part.Name,
                    Position = part.Position,
                    EntityID = part.EntityID,
                    Extra = part.ModelName ?? "",
                    Part = part
                });
            }

            foreach (var region in _store.GetRegions(_loaded))
            {
                _unifiedEntries.Add(new MsbEntry
                {
                    Kind = MsbEntry.KindType.Region,
                    Subtype = "Region",
                    Name = region.Name,
                    Position = region.Position,
                    EntityID = region.EntityID,
                    Extra = DescribeShape(region.Shape),
                    Region = region
                });
            }

            foreach (var ev in _store.GetEvents(_loaded))
            {
                string extra = ev switch
                {
                    MSB1.Event.Treasure t => $"Part:{t.TreasurePartName}",
                    MSB1.Event.SpawnPoint s => $"Region:{s.RegionName}",
                    MSB1.Event.SFX f => $"Effect:{f.EffectID}",
                    _ => $"Part:{ev.PartName} Region:{ev.RegionName}"
                };
                _unifiedEntries.Add(new MsbEntry
                {
                    Kind = MsbEntry.KindType.Event,
                    Subtype = ev.GetType().Name,
                    Name = ev.Name,
                    EntityID = ev.EntityID,
                    Extra = extra,
                    Event = ev
                });
            }
        }

        private void RebuildFilteredUnified()
        {
            IEnumerable<MsbEntry> items = _unifiedEntries;

            if (_unifiedSubtypeFilter > 0)
            {
                string filterType = UnifiedSubtypeNames[_unifiedSubtypeFilter];
                items = items.Where(e => e.Subtype == filterType);
            }

            if (!string.IsNullOrEmpty(_unifiedFilter))
                items = items.Where(e => e.Name.Contains(_unifiedFilter, StringComparison.OrdinalIgnoreCase));

            if (_unifiedSortCol >= 0)
            {
                var cols = GetColumns();
                if (_unifiedSortCol < cols.Count && cols[_unifiedSortCol].SortKey != null)
                {
                    var key = cols[_unifiedSortCol].SortKey!;
                    items = _unifiedSortAsc ? items.OrderBy(key) : items.OrderByDescending(key);
                }
            }

            _filteredUnified = items.ToList();
            RecalcColumnWidths();
            if (_unifiedSelected >= _filteredUnified.Count)
                _unifiedSelected = -1;
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
                _partTypeFilter = 0;
                _partFilter = string.Empty;
                _regionFilter = string.Empty;
                _unifiedSubtypeFilter = 0;
                _unifiedFilter = string.Empty;
                _unifiedSelected = -1;
                _unifiedSortCol = -1;
                _unifiedSortAsc = true;
                _editDirty = false;
                RebuildPartList();
                RebuildRegionList();
                RebuildUnifiedList();
                RebuildFilteredUnified();
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
                5 => _loaded.Msb.Parts.Collisions.Cast<MSB1.Part>(),
                6 => _loaded.Msb.Parts.ConnectCollisions.Cast<MSB1.Part>(),
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
            MSB1.Part.MapPiece         => "MapPiece",
            MSB1.Part.Object           => "Object",
            MSB1.Part.Enemy            => "Enemy",
            MSB1.Part.Player           => "Player",
            MSB1.Part.Collision        => "Collision",
            MSB1.Part.ConnectCollision => "ConnectCollision",
            _                          => "Part"
        };
    }
}

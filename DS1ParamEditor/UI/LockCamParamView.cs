using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SoulsFormats;

namespace DS1ParamEditor
{
    /// <summary>
    /// Compact editor for LockCamParam (LOCK_CAM_PARAM_ST).
    /// Six sliders + a minimal side-view diagram.
    /// </summary>
    public sealed class LockCamParamView
    {
        private readonly EditorState _state;

        // Field byte offsets (all f32, packed)
        const int OFF_CAM_DIST       = 0;
        const int OFF_ROT_MIN_X      = 4;
        const int OFF_LOCK_ROT_SHIFT = 8;
        const int OFF_CHR_ORG_OFFSET = 12;
        const int OFF_LOCK_RANGE_MAX = 16;
        const int OFF_CAM_FOV_Y      = 20;
        const int ROW_BYTES          = 24;

        private byte[]? _cache;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        const double REFRESH_MS = 100.0;
        
        private int _selectedRowIndex = 0;

        public LockCamParamView(EditorState state) => _state = state;

        public void Draw()
        {
            var param = _state.LockCamParam;
            if (param == null || param.Param.Rows.Count == 0) return;

            var addr = _state.GetHookedAddress(param.Name);
            if (addr == null)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "Not hooked.");
                return;
            }
            if (!_state.IsAttached)
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Disconnected.");
                return;
            }

            var process = _state.Process!;
            
            // Clamp selected row index
            if (_selectedRowIndex >= param.Param.Rows.Count)
                _selectedRowIndex = 0;

            // Row selector
            ImGui.Text("Lock Cam ID:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("##rowselect", $"ID {param.Param.Rows[_selectedRowIndex].ID}"))
            {
                for (int i = 0; i < param.Param.Rows.Count; i++)
                {
                    var r = param.Param.Rows[i];
                    bool selected = i == _selectedRowIndex;
                    if (ImGui.Selectable($"ID {r.ID}##{i}", selected))
                        _selectedRowIndex = i;
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            
            ImGui.SameLine();
            ImGui.TextDisabled($"({_selectedRowIndex + 1} / {param.Param.Rows.Count})");

            ImGui.Separator();

            var  row      = param.Param.Rows[_selectedRowIndex];
            nint rowAddr  = addr.Value + (nint)row.DataOffset;

            if (_timer.Elapsed.TotalMilliseconds >= REFRESH_MS || _cache == null)
            {
                var fresh = process.ReadBytes(rowAddr, ROW_BYTES);
                if (fresh != null) _cache = fresh;
                _timer.Restart();
            }
            if (_cache == null || _cache.Length < ROW_BYTES) return;

            byte[] b = (byte[])_cache.Clone();
            float w = ImGui.GetContentRegionAvail().X * 0.6f;

            if (ImGui.CollapsingHeader("Camera", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Field(process, param, row, rowAddr, b, OFF_CAM_DIST,       "Distance (m)",        "camDistTarget",       0.1f, 30f,  w);
                Field(process, param, row, rowAddr, b, OFF_ROT_MIN_X,      "Min angle (deg)",     "rotRangeMinX",       -80f, 80f,  w);
                Field(process, param, row, rowAddr, b, OFF_LOCK_ROT_SHIFT, "Vertical shift",      "lockRotXShiftRatio",  0f,  1f,   w);
                Field(process, param, row, rowAddr, b, OFF_CHR_ORG_OFFSET, "Pivot Y offset (m)",  "chrOrgOffset_Y",     -10f, 10f,  w);
                Field(process, param, row, rowAddr, b, OFF_CAM_FOV_Y,      "FOV (deg)",           "camFovY",             10f, 120f, w);
            }

            if (ImGui.CollapsingHeader("Lock-on", ImGuiTreeNodeFlags.DefaultOpen))
            {
                Field(process, param, row, rowAddr, b, OFF_LOCK_RANGE_MAX, "Max range (m)",       "chrLockRangeMaxRadius", 0f, 60f, w);
            }

            Buffer.BlockCopy(b, 0, _cache, 0, ROW_BYTES);
        }

        // ── Slider ────────────────────────────────────────────────────────────

        private static void Field(
            GameProcess process, LoadedParam param, PARAM.Row row,
            nint rowAddr, byte[] b, int off,
            string label, string fieldName, float min, float max, float width)
        {
            float v = MemoryMarshal.Read<float>(b.AsSpan(off));
            ImGui.SetNextItemWidth(width);
            if (ImGui.SliderFloat($"{label}##{fieldName}", ref v, min, max, "%.2f"))
            {
                process.WriteField(rowAddr + off, PARAMDEF.DefType.f32, v);
                MemoryMarshal.Write(b.AsSpan(off), v);
                foreach (var cell in row.Cells)
                    if (cell.Def.InternalName == fieldName) { cell.Value = v; break; }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"{fieldName}  =  {v:F4}");
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SoulsFormats;

namespace DS1ParamEditor
{
    /// <summary>
    /// Renders the param row/cell table.
    /// Each visible row is read from game memory as a single bulk ReadBytes call
    /// at most once per <see cref="READ_INTERVAL_MS"/> ms, then cached.
    /// Writes happen immediately on user interaction.
    /// </summary>
    public sealed class ParamTableView
    {
        private readonly EditorState _state;

        // ── Row cache ─────────────────────────────────────────────────────────
        // Key: rowId → raw bytes of that row's data block
        private readonly Dictionary<int, byte[]> _rowCache = new();
        private readonly Stopwatch _cacheTimer = Stopwatch.StartNew();
        private string _cachedParamName = string.Empty;

        const double READ_INTERVAL_MS = 100.0; // 10 Hz live refresh

        // ── Filters ───────────────────────────────────────────────────────────
        private string _rowFilter   = string.Empty;
        private string _fieldFilter = string.Empty;

        public ParamTableView(EditorState state) => _state = state;

        public void Draw()
        {
            var param = _state.SelectedParam;
            if (param == null) return;

            var addr = _state.GetHookedAddress(param.Name);
            if (addr == null)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "Not hooked. Press 'Hook' to scan.");
                return;
            }

            if (!_state.IsAttached)
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Process disconnected.");
                return;
            }

            // Invalidate cache when param changes
            if (_cachedParamName != param.Name)
            {
                _rowCache.Clear();
                _cachedParamName = param.Name;
                _cacheTimer.Restart();
            }

            bool refresh = _cacheTimer.Elapsed.TotalMilliseconds >= READ_INTERVAL_MS;
            if (refresh) _cacheTimer.Restart();

            var   process  = _state.Process!;
            nint  baseAddr = addr.Value;
            int   rowSize  = param.Param.DetectedSize > 0 ? (int)param.Param.DetectedSize : 0;

            // Filters
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Row filter", ref _rowFilter, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Field filter", ref _fieldFilter, 64);
            ImGui.Separator();

            ImGui.BeginChild("ParamTableScroll", Vector2.Zero, ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar);

            foreach (var row in param.Param.Rows)
            {
                string rowLabel = $"[{row.ID}] {row.Name}";
                if (!string.IsNullOrEmpty(_rowFilter) &&
                    !rowLabel.Contains(_rowFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ImGui.CollapsingHeader($"{rowLabel}##r{row.ID}"))
                    continue;

                // Bulk-read the entire row data block once per refresh interval
                _rowCache.TryGetValue(row.ID, out byte[]? rowBytes);
                if (refresh || rowBytes == null)
                {
                    int readLen = rowSize > 0 ? rowSize : EstimateRowSize(row);
                    if (readLen > 0)
                    {
                        nint rowAddr = baseAddr + (nint)row.DataOffset;
                        var  fresh   = process.ReadBytes(rowAddr, readLen);
                        if (fresh != null)
                        {
                            _rowCache[row.ID] = fresh;
                            rowBytes = fresh;
                        }
                    }
                }

                ImGui.Indent();

                int fieldOffset = 0;
                foreach (var cell in row.Cells)
                {
                    int fieldSize = FieldSize(cell.Def.DisplayType, cell.Def.ArrayLength);

                    if (cell.Def.DisplayType != PARAMDEF.DefType.dummy8)
                    {
                        if (string.IsNullOrEmpty(_fieldFilter) ||
                            cell.Def.InternalName.Contains(_fieldFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            double cached    = ReadFromCache(rowBytes, fieldOffset, cell);
                            nint   fieldAddr = baseAddr + (nint)row.DataOffset + fieldOffset;
                            DrawField(process, fieldAddr, cell, row.ID, fieldOffset, cached, rowBytes);
                        }
                    }

                    fieldOffset += fieldSize;
                }

                ImGui.Unindent();
            }

            ImGui.EndChild();
        }

        // ── Cache helpers ─────────────────────────────────────────────────────

        private static double ReadFromCache(byte[]? rowBytes, int offset, PARAM.Cell cell)
        {
            if (rowBytes == null || offset + FieldSize(cell.Def.DisplayType, cell.Def.ArrayLength) > rowBytes.Length)
                return Convert.ToDouble(cell.Value);

            var span = rowBytes.AsSpan(offset);
            return cell.Def.DisplayType switch
            {
                PARAMDEF.DefType.s8      => MemoryMarshal.Read<sbyte>(span),
                PARAMDEF.DefType.u8      => MemoryMarshal.Read<byte>(span),
                PARAMDEF.DefType.s16     => MemoryMarshal.Read<short>(span),
                PARAMDEF.DefType.u16     => MemoryMarshal.Read<ushort>(span),
                PARAMDEF.DefType.s32     => MemoryMarshal.Read<int>(span),
                PARAMDEF.DefType.b32     => MemoryMarshal.Read<int>(span),
                PARAMDEF.DefType.u32     => MemoryMarshal.Read<uint>(span),
                PARAMDEF.DefType.f32     => MemoryMarshal.Read<float>(span),
                PARAMDEF.DefType.angle32 => MemoryMarshal.Read<float>(span),
                PARAMDEF.DefType.f64     => MemoryMarshal.Read<double>(span),
                _                        => Convert.ToDouble(cell.Value)
            };
        }

        private static void WriteToCache(byte[]? rowBytes, int offset, PARAMDEF.DefType type, double value)
        {
            if (rowBytes == null) return;
            int size = FieldSize(type, 1);
            if (offset + size > rowBytes.Length) return;

            var span = rowBytes.AsSpan(offset);
            switch (type)
            {
                case PARAMDEF.DefType.s8:      MemoryMarshal.Write(span, (sbyte)value);  break;
                case PARAMDEF.DefType.u8:      MemoryMarshal.Write(span, (byte)value);   break;
                case PARAMDEF.DefType.s16:     MemoryMarshal.Write(span, (short)value);  break;
                case PARAMDEF.DefType.u16:     MemoryMarshal.Write(span, (ushort)value); break;
                case PARAMDEF.DefType.s32:
                case PARAMDEF.DefType.b32:     MemoryMarshal.Write(span, (int)value);    break;
                case PARAMDEF.DefType.u32:     MemoryMarshal.Write(span, (uint)value);   break;
                case PARAMDEF.DefType.f32:
                case PARAMDEF.DefType.angle32: MemoryMarshal.Write(span, (float)value);  break;
                case PARAMDEF.DefType.f64:     MemoryMarshal.Write(span, value);         break;
            }
        }

        // ── Field rendering ───────────────────────────────────────────────────

        private void DrawField(GameProcess process, nint address, PARAM.Cell cell,
            int rowId, int fieldOffset, double cached, byte[]? rowBytes)
        {
            var    type = cell.Def.DisplayType;
            string id   = $"##{cell.Def.InternalName}_{rowId}";
            string name = $"{cell.Def.InternalName} ({TypeLabel(type)})";

            switch (type)
            {
                case PARAMDEF.DefType.s8:
                {
                    int v = (int)(sbyte)cached;
                    (int mn, int mx) = ClampedRange(cell, sbyte.MinValue, sbyte.MaxValue);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var sv = (sbyte)v; process.WriteField(address, type, sv); cell.Value = sv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u8:
                {
                    int v = (int)(byte)cached;
                    (int mn, int mx) = ClampedRange(cell, 0, byte.MaxValue);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var bv = (byte)v; process.WriteField(address, type, bv); cell.Value = bv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.s16:
                {
                    int v = (int)(short)cached;
                    (int mn, int mx) = ClampedRange(cell, short.MinValue, short.MaxValue);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var sv = (short)v; process.WriteField(address, type, sv); cell.Value = sv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u16:
                {
                    int v = (int)(ushort)cached;
                    (int mn, int mx) = ClampedRange(cell, 0, ushort.MaxValue);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var uv = (ushort)v; process.WriteField(address, type, uv); cell.Value = uv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.s32:
                case PARAMDEF.DefType.b32:
                {
                    int v = (int)cached;
                    (int mn, int mx) = ClampedRange(cell, int.MinValue, int.MaxValue);
                    if (ImGui.InputInt(name + id, ref v))
                    { v = Math.Clamp(v, mn, mx); process.WriteField(address, type, v); cell.Value = v; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u32:
                {
                    int v = (int)(uint)cached;
                    if (ImGui.InputInt(name + id, ref v))
                    { uint uv = (uint)Math.Max(0, v); process.WriteField(address, type, uv); cell.Value = uv; WriteToCache(rowBytes, fieldOffset, type, uv); }
                    break;
                }
                case PARAMDEF.DefType.f32:
                case PARAMDEF.DefType.angle32:
                {
                    float v = (float)cached;
                    (float mn, float mx) = ClampedRangeF(cell, -1e6f, 1e6f);
                    if (ImGui.SliderFloat(name + id, ref v, mn, mx))
                    { process.WriteField(address, type, v); cell.Value = v; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.f64:
                {
                    float v = (float)cached;
                    (float mn, float mx) = ClampedRangeF(cell, -1e6f, 1e6f);
                    if (ImGui.SliderFloat(name + id, ref v, mn, mx))
                    { double dv = v; process.WriteField(address, type, dv); cell.Value = dv; WriteToCache(rowBytes, fieldOffset, type, dv); }
                    break;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static (int mn, int mx) ClampedRange(PARAM.Cell cell, int typeMin, int typeMax)
        {
            int mn = Convert.ToInt32(cell.Def.Minimum);
            int mx = Convert.ToInt32(cell.Def.Maximum);
            return mn < mx ? (mn, mx) : (typeMin, typeMax);
        }

        private static (float mn, float mx) ClampedRangeF(PARAM.Cell cell, float typeMin, float typeMax)
        {
            float mn = Convert.ToSingle(cell.Def.Minimum);
            float mx = Convert.ToSingle(cell.Def.Maximum);
            return mn < mx ? (mn, mx) : (typeMin, typeMax);
        }

        private static int EstimateRowSize(PARAM.Row row)
        {
            int total = 0;
            foreach (var cell in row.Cells)
                total += FieldSize(cell.Def.DisplayType, cell.Def.ArrayLength);
            return total;
        }

        public static int FieldSize(PARAMDEF.DefType type, int arrayLength) => type switch
        {
            PARAMDEF.DefType.s8      => 1,
            PARAMDEF.DefType.u8      => 1,
            PARAMDEF.DefType.s16     => 2,
            PARAMDEF.DefType.u16     => 2,
            PARAMDEF.DefType.s32     => 4,
            PARAMDEF.DefType.b32     => 4,
            PARAMDEF.DefType.u32     => 4,
            PARAMDEF.DefType.f32     => 4,
            PARAMDEF.DefType.angle32 => 4,
            PARAMDEF.DefType.f64     => 8,
            PARAMDEF.DefType.dummy8  => arrayLength,
            _ => 0
        };

        private static string TypeLabel(PARAMDEF.DefType t) => t switch
        {
            PARAMDEF.DefType.s8      => "s8",
            PARAMDEF.DefType.u8      => "u8",
            PARAMDEF.DefType.s16     => "s16",
            PARAMDEF.DefType.u16     => "u16",
            PARAMDEF.DefType.s32     => "s32",
            PARAMDEF.DefType.b32     => "b32",
            PARAMDEF.DefType.u32     => "u32",
            PARAMDEF.DefType.f32     => "f32",
            PARAMDEF.DefType.angle32 => "angle",
            PARAMDEF.DefType.f64     => "f64",
            _ => t.ToString()
        };
    }
}

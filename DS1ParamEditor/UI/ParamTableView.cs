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

        public void Draw() => DrawParam(_state.SelectedParam);

        /// <summary>Draws the table for a specific param (used in multi-param view).</summary>
        public void DrawParam(LoadedParam? param)
        {
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
            ImGui.InputText("Row filter##" + param.Name, ref _rowFilter, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Field filter##" + param.Name, ref _fieldFilter, 64);
            ImGui.Separator();

            ImGui.BeginChild("ParamTableScroll##" + param.Name, Vector2.Zero, ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar);

            foreach (var row in param.Param.Rows)
            {
                string rowLabel = $"[{row.ID}] {row.Name}";
                if (!string.IsNullOrEmpty(_rowFilter) &&
                    !rowLabel.Contains(_rowFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ImGui.CollapsingHeader($"{rowLabel}##r{row.ID}_{param.Name}"))
                    continue;

                _rowCache.TryGetValue(row.ID, out byte[]? rowBytes);
                if (refresh || rowBytes == null)
                {
                    int readLen = rowSize > 0 ? rowSize : EstimateRowSize(row);
                    if (readLen > 0)
                    {
                        nint rowAddr = baseAddr + (nint)row.DataOffset;
                        var  fresh   = process.ReadBytes(rowAddr, readLen);
                        if (fresh != null) { _rowCache[row.ID] = fresh; rowBytes = fresh; }
                    }
                }

                ImGui.Indent();
                int fieldOffset = 0;
                // ToList() once per row render — avoid repeated allocation
                var cells = row.Cells is List<PARAM.Cell> cl ? cl : row.Cells.ToList();
                var renderedColorGroups = new System.Collections.Generic.HashSet<string>();

                for (int ci = 0; ci < cells.Count; ci++)
                {
                    var cell = cells[ci];
                    int fieldSize = FieldSize(cell.Def.DisplayType, cell.Def.ArrayLength);

                    if (cell.Def.DisplayType != PARAMDEF.DefType.dummy8)
                    {
                        if (string.IsNullOrEmpty(_fieldFilter) ||
                            cell.Def.InternalName.Contains(_fieldFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            double cached    = ReadFromCache(rowBytes, fieldOffset, cell);
                            nint   fieldAddr = baseAddr + (nint)row.DataOffset + fieldOffset;
                            DrawField(process, fieldAddr, cell, row.ID, fieldOffset, cached, rowBytes);

                            // After drawing A field — add color picker for the RGBA group
                            var rgbGroupForA = TryFindRgbGroupFromA(cells, ci);
                            if (rgbGroupForA != null)
                            {
                                string groupKey = rgbGroupForA.Value.cellR.Def.InternalName;
                                if (!renderedColorGroups.Contains(groupKey))
                                {
                                    renderedColorGroups.Add(groupKey);
                                    DrawColorField(process, baseAddr, row, rowBytes, rgbGroupForA.Value, param.Name);
                                }
                            }
                            // Fallback: if no A field exists, show after B
                            else
                            {
                                var rgbGroupForB = TryFindRgbGroupFromB(cells, ci);
                                if (rgbGroupForB != null)
                                {
                                    string groupKey = rgbGroupForB.Value.cellR.Def.InternalName;
                                    // Only show if no A field will come later
                                    bool hasAlpha = cells.Any(c =>
                                    {
                                        string nb = rgbGroupForB.Value.cellB.Def.InternalName;
                                        // Check if there's a matching A field
                                        int idx = nb.IndexOf("colB", StringComparison.OrdinalIgnoreCase);
                                        if (idx >= 0)
                                        {
                                            string aName = nb[..idx] + "colA" + nb[(idx+4)..];
                                            return c.Def.InternalName.Equals(aName, StringComparison.OrdinalIgnoreCase);
                                        }
                                        string stem = nb.EndsWith("B") ? nb[..^1] : nb;
                                        return c.Def.InternalName.Equals(stem + "A", StringComparison.OrdinalIgnoreCase);
                                    });
                                    if (!hasAlpha && !renderedColorGroups.Contains(groupKey))
                                    {
                                        renderedColorGroups.Add(groupKey);
                                        DrawColorField(process, baseAddr, row, rowBytes, rgbGroupForB.Value, param.Name);
                                    }
                                }
                            }
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

            const float FIELD_WIDTH = 300f;

            switch (type)
            {
                case PARAMDEF.DefType.s8:
                {
                    int v = (int)(sbyte)cached;
                    (int mn, int mx) = ClampedRange(cell, sbyte.MinValue, sbyte.MaxValue);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var sv = (sbyte)v; process.WriteField(address, type, sv); cell.Value = sv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u8:
                {
                    int v = (int)(byte)cached;
                    (int mn, int mx) = ClampedRange(cell, 0, byte.MaxValue);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var bv = (byte)v; process.WriteField(address, type, bv); cell.Value = bv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.s16:
                {
                    int v = (int)(short)cached;
                    (int mn, int mx) = ClampedRange(cell, short.MinValue, short.MaxValue);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var sv = (short)v; process.WriteField(address, type, sv); cell.Value = sv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u16:
                {
                    int v = (int)(ushort)cached;
                    (int mn, int mx) = ClampedRange(cell, 0, ushort.MaxValue);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderInt(name + id, ref v, mn, mx))
                    { var uv = (ushort)v; process.WriteField(address, type, uv); cell.Value = uv; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.s32:
                case PARAMDEF.DefType.b32:
                {
                    int v = (int)cached;
                    (int mn, int mx) = ClampedRange(cell, int.MinValue, int.MaxValue);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.InputInt(name + id, ref v))
                    { v = Math.Clamp(v, mn, mx); process.WriteField(address, type, v); cell.Value = v; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.u32:
                {
                    int v = (int)(uint)cached;
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.InputInt(name + id, ref v))
                    { uint uv = (uint)Math.Max(0, v); process.WriteField(address, type, uv); cell.Value = uv; WriteToCache(rowBytes, fieldOffset, type, uv); }
                    break;
                }
                case PARAMDEF.DefType.f32:
                case PARAMDEF.DefType.angle32:
                {
                    float v = (float)cached;
                    (float mn, float mx) = ClampedRangeF(cell, -1e6f, 1e6f);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderFloat(name + id, ref v, mn, mx))
                    { process.WriteField(address, type, v); cell.Value = v; WriteToCache(rowBytes, fieldOffset, type, v); }
                    break;
                }
                case PARAMDEF.DefType.f64:
                {
                    float v = (float)cached;
                    (float mn, float mx) = ClampedRangeF(cell, -1e6f, 1e6f);
                    ImGui.SetNextItemWidth(FIELD_WIDTH);
                    if (ImGui.SliderFloat(name + id, ref v, mn, mx))
                    { double dv = v; process.WriteField(address, type, dv); cell.Value = dv; WriteToCache(rowBytes, fieldOffset, type, dv); }
                    break;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private record struct RgbGroup(PARAM.Cell cellR, PARAM.Cell cellG, PARAM.Cell cellB,
            int offsetR, int offsetG, int offsetB);
        /// <summary>
        /// If cell at index ci looks like a Red component, find matching G and B cells.
        /// Supports patterns: colR/colG/colB, _R/_G/_B, R/G/B suffix.
        /// </summary>
        /// <summary>If cell at ci is an A (alpha) component, find the matching R, G, B.</summary>
        private static RgbGroup? TryFindRgbGroupFromA(List<PARAM.Cell> cells, int ci)
        {
            var cellA = cells[ci];
            string nameA = cellA.Def.InternalName;

            string? nameR = null, nameG = null, nameB = null;

            // Pattern: *colA* → replace colA with colR/colG/colB
            int colAIdx = nameA.IndexOf("colA", StringComparison.OrdinalIgnoreCase);
            if (colAIdx >= 0)
            {
                string before = nameA[..colAIdx];
                string after  = nameA[(colAIdx + 4)..]; // after "colA"
                nameR = before + "colR" + after;
                nameG = before + "colG" + after;
                nameB = before + "colB" + after;
            }
            // Pattern: ends with A suffix (colA_X → colR_X)
            else if (nameA.StartsWith("col", StringComparison.OrdinalIgnoreCase) &&
                     nameA.Length > 3 && nameA[3] == 'A')
            {
                string suffix = nameA[4..];
                nameR = "colR" + suffix;
                nameG = "colG" + suffix;
                nameB = "colB" + suffix;
            }
            // Generic: ends with A
            else if (nameA.EndsWith("A", StringComparison.OrdinalIgnoreCase) && nameA.Length > 1)
            {
                string stem = nameA[..^1];
                nameR = stem + "R";
                nameG = stem + "G";
                nameB = stem + "B";
            }
            else
                return null;

            PARAM.Cell? cellR = null, cellG = null, cellB = null;
            int offsetR = 0, offsetG = 0, offsetB = 0;

            int off = 0;
            foreach (var c in cells)
            {
                if (c.Def.InternalName.Equals(nameR, StringComparison.OrdinalIgnoreCase))
                { cellR = c; offsetR = off; }
                if (c.Def.InternalName.Equals(nameG, StringComparison.OrdinalIgnoreCase))
                { cellG = c; offsetG = off; }
                if (c.Def.InternalName.Equals(nameB, StringComparison.OrdinalIgnoreCase))
                { cellB = c; offsetB = off; }
                off += FieldSize(c.Def.DisplayType, c.Def.ArrayLength);
            }

            if (cellR == null || cellG == null || cellB == null) return null;
            return new RgbGroup(cellR, cellG, cellB, offsetR, offsetG, offsetB);
        }

        /// <summary>If cell at ci is a B component, find the matching R and G.</summary>
        private static RgbGroup? TryFindRgbGroupFromB(List<PARAM.Cell> cells, int ci)
        {
            var cellB = cells[ci];
            string nameB = cellB.Def.InternalName;

            string? nameR = null, nameG = null;

            if (nameB.StartsWith("col", StringComparison.OrdinalIgnoreCase) &&
                nameB.Length > 3 && nameB[3] == 'B')
            {
                string suffix = nameB[4..];
                nameR = "col" + "R" + suffix;
                nameG = "col" + "G" + suffix;
            }
            else if (nameB.EndsWith("B", StringComparison.OrdinalIgnoreCase) && nameB.Length > 1)
            {
                string stem = nameB[..^1];
                nameR = stem + "R";
                nameG = stem + "G";
            }
            else
                return null;

            PARAM.Cell? cellR = null, cellG = null;
            int offsetR = 0, offsetG = 0, offsetB = 0;

            int off = 0;
            foreach (var c in cells)
            {
                if (c == cellB) offsetB = off;
                if (c.Def.InternalName.Equals(nameR, StringComparison.OrdinalIgnoreCase))
                { cellR = c; offsetR = off; }
                if (c.Def.InternalName.Equals(nameG, StringComparison.OrdinalIgnoreCase))
                { cellG = c; offsetG = off; }
                off += FieldSize(c.Def.DisplayType, c.Def.ArrayLength);
            }

            if (cellR == null || cellG == null) return null;
            return new RgbGroup(cellR, cellG, cellB, offsetR, offsetG, offsetB);
        }

        private static RgbGroup? TryFindRgbGroup(List<PARAM.Cell> cells, int ci)        {
            var cellR = cells[ci];
            string nameR = cellR.Def.InternalName;

            // Patterns supported:
            // colR_X / colG_X / colB_X  (e.g. colR_0, colR_u, colR_s)
            // *R / *G / *B suffix        (e.g. envDif_colR, someR)
            // *_R / *_G / *_B            (e.g. col_R)

            string? prefixG = null, prefixB = null;

            // colR_X pattern: prefix = "col", suffix = "_X"
            if (nameR.Length > 4 &&
                nameR.StartsWith("col", StringComparison.OrdinalIgnoreCase) &&
                nameR[3] == 'R')
            {
                string suffix = nameR[4..]; // e.g. "_0", "_u", ""
                prefixG = "col" + "G" + suffix;
                prefixB = "col" + "B" + suffix;
            }
            // *R_X or *R suffix
            else if (nameR.EndsWith("R", StringComparison.OrdinalIgnoreCase) && nameR.Length > 1)
            {
                string stem = nameR[..^1];
                prefixG = stem + "G";
                prefixB = stem + "B";
            }
            else
                return null;

            // Find G and B cells by name
            PARAM.Cell? cellG = null, cellB = null;
            int offsetR = 0, offsetG = 0, offsetB = 0;

            int off = 0;
            foreach (var c in cells)
            {
                if (c == cellR) offsetR = off;
                if (c.Def.InternalName.Equals(prefixG, StringComparison.OrdinalIgnoreCase))
                { cellG = c; offsetG = off; }
                if (c.Def.InternalName.Equals(prefixB, StringComparison.OrdinalIgnoreCase))
                { cellB = c; offsetB = off; }
                off += FieldSize(c.Def.DisplayType, c.Def.ArrayLength);
            }

            if (cellG == null || cellB == null) return null;
            return new RgbGroup(cellR, cellG, cellB, offsetR, offsetG, offsetB);
        }

        private void DrawColorField(GameProcess process, nint baseAddr, PARAM.Row row,
            byte[]? rowBytes, RgbGroup rgb, string paramName)
        {
            // For s16 fields: values are 0-255 range (or higher for HDR)
            // Normalize to 0..1 for ColorEdit, but use HDR mode if values can exceed 255
            float ToFloat(PARAM.Cell cell, int offset)
            {
                double raw = ReadFromCache(rowBytes, offset, cell);
                return cell.Def.DisplayType switch
                {
                    PARAMDEF.DefType.f32 or PARAMDEF.DefType.angle32 => (float)raw,
                    _ => (float)(raw / 255.0)
                };
            }

            double FromFloat(PARAM.Cell cell, float v) => cell.Def.DisplayType switch
            {
                PARAMDEF.DefType.f32 or PARAMDEF.DefType.angle32 => v,
                _ => Math.Round(v * 255.0)
            };

            var col = new Vector3(
                ToFloat(rgb.cellR, rgb.offsetR),
                ToFloat(rgb.cellG, rgb.offsetG),
                ToFloat(rgb.cellB, rgb.offsetB));

            // Strip R suffix for label
            string nameR = rgb.cellR.Def.InternalName;
            string label = nameR.StartsWith("col", StringComparison.OrdinalIgnoreCase) && nameR.Length > 3 && nameR[3] == 'R'
                ? "col" + nameR[4..] // colR_0 → col_0
                : nameR[..^1];       // someR → some

            ImGui.SetNextItemWidth(300f);
            // Use HDR flag to allow values > 1.0 (which maps to > 255)
            if (ImGui.ColorEdit3($"{label} (RGB)##color_{row.ID}_{paramName}",
                ref col, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR))
            {
                void WriteColor(PARAM.Cell cell, int offset, float v)
                {
                    double dv = FromFloat(cell, v);
                    nint addr = baseAddr + (nint)row.DataOffset + offset;
                    process.WriteField(addr, cell.Def.DisplayType, dv);
                    WriteToCache(rowBytes, offset, cell.Def.DisplayType, dv);
                    try { cell.Value = Convert.ChangeType(dv, cell.Value?.GetType() ?? typeof(double)); } catch { }
                }
                WriteColor(rgb.cellR, rgb.offsetR, col.X);
                WriteColor(rgb.cellG, rgb.offsetG, col.Y);
                WriteColor(rgb.cellB, rgb.offsetB, col.Z);
            }
        }

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

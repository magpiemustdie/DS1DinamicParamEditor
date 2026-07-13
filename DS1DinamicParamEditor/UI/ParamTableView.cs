using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SoulsFormats;

namespace DS1ParamEditor
{
    public sealed class ParamTableView
    {
        private readonly EditorState _state;
        private int _tablePageOffset;
        private string _pinIdsText = string.Empty;
        private readonly HashSet<int> _frozenIds = new();

        private const int MAX_COLS = 64;

        // ── Row cache ─────────────────────────────────────────────────────────
        private readonly Dictionary<int, byte[]> _rowCache = new();
        private readonly Stopwatch _cacheTimer = Stopwatch.StartNew();
        private string _cachedParamName = string.Empty;

        const double READ_INTERVAL_MS = 60000.0;

        // ── String caches ─────────────────────────────────────────────────────
        private List<string>? _fieldLabels;
        private readonly Dictionary<(int fieldIdx, int rowId), string> _cellIdCache = new();
        private readonly Dictionary<int, string> _colHeaderCache = new();
        private readonly Dictionary<int, List<PARAM.Cell>> _rowCellsCache = new();

        // ── Filters ───────────────────────────────────────────────────────────
        private string _rowFilter   = string.Empty;
        private string _fieldFilter = string.Empty;

        // ── Computed row caches (reused across frames, invalidated on change) ─
        private List<PARAM.Row>? _cachedAllRows;
        private List<PARAM.Row>? _cachedFiltered;
        private List<PARAM.Row>? _cachedVisible;
        private List<(int ci, int off, int fsize)>? _cachedVisibleFields;
        private HashSet<int> _cachedPinSet = new();
        private string _lastRowFilter   = string.Empty;
        private string _lastFieldFilter = string.Empty;
        private string _lastPinIdsText  = string.Empty;
        private int _lastPageOffset;

        public ParamTableView(EditorState state) => _state = state;

        public void Draw() => DrawParam(_state.SelectedParam);

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
                _tablePageOffset = 0;
                _pinIdsText = string.Empty;
                _frozenIds.Clear();
                _fieldLabels = null;
                _cellIdCache.Clear();
                _colHeaderCache.Clear();
                _rowCellsCache.Clear();
                _cacheTimer.Restart();
                _cachedAllRows = null;
                _cachedFiltered = null;
                _cachedVisible = null;
                _cachedVisibleFields = null;
                _lastRowFilter = string.Empty;
                _lastFieldFilter = string.Empty;
                _lastPinIdsText = string.Empty;
                _lastPageOffset = 0;
            }

            bool refresh = _cacheTimer.Elapsed.TotalMilliseconds >= READ_INTERVAL_MS;
            if (refresh) _cacheTimer.Restart();

            var   process  = _state.Process!;
            nint  baseAddr = addr.Value;
            int   rowSize  = param.Param.DetectedSize > 0 ? (int)param.Param.DetectedSize : 0;

            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Row filter##" + param.Name, ref _rowFilter, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Field filter##" + param.Name, ref _fieldFilter, 64);
            ImGui.Separator();

            DrawParamTable(param, process, baseAddr, rowSize, refresh);
        }

        private void DrawParamTable(LoadedParam param, GameProcess process, nint baseAddr, int rowSize, bool refresh)
        {
            // ── All rows (cached) ────────────────────────────────────────────────
            if (_cachedAllRows == null)
                _cachedAllRows = new List<PARAM.Row>(param.Param.Rows);
            if (_cachedAllRows.Count == 0) return;

            // ── Filtered rows (cached by _rowFilter text) ───────────────────────
            bool rowFilterChanged = _rowFilter != _lastRowFilter;
            if (rowFilterChanged)
            {
                _lastRowFilter = _rowFilter;
                _cachedFiltered = null;
                _cachedVisible = null;
            }
            _cachedFiltered ??= BuildFilteredRows(_cachedAllRows);

            if (_cachedFiltered.Count == 0) return;

            // ── Pin IDs ─────────────────────────────────────────────────────────
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##pinids", "Pin IDs: 2, 500, 2000", ref _pinIdsText, 256);
            ImGui.SameLine();
            bool pinChanged = _pinIdsText != _lastPinIdsText;
            if (pinChanged)
            {
                _lastPinIdsText = _pinIdsText;
                _cachedPinSet = new HashSet<int>();
                foreach (var part in _pinIdsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out int id))
                        _cachedPinSet.Add(id);
                _frozenIds.Clear();
                foreach (var id in _cachedPinSet) _frozenIds.Add(id);
                _cachedVisible = null;
            }
            ImGui.TextDisabled($"{_cachedPinSet.Count} pinned");

            // ── Build visible rows (pinned + unpinned page, cached) ─────────────
            bool pageChanged = _tablePageOffset != _lastPageOffset;
            _lastPageOffset = _tablePageOffset;

            if (_cachedVisible == null || rowFilterChanged || pinChanged || pageChanged)
                _cachedVisible = BuildVisibleRows(_cachedFiltered);

            var visibleRows = _cachedVisible;
            if (visibleRows == null || visibleRows.Count == 0) return;
            ImGui.Separator();

            // ── Field labels (rebuilt on param change only) ─────────────────────
            var firstCells = visibleRows[0].Cells is List<PARAM.Cell> cl ? cl : visibleRows[0].Cells.ToList();
            if (_fieldLabels == null || _fieldLabels.Count != firstCells.Count)
            {
                _fieldLabels = new List<string>(firstCells.Count);
                for (int i = 0; i < firstCells.Count; i++)
                    _fieldLabels.Add($"{firstCells[i].Def.InternalName} ({TypeLabel(firstCells[i].Def.DisplayType)})");
            }

            // ── Visible field indices (cached by _fieldFilter text) ─────────────
            bool fieldFilterChanged = _fieldFilter != _lastFieldFilter;
            if (fieldFilterChanged)
            {
                _lastFieldFilter = _fieldFilter;
                _cachedVisibleFields = null;
            }
            _cachedVisibleFields ??= BuildVisibleFields(firstCells);

            var visibleFields = _cachedVisibleFields;
            if (visibleFields == null || visibleFields.Count == 0) return;

            // ── Begin table ─────────────────────────────────────────────────────
            int colCount = visibleRows.Count + 1;
            var size = new Vector2(-1, -1);

            if (!ImGui.BeginTable($"Table_{param.Name}_p{_tablePageOffset}", colCount,
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Borders |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings, size))
                return;

            int freezeCols = Math.Min(_cachedPinSet.Count > 0 ? 1 + _cachedPinSet.Count : 1, colCount - 1);
            if (freezeCols < 1) freezeCols = 1;
            ImGui.TableSetupScrollFreeze(freezeCols, 1);
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 160);
            for (int i = 0; i < visibleRows.Count; i++)
            {
                int id = visibleRows[i].ID;
                if (!_colHeaderCache.TryGetValue(id, out var colLabel))
                    _colHeaderCache[id] = colLabel = $"[{id}]";
                ImGui.TableSetupColumn(colLabel, ImGuiTableColumnFlags.WidthFixed, 120);
            }
            ImGui.TableHeadersRow();

            // ── Bulk-read visible rows ──────────────────────────────────────────
            if (refresh && rowSize > 0 && visibleRows.Count > 0)
            {
                long minOff = long.MaxValue, maxOff = long.MinValue;
                for (int ri = 0; ri < visibleRows.Count; ri++)
                {
                    long o = visibleRows[ri].DataOffset;
                    if (o < minOff) minOff = o;
                    if (o > maxOff) maxOff = o;
                }
                int totalLen = (int)(maxOff - minOff) + rowSize;
                nint bulkAddr = baseAddr + (nint)minOff;
                byte[]? bulk = process.ReadBytes(bulkAddr, totalLen);
                if (bulk != null)
                {
                    for (int ri = 0; ri < visibleRows.Count; ri++)
                    {
                        var row = visibleRows[ri];
                        int off = (int)(row.DataOffset - minOff);
                        if (off + rowSize <= totalLen)
                        {
                            byte[] rowData = new byte[rowSize];
                            Buffer.BlockCopy(bulk, off, rowData, 0, rowSize);
                            _rowCache[row.ID] = rowData;
                        }
                    }
                }
            }

            // ── Render rows with clipper ────────────────────────────────────────
            unsafe
            {
                ImGuiListClipper clipper;
                var clipperPtr = new ImGuiListClipperPtr(&clipper);
                clipperPtr.Begin(visibleFields.Count);
                while (clipperPtr.Step())
                {
                    for (int vi = clipperPtr.DisplayStart; vi < clipperPtr.DisplayEnd; vi++)
                    {
                        var (ci, off, fsize) = visibleFields[vi];
                        var firstCell = firstCells[ci];

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.Text(_fieldLabels![ci]);

                        for (int ri = 0; ri < visibleRows.Count; ri++)
                        {
                            if (!ImGui.TableSetColumnIndex(ri + 1)) continue;
                            var row = visibleRows[ri];

                            _rowCache.TryGetValue(row.ID, out byte[]? rowBytes);
                            if (refresh || rowBytes == null)
                            {
                                int readLen = rowSize > 0 ? rowSize : EstimateRowSize(row);
                                if (readLen > 0)
                                {
                                    nint rowAddr = baseAddr + (nint)row.DataOffset;
                                    var fresh = process.ReadBytes(rowAddr, readLen);
                                    if (fresh != null) { _rowCache[row.ID] = fresh; rowBytes = fresh; }
                                }
                            }
                            if (rowBytes == null) continue;

                            if (!_rowCellsCache.TryGetValue(row.ID, out var rowCells))
                                _rowCellsCache[row.ID] = rowCells = row.Cells is List<PARAM.Cell> rcl ? rcl : row.Cells.ToList();
                            if (ci >= rowCells.Count) continue;
                            var cell = rowCells[ci];

                            double val = ReadFromCache(rowBytes, off, cell);
                            nint fieldAddr = baseAddr + (nint)row.DataOffset + off;
                            var type = firstCell.Def.DisplayType;
                            if (!_cellIdCache.TryGetValue((ci, row.ID), out var id))
                                _cellIdCache[(ci, row.ID)] = id = $"##t{cell.Def.InternalName}_{row.ID}";

                            ImGui.SetNextItemWidth(-1);
                            double minVal = Convert.ToDouble(firstCell.Def.Minimum);
                            double maxVal = Convert.ToDouble(firstCell.Def.Maximum);
                            switch (type)
                            {
                                case PARAMDEF.DefType.f32:
                                case PARAMDEF.DefType.angle32:
                                {
                                    float fv = (float)val;
                                    if (ImGui.DragFloat(id, ref fv, 0.1f, (float)minVal, (float)maxVal, "%.3f"))
                                    {
                                        process.WriteField(fieldAddr, type, fv);
                                        cell.Value = fv;
                                        WriteToCache(rowBytes, off, type, fv);
                                    }
                                    break;
                                }
                                case PARAMDEF.DefType.f64:
                                {
                                    float fv = (float)val;
                                    if (ImGui.DragFloat(id, ref fv, 0.1f, (float)minVal, (float)maxVal, "%.3f"))
                                    {
                                    double dv = fv;
                                    process.WriteField(fieldAddr, type, dv);
                                    cell.Value = dv;
                                    WriteToCache(rowBytes, off, type, dv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.s32:
                            case PARAMDEF.DefType.b32:
                            {
                                int iv = (int)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    process.WriteField(fieldAddr, type, iv);
                                    cell.Value = iv;
                                    WriteToCache(rowBytes, off, type, iv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.u32:
                            {
                                int iv = (int)(uint)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    uint uv = (uint)Math.Max(0, iv);
                                    process.WriteField(fieldAddr, type, uv);
                                    cell.Value = (int)uv;
                                    WriteToCache(rowBytes, off, type, uv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.s16:
                            {
                                int iv = (int)(short)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    short sv = (short)Math.Clamp(iv, short.MinValue, short.MaxValue);
                                    process.WriteField(fieldAddr, type, sv);
                                    cell.Value = sv;
                                    WriteToCache(rowBytes, off, type, sv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.u16:
                            {
                                int iv = (int)(ushort)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    ushort uv = (ushort)Math.Clamp(iv, 0, ushort.MaxValue);
                                    process.WriteField(fieldAddr, type, uv);
                                    cell.Value = uv;
                                    WriteToCache(rowBytes, off, type, uv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.s8:
                            {
                                int iv = (int)(sbyte)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    sbyte sv = (sbyte)Math.Clamp(iv, sbyte.MinValue, sbyte.MaxValue);
                                    process.WriteField(fieldAddr, type, sv);
                                    cell.Value = sv;
                                    WriteToCache(rowBytes, off, type, sv);
                                }
                                break;
                            }
                            case PARAMDEF.DefType.u8:
                            {
                                int iv = (int)(byte)val;
                                if (ImGui.DragInt(id, ref iv, 1, (int)minVal, (int)maxVal))
                                {
                                    byte bv = (byte)Math.Clamp(iv, 0, byte.MaxValue);
                                    process.WriteField(fieldAddr, type, bv);
                                    cell.Value = bv;
                                    WriteToCache(rowBytes, off, type, bv);
                                }
                                break;
                            }
                            default:
                                ImGui.TextUnformatted($"{val:G3}");
                                break;
                        }
                    }
                }
            }
            clipperPtr.End();
        }
            ImGui.EndTable();
        }

        // ── Build helpers (deferred, cached across frames) ──────────────────────

        private List<PARAM.Row> BuildFilteredRows(List<PARAM.Row> allRows)
        {
            if (string.IsNullOrEmpty(_rowFilter))
                return new List<PARAM.Row>(allRows);

            var result = new List<PARAM.Row>(allRows.Count);
            foreach (var row in allRows)
            {
                var key = $"[{row.ID}] {row.Name}";
                if (key.Contains(_rowFilter, StringComparison.OrdinalIgnoreCase))
                    result.Add(row);
            }
            return result;
        }

        private List<PARAM.Row> BuildVisibleRows(List<PARAM.Row> filtered)
        {
            var pinned   = new List<PARAM.Row>();
            var unpinned = new List<PARAM.Row>();

            if (_cachedPinSet.Count > 0)
            {
                foreach (var r in filtered)
                {
                    if (_cachedPinSet.Contains(r.ID))
                        pinned.Add(r);
                    else
                        unpinned.Add(r);
                }
            }
            else
            {
                unpinned = filtered;
            }

            if (unpinned.Count > MAX_COLS)
            {
                int totalPages = (unpinned.Count + MAX_COLS - 1) / MAX_COLS;
                if (_tablePageOffset >= unpinned.Count)
                    _tablePageOffset = 0;

                int curPage = _tablePageOffset / MAX_COLS;

                if (ImGui.ArrowButton("##prev", ImGuiDir.Left) && curPage > 0)
                    _tablePageOffset -= MAX_COLS;
                ImGui.SameLine();
                ImGui.Text($"Page {curPage + 1}/{totalPages} ({unpinned.Count} unpinned)");
                ImGui.SameLine();
                if (ImGui.ArrowButton("##next", ImGuiDir.Right) && curPage < totalPages - 1)
                    _tablePageOffset += MAX_COLS;
                ImGui.SameLine();

                int skip = _tablePageOffset;
                int take = Math.Min(MAX_COLS, unpinned.Count - skip);
                if (take > 0)
                    unpinned = unpinned.GetRange(skip, take);
                else
                    unpinned.Clear();
            }
            else
            {
                _tablePageOffset = 0;
            }

            var result = new List<PARAM.Row>(pinned.Count + unpinned.Count);
            result.AddRange(pinned);
            result.AddRange(unpinned);
            return result;
        }

        private List<(int ci, int off, int fsize)> BuildVisibleFields(List<PARAM.Cell> firstCells)
        {
            var result = new List<(int ci, int off, int fsize)>();
            int fieldOffset = 0;
            bool hasFilter = !string.IsNullOrEmpty(_fieldFilter);
            for (int ci = 0; ci < firstCells.Count; ci++)
            {
                var fc = firstCells[ci];
                int fsize = FieldSize(fc.Def.DisplayType, fc.Def.ArrayLength);
                if (fc.Def.DisplayType != PARAMDEF.DefType.dummy8 &&
                    (!hasFilter ||
                     fc.Def.InternalName.Contains(_fieldFilter, StringComparison.OrdinalIgnoreCase)))
                    result.Add((ci, fieldOffset, fsize));
                fieldOffset += fsize;
            }
            return result;
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

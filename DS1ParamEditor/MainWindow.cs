using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ImGuiNET;
using SoulsFormats;
using Veldrid;
using Vortice.Win32;

namespace DS1ParamEditor
{
    public class MainWindow
    {
        private AppConfig config = new();
        private ParamReader? paramReader = new();
        private AOBReader? aobReader = new();

        private Dictionary<string, nint> addresses = [];
        private List<ParamContainer>? paramContainers = [];

        private ImGuiChildFlags _childFlags = ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeX & ImGuiChildFlags.AutoResizeY & ImGuiChildFlags.AlwaysAutoResize;
        private Vector2 _controlSize = new(500, 150);
        private Vector2 _tableSize = new();
        private Vector2 _tableSizeMin = Vector2.Zero;
        private Vector2 _tableSizeMax = new(900, 900);

        //Selection of game path


        //Selection of file
        private string[] paramNameList = [];
        private bool showParamFileSelector = false;
        private int selectedParamFileKey = -1;
        private string selectedParamFileName = string.Empty;

        //Selection of param
        private bool editDrawParams = true;
        private string[] paramFileNameList = [];
        private bool showParamSelector = false;
        private int selectedParamKey = -1;
        private string selectedParamName = string.Empty;

        //Hook
        private bool showHookButton = false;
        private int patternSize = 100;
        private bool autoPattern = false;

        //bool showSaveButton = false;

        //Table
        private bool showParamTable = false;

        public MainWindow()
        {
            config.TryToGetGamePath();
        }

        public void BuildWindow()
        {
            if (ImGui.BeginMenuBar())
            {
                if (showParamTable & editDrawParams)
                {
                    if (ImGui.Button("Save params"))
                    {
                        SaveParams();
                    }
                }
                ImGui.EndMenuBar();
            }

            ImGui.BeginChild("Control", _controlSize, _childFlags);
            {
                if (ImGui.Button("Set exe path"))
                {
                    config.ShowFileDialog();
                }
                ImGui.SameLine();
                if (!string.IsNullOrEmpty(config.SelectedExe))
                {
                    ImGui.Text(config.SelectedExe);
                }
                else
                {
                    ImGui.Text("Please select exe");
                }

                if (ImGui.Button("Read params files"))
                {
                    GetParams();
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Use draw params", editDrawParams))
                {
                    editDrawParams = !editDrawParams;
                }

                if (ImGui.Button("Show addresses"))
                {
                    foreach (var address in addresses)
                    {
                        Console.WriteLine(address);
                    }
                }

                ImGui.SameLine();

                if (ImGui.Button("Reset addresses"))
                {
                    addresses = [];
                    showParamTable = false;
                }

                if (showParamFileSelector)
                {
                    if (ImGui.Combo("Choose file", ref selectedParamFileKey, paramFileNameList,
                        paramFileNameList.Length) && selectedParamFileKey != -1)
                    {
                        showParamSelector = showHookButton = showParamTable = false;
                        selectedParamKey = -1;
                        selectedParamName = string.Empty;
                        selectedParamFileName = paramFileNameList[selectedParamFileKey];
                        paramNameList = paramContainers[selectedParamFileKey].Bnd3File.Files.Select(p => Path.GetFileNameWithoutExtension(p.Name.Split('\\').Last())).ToArray();
                        showParamSelector = true;
                    }
                }

                if (showParamSelector)
                {
                    if (ImGui.Combo("Choose parameter", ref selectedParamKey, paramNameList, paramNameList.Length) &&
                        selectedParamKey != -1)
                    {
                        showHookButton = showParamTable = false;
                        selectedParamName = paramNameList[selectedParamKey];
                        showHookButton = true;
                    }
                }

                if (showHookButton)
                {
                    if (ImGui.Button("Hook"))
                    {
                        GetAddress();
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputInt("Pattern size", ref patternSize);
                    ImGui.SameLine();

                    if (ImGui.RadioButton("Use full pattern", autoPattern))
                    {
                        autoPattern = !autoPattern;
                    }

                }
            }
            ImGui.EndChild();

            if (showParamTable)
            {
                if (ImGui.CollapsingHeader("Table"))
                {
                    ImGui.BeginChild("Table child", _tableSize, _childFlags);
                    {
                        ImGui.SetNextWindowSizeConstraints(_tableSizeMin, _tableSizeMax);
                        {
                            BuildTable();
                        }
                    }
                    ImGui.EndChild();
                }
            }
        }

        public void GetParams()
        {
            if (string.IsNullOrEmpty(config.SelectedParamDefPath) || 
                string.IsNullOrEmpty(config.SelectedParamPath) || 
                string.IsNullOrEmpty(config.SelectedDrawParamPath))
                return;

            paramContainers = [];
            paramFileNameList = [];
            showParamFileSelector = showParamSelector = showHookButton = showParamTable = false;
            selectedParamFileKey = selectedParamKey = -1;
            selectedParamFileName = selectedParamName = string.Empty;

            paramReader ??= new ParamReader();

            if (editDrawParams)
                paramContainers = paramReader.ReadParamMass(config.SelectedParamDefPath, config.SelectedDrawParamPath);
            else
                paramContainers = paramReader.ReadParamMass(config.SelectedParamDefPath, config.SelectedParamPath);

            paramFileNameList = paramContainers.Select(p => p.Name).ToArray();
            showParamFileSelector = true;
        }

        public void SaveParams()
        {
            paramReader.WriteParam(paramContainers[selectedParamFileKey]);
        }

        public void GetAddress()
        {
            if (!string.IsNullOrEmpty(config.SelectedExe))
                aobReader = new AOBReader(config.SelectedExe);
            else
                return;

            if (!aobReader.IsProcessValid)
                return;

            if (!string.IsNullOrEmpty(selectedParamName))
            {
                if (!addresses.ContainsKey(selectedParamName))
                {
                    var addr = aobReader.AOBScan(paramContainers[selectedParamFileKey].Bnd3File.Files[selectedParamKey].Bytes, autoPattern, patternSize);

                    if (addr != nint.Zero)
                        addresses[selectedParamName] = addr;
                }

                showParamTable = true;
            }
            else
                showParamTable = false;
        }

        private void BuildTable()
        {
            if (string.IsNullOrEmpty(selectedParamName) || !addresses.TryGetValue(selectedParamName, out var address))
                return;

            if (!aobReader.IsProcessValid)
                return;

            foreach (var param in paramContainers[selectedParamFileKey].ParmsDef[selectedParamName].Rows)
            {
                long offset = param.DataOffset;
                int typeOffset = 0;

                if (!ImGui.CollapsingHeader($"{param.ID} {param.Name}"))
                    continue;

                foreach (var cell in param.Cells)
                {
                    switch (cell.Def.DisplayType)
                    {
                        case PARAMDEF.DefType.s8:
                            HandleSByteField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 1;
                            break;

                        case PARAMDEF.DefType.u8:
                            HandleByteField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 1;
                            break;

                        case PARAMDEF.DefType.s16:
                            HandleShortField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 2;
                            break;

                        case PARAMDEF.DefType.u16:
                            HandleUShortField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 2;
                            break;

                        case PARAMDEF.DefType.s32:
                        case PARAMDEF.DefType.b32:
                            HandleIntField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 4;
                            break;

                        case PARAMDEF.DefType.u32:
                            HandleUIntField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 4;
                            break;

                        case PARAMDEF.DefType.f32:
                        case PARAMDEF.DefType.angle32:
                            HandleFloatField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 4;
                            break;

                        case PARAMDEF.DefType.f64:
                            HandleDoubleField(address, offset, typeOffset, cell, param.ID);
                            typeOffset += 8;
                            break;

                        case PARAMDEF.DefType.dummy8:
                            ImGui.Text($"Pad:{cell.Def.ArrayLength}");
                            typeOffset += cell.Def.ArrayLength;
                            break;
                    }
                }
            }
        }


        private void HandleSByteField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            int cellValue = Convert.ToInt16(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderInt($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToInt32(cell.Def.Minimum), Convert.ToInt32(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, (sbyte)cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToSByte(cellValue);
            }
        }

        private void HandleByteField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            int cellValue = Convert.ToByte(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderInt($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToInt32(cell.Def.Minimum), Convert.ToInt32(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {

                aobReader.WriteMemory(address, offset + typeOffset, (byte)cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToByte(cellValue);
            }
        }

        private void HandleShortField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            int cellValue = Convert.ToInt16(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderInt($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToInt32(cell.Def.Minimum), Convert.ToInt32(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, (short)cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToInt16(cellValue);
            }
        }

        private void HandleUShortField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            int cellValue = Convert.ToUInt16(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderInt($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToInt32(cell.Def.Minimum), Convert.ToInt32(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, (ushort)cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToUInt16(cellValue);
            }
        }

        private void HandleIntField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            int cellValue = Convert.ToInt32(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderInt($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToInt32(cell.Def.Minimum), Convert.ToInt32(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToInt32(cellValue);
            }
        }

        private void HandleUIntField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            float cellValue = Convert.ToSingle(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderFloat($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToSingle(cell.Def.Minimum), Convert.ToSingle(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, Convert.ToUInt32(cellValue), cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToUInt32(cellValue);
            }
        }

        private void HandleFloatField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            float cellValue = (float)aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType);
            ImGui.SliderFloat($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToSingle(cell.Def.Minimum), Convert.ToSingle(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToSingle(cellValue);
            }
        }


        private void HandleDoubleField(nint address, long offset, int typeOffset, PARAM.Cell cell, int paramId)
        {
            float cellValue = Convert.ToSingle(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
            ImGui.SliderFloat($"{cell.Def.InternalName} {cell.Def.DisplayType} ##{paramId}", ref cellValue,
                Convert.ToSingle(cell.Def.Minimum), Convert.ToSingle(cell.Def.Maximum));

            if (ImGui.IsItemActive())
            {
                aobReader.WriteMemory(address, offset + typeOffset, (double)cellValue, cell.Def.DisplayType);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                cell.Value = Convert.ToDouble(cellValue);
            }
        }
    }
}

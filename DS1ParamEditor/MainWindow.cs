using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileParser;
using ImGuiNET;
using SoulsFormats;
using Veldrid;
using Vortice.Win32;

namespace DS1DrawParamEditor
{
    public class MainWindow
    {
        private ParamReader? paramReader;
        private AOBReader? aobReader;

        private Dictionary<string, nint> addresses = [];
        private List<ParamContainer>? paramContainers;

        ImGuiChildFlags _childFlags = ImGuiChildFlags.Borders | (ImGuiChildFlags.AutoResizeX & ImGuiChildFlags.AutoResizeY & ImGuiChildFlags.AlwaysAutoResize);
        Vector2 _controlSize = new(500, 150);
        Vector2 _tableSize;
        Vector2 _tableSizeMin = Vector2.Zero;
        Vector2 _tableSizeMax = new(900, 900);

        //Selection of game path
        private string _selectedExe = string.Empty;
        private string _selectedExePath = string.Empty;
        private string _selectedGamePath = string.Empty;
        private string _selectedParamPath = string.Empty;
        private string _selectedDrawParamPath = string.Empty;
        private string _selectedParamDefPath = string.Empty;

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
        bool showHookButton = false;
        int patternSize = 100;
        bool autoPattern = false;

        //bool showSaveButton = false;

        //Table
        bool showParamTable = false;

        public MainWindow()
        {
            Console.WriteLine("Created by ElsterDePie v.0.4 \nIf nothing works, delete AppSettings.json or imgui.ini :3");
            if (File.Exists("AppSettings.json"))
            {
                AppConfig appconfig = AppConfig.Load("AppSettings.json");
                Console.WriteLine("Read AppSettings.json");
                if (!string.IsNullOrEmpty(appconfig.ExePath))
                {
                    Console.WriteLine($"ExePath: {appconfig.ExePath}");
                    _selectedExePath = appconfig.ExePath;
                    _selectedExe = Path.GetFileNameWithoutExtension(_selectedExePath);
                    _selectedGamePath = _selectedExePath.Substring(0, _selectedExePath.LastIndexOf('\\'));
                    TryToGetParams();
                }
                else
                    ShowFileDialog();
            }
            else
                ShowFileDialog();
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
                    ShowFileDialog();
                }
                ImGui.SameLine();
                if (!string.IsNullOrEmpty(_selectedExe))
                {
                    ImGui.Text(_selectedExe);
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
            if (string.IsNullOrEmpty(_selectedParamDefPath) || string.IsNullOrEmpty(_selectedParamPath) || string.IsNullOrEmpty(_selectedDrawParamPath))
                return;

            paramContainers = [];
            paramFileNameList = [];
            showParamFileSelector = showParamSelector = showHookButton = showParamTable = false;
            selectedParamFileKey = selectedParamKey = -1;
            selectedParamFileName = selectedParamName = string.Empty;

            paramReader ??= new ParamReader();

            if (editDrawParams)
                paramContainers = paramReader.ReadParamMass(_selectedParamDefPath, _selectedDrawParamPath);
            else
                paramContainers = paramReader.ReadParamMass(_selectedParamDefPath, _selectedParamPath);

            paramFileNameList = paramContainers.Select(p => p.Name).ToArray();
            showParamFileSelector = true;
        }

        public void SaveParams()
        {
            paramReader.WriteParam(paramContainers[selectedParamFileKey]);
        }

        public void GetAddress()
        {
            if (!string.IsNullOrEmpty(_selectedExe))
                aobReader = new AOBReader(_selectedExe);
            else
                return;

            if (!aobReader.IsProcessValid)
                return;

            if (!string.IsNullOrEmpty(selectedParamName))
            {
                if (!addresses.ContainsKey(selectedParamName))
                {
                    var addr = aobReader.AOBScan(paramContainers[selectedParamFileKey].Bnd3File.Files[selectedParamKey].Bytes, autoPattern, patternSize);

                    if (addr != IntPtr.Zero)
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
            float cellValue = (float)(aobReader.ReadMemory(address, offset + typeOffset, cell.Def.DisplayType));
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

        private void ShowFileDialog()
        {
            _selectedExe = _selectedExePath = _selectedGamePath = _selectedParamDefPath = _selectedParamPath = _selectedDrawParamPath = string.Empty;

            var thread = new System.Threading.Thread(() =>
            {
                using (var openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                    openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    openFileDialog.Title = "Select an executable file";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        _selectedExePath = openFileDialog.FileName;
                        _selectedExe = Path.GetFileNameWithoutExtension(_selectedExePath);
                        _selectedGamePath = _selectedExePath.Substring(0, _selectedExePath.LastIndexOf('\\'));

                        TryToGetParams();

                        AppConfig appConfig = new()
                        {
                            ExePath = _selectedExePath
                        };
                        appConfig.Save("AppSettings.json");
                    }
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join(); // Wait for the dialog to close
        }

        private void TryToGetParams()
        {
            switch (_selectedExe)
            {
                case ("DARKSOULS"):
                    {
                        if (File.Exists(_selectedGamePath + "\\paramdef\\paramdef.paramdefbnd"))
                            _selectedParamDefPath = _selectedGamePath + "\\paramdef\\paramdef.paramdefbnd";
                        else if (File.Exists(_selectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx"))
                            _selectedParamDefPath = _selectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx";

                        _selectedParamPath = _selectedGamePath + "\\param\\GameParam";
                        _selectedDrawParamPath = _selectedGamePath + "\\param\\DrawParam";
                        break;
                    }
                case ("DarkSoulsRemastered"):
                    {
                        if (File.Exists(_selectedGamePath + "\\paramdef\\paramdef.paramdefbnd"))
                            _selectedParamDefPath = _selectedGamePath + "\\paramdef\\paramdef.paramdefbnd";

                        else if (File.Exists(_selectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx"))
                            _selectedParamDefPath = _selectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx";

                        _selectedParamPath = _selectedGamePath + "\\param\\GameParam";
                        _selectedDrawParamPath = _selectedGamePath + "\\param\\DrawParam";
                        break;
                    }
            }
        }
    }
}

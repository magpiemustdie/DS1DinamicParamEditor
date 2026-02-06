using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using SoulsFormats;

namespace DS1ParamEditor
{
    public class GPARAMEditor
    {
        private AppConfig config = new();
        //private GParamReader? gparamReader = new();
        private AOBReader? aobReader = new();

        private Dictionary<string, nint> addresses = [];
        private List<GParamContainer>? gparamContainers = [];

        private ImGuiChildFlags _childFlags = ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeX & ImGuiChildFlags.AutoResizeY & ImGuiChildFlags.AlwaysAutoResize;
        private Vector2 _controlSize = new(500, 150);
        private Vector2 _tableSize = new();
        private Vector2 _tableSizeMin = Vector2.Zero;
        private Vector2 _tableSizeMax = new(900, 900);

        // UI состояние
        private bool[] groupExpanded = [];
        private Dictionary<int, bool[]> paramExpanded = new();
        private Dictionary<string, string> stringEditBuffers = new();
        private Dictionary<string, object> valueEditBuffers = new();

        // Selection of file
        private string[] gparamNameList = [];
        private bool showGParamFileSelector = false;
        private int selectedGParamFileKey = -1;
        private string selectedGParamFileName = string.Empty;

        // Selection of group
        private string[] groupNameList = [];
        private bool showGroupSelector = false;
        private int selectedGroupKey = -1;
        private string selectedGroupName = string.Empty;

        // Selection of param
        private string[] paramNameList = [];
        private bool showParamSelector = false;
        private int selectedParamKey = -1;
        private string selectedParamName = string.Empty;

        // Hook
        private bool showHookButton = true;
        private int patternSize = 100;
        private bool autoPattern = false;

        // Table
        private bool showParamTable = false;

        public GPARAMEditor()
        {
            config.TryToGetGamePath();
        }

        public void BuildWindow()
        {
            if (ImGui.BeginMenuBar())
            {
                if (showParamTable)
                {
                    if (ImGui.Button("Save GPARAM"))
                    {
                        SaveGParam();
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

                if (ImGui.Button("Load GPARAM file"))
                {
                    LoadGParamFile();
                }

                ImGui.SameLine();

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

                if (showGParamFileSelector)
                {
                    if (ImGui.Combo("Choose GPARAM file", ref selectedGParamFileKey, gparamNameList,
                        gparamNameList.Length) && selectedGParamFileKey != -1)
                    {
                        showGroupSelector = showParamSelector = showParamTable = false;
                        selectedGroupKey = selectedParamKey = -1;
                        selectedGroupName = selectedParamName = string.Empty;
                        selectedGParamFileName = gparamNameList[selectedGParamFileKey];

                        var gparam = gparamContainers[selectedGParamFileKey].Gparam;
                        groupNameList = gparam.Groups.Select(g =>
                            !string.IsNullOrEmpty(g.Name1) ? g.Name1 : $"Group_{gparam.Groups.IndexOf(g)}").ToArray();
                        showGroupSelector = true;
                        showHookButton = true;
                    }
                }

                if (showGroupSelector)
                {
                    if (ImGui.Combo("Choose group", ref selectedGroupKey, groupNameList, groupNameList.Length) &&
                        selectedGroupKey != -1)
                    {
                        showParamSelector = showParamTable = false;
                        selectedParamKey = -1;
                        selectedParamName = string.Empty;
                        selectedGroupName = groupNameList[selectedGroupKey];

                        var group = gparamContainers[selectedGParamFileKey].Gparam.Groups[selectedGroupKey];
                        paramNameList = group.Params.Select(p =>
                            !string.IsNullOrEmpty(p.Name1) ? p.Name1 : $"Param_{group.Params.IndexOf(p)}").ToArray();
                        showParamSelector = true;
                    }
                }

                if (showParamSelector)
                {
                    if (ImGui.Combo("Choose parameter", ref selectedParamKey, paramNameList, paramNameList.Length) &&
                        selectedParamKey != -1)
                    {
                        showParamTable = false;
                        selectedParamName = paramNameList[selectedParamKey];
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
                if (ImGui.CollapsingHeader("GPARAM Editor"))
                {
                    ImGui.BeginChild("GPARAM child", _tableSize, _childFlags);
                    {
                        ImGui.SetNextWindowSizeConstraints(_tableSizeMin, _tableSizeMax);
                        {
                            BuildGParamTable();
                        }
                    }
                    ImGui.EndChild();
                }
            }
        }

        public void LoadGParamFile()
        {
            var thread = new Thread(() =>
            {
                using (var fileDialog = new OpenFileDialog())
                {
                fileDialog.Title = "Open GPARAM file";
                fileDialog.Filter = "GPARAM files (*.gparam)|*.gparam|All files (*.*)|*.*";
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    var filePath = fileDialog.FileName;
                    LoadGParam(filePath);
                }
            }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private void LoadGParam(string filePath)
        {
            try
            {
                var gparam = GPARAM.Read(filePath);
                var container = new GParamContainer
                {
                    Name = Path.GetFileName(filePath),
                    FilePath = filePath,
                    Gparam = gparam
                };

                gparamContainers = new List<GParamContainer> { container };
                gparamNameList = new[] { container.Name };

                showGParamFileSelector = true;
                selectedGParamFileKey = 0;
                selectedGParamFileName = container.Name;

                // Initialize UI state
                groupExpanded = new bool[gparam.Groups.Count];
                paramExpanded.Clear();
                for (int i = 0; i < gparam.Groups.Count; i++)
                {
                    paramExpanded[i] = new bool[gparam.Groups[i].Params.Count];
                }

                // Initialize group name list
                groupNameList = gparam.Groups.Select(g =>
                    !string.IsNullOrEmpty(g.Name1) ? g.Name1 : $"Group_{gparam.Groups.IndexOf(g)}").ToArray();
                showGroupSelector = true;

                Console.WriteLine($"Loaded GPARAM: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading GPARAM file: {ex.Message}");
            }
        }

        public void SaveGParam()
        {
            if (gparamContainers == null || selectedGParamFileKey < 0)
                return;

            var container = gparamContainers[selectedGParamFileKey];
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Title = "Save GPARAM file";
                saveDialog.Filter = "GPARAM files (*.gparam)|*.gparam|All files (*.*)|*.*";
                saveDialog.FileName = container.Name;

                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        container.Gparam.Write(saveDialog.FileName);
                        Console.WriteLine($"Saved GPARAM to: {saveDialog.FileName}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving GPARAM file: {ex.Message}");
                    }
                }
            }
        }

        public void GetAddress()
        {
            if (!string.IsNullOrEmpty(config.SelectedExe))
                aobReader = new AOBReader(config.SelectedExe);
            else
                return;

            if (!aobReader.IsProcessValid)
                return;

            if (!string.IsNullOrEmpty(selectedGParamFileName))
            {
                if (!addresses.ContainsKey(selectedGParamFileName))
                {
                    // Convert GPARAM to bytes for AOB scan
                    var gparam = gparamContainers[selectedGParamFileKey].Gparam;

                    var bytes = gparam.Write(compression: DCX.Type.None);

                    var addr = aobReader.AOBScan(bytes, autoPattern, patternSize);

                    if (addr != nint.Zero)
                        addresses[selectedParamName] = addr;
                }

                showParamTable = true;
            }
            else
                showParamTable = false;

        }

        private void BuildGParamTable()
        {
            if (gparamContainers == null || selectedGParamFileKey < 0)
                return;

            var gparam = gparamContainers[selectedGParamFileKey].Gparam;

            // File info
            ImGui.Text($"File: {Path.GetFileName(gparamContainers[selectedGParamFileKey].FilePath)}");
            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                SaveGParam();
            }

            ImGui.Separator();

            // Groups
            for (int i = 0; i < gparam.Groups.Count; i++)
            {
                var group = gparam.Groups[i];
                string groupLabel = !string.IsNullOrEmpty(group.Name1) ? group.Name1 : $"Group {i}";
                if (!string.IsNullOrEmpty(group.Name2))
                    groupLabel += $" / {group.Name2}";

                groupExpanded[i] = ImGui.TreeNodeEx(groupLabel,
                    groupExpanded[i] ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

                if (groupExpanded[i])
                {
                    // Group editing
                    EditGroup(group, i);

                    // Parameters in group
                    if (group.Params != null)
                    {
                        for (int j = 0; j < group.Params.Count; j++)
                        {
                            DrawParam(group.Params[j], i, j);
                        }
                    }

                    ImGui.TreePop();
                }
            }
        }

        private void EditGroup(GPARAM.Group group, int groupIndex)
        {
            ImGui.Text("Name1:");
            ImGui.SameLine();
            string name1Key = $"group_{groupIndex}_name1";
            if (!stringEditBuffers.ContainsKey(name1Key))
                stringEditBuffers[name1Key] = group.Name1 ?? "";

            ImGui.Text($"##{name1Key}");

            ImGui.Text("Name2:");
            ImGui.SameLine();
            string name2Key = $"group_{groupIndex}_name2";
            if (!stringEditBuffers.ContainsKey(name2Key))
                stringEditBuffers[name2Key] = group.Name2 ?? "";

            ImGui.Text($"##{name2Key}");
        }

        private void DrawParam(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            string paramLabel = !string.IsNullOrEmpty(param.Name1) ? param.Name1 : $"Param {paramIndex}";
            string label = $"{paramLabel} (Type: {param.Type})";

            if (!string.IsNullOrEmpty(param.Name2))
                label += $" / {param.Name2}";

            if (!paramExpanded.ContainsKey(groupIndex))
                paramExpanded[groupIndex] = new bool[gparamContainers[selectedGParamFileKey].Gparam.Groups[groupIndex].Params.Count];

            paramExpanded[groupIndex][paramIndex] = ImGui.TreeNodeEx(label,
                paramExpanded[groupIndex][paramIndex] ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);

            if (paramExpanded[groupIndex][paramIndex])
            {
                // Edit parameter names
                EditParamNames(param, groupIndex, paramIndex);

                ImGui.Text($"Type: {param.Type}");
                ImGui.Text($"Value Count: {param.Values.Count}");

                // Display and edit values based on type
                switch (param.Type)
                {
                    case GPARAM.ParamType.Byte:
                        DrawEditableByteValues(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Short:
                        DrawEditableShortValues(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.IntA:
                    case GPARAM.ParamType.IntB:
                        DrawEditableIntValues(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.BoolA:
                    case GPARAM.ParamType.BoolB:
                        DrawEditableBoolValues(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Float:
                        DrawEditableFloatValues(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Float2:
                        DrawEditableFloat2Values(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Float3:
                        DrawEditableFloat3Values(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Float4:
                        DrawEditableFloat4Values(param, groupIndex, paramIndex);
                        break;
                    case GPARAM.ParamType.Byte4:
                        DrawEditableByte4Values(param, groupIndex, paramIndex);
                        break;
                    default:
                        ImGui.Text($"Unsupported type: {param.Type}");
                        break;
                }

                ImGui.TreePop();
            }
        }

        private void EditParamNames(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            ImGui.Text("Name1:");
            ImGui.SameLine();
            string name1Key = $"param_{groupIndex}_{paramIndex}_name1";
            if (!stringEditBuffers.ContainsKey(name1Key))
                stringEditBuffers[name1Key] = param.Name1 ?? "";

            ImGui.Text($"##{name1Key}");

            ImGui.Text("Name2:");
            ImGui.SameLine();
            string name2Key = $"param_{groupIndex}_{paramIndex}_name2";
            if (!stringEditBuffers.ContainsKey(name2Key))
                stringEditBuffers[name2Key] = param.Name2 ?? "";

            ImGui.Text($"##{name2Key}");
        }

        private void DrawEditableByteValues(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"ByteValues_{groupIndex}_{paramIndex}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    byte value = (byte)param.Values[i];
                    int intValue = value;
                    if (ImGui.InputInt($"##byte_{groupIndex}_{paramIndex}_{i}", ref intValue, 1, 10))
                    {
                        if (intValue >= byte.MinValue && intValue <= byte.MaxValue)
                        {
                            param.Values[i] = (byte)intValue;
                        }
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##byte_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Byte Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add((byte)0);
            }
        }

        private void DrawEditableShortValues(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"ShortValues_{groupIndex}_{paramIndex}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    short value = (short)param.Values[i];
                    int intValue = value;
                    if (ImGui.InputInt($"##short_{groupIndex}_{paramIndex}_{i}", ref intValue, 1, 10))
                    {
                        if (intValue >= short.MinValue && intValue <= short.MaxValue)
                        {
                            param.Values[i] = (short)intValue;
                        }
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##short_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Short Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add((short)0);
            }
        }

        private void DrawEditableIntValues(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"IntValues_{groupIndex}_{paramIndex}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    int value = (int)param.Values[i];
                    if (ImGui.InputInt($"##int_{groupIndex}_{paramIndex}_{i}", ref value))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##int_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Int Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(0);
            }
        }

        private void DrawEditableBoolValues(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"BoolValues_{groupIndex}_{paramIndex}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    bool value = (bool)param.Values[i];
                    if (ImGui.Checkbox($"##bool_{groupIndex}_{paramIndex}_{i}", ref value))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##bool_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Bool Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(false);
            }
        }

        private void DrawEditableFloatValues(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"FloatValues_{groupIndex}_{paramIndex}", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    float value = (float)param.Values[i];
                    if (ImGui.InputFloat($"##float_{groupIndex}_{paramIndex}_{i}", ref value))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##float_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Float Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(0f);
            }
        }

        private void DrawEditableFloat2Values(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"Float2Values_{groupIndex}_{paramIndex}", 4,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    Vector2 value = (Vector2)param.Values[i];
                    if (ImGui.InputFloat($"##float2_x_{groupIndex}_{paramIndex}_{i}", ref value.X))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float2_y_{groupIndex}_{paramIndex}_{i}", ref value.Y))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##float2_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Float2 Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(Vector2.Zero);
            }
        }

        private void DrawEditableFloat3Values(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"Float3Values_{groupIndex}_{paramIndex}", 5,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed, 70);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    Vector3 value = (Vector3)param.Values[i];
                    if (ImGui.InputFloat($"##float3_x_{groupIndex}_{paramIndex}_{i}", ref value.X))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float3_y_{groupIndex}_{paramIndex}_{i}", ref value.Y))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float3_z_{groupIndex}_{paramIndex}_{i}", ref value.Z))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##float3_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Float3 Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(Vector3.Zero);
            }
        }

        private void DrawEditableFloat4Values(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"Float4Values_{groupIndex}_{paramIndex}", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("W", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    ImGui.TableNextColumn();
                    Vector4 value = (Vector4)param.Values[i];
                    if (ImGui.InputFloat($"##float4_x_{groupIndex}_{paramIndex}_{i}", ref value.X))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float4_y_{groupIndex}_{paramIndex}_{i}", ref value.Y))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float4_z_{groupIndex}_{paramIndex}_{i}", ref value.Z))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.InputFloat($"##float4_w_{groupIndex}_{paramIndex}_{i}", ref value.W))
                    {
                        param.Values[i] = value;
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##float4_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Float4 Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(Vector4.Zero);
            }
        }

        private void DrawEditableByte4Values(GPARAM.Param param, int groupIndex, int paramIndex)
        {
            if (ImGui.BeginTable($"Byte4Values_{groupIndex}_{paramIndex}", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("B0", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("B1", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("B2", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("B3", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                for (int i = 0; i < param.Values.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(i.ToString());

                    byte[] bytes = (byte[])param.Values[i];
                    if (bytes.Length != 4)
                    {
                        Array.Resize(ref bytes, 4);
                        param.Values[i] = bytes;
                    }

                    string bufferKey = $"byte4_{groupIndex}_{paramIndex}_{i}";
                    if (!valueEditBuffers.ContainsKey(bufferKey))
                    {
                        valueEditBuffers[bufferKey] = new byte[4];
                        Array.Copy(bytes, (byte[])valueEditBuffers[bufferKey], 4);
                    }

                    byte[] editBuffer = (byte[])valueEditBuffers[bufferKey];

                    for (int j = 0; j < 4; j++)
                    {
                        ImGui.TableNextColumn();
                        int byteValue = editBuffer[j];
                        if (ImGui.InputInt($"##byte4_{groupIndex}_{paramIndex}_{i}_{j}", ref byteValue, 1, 10))
                        {
                            if (byteValue >= byte.MinValue && byteValue <= byte.MaxValue)
                            {
                                editBuffer[j] = (byte)byteValue;
                                param.Values[i] = editBuffer.ToArray();
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##byte4_{groupIndex}_{paramIndex}_{i}"))
                    {
                        param.Values.RemoveAt(i);
                        valueEditBuffers.Remove(bufferKey);
                        i--;
                    }
                }

                ImGui.EndTable();
            }

            if (ImGui.Button($"Add Byte4 Value##{groupIndex}_{paramIndex}"))
            {
                param.Values.Add(new byte[4]);
            }
        }
    }

    public class GParamContainer
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public GPARAM Gparam { get; set; } = new GPARAM();
    }
}
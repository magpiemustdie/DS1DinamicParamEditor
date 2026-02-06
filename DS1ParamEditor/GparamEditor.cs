using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using ImGuiNET;
using SoulsFormats;

public class GParamReader
{
    private GPARAM gparam;
    private string currentFilePath;

    // UI состояние
    private bool showGroups = true;
    private bool showParams = true;
    private bool showUnk3s = true;
    private Dictionary<string, bool> groupExpanded = new Dictionary<string, bool>();
    private Dictionary<int, bool> paramExpanded = new Dictionary<int, bool>();

    // Для редактирования строк
    private Dictionary<string, string> stringEditBuffers = new Dictionary<string, string>();
    private Dictionary<string, byte[]> byteArrayEditBuffers = new Dictionary<string, byte[]>();

    public void LoadFile(string filePath)
    {
        try
        {
            gparam = GPARAM.Read(filePath);
            currentFilePath = filePath;
            groupExpanded.Clear();
            paramExpanded.Clear();
            stringEditBuffers.Clear();
            byteArrayEditBuffers.Clear();

            Console.WriteLine($"Loaded GPARAM: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading file: {ex.Message}");
            gparam = null;
        }
    }

    public void DrawUI()
    {
        if (gparam == null)
        {
            ImGui.Text("No GPARAM file loaded");
            if (ImGui.Button("Load GPARAM File"))
            {
                // Реализация диалога выбора файла
                var filePath = ShowOpenFileDialog();
                /*
                if (!string.IsNullOrEmpty(filePath))
                {
                    LoadFile(filePath);
                }
                */
            }
            return;
        }
        else
        {
            if (ImGui.Button("Load GPARAM File"))
            {
                // Реализация диалога выбора файла
                var filePath = ShowOpenFileDialog();
                /*
                if (!string.IsNullOrEmpty(filePath))
                {
                    LoadFile(filePath);
                }
                */
            }
        }

        ImGui.Text($"File: {Path.GetFileName(currentFilePath)}");

        // Кнопки сохранения
        ImGui.SameLine();
        if (ImGui.Button("Save"))
        {
            SaveFile();
        }

        ImGui.SameLine();
        if (ImGui.Button("Save As"))
        {
            var filePath = ShowSaveFileDialog();
            if (!string.IsNullOrEmpty(filePath))
            {
                SaveFile(filePath);
            }
        }

        ImGui.Separator();

        ImGui.BeginChild("GPARAM_Content");

        // Группы
        if (ImGui.CollapsingHeader($"Groups ({gparam.Groups.Count})", ref showGroups, ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < gparam.Groups.Count; i++)
            {
                var group = gparam.Groups[i];
                string groupKey = $"Group_{i}";

                if (!groupExpanded.ContainsKey(groupKey))
                    groupExpanded[groupKey] = false;

                string groupLabel = !string.IsNullOrEmpty(group.Name1) ? group.Name1 : $"Group {i}";
                if (!string.IsNullOrEmpty(group.Name2))
                    groupLabel += $" / {group.Name2}";

                bool expanded = ImGui.TreeNodeEx(groupLabel,
                    groupExpanded[groupKey] ? ImGuiTreeNodeFlags.DefaultOpen : 0);
                groupExpanded[groupKey] = expanded;

                if (expanded)
                {
                    // Редактирование имени группы
                    ImGui.Text("Name1:");
                    ImGui.SameLine();
                    string name1Key = $"group_{i}_name1";
                    if (!stringEditBuffers.ContainsKey(name1Key))
                        stringEditBuffers[name1Key] = group.Name1 ?? "";
                    /*
                    if (ImGui.InputText($"##{name1Key}", ref stringEditBuffers[name1Key], 256))
                    {
                        group.Name1 = stringEditBuffers[name1Key];
                    }
                    */

                    ImGui.Text("Name2:");
                    ImGui.SameLine();
                    string name2Key = $"group_{i}_name2";
                    if (!stringEditBuffers.ContainsKey(name2Key))
                        stringEditBuffers[name2Key] = group.Name2 ?? "";

                    /*
                    if (ImGui.InputText($"##{name2Key}", ref stringEditBuffers[name2Key], 256))
                    {
                        group.Name2 = stringEditBuffers[name2Key];
                    }
                    */

                    // Параметры в группе
                    if (group.Params != null && ImGui.TreeNode($"Parameters ({group.Params.Count})"))
                    {
                        for (int j = 0; j < group.Params.Count; j++)
                        {
                            DrawParam(group.Params[j], j);
                        }
                        ImGui.TreePop();
                    }

                    ImGui.TreePop();
                }
            }
        }

        /*
        // Unk3 (если есть)
        if (gparam.Unk3 != null && ImGui.CollapsingHeader($"Unk3 ({gparam.Unk3.Count})", ref showUnk3s))
        {
            for (int i = 0; i < gparam.Unk3.Count; i++)
            {
                ImGui.Text($"Unk3[{i}]: {gparam.Unk3[i]}");
            }
        }*/

        ImGui.EndChild();
    }

    private void DrawParam(GPARAM.Param param, int index)
    {
        string paramLabel = !string.IsNullOrEmpty(param.Name1) ? param.Name1 : $"Param {index}";
        string label = $"{paramLabel} (Type: {param.Type})";

        if (!string.IsNullOrEmpty(param.Name2))
            label += $" / {param.Name2}";

        if (!paramExpanded.ContainsKey(index))
            paramExpanded[index] = false;

        bool expanded = ImGui.TreeNodeEx(label,
            paramExpanded[index] ? ImGuiTreeNodeFlags.DefaultOpen : 0);
        paramExpanded[index] = expanded;

        if (expanded)
        {
            // Редактирование имени параметра
            ImGui.Text("Name1:");
            ImGui.SameLine();
            string name1Key = $"param_{index}_name1";
            if (!stringEditBuffers.ContainsKey(name1Key))
                stringEditBuffers[name1Key] = param.Name1 ?? "";

            /*
            if (ImGui.InputText($"##{name1Key}", ref stringEditBuffers[name1Key], 256))
            {
                param.Name1 = stringEditBuffers[name1Key];
            }
            */

            ImGui.Text("Name2:");
            ImGui.SameLine();
            string name2Key = $"param_{index}_name2";
            if (!stringEditBuffers.ContainsKey(name2Key))
                stringEditBuffers[name2Key] = param.Name2 ?? "";

            /*
            if (ImGui.InputText($"##{name2Key}", ref stringEditBuffers[name2Key], 256))
            {
                param.Name2 = stringEditBuffers[name2Key];
            }
            */

            ImGui.Text($"Type: {param.Type}");
            ImGui.Text($"Value Count: {param.Values.Count}");

            // Отображение и редактирование значений в зависимости от типа
            switch (param.Type)
            {
                case GPARAM.ParamType.Byte:
                    DrawEditableByteValues(param, index);
                    break;
                case GPARAM.ParamType.Short:
                    DrawEditableShortValues(param, index);
                    break;
                case GPARAM.ParamType.IntA:
                case GPARAM.ParamType.IntB:
                    DrawEditableIntValues(param, index);
                    break;
                case GPARAM.ParamType.BoolA:
                case GPARAM.ParamType.BoolB:
                    DrawEditableBoolValues(param, index);
                    break;
                case GPARAM.ParamType.Float:
                    DrawEditableFloatValues(param, index);
                    break;
                case GPARAM.ParamType.Float2:
                    DrawEditableFloat2Values(param, index);
                    break;
                case GPARAM.ParamType.Float3:
                    DrawEditableFloat3Values(param, index);
                    break;
                case GPARAM.ParamType.Float4:
                    DrawEditableFloat4Values(param, index);
                    break;
                case GPARAM.ParamType.Byte4:
                    DrawEditableByte4Values(param, index);
                    break;
                default:
                    ImGui.Text($"Unsupported type: {param.Type}");
                    break;
            }

            ImGui.TreePop();
        }
    }

    private void DrawEditableByteValues(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"ByteValues_{paramIndex}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputInt($"##byte_{paramIndex}_{i}", ref intValue, 1, 10))
                {
                    if (intValue >= byte.MinValue && intValue <= byte.MaxValue)
                    {
                        param.Values[i] = (byte)intValue;
                    }
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##byte_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Byte Value##{paramIndex}"))
        {
            param.Values.Add((byte)0);
        }
    }

    private void DrawEditableShortValues(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"ShortValues_{paramIndex}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputInt($"##short_{paramIndex}_{i}", ref intValue, 1, 10))
                {
                    if (intValue >= short.MinValue && intValue <= short.MaxValue)
                    {
                        param.Values[i] = (short)intValue;
                    }
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##short_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Short Value##{paramIndex}"))
        {
            param.Values.Add((short)0);
        }
    }

    private void DrawEditableIntValues(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"IntValues_{paramIndex}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputInt($"##int_{paramIndex}_{i}", ref value))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##int_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Int Value##{paramIndex}"))
        {
            param.Values.Add(0);
        }
    }

    private void DrawEditableBoolValues(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"BoolValues_{paramIndex}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.Checkbox($"##bool_{paramIndex}_{i}", ref value))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##bool_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Bool Value##{paramIndex}"))
        {
            param.Values.Add(false);
        }
    }

    private void DrawEditableFloatValues(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"FloatValues_{paramIndex}", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputFloat($"##float_{paramIndex}_{i}", ref value))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##float_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Float Value##{paramIndex}"))
        {
            param.Values.Add(0f);
        }
    }

    private void DrawEditableFloat2Values(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"Float2Values_{paramIndex}", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputFloat($"##float2_x_{paramIndex}_{i}", ref value.X))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float2_y_{paramIndex}_{i}", ref value.Y))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##float2_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Float2 Value##{paramIndex}"))
        {
            param.Values.Add(Vector2.Zero);
        }
    }

    private void DrawEditableFloat3Values(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"Float3Values_{paramIndex}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputFloat($"##float3_x_{paramIndex}_{i}", ref value.X))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float3_y_{paramIndex}_{i}", ref value.Y))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float3_z_{paramIndex}_{i}", ref value.Z))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##float3_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Float3 Value##{paramIndex}"))
        {
            param.Values.Add(Vector3.Zero);
        }
    }

    private void DrawEditableFloat4Values(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"Float4Values_{paramIndex}", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
                if (ImGui.InputFloat($"##float4_x_{paramIndex}_{i}", ref value.X))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float4_y_{paramIndex}_{i}", ref value.Y))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float4_z_{paramIndex}_{i}", ref value.Z))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.InputFloat($"##float4_w_{paramIndex}_{i}", ref value.W))
                {
                    param.Values[i] = value;
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##float4_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Float4 Value##{paramIndex}"))
        {
            param.Values.Add(Vector4.Zero);
        }
    }

    private void DrawEditableByte4Values(GPARAM.Param param, int paramIndex)
    {
        if (ImGui.BeginTable($"Byte4Values_{paramIndex}", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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

                string bufferKey = $"byte4_{paramIndex}_{i}";
                if (!byteArrayEditBuffers.ContainsKey(bufferKey))
                {
                    byteArrayEditBuffers[bufferKey] = new byte[4];
                    Array.Copy(bytes, byteArrayEditBuffers[bufferKey], 4);
                }

                byte[] editBuffer = byteArrayEditBuffers[bufferKey];

                for (int j = 0; j < 4; j++)
                {
                    ImGui.TableNextColumn();
                    int byteValue = editBuffer[j];
                    if (ImGui.InputInt($"##byte4_{paramIndex}_{i}_{j}", ref byteValue, 1, 10))
                    {
                        if (byteValue >= byte.MinValue && byteValue <= byte.MaxValue)
                        {
                            editBuffer[j] = (byte)byteValue;
                            param.Values[i] = editBuffer.ToArray();
                        }
                    }
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($"Delete##byte4_{paramIndex}_{i}"))
                {
                    param.Values.RemoveAt(i);
                    byteArrayEditBuffers.Remove(bufferKey);
                    i--;
                }
            }

            ImGui.EndTable();
        }

        if (ImGui.Button($"Add Byte4 Value##{paramIndex}"))
        {
            param.Values.Add(new byte[4]);
        }
    }

    private string ShowOpenFileDialog()
    {
        var thread = new Thread(() =>
        {
            using (var fileDialog = new OpenFileDialog()) //Windows dialog
            {
                fileDialog.Title = "Open gparam";
                fileDialog.Filter = "All (*.*)|*.*";
                if (fileDialog.ShowDialog() == DialogResult.OK)
                {
                    var filePath = fileDialog.FileName;
                    LoadFile(filePath);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return null;
    }

    private string ShowSaveFileDialog()
    {
        // Вам нужно будет реализовать диалог сохранения файла в зависимости от вашего фреймворка
        // Например, для WinForms:
        // using (var dialog = new SaveFileDialog())
        // {
        //     dialog.Filter = "GPARAM files (*.gparam)|*.gparam|All files (*.*)|*.*";
        //     if (dialog.ShowDialog() == DialogResult.OK)
        //         return dialog.FileName;
        // }
        return null;
    }

    public void SaveFile(string filePath = null)
    {
        if (gparam == null) return;

        try
        {
            string savePath = filePath ?? currentFilePath;
            gparam.Write(savePath);
            Console.WriteLine($"Saved to: {savePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving file: {ex.Message}");
        }
    }
}
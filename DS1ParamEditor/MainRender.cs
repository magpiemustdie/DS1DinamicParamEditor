using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using System.Security.Cryptography;
using Veldrid;
using System.Text;
using System.Diagnostics;
using Vulkan;
using System.Linq.Expressions;


namespace DS1ParamEditor
{
    internal class ImGuiMainRender
    {
        MainWindow mainWindow = new();
        public void Render()
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(new Vector2(750, 900), ImGuiCond.Appearing);
            ImGui.Begin("DrawParamEditor", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.MenuBar);
            {
                mainWindow.BuildWindow();
            }
            ImGui.End();
        }
    }
}

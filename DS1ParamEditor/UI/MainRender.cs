using System.Numerics;
using ImGuiNET;

namespace DS1ParamEditor
{
    internal sealed class ImGuiMainRender
    {
        private readonly MainWindow _window = new();

        public void Render()
        {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
            ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);

            ImGui.Begin("##root",
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.MenuBar);

            _window.Draw();

            ImGui.End();
        }
    }
}

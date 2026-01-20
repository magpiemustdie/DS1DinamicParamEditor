using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using Veldrid.MetalBindings;

namespace DS1DrawParamEditor
{
    class Program
    {
        private static Sdl2Window _window;
        private static GraphicsDevice _gd;
        private static CommandList _cl;
        private static ImGuiController _controller;
        private static Vector3 _clearColor = new Vector3(0.45f, 0.55f, 0.6f);

    
        static void Main(string[] args)
        {
            // Create window, GraphicsDevice, and all resources necessary for the demo.
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 750, 900, WindowState.Normal, "DS1ParamEditor "),
                new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
                out _window,
                out _gd);
            _window.Resized += () =>
            {
                _gd.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _controller.WindowResized(_window.Width, _window.Height);
            };
            _cl = _gd.ResourceFactory.CreateCommandList();
            _controller = new ImGuiController(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription, _window.Width, _window.Height);

            var stopwatch = Stopwatch.StartNew();
            float deltaTime = 0f;
            // Main application loop

            ImGuiMainRender mainRender = new();

            // Main loop
            while (_window.Exists)
            {
                deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                stopwatch.Restart();

                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) { break; }
                _controller.Update(deltaTime, snapshot);

                // Draw ImGui UI
                mainRender.Render();

                _cl.Begin();
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, new RgbaFloat(_clearColor.X, _clearColor.Y, _clearColor.Z, 1f));
                _controller.Render(_gd, _cl);
                _cl.End();
                _gd.SubmitCommands(_cl);
                _gd.SwapBuffers(_gd.MainSwapchain);
            }

            // Clean up Veldrid resources
            _gd.WaitForIdle();
            _controller.Dispose();
            _cl.Dispose();
            _gd.Dispose();
        }
    }
}

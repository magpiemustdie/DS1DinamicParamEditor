using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace DS1ParamEditor
{
    class Program
    {
        private static Sdl2Window    _window     = null!;
        private static GraphicsDevice _gd        = null!;
        private static CommandList   _cl         = null!;
        private static ImGuiController _controller = null!;
        private static readonly Vector3 _clearColor = new(0.15f, 0.15f, 0.15f);

        // Target ~60 fps; sleep the remainder of each frame to avoid 100% CPU
        const double TARGET_FPS      = 60.0;
        const double TARGET_FRAME_MS = 1000.0 / TARGET_FPS;

        static void Main(string[] args)
        {
            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 1000, 900, WindowState.Normal, "DS1ParamEditor"),
                new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
                out _window,
                out _gd);

            _window.Resized += () =>
            {
                _gd.MainSwapchain.Resize((uint)_window.Width, (uint)_window.Height);
                _controller.WindowResized(_window.Width, _window.Height);
            };

            _cl         = _gd.ResourceFactory.CreateCommandList();
            _controller = new ImGuiController(_gd, _gd.MainSwapchain.Framebuffer.OutputDescription,
                              _window.Width, _window.Height);

            var render    = new ImGuiMainRender();
            var stopwatch = Stopwatch.StartNew();

            while (_window.Exists)
            {
                long frameStart = stopwatch.ElapsedMilliseconds;

                float deltaTime = (float)(stopwatch.Elapsed.TotalSeconds);
                stopwatch.Restart();

                InputSnapshot snapshot = _window.PumpEvents();
                if (!_window.Exists) break;

                _controller.Update(deltaTime, snapshot);
                render.Render();

                _cl.Begin();
                _cl.SetFramebuffer(_gd.MainSwapchain.Framebuffer);
                _cl.ClearColorTarget(0, new RgbaFloat(_clearColor.X, _clearColor.Y, _clearColor.Z, 1f));
                _controller.Render(_gd, _cl);
                _cl.End();
                _gd.SubmitCommands(_cl);
                _gd.SwapBuffers(_gd.MainSwapchain);

                // Cap to ~60 fps — sleep only the remaining frame budget
                int sleepMs = (int)(TARGET_FRAME_MS - stopwatch.ElapsedMilliseconds);
                if (sleepMs > 1) Thread.Sleep(sleepMs);
            }

            _gd.WaitForIdle();
            _controller.Dispose();
            _cl.Dispose();
            _gd.Dispose();
        }
    }
}

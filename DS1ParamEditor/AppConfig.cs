using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace DS1ParamEditor
{
    public sealed class AppConfig
    {
        private const string SETTINGS_FILE = "AppSettings.json";

        public string SelectedExe         { get; set; } = string.Empty;
        public string SelectedExePath      { get; set; } = string.Empty;
        public string SelectedGamePath     { get; set; } = string.Empty;
        public string SelectedParamPath    { get; set; } = string.Empty;
        public string SelectedDrawParamPath{ get; set; } = string.Empty;
        public string SelectedParamDefPath { get; set; } = string.Empty;

        public bool IsReady =>
            !string.IsNullOrEmpty(SelectedParamDefPath) &&
            !string.IsNullOrEmpty(SelectedParamPath)    &&
            !string.IsNullOrEmpty(SelectedDrawParamPath);

        // ── Init ─────────────────────────────────────────────────────────────

        public void TryToGetGamePath()
        {
            if (!File.Exists(SETTINGS_FILE)) return; // no saved config — wait for user action
            Load(SETTINGS_FILE);
            if (!string.IsNullOrEmpty(SelectedExePath) && File.Exists(SelectedExePath))
                ApplyExePath(SelectedExePath);
            // If saved path is invalid, just leave fields empty — UI will prompt
        }

        // ── File dialog ───────────────────────────────────────────────────────

        public void ShowFileDialog()
        {
            ClearPaths();

            // WinForms OpenFileDialog requires STA thread
            string? chosen = null;
            var thread = new Thread(() =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter          = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Title           = "Select Dark Souls executable"
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                    chosen = dlg.FileName;
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (chosen != null)
            {
                ApplyExePath(chosen);
                Save(SETTINGS_FILE);
            }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        public void Save(string filePath)
        {
            try
            {
                File.WriteAllText(filePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppConfig] Save failed: {ex.Message}");
            }
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void ApplyExePath(string exePath)
        {
            SelectedExePath  = exePath;
            SelectedExe      = Path.GetFileNameWithoutExtension(exePath);
            SelectedGamePath = Path.GetDirectoryName(exePath) ?? string.Empty;
            ResolveParamPaths();
        }

        private void ResolveParamPaths()
        {
            // Both DS1 variants share the same directory layout
            bool isKnown = SelectedExe is "DARKSOULS" or "DarkSoulsRemastered";
            if (!isKnown) return;

            // paramdef — prefer non-DCX
            string paramdefDir = Path.Combine(SelectedGamePath, "paramdef");
            string paramdefBnd = Path.Combine(paramdefDir, "paramdef.paramdefbnd");
            string paramdefDcx = Path.Combine(paramdefDir, "paramdef.paramdefbnd.dcx");

            SelectedParamDefPath = File.Exists(paramdefBnd) ? paramdefBnd
                                 : File.Exists(paramdefDcx) ? paramdefDcx
                                 : string.Empty;

            SelectedParamPath     = Path.Combine(SelectedGamePath, "param", "GameParam");
            SelectedDrawParamPath = Path.Combine(SelectedGamePath, "param", "DrawParam");
        }

        private void ClearPaths()
        {
            SelectedExe = SelectedExePath = SelectedGamePath =
            SelectedParamDefPath = SelectedParamPath = SelectedDrawParamPath = string.Empty;
        }

        private void Load(string filePath)
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(filePath));
                if (loaded == null) return;

                SelectedExe          = loaded.SelectedExe;
                SelectedExePath      = loaded.SelectedExePath;
                SelectedGamePath     = loaded.SelectedGamePath;
                SelectedParamPath    = loaded.SelectedParamPath;
                SelectedDrawParamPath= loaded.SelectedDrawParamPath;
                SelectedParamDefPath = loaded.SelectedParamDefPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppConfig] Load failed: {ex.Message}");
            }
        }
    }
}

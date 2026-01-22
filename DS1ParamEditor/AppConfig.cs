using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DS1ParamEditor
{
    public class AppConfig
    {
        //facepalm
        public string SelectedExe { get; set; } = string.Empty;
        public string SelectedExePath { get; set; } = string.Empty;
        public string SelectedGamePath { get; set; } = string.Empty;
        public string SelectedParamPath { get; set; } = string.Empty;
        public string SelectedDrawParamPath { get; set; } = string.Empty;
        public string SelectedParamDefPath { get; set; } = string.Empty;

        public void Save(string filePath)
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public void TryToGetGamePath()
        {
            if (File.Exists("AppSettings.json"))
            {
                this.Load("AppSettings.json");
                Console.WriteLine("Read AppSettings.json");
                if (!string.IsNullOrEmpty(this.SelectedExePath))
                {
                    Console.WriteLine($"ExePath: {this.SelectedExePath}");
                    this.SelectedExe = Path.GetFileNameWithoutExtension(this.SelectedExePath);
                    this.SelectedGamePath = this.SelectedExePath.Substring(0, this.SelectedExePath.LastIndexOf('\\'));
                    this.TryToGetParams();
                }
                else
                    ShowFileDialog();
            }
            else
                ShowFileDialog();
        }

        public void ShowFileDialog()
        {
            this.SelectedExe = this.SelectedExePath = this.SelectedGamePath = this.SelectedParamDefPath = this.SelectedParamPath = this.SelectedDrawParamPath = string.Empty;

            var thread = new Thread(() =>
            {
                using (var openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                    openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    openFileDialog.Title = "Select an executable file";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        this.SelectedExePath = openFileDialog.FileName;
                        this.SelectedExe = Path.GetFileNameWithoutExtension(this.SelectedExePath);
                        this.SelectedGamePath = this.SelectedExePath.Substring(0, this.SelectedExePath.LastIndexOf('\\'));

                        this.TryToGetParams();

                        this.Save("AppSettings.json");
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(); // Wait for the dialog to close
        }
        private void Load(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                var loadedConfig = JsonSerializer.Deserialize<AppConfig>(json);

                if (loadedConfig != null)
                {
                    this.SelectedExe = loadedConfig.SelectedExe;
                    this.SelectedExePath = loadedConfig.SelectedExePath;
                    this.SelectedGamePath = loadedConfig.SelectedGamePath;
                    this.SelectedParamPath = loadedConfig.SelectedParamPath;
                    this.SelectedDrawParamPath = loadedConfig.SelectedDrawParamPath;
                    this.SelectedParamDefPath = loadedConfig.SelectedParamDefPath;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        private void TryToGetParams()
        {
            switch (this.SelectedExe)
            {
                case "DARKSOULS":
                    {
                        if (File.Exists(this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd"))
                            this.SelectedParamDefPath = this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd";
                        else if (File.Exists(this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx"))
                            this.SelectedParamDefPath = this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx";

                        this.SelectedParamPath = this.SelectedGamePath + "\\param\\GameParam";
                        this.SelectedDrawParamPath = this.SelectedGamePath + "\\param\\DrawParam";
                        break;
                    }
                case "DarkSoulsRemastered":
                    {
                        if (File.Exists(this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd"))
                            this.SelectedParamDefPath = this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd";

                        else if (File.Exists(this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx"))
                            this.SelectedParamDefPath = this.SelectedGamePath + "\\paramdef\\paramdef.paramdefbnd.dcx";

                        this.SelectedParamPath = this.SelectedGamePath + "\\param\\GameParam";
                        this.SelectedDrawParamPath = this.SelectedGamePath + "\\param\\DrawParam";
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }

    }
}

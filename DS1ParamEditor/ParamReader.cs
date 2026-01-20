using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SoulsFormats;

namespace DS1ParamEditor
{
    public class ParamContainer
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public BND3 Bnd3File { get; set; }
        public Dictionary<string, PARAM> ParmsDef { get; set; }
    }

    public class ParamReader
    {
        public List<ParamContainer> ReadParamMass(string paramDefPath, string paramsPath)
        {
            List<ParamContainer> paramContainer = [];
            var paramdefs = new List<PARAMDEF>();
            var paramdefbnd = BND3.Read(paramDefPath);
            foreach (BinderFile file in paramdefbnd.Files)
            {
                var paramdef = PARAMDEF.Read(file.Bytes);
                paramdefs.Add(paramdef);
            }

            foreach (var file in Directory.GetFiles(paramsPath))
            {
                (BND3 parambnd, Dictionary<string, PARAM> parms) = ReadParam(paramdefs, file);
                ParamContainer container = new()
                {
                    Path = file,
                    Name = Path.GetFileName(file),
                    Bnd3File = parambnd,
                    ParmsDef = parms
                };
                paramContainer.Add(container);
            }
            return paramContainer;
        }

        public (BND3, Dictionary<string, PARAM>) ReadParam(List<PARAMDEF> paramdefs, string paramsPath)
        {
            var parms = new Dictionary<string, PARAM>();
            var parambnd = BND3.Read(paramsPath);
            foreach (BinderFile file in parambnd.Files)
            {
                string name = Path.GetFileNameWithoutExtension(file.Name);

                var param = PARAM.Read(file.Bytes);

                if (param.ApplyParamdefCarefully(paramdefs))
                    parms[name] = param;
                else
                    parms[name] = param;
            }

            return (parambnd, parms);
        }

        public void WriteParam(ParamContainer paramContainer)
        {
            string path = paramContainer.Path;

            foreach (BinderFile file in paramContainer.Bnd3File.Files)
            {
                string name = Path.GetFileNameWithoutExtension(file.Name);
                if (paramContainer.ParmsDef.ContainsKey(name))
                    file.Bytes = paramContainer.ParmsDef[name].Write();
            }

            try
            {
                Console.WriteLine($"Try to save in {path}...");
                paramContainer.Bnd3File.Write(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Write failed: {ex.Message}");
            }

            Console.WriteLine($"Done.");

        }
    }
}

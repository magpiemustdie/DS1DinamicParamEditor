using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace DS1ParamEditor
{
    /// <summary>
    /// A single param entry inside a BND3 archive, with its PARAMDEF applied.
    /// </summary>
    public sealed class LoadedParam
    {
        public string Name { get; }          // e.g. "NpcParam"
        public PARAM Param { get; }
        public byte[] RawBytes { get; }      // full PARAM file bytes (for offset base)
        public byte[] ScanPattern { get; }   // bytes used for AOB scan (row data, not header)
        public long ScanOffset { get; }      // offset of ScanPattern within RawBytes

        public LoadedParam(string name, PARAM param, byte[] rawBytes)
        {
            Name     = name;
            Param    = param;
            RawBytes = rawBytes;

            // Scan from row data, not the file header.
            // Header contains pointers that differ between disk and memory.
            // We use a shorter, more reliable pattern from the first row data.
            if (param.Rows.Count > 0
                && param.Rows[0].DataOffset > 0
                && param.Rows[0].DataOffset < rawBytes.Length)
            {
                long dataStart = param.Rows[0].DataOffset;
                int  available = (int)(rawBytes.Length - dataStart);
                
                // Use a shorter pattern (96 bytes) for better reliability
                // Longer patterns are more likely to fail if data is modified in memory
                int slideLen = Math.Min(96, available);
                
                // Find the most unique window within available data
                int bestOffset = 0;
                int bestScore  = -1;
                int maxSlide   = Math.Max(1, Math.Min(available - slideLen + 1, 256));

                for (int off = 0; off < maxSlide; off += Math.Max(1, slideLen / 4))
                {
                    int checkLen = Math.Min(slideLen, available - off);
                    int score = CountUniqueBytes(rawBytes, (int)dataStart + off, checkLen);
                    if (score > bestScore)
                    {
                        bestScore  = score;
                        bestOffset = off;
                    }
                }

                // Use the best window
                ScanOffset  = dataStart + bestOffset;
                int patLen  = Math.Min(slideLen, available - bestOffset);
                ScanPattern = rawBytes[(int)ScanOffset..(int)(ScanOffset + patLen)];
                
                Console.WriteLine($"[ParamStore] {name}: pattern {patLen} bytes @ offset 0x{ScanOffset:X} (uniqueness: {bestScore}/256)");
            }
            else
            {
                // Fallback: use first 64 bytes of file
                ScanOffset  = 0;
                int len = Math.Min(64, rawBytes.Length);
                ScanPattern = rawBytes[0..len];
                Console.WriteLine($"[ParamStore] {name}: using fallback pattern {len} bytes (no row data)");
            }
        }

        private static int CountUniqueBytes(byte[] data, int offset, int length)
        {
            var seen = new bool[256];
            int count = 0;
            for (int i = 0; i < length; i++)
            {
                byte b = data[offset + i];
                if (!seen[b]) { seen[b] = true; count++; }
            }
            return count;
        }
    }

    /// <summary>
    /// A loaded BND3 archive containing one or more <see cref="LoadedParam"/> entries.
    /// </summary>
    public sealed class ParamFile
    {
        public string FilePath { get; }
        public string FileName { get; }
        public BND3 Bnd3 { get; }
        public IReadOnlyList<LoadedParam> Params { get; }

        public ParamFile(string filePath, BND3 bnd3, List<LoadedParam> parms)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Bnd3 = bnd3;
            Params = parms;
        }
    }

    /// <summary>
    /// Loads and saves param files from disk.
    /// </summary>
    public sealed class ParamStore
    {
        /// <summary>Loads all .parambnd files from a directory.</summary>
        public List<ParamFile> Load(string paramDefPath, string paramsDir)
        {
            var paramdefs = ReadParamdefs(paramDefPath);
            var result = new List<ParamFile>();

            foreach (var filePath in Directory.GetFiles(paramsDir))
            {
                try
                {
                    var file = LoadFile(paramdefs, filePath);
                    result.Add(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ParamStore] Skip '{Path.GetFileName(filePath)}': {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>Writes modified params back into the BND3 and saves to disk.</summary>
        public void Save(ParamFile file)
        {
            foreach (var entry in file.Bnd3.Files)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Name);
                var lp = file.Params.FirstOrDefault(p => p.Name == name);
                if (lp != null)
                    entry.Bytes = lp.Param.Write();
            }
            file.Bnd3.Write(file.FilePath);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private static List<PARAMDEF> ReadParamdefs(string path)
        {
            var list = new List<PARAMDEF>();
            var bnd = BND3.Read(path);
            foreach (var f in bnd.Files)
                list.Add(PARAMDEF.Read(f.Bytes));
            return list;
        }

        private static ParamFile LoadFile(List<PARAMDEF> defs, string filePath)
        {
            var bnd = BND3.Read(filePath);
            var parms = new List<LoadedParam>();

            foreach (var entry in bnd.Files)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Name);
                try
                {
                    var param = PARAM.Read(entry.Bytes);
                    bool ok = param.ApplyParamdefCarefully(defs);
                    if (!ok)
                    {
                        var def = defs.Find(d => d.ParamType == param.ParamType);
                        if (def != null) param.ApplyParamdef(def);
                    }
                    parms.Add(new LoadedParam(name, param, entry.Bytes));
                    Console.WriteLine($"[ParamStore] {name} => {(ok ? "OK" : "OK?")}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ParamStore] {name} => broken ({ex.Message})");
                }
            }

            return new ParamFile(filePath, bnd, parms);
        }
    }
}

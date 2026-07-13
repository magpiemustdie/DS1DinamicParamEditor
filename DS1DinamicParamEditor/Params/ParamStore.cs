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

        /// <summary>
        /// Pattern length mode used when this param was loaded.
        /// </summary>
        public PatternMode Mode { get; }

        public enum PatternMode { Auto = 0, Custom = 1, Full = 2 }

        /// <summary>Where within the row data to take the pattern from.</summary>
        public enum PatternStart { FromStart = 0, BestWindow = 1, FromFileStart = 2 }

        public LoadedParam(string name, PARAM param, byte[] rawBytes,
            PatternMode mode = PatternMode.Auto, int customLength = 96,
            PatternStart start = PatternStart.FromStart)
        {
            Name     = name;
            Param    = param;
            RawBytes = rawBytes;
            Mode     = mode;
            if (param.Rows.Count > 0
                && param.Rows[0].DataOffset > 0
                && param.Rows[0].DataOffset < rawBytes.Length)
            {
                long dataStart = param.Rows[0].DataOffset;
                int  available = (int)(rawBytes.Length - dataStart);

                int slideLen = mode switch
                {
                    PatternMode.Custom => Math.Min(Math.Max(1, customLength), available),
                    PatternMode.Full   => available,
                    _                  => Math.Min(96, available),   // Auto
                };

                if (mode == PatternMode.Full)
                {
                    // Full: entire row data block, ignore start mode
                    ScanOffset  = dataStart;
                    ScanPattern = rawBytes[(int)dataStart..(int)(dataStart + slideLen)];
                    int score   = CountUniqueBytes(rawBytes, (int)dataStart, slideLen);
                    Console.WriteLine($"[ParamStore] {name}: pattern {slideLen}b [full] @ 0x{ScanOffset:X} (uniqueness: {score}/256)");
                }
                else if (start == PatternStart.FromFileStart)
                {
                    // FromFileStart: use bytes from the very beginning of the file.
                    // The PARAM header contains ParamType string, row count, offsets — unique and stable.
                    // This matches DS1ParamEditor 0.4.1 behavior ("first 100 bytes").
                    int patLen  = Math.Min(slideLen, rawBytes.Length);
                    ScanOffset  = 0;
                    ScanPattern = rawBytes[0..patLen];
                    int score   = CountUniqueBytes(rawBytes, 0, patLen);
                    Console.WriteLine($"[ParamStore] {name}: pattern {patLen}b [file-start] @ 0x0 (uniqueness: {score}/256)");
                }
                else if (start == PatternStart.BestWindow)
                {
                    // BestWindow: slide through data to find most unique window
                    int bestOffset = 0;
                    int bestScore  = -1;
                    int maxSlide   = Math.Max(1, Math.Min(available - slideLen + 1, 256));

                    for (int off = 0; off < maxSlide; off += Math.Max(1, slideLen / 4))
                    {
                        int checkLen = Math.Min(slideLen, available - off);
                        int score = CountUniqueBytes(rawBytes, (int)dataStart + off, checkLen);
                        if (score > bestScore) { bestScore = score; bestOffset = off; }
                    }

                    ScanOffset  = dataStart + bestOffset;
                    int patLen  = Math.Min(slideLen, available - bestOffset);
                    ScanPattern = rawBytes[(int)ScanOffset..(int)(ScanOffset + patLen)];
                    Console.WriteLine($"[ParamStore] {name}: pattern {patLen}b [best] @ 0x{ScanOffset:X} (uniqueness: {bestScore}/256)");
                }
                else
                {
                    // FromStart (default): take first N bytes from row data start
                    // Most stable — game rarely modifies the beginning of param rows
                    ScanOffset  = dataStart;
                    int patLen  = Math.Min(slideLen, available);
                    ScanPattern = rawBytes[(int)ScanOffset..(int)(ScanOffset + patLen)];
                    int score   = CountUniqueBytes(rawBytes, (int)dataStart, patLen);
                    Console.WriteLine($"[ParamStore] {name}: pattern {patLen}b [start] @ 0x{ScanOffset:X} (uniqueness: {score}/256)");
                }
            }
            else
            {
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
        public int SkippedCount { get; }  // params that failed ApplyParamdefCarefully

        public ParamFile(string filePath, BND3 bnd3, List<LoadedParam> parms, int skipped = 0)
        {
            FilePath     = filePath;
            FileName     = Path.GetFileName(filePath);
            Bnd3         = bnd3;
            Params       = parms;
            SkippedCount = skipped;
        }
    }

    /// <summary>
    /// Loads and saves param files from disk.
    /// </summary>
    public sealed class ParamStore
    {
        /// <summary>Loads all .parambnd files from a directory.</summary>
        public List<ParamFile> Load(string paramDefPath, string paramsDir,
            LoadedParam.PatternMode mode = LoadedParam.PatternMode.Auto,
            int customLength = 96,
            bool forceLoad = false,
            LoadedParam.PatternStart patternStart = LoadedParam.PatternStart.FromStart)
        {
            var paramdefs = ReadParamdefs(paramDefPath);
            var result = new List<ParamFile>();

            foreach (var filePath in Directory.GetFiles(paramsDir))
            {
                try
                {
                    var file = LoadFile(paramdefs, filePath, mode, customLength, forceLoad, patternStart);
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

        /// <summary>Saves only a single param's entry back into the BND3, leaving other entries untouched.</summary>
        public void SaveParam(ParamFile file, string paramName)
        {
            foreach (var entry in file.Bnd3.Files)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Name);
                if (name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                {
                    var lp = file.Params.FirstOrDefault(p => p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
                    if (lp != null)
                        entry.Bytes = lp.Param.Write();
                    break;
                }
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

        private static ParamFile LoadFile(List<PARAMDEF> defs, string filePath,
            LoadedParam.PatternMode mode = LoadedParam.PatternMode.Auto,
            int customLength = 96,
            bool forceLoad = false,
            LoadedParam.PatternStart patternStart = LoadedParam.PatternStart.FromStart)
        {
            var bnd = BND3.Read(filePath);
            var parms = new List<LoadedParam>();
            int skipped = 0;

            foreach (var entry in bnd.Files)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Name);
                try
                {
                    var param = PARAM.Read(entry.Bytes);

                    if (param.ApplyParamdefCarefully(defs))
                    {
                        parms.Add(new LoadedParam(name, param, entry.Bytes, mode, customLength, patternStart));
                        Console.WriteLine($"[ParamStore] {name} => OK");
                    }
                    else if (param.ApplyParamdefSomewhatCarefully(defs))
                    {
                        parms.Add(new LoadedParam(name, param, entry.Bytes, mode, customLength, patternStart));
                        Console.WriteLine($"[ParamStore] {name} => OK (somewhat, row size mismatch)");
                    }
                    else
                    {
                        // Level 3: ParamType + row size match, ignore DataVersion (DSR 1.03 version bump)
                        var defBySize = defs.FirstOrDefault(d =>
                            d.ParamType == param.ParamType &&
                            (param.DetectedSize == -1 || param.DetectedSize == d.GetRowSize()));

                        if (defBySize != null)
                        {
                            param.ApplyParamdef(defBySize);
                            parms.Add(new LoadedParam(name, param, entry.Bytes, mode, customLength, patternStart));
                            Console.WriteLine($"[ParamStore] {name} => OK (row size match, DataVersion ignored: param={param.ParamdefDataVersion} def={defBySize.DataVersion})");
                        }
                        else if (forceLoad)
                        {
                            var def = defs.Find(d => d.ParamType == param.ParamType);
                            if (def != null)
                            {
                                param.ApplyParamdef(def);
                                parms.Add(new LoadedParam(name, param, entry.Bytes, mode, customLength, patternStart));
                                Console.WriteLine("[ParamStore] " + name + " => OK (forced)");
                            }
                            else
                            {
                                skipped++;
                                Console.WriteLine("[ParamStore] " + name + " => skipped (no def for ParamType='" + param.ParamType + "')");
                            }
                        }
                        else
                        {
                            skipped++;
                            Console.WriteLine("[ParamStore] " + name + " => skipped (ParamType='" + param.ParamType + "')");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ParamStore] {name} => broken ({ex.Message})");
                }
            }

            return new ParamFile(filePath, bnd, parms, skipped);
        }
    }
}

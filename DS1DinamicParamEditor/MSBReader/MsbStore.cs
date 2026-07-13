using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SoulsFormats;

namespace DS1ParamEditor.MSBReader
{
    /// <summary>
    /// A loaded MSB file with its parts, regions and events.
    /// </summary>
    public sealed class LoadedMsb
    {
        public string FilePath { get; }
        public string FileName { get; }
        public MSB1 Msb { get; }

        public LoadedMsb(string filePath, MSB1 msb)
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Msb = msb;
        }
    }

    /// <summary>
    /// Loads, saves and provides access to MSB map files for DSR.
    /// </summary>
    public sealed class MsbStore
    {
        private readonly Dictionary<string, LoadedMsb> _loaded = new();

        // ── Load / Save ───────────────────────────────────────────────────────

        /// <summary>Loads an MSB file from disk.</summary>
        public LoadedMsb Load(string filePath)
        {
            var msb = MSB1.Read(filePath);
            var loaded = new LoadedMsb(filePath, msb);
            _loaded[filePath] = loaded;
            Console.WriteLine($"[MsbStore] Loaded '{Path.GetFileName(filePath)}': " +
                $"{msb.Parts.GetEntries().Count} parts, " +
                $"{msb.Regions.GetEntries().Count} regions, " +
                $"{msb.Events.GetEntries().Count} events");
            return loaded;
        }

        /// <summary>Saves an MSB file back to disk.</summary>
        public void Save(LoadedMsb loaded)
        {
            loaded.Msb.Write(loaded.FilePath);
            Console.WriteLine($"[MsbStore] Saved '{loaded.FileName}'");
        }

        /// <summary>Saves to a different path.</summary>
        public void SaveAs(LoadedMsb loaded, string newPath)
        {
            loaded.Msb.Write(newPath);
            Console.WriteLine($"[MsbStore] Saved '{loaded.FileName}' -> '{Path.GetFileName(newPath)}'");
        }

        /// <summary>Gets all currently loaded MSBs.</summary>
        public IReadOnlyCollection<LoadedMsb> All => _loaded.Values;

        /// <summary>Unloads an MSB from memory.</summary>
        public void Unload(LoadedMsb loaded) => _loaded.Remove(loaded.FilePath);

        /// <summary>Clears all loaded MSB data.</summary>
        public void Reset() => _loaded.Clear();

        // ── Parts ─────────────────────────────────────────────────────────────

        /// <summary>Gets all parts of a given type.</summary>
        public IReadOnlyList<T> GetParts<T>(LoadedMsb loaded) where T : MSB1.Part
            => loaded.Msb.Parts.GetEntries().OfType<T>().ToList();

        /// <summary>Gets all map objects (static props).</summary>
        public IReadOnlyList<MSB1.Part.Object> GetObjects(LoadedMsb loaded)
            => GetParts<MSB1.Part.Object>(loaded);

        /// <summary>Gets all map pieces (terrain).</summary>
        public IReadOnlyList<MSB1.Part.MapPiece> GetMapPieces(LoadedMsb loaded)
            => GetParts<MSB1.Part.MapPiece>(loaded);

        /// <summary>Gets all enemies.</summary>
        public IReadOnlyList<MSB1.Part.Enemy> GetEnemies(LoadedMsb loaded)
            => GetParts<MSB1.Part.Enemy>(loaded);

        /// <summary>Gets all player starts.</summary>
        public IReadOnlyList<MSB1.Part.Player> GetPlayerStarts(LoadedMsb loaded)
            => GetParts<MSB1.Part.Player>(loaded);

        /// <summary>Finds a part by name (case-insensitive).</summary>
        public MSB1.Part? FindPart(LoadedMsb loaded, string name)
            => loaded.Msb.Parts.GetEntries()
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        // ── Part editing ──────────────────────────────────────────────────────

        /// <summary>Moves a part to a new position.</summary>
        public void SetPartPosition(MSB1.Part part, Vector3 position)
        {
            part.Position = position;
        }

        /// <summary>Sets part rotation (degrees).</summary>
        public void SetPartRotation(MSB1.Part part, Vector3 rotation)
        {
            part.Rotation = rotation;
        }

        /// <summary>Sets part scale.</summary>
        public void SetPartScale(MSB1.Part part, Vector3 scale)
        {
            part.Scale = scale;
        }

        /// <summary>Renames a part.</summary>
        public void RenamePart(MSB1.Part part, string newName)
        {
            part.Name = newName;
        }

        /// <summary>Changes the model name of a part.</summary>
        public void SetPartModel(MSB1.Part part, string modelName)
        {
            part.ModelName = modelName;
        }

        // ── Regions ───────────────────────────────────────────────────────────

        /// <summary>Gets all regions.</summary>
        public IReadOnlyList<MSB1.Region> GetRegions(LoadedMsb loaded)
            => loaded.Msb.Regions.GetEntries().ToList();

        /// <summary>Finds a region by name.</summary>
        public MSB1.Region? FindRegion(LoadedMsb loaded, string name)
            => loaded.Msb.Regions.GetEntries()
                .FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Sets region position.</summary>
        public void SetRegionPosition(MSB1.Region region, Vector3 position)
        {
            region.Position = position;
        }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Gets all events.</summary>
        public IReadOnlyList<MSB1.Event> GetEvents(LoadedMsb loaded)
            => loaded.Msb.Events.GetEntries().ToList();

        /// <summary>Gets all spawn points.</summary>
        public IReadOnlyList<MSB1.Event.SpawnPoint> GetSpawnPoints(LoadedMsb loaded)
            => loaded.Msb.Events.GetEntries().OfType<MSB1.Event.SpawnPoint>().ToList();

        /// <summary>Gets all treasures.</summary>
        public IReadOnlyList<MSB1.Event.Treasure> GetTreasures(LoadedMsb loaded)
            => loaded.Msb.Events.GetEntries().OfType<MSB1.Event.Treasure>().ToList();

        // ── Diagnostics ───────────────────────────────────────────────────────

        /// <summary>Prints a summary of the MSB to console.</summary>
        public void PrintSummary(LoadedMsb loaded)
        {
            var msb = loaded.Msb;
            Console.WriteLine($"[MsbStore] === {loaded.FileName} ===");
            Console.WriteLine($"  Parts:   {msb.Parts.GetEntries().Count}");
            Console.WriteLine($"    MapPieces: {msb.Parts.MapPieces.Count}");
            Console.WriteLine($"    Objects:   {msb.Parts.Objects.Count}");
            Console.WriteLine($"    Enemies:   {msb.Parts.Enemies.Count}");
            Console.WriteLine($"    Players:   {msb.Parts.Players.Count}");
            Console.WriteLine($"  Regions: {msb.Regions.GetEntries().Count}");
            Console.WriteLine($"  Events:  {msb.Events.GetEntries().Count}");
        }
    }
}

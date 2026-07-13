using System;
using System.Collections.Generic;

namespace DS1ParamEditor.Experimental
{
    /// <summary>
    /// Reads the current map ID from game memory.
    ///
    /// In DSR the map is identified by 4 bytes stored at ChrMapData+0xA8:
    ///   [0] area   (mAA_BB_CC_DD → AA)
    ///   [1] block  (BB)
    ///   [2] region (CC, usually 0)
    ///   [3] index  (DD, usually 0)
    ///
    /// Example: Firelink Shrine = m10_02_00_00 → area=0x10, block=0x02
    ///
    /// NOTE: offset 0xA8 is experimental — may need adjustment.
    /// </summary>
    public sealed class MapInfo
    {
        private readonly IGadgetHook _hook;

        public byte Area   { get; private set; }
        public byte Block  { get; private set; }

        public bool IsValid { get; private set; }

        /// <summary>Formatted as mAA_BB_00_00 (e.g. m10_02_00_00)</summary>
        public string MapName => IsValid
            ? $"m{Area:X2}_{Block:X2}_00_00"
            : "---";

        /// <summary>Human-readable area name if known.</summary>
        public string AreaName => IsValid ? LookupAreaName(Area, Block) : "Unknown";

        public MapInfo(IGadgetHook hook)
        {
            _hook = hook;
        }

        /// <summary>
        /// Reads the current map ID from game memory.
        /// Call this every frame (cheap — just 2 bytes).
        /// </summary>
        public void Refresh()
        {
            if (!_hook.Hooked || !_hook.IsValid)
            {
                IsValid = false;
                return;
            }

            try
            {
                byte[]? bytes = _hook.ReadMapId();
                if (bytes == null || bytes.Length < 2)
                {
                    IsValid = false;
                    return;
                }

                Area  = bytes[0];
                Block = bytes[1];
                IsValid = Area != 0 || Block != 0; // 0,0 = not loaded
            }
            catch
            {
                IsValid = false;
            }
        }

        private static string LookupAreaName(byte area, byte block)
        {
            int key = (area << 8) | block;
            return AreaNames.TryGetValue(key, out string? name) ? name : $"m{area:X2}_{block:X2}";
        }

        // Map IDs: area and block bytes as stored in memory
        // Confirmed: ChrMapData+0x23D9 gives 15 01 for Anor Londo (m15_01)
        // Other values are derived from bonfire IDs and need in-game verification
        private static readonly Dictionary<int, string> AreaNames = new()
        {
            { (0x10 << 8) | 0x00, "Depths" },
            { (0x10 << 8) | 0x01, "Undead Burg / Parish" },
            { (0x10 << 8) | 0x02, "Firelink Shrine" },
            { (0x11 << 8) | 0x00, "Painted World of Ariamis" },
            { (0x12 << 8) | 0x00, "Darkroot Garden / Basin" },
            { (0x12 << 8) | 0x01, "Oolacile / Chasm of the Abyss" },
            { (0x13 << 8) | 0x00, "The Catacombs" },
            { (0x13 << 8) | 0x01, "Tomb of the Giants" },
            { (0x13 << 8) | 0x02, "Ash Lake / Great Hollow" },
            { (0x14 << 8) | 0x00, "Blighttown / Quelaag's Domain" },
            { (0x14 << 8) | 0x01, "Demon Ruins / Lost Izalith" },
            { (0x15 << 8) | 0x00, "Sen's Fortress" },
            { (0x15 << 8) | 0x01, "Anor Londo" },                      // ✓ confirmed
            { (0x16 << 8) | 0x00, "New Londo Ruins / Valley of Drakes" },
            { (0x16 << 8) | 0x01, "The Abyss" },
            { (0x17 << 8) | 0x00, "Duke's Archives / Crystal Cave" },
            { (0x18 << 8) | 0x00, "Kiln of the First Flame" },
            { (0x18 << 8) | 0x01, "Northern Undead Asylum" },
        };
    }
}

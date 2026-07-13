using System.Collections.Generic;

namespace DS1ParamEditor
{
    public class DSRBonfire
    {
        public int Id { get; }
        public string Name { get; }
        public string Area { get; }

        public DSRBonfire(int id, string name, string area)
        {
            Id = id;
            Name = name;
            Area = area;
        }

        public override string ToString() => string.IsNullOrEmpty(Area) ? Name : $"{Name} ({Area})";
    }

    public static class BonfireData
    {
        public static readonly DSRBonfire[] All = new DSRBonfire[]
        {
            new DSRBonfire(-1, "[None]", ""),
            new DSRBonfire(1002900, "Depths", "Entrance"),
            new DSRBonfire(1002950, "Depths", "Gaping Dragon's Room"),
            new DSRBonfire(1002960, "Depths", "Bonfire"),
            new DSRBonfire(1012960, "Undead Burg", "Pre-Dragon Scare"),
            new DSRBonfire(1012961, "Undead Parish", "Sunlight Altar Bonfire"),
            new DSRBonfire(1012962, "Undead Burg", "Bonfire"),
            new DSRBonfire(1012964, "Undead Parish", "Bonfire"),
            new DSRBonfire(1012965, "Undead Parish", "Near Boar"),
            new DSRBonfire(1012966, "Undead Parish", "Near Cell"),
            new DSRBonfire(1020980, "Firelink Shrine", "Bonfire, repeat"),
            new DSRBonfire(1022960, "Firelink Shrine", "Bonfire"),
            new DSRBonfire(1102511, "Painted World of Ariamis", "Rope Bridge"),
            new DSRBonfire(1102960, "Painted World of Ariamis", "Bonfire"),
            new DSRBonfire(1102961, "Painted World of Ariamis", "Entrance"),
            new DSRBonfire(1202961, "Darkroot Garden", "Bonfire"),
            new DSRBonfire(1212950, "Chasm of the Abyss", "Manus"),
            new DSRBonfire(1212961, "Oolacile Sanctuary", "Bonfire"),
            new DSRBonfire(1212962, "Oolacile Township", "Bonfire"),
            new DSRBonfire(1212963, "Sanctuary Garden", "Bonfire"),
            new DSRBonfire(1212964, "Oolacile Township Dungeon", "Bonfire"),
            new DSRBonfire(1302960, "The Catacombs", "Bonfire #1"),
            new DSRBonfire(1302961, "The Catacombs", "Bonfire #2"),
            new DSRBonfire(1302962, "The Catacombs", "Entrance"),
            new DSRBonfire(1312950, "The Catacombs", "Nito"),
            new DSRBonfire(1312960, "Tomb of the Giants", "Bonfire #1"),
            new DSRBonfire(1312961, "Tomb of the Giants", "Bonfire #2"),
            new DSRBonfire(1312962, "Tomb of the Giants", "Entrance"),
            new DSRBonfire(1320980, "Great Hollow", "Bonfire, repeat"),
            new DSRBonfire(1322960, "Ash Lake", "Bonfire #2"),
            new DSRBonfire(1322961, "Ash Lake", "Bonfire #1"),
            new DSRBonfire(1322962, "Great Hollow", "Bonfire"),
            new DSRBonfire(1400980, "Blighttown", "Entrance, repeat"),
            new DSRBonfire(1402960, "Quelaag's Domain", "Bonfire"),
            new DSRBonfire(1402961, "Blighttown", "Swamp Bonfire"),
            new DSRBonfire(1402962, "Blighttown", "Bridge Bonfire"),
            new DSRBonfire(1402963, "Blighttown", "Entrance"),
            new DSRBonfire(1412950, "Lost Izalith", "Bed of Chaos"),
            new DSRBonfire(1412960, "Lost Izalith", "Bonfire #2"),
            new DSRBonfire(1412961, "Demon Ruins", "Bonfire #1"),
            new DSRBonfire(1412962, "Demon Ruins", "Bonfire #2"),
            new DSRBonfire(1412963, "Demon Ruins", "Bonfire #3"),
            new DSRBonfire(1412964, "Lost Izalith", "Bonfire #1"),
            new DSRBonfire(1502960, "Sen's Fortress", "Bridge, #1"),
            new DSRBonfire(1502961, "Sen's Fortress", "Bonfire"),
            new DSRBonfire(1502962, "Sen's Fortress", "Bridge, #2"),
            new DSRBonfire(1510980, "Anor Londo", "Entrance"),
            new DSRBonfire(1512950, "Anor Londo", "Gwynevere Bonfire"),
            new DSRBonfire(1512960, "Anor Londo", "Lady of the Darkling Bonfire"),
            new DSRBonfire(1512961, "Anor Londo", "Interior Bonfire"),
            new DSRBonfire(1512962, "Anor Londo", "Gwyndolin Bonfire"),
            new DSRBonfire(1600980, "New Londo Ruins", "Entrance, repeat"),
            new DSRBonfire(1602950, "Abyss", "Bonfire"),
            new DSRBonfire(1602951, "New Londo Ruins", "Entrance"),
            new DSRBonfire(1602960, "New Londo Ruins", "Pre-Ingward"),
            new DSRBonfire(1602961, "Darkroot Basin", "Bonfire"),
            new DSRBonfire(1702900, "The Duke's Archives", "Cell, After Seath"),
            new DSRBonfire(1702950, "Crystal Cave", "Seath"),
            new DSRBonfire(1702960, "The Duke's Archives", "Balcony Bonfire"),
            new DSRBonfire(1702961, "The Duke's Archives", "Cell Bonfire"),
            new DSRBonfire(1702962, "The Duke's Archives", "Elevator Bonfire"),
            new DSRBonfire(1802130, "Kiln of the First Flame", "Gwyn"),
            new DSRBonfire(1802960, "Firelink Altar", ""),
            new DSRBonfire(1802961, "Kiln of the First Flame", "Entrance"),
            new DSRBonfire(1812100, "Northern Undead Asylum", "Cell"),
            new DSRBonfire(1812900, "Northern Undead Asylum", "Cell, Repeat"),
            new DSRBonfire(1812960, "Northern Undead Asylum", "Bonfire #1"),
            new DSRBonfire(1812961, "Northern Undead Asylum", "Bonfire #2"),
        };
    }
}

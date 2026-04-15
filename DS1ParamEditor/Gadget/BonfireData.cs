namespace DS1ParamEditor
{
    /// <summary>
    /// DS1/DSR bonfire with its game ID (for LastBonfire warp).
    /// IDs sourced from DSR-Gadget Bonfires.txt.
    /// </summary>
    public sealed record Bonfire(string Area, string Name, int Id);

    public static class BonfireData
    {
        public static readonly Bonfire[] All =
        [
            new("Firelink Shrine",       "Firelink Shrine",                          1022960),
            new("Firelink Shrine",       "Firelink Shrine (repeat)",                 1020980),
            new("Firelink Altar",        "Firelink Altar",                           1802960),

            new("Northern Undead Asylum","Asylum Cell",                              1812960),
            new("Northern Undead Asylum","Asylum Bonfire #1",                        1812961),
            new("Northern Undead Asylum","Asylum Cell (repeat)",                     1812900),

            new("Undead Burg",           "Undead Burg",                              1012962),
            new("Undead Burg",           "Undead Burg (Pre-Dragon Scare)",           1012960),
            new("Undead Parish",         "Undead Parish",                            1012964),
            new("Undead Parish",         "Sunlight Altar",                           1012961),
            new("Undead Parish",         "Near Boar",                                1012965),
            new("Undead Parish",         "Near Cell",                                1012966),

            new("Darkroot Garden",       "Darkroot Garden",                          1202961),
            new("Darkroot Basin",        "Darkroot Basin",                           1602961),

            new("The Depths",            "Depths (Entrance)",                        1002900),
            new("The Depths",            "Depths (Gaping Dragon's Room)",            1002950),
            new("The Depths",            "Depths (Bonfire)",                         1002960),

            new("Blighttown",            "Blighttown (Entrance)",                    1402963),
            new("Blighttown",            "Blighttown (Entrance, repeat)",            1400980),
            new("Blighttown",            "Blighttown (Bridge)",                      1402962),
            new("Blighttown",            "Blighttown (Swamp)",                       1402961),
            new("Quelaag's Domain",      "Quelaag's Domain",                         1402960),

            new("Sen's Fortress",        "Sen's Fortress (Bridge #1)",               1502960),
            new("Sen's Fortress",        "Sen's Fortress (Bridge #2)",               1502962),
            new("Sen's Fortress",        "Sen's Fortress (Bonfire)",                 1502961),

            new("Anor Londo",            "Anor Londo (Entrance)",                    1510980),
            new("Anor Londo",            "Anor Londo (Interior)",                    1512961),
            new("Anor Londo",            "Anor Londo (Lady of the Darkling)",        1512960),
            new("Anor Londo",            "Anor Londo (Gwyndolin)",                   1512962),
            new("Anor Londo",            "Anor Londo (Gwynevere)",                   1512950),

            new("New Londo Ruins",       "New Londo Ruins (Entrance)",               1602951),
            new("New Londo Ruins",       "New Londo Ruins (Entrance, repeat)",       1600980),
            new("New Londo Ruins",       "New Londo Ruins (Pre-Ingward)",            1602960),
            new("The Abyss",             "Abyss (Four Kings)",                       1602950),

            new("The Catacombs",         "Catacombs (Entrance)",                     1302962),
            new("The Catacombs",         "Catacombs (Bonfire #1)",                   1302960),
            new("The Catacombs",         "Catacombs (Bonfire #2)",                   1302961),
            new("Tomb of the Giants",    "Tomb of the Giants (Entrance)",            1312962),
            new("Tomb of the Giants",    "Tomb of the Giants (Bonfire #1)",          1312960),
            new("Tomb of the Giants",    "Tomb of the Giants (Bonfire #2)",          1312961),
            new("Tomb of the Giants",    "Nito",                                     1312950),

            new("Great Hollow",          "Great Hollow",                             1322962),
            new("Great Hollow",          "Great Hollow (repeat)",                    1320980),
            new("Ash Lake",              "Ash Lake (Bonfire #1)",                    1322961),
            new("Ash Lake",              "Ash Lake (Bonfire #2)",                    1322960),

            new("Demon Ruins",           "Demon Ruins (Bonfire #1)",                 1412961),
            new("Demon Ruins",           "Demon Ruins (Bonfire #2)",                 1412962),
            new("Demon Ruins",           "Demon Ruins (Bonfire #3)",                 1412963),
            new("Lost Izalith",          "Lost Izalith (Bonfire #1)",                1412964),
            new("Lost Izalith",          "Lost Izalith (Bonfire #2)",                1412960),
            new("Lost Izalith",          "Bed of Chaos",                             1412950),

            new("Duke's Archives",       "Duke's Archives (Elevator)",               1702962),
            new("Duke's Archives",       "Duke's Archives (Balcony)",                1702960),
            new("Duke's Archives",       "Duke's Archives (Cell)",                   1702961),
            new("Duke's Archives",       "Duke's Archives (Cell, After Seath)",      1702900),
            new("Crystal Cave",          "Crystal Cave (Seath)",                     1702950),

            new("Painted World",         "Painted World (Entrance)",                 1102961),
            new("Painted World",         "Painted World (Bonfire)",                  1102960),
            new("Painted World",         "Painted World (Rope Bridge)",              1102511),

            new("Kiln of the First Flame","Kiln (Entrance)",                         1802961),
            new("Kiln of the First Flame","Kiln (Gwyn)",                             1802130),

            new("Oolacile (DLC)",        "Sanctuary Garden",                         1212963),
            new("Oolacile (DLC)",        "Oolacile Sanctuary",                       1212961),
            new("Oolacile (DLC)",        "Royal Wood",                               1212962),  // approximate
            new("Oolacile (DLC)",        "Oolacile Township",                        1212962),
            new("Oolacile (DLC)",        "Oolacile Township Dungeon",                1212964),
            new("Oolacile (DLC)",        "Chasm of the Abyss (Manus)",               1212950),
        ];
    }
}

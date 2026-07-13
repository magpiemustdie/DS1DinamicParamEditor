namespace DS1ParamEditor
{
    public sealed record GameClass(string Name, byte Id, int SoulLevel, int Vitality, int Attunement, int Endurance, int Strength, int Dexterity, int Resistance, int Intelligence, int Faith);

    public static class ClassData
    {
        public static readonly GameClass[] All =
        [
            new("Warrior",    0, 11, 8,  12, 13, 13, 11, 9,  9,  1),
            new("Knight",     1, 14, 10, 10, 11, 11, 10, 9,  11, 1),
            new("Wanderer",   2, 10, 11, 10, 10, 14, 12, 11, 8,  1),
            new("Thief",      3, 9,  11, 9,  9,  15, 10, 12, 11, 1),
            new("Bandit",     4, 12, 8,  14, 14, 9,  11, 8,  10, 1),
            new("Hunter",     5, 11, 9,  11, 12, 14, 11, 9,  9,  1),
            new("Sorcerer",   6, 8,  15, 8,  9,  11, 8,  15, 8,  1),
            new("Pyromancer", 7, 10, 12, 11, 12, 9,  12, 10, 8,  1),
            new("Cleric",     8, 11, 11, 9,  12, 8,  11, 8,  14, 1),
            new("Deprived",   9, 11, 11, 11, 11, 11, 11, 11, 11, 1),
        ];
    }
}

using System.Collections.Generic;

namespace DS1ParamEditor
{
    public class DSRInfusion
    {
        public string Name { get; }
        public int Value { get; }
        public int MaxUpgrade { get; }
        public bool Restricted { get; }

        public DSRInfusion(string name, int value, int maxUpgrade, bool restricted)
        {
            Name = name; Value = value; MaxUpgrade = maxUpgrade; Restricted = restricted;
        }

        public override string ToString() => Name;

        public static readonly List<DSRInfusion> All = new()
        {
            new("Normal",    000, 15, false),
            new("Chaos",     900, 5,  true),
            new("Crystal",   100, 5,  false),
            new("Divine",    600, 10, false),
            new("Enchanted", 500, 5,  true),
            new("Fire",      800, 10, false),
            new("Lightning", 200, 5,  false),
            new("Magic",     400, 10, false),
            new("Occult",    700, 5,  true),
            new("Raw",       300, 5,  true),
        };
    }
}

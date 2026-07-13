using System;
using System.IO;
using System.Linq;
using SoulsFormats;

string dir = @"D:\DarkSoulsRemastered\map\MapStudio";
string file = Path.Combine(dir, "m12_01_00_00.msb");
var msb = MSB1.Read(file);
var regions = msb.Regions.GetEntries();

Console.WriteLine($"Total regions: {regions.Count}\n");

// Group by shape type or by name prefix
var byShape = regions.GroupBy(r => r.Shape?.GetType().Name ?? "null")
    .OrderByDescending(g => g.Count());
Console.WriteLine("By shape type:");
foreach (var g in byShape)
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();

// Name prefix analysis
var prefixes = regions.Select(r => {
    int idx = r.Name.IndexOf('_');
    return idx > 0 ? r.Name.Substring(0, idx) : r.Name;
}).GroupBy(p => p).OrderByDescending(g => g.Count());

Console.WriteLine("By name prefix (top 30):");
foreach (var g in prefixes.Take(40))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine();
Console.WriteLine("Sample region names (first 30):");
foreach (var r in regions.Take(30))
    Console.WriteLine($"  {r.Name} [{r.Shape?.GetType().Name}]");

// Also count by shape + name prefix combo
Console.WriteLine("\n\nBy shape type:");
foreach (var g in byShape)
    Console.WriteLine($"  {g.Key}: {g.Count()}");

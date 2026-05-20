using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class Program
{
    // 3 variables
    static string? var1 = null;
    static string? var2 = null;
    static string? var3 = null;

    // 1 list with numbers
    static List<int> numbers = new List<int>();

    static void Main()
    {
        LoadConfig("w.prop");

        Console.WriteLine("Loaded Variables from Config:");
        Console.WriteLine($"var1: {var1 ?? "null"}");
        Console.WriteLine($"var2: {var2 ?? "null"}");
        Console.WriteLine($"var3: {var3 ?? "null"}");
        Console.WriteLine();

        Console.WriteLine("List Numbers:");
        foreach (int num in numbers)
        {
            Console.Write(num + " ");
        }
        Console.WriteLine();
    }

    static void LoadConfig(string fileName)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            // Fallback to checking active directory or parent directories if run from project folder
            path = Path.Combine(Directory.GetCurrentDirectory(), "testfolder", fileName);
            if (!File.Exists(path))
            {
                path = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                if (!File.Exists(path)) return;
            }
        }

        foreach (var line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length < 2) continue;

            string name = parts[0].Trim();
            string val = parts[1].Trim();

            if (val.EndsWith(";")) val = val.Substring(0, val.Length - 1).Trim();

            int openParen = name.IndexOf('(');
            int closeParen = name.IndexOf(')');
            if (openParen >= 0 && closeParen > openParen)
            {
                name = name.Substring(0, openParen).Trim();
            }

            if (name == "var1") var1 = val == "null" ? null : val;
            else if (name == "var2") var2 = val == "null" ? null : val;
            else if (name == "var3") var3 = val == "null" ? null : val;
            else if (name == "list0" && val.StartsWith("[") && val.EndsWith("]"))
            {
                string content = val.Substring(1, val.Length - 2);
                numbers = content.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                                 .ToList();
            }
        }
    }
}

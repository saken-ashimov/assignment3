using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

public static class AttendanceLog
{
    public static void Record(string path, string name)
    {
        string line = name + "," + DateTime.Now;
        File.WriteAllText(path, line);
    }

    public static string ReadAll(string path)
    {
        return File.ReadAllText(path);
    }
}

public record AttendanceEntry(string Name, DateTime CheckInTime);

public static class EntryFormat
{
    private const char Separator = '|';

    public static string ToLine(AttendanceEntry entry)
    {
        if (entry.Name.Contains('\n') || entry.Name.Contains('\r'))
            throw new ArgumentException("A name cannot contain a line break.");
        return entry.CheckInTime.ToString("O", CultureInfo.InvariantCulture) + Separator + entry.Name;
    }

    public static AttendanceEntry FromLine(string line)
    {
        int cut = line.IndexOf(Separator);
        if (cut < 0) throw new FormatException("No separator in line: " + line);
        string timeText = line.Substring(0, cut);
        string name = line.Substring(cut + 1);
        if (!DateTime.TryParseExact(timeText, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time))
            throw new FormatException("Bad time in line: " + line);
        return new AttendanceEntry(name, time);
    }
}

public interface IAttendanceStore
{
    void Append(IEnumerable<AttendanceEntry> entries);
    IEnumerable<AttendanceEntry> Load();
}

public class TextLogStore : IAttendanceStore
{
    private readonly string path;

    public TextLogStore(string path)
    {
        this.path = path;
    }

    public void Append(IEnumerable<AttendanceEntry> entries)
    {
        var lines = entries.Select(EntryFormat.ToLine).ToList();
        using (var writer = new StreamWriter(path, true))
        {
            foreach (string line in lines)
                writer.WriteLine(line);
        }
    }

    public IEnumerable<AttendanceEntry> Load()
    {
        var result = new List<AttendanceEntry>();
        if (!File.Exists(path)) return result;
        using (var reader = new StreamReader(path))
        {
            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line)) continue;
                result.Add(EntryFormat.FromLine(line));
            }
        }
        return result;
    }
}

public class JsonStore : IAttendanceStore
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };
    private readonly string path;

    public JsonStore(string path)
    {
        this.path = path;
    }

    public void Append(IEnumerable<AttendanceEntry> entries)
    {
        var all = Load().ToList();
        all.AddRange(entries);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(stream, all, Options);
        }
        File.Move(temp, path, true);
    }

    public IEnumerable<AttendanceEntry> Load()
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return new List<AttendanceEntry>();
        using (var stream = File.OpenRead(path))
        {
            return JsonSerializer.Deserialize<List<AttendanceEntry>>(stream) ?? new List<AttendanceEntry>();
        }
    }
}

class Program
{
    static void Main()
    {
        Task1();
        Console.WriteLine();
        Task2();
        Console.WriteLine();
        Task3();
        Console.WriteLine();
        Task4();
    }

    static void Reset(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    static void Check(string what, bool ok)
    {
        Console.WriteLine((ok ? "ok: " : "FAILED: ") + what);
    }

    static DateTime At(int hour, int minute)
    {
        return new DateTime(2026, 10, 4, hour, minute, 0);
    }

    static string Describe(AttendanceEntry entry)
    {
        return entry.Name + " at " + entry.CheckInTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    static bool IsRejected(string line)
    {
        try { EntryFormat.FromLine(line); return false; }
        catch (FormatException) { return true; }
    }

    static void Task1()
    {
        string path = "task1_starter.txt";
        Console.WriteLine("Task 1. Looking at the starter code.");

        Reset(path);
        AttendanceLog.Record(path, "Aigerim");
        AttendanceLog.Record(path, "Dias");
        int lines = File.ReadAllLines(path).Length;
        Console.WriteLine($"Bug 1: two check-ins in a row. Expected: 2 lines in the file. Actual: {lines}.");

        Reset(path);
        string actual;
        try { actual = "returned \"" + AttendanceLog.ReadAll(path) + "\""; }
        catch (Exception ex) { actual = "threw " + ex.GetType().Name; }
        Console.WriteLine($"Bug 2: ReadAll on a log that does not exist yet. Expected: empty text. Actual: {actual}.");

        AttendanceLog.Record(path, "Doe, Jane");
        string[] parts = AttendanceLog.ReadAll(path).Split(',');
        Console.WriteLine($"Bug 3: a name with a comma. Expected: 2 fields (name, time). Actual: {parts.Length} fields: {string.Join(" / ", parts)}.");

        Reset(path);
        AttendanceLog.Record(path, "Aigerim");
        bool endsWithBreak = File.ReadAllText(path).EndsWith("\n");
        Console.WriteLine($"Bug 4: the written line should end with a line break. Expected: True. Actual: {endsWithBreak}.");

        Console.WriteLine("Bug 1 comes from the line File.WriteAllText(path, line) in Record: it creates the file from scratch on every call, so the new text replaces everything that was there.");
    }

    static void Task2()
    {
        Console.WriteLine("Task 2. The entry format and the text log.");

        var entry = new AttendanceEntry("Aigerim", At(9, 30));
        string line = EntryFormat.ToLine(entry);
        Console.WriteLine("One entry as a line: " + line);
        Check("the line turns back into an equal entry", EntryFormat.FromLine(line) == entry);

        var tricky = new AttendanceEntry("Doe, Jane | Jr.", At(9, 45));
        Check("a name with a comma and a bar survives the round trip", EntryFormat.FromLine(EntryFormat.ToLine(tricky)) == tricky);
        Check("a broken line is rejected", IsRejected("this is not a log line"));

        var renamed = entry with { Name = "Dias" };
        Check("changing a name creates a new entry and leaves the old one alone", renamed.Name == "Dias" && entry.Name == "Aigerim");

        string path = "task2_log.txt";
        Reset(path);
        var store = new TextLogStore(path);
        Check("a missing file loads as an empty collection", !store.Load().Any());

        store.Append(new[] { entry, tricky });
        store.Append(new[] { renamed });
        var loaded = store.Load().ToList();
        Check("three entries after two appends", loaded.Count == 3);
        Check("the first two entries are still there, in order", loaded[0] == entry && loaded[1] == tricky);
        Console.WriteLine("The file on disk:");
        foreach (string fileLine in File.ReadAllLines(path))
            Console.WriteLine("  " + fileLine);
    }

    static List<AttendanceEntry> Demo(IAttendanceStore store, string label)
    {
        Console.WriteLine($"Using {label}:");
        Console.WriteLine($"  before adding anything there are {store.Load().Count()} entries");
        store.Append(new[] { new AttendanceEntry("Aigerim", At(9, 0)), new AttendanceEntry("Dias", At(9, 5)) });
        store.Append(new[] { new AttendanceEntry("Madina", At(9, 10)) });
        var loaded = store.Load().ToList();
        Console.WriteLine($"  after two appends there are {loaded.Count} entries:");
        foreach (var entry in loaded)
            Console.WriteLine("    " + Describe(entry));
        return loaded;
    }

    static void Task3()
    {
        string textPath = "task3_log.txt";
        string jsonPath = "task3_log.json";
        Reset(textPath);
        Reset(jsonPath);

        Console.WriteLine("Task 3. The same demo function with two different stores.");
        var fromText = Demo(new TextLogStore(textPath), "the text log");
        var fromJson = Demo(new JsonStore(jsonPath), "the JSON file");

        Check("both stores returned the same number of entries", fromText.Count == fromJson.Count);
        Check("both stores returned the same entries in the same order", fromText.SequenceEqual(fromJson));
        Console.WriteLine("The JSON file on disk:");
        Console.WriteLine(File.ReadAllText(jsonPath));
    }

    static List<AttendanceEntry> RunOnce(IAttendanceStore store, IEnumerable<AttendanceEntry> newEntries)
    {
        store.Append(newEntries);
        return store.Load().ToList();
    }

    static void Task4()
    {
        string textPath = "task4_log.txt";
        string jsonPath = "task4_log.json";
        Reset(textPath);
        Reset(jsonPath);
        Console.WriteLine("Task 4. Two program runs against both stores.");

        var runs = new List<AttendanceEntry[]>
        {
            new[] { new AttendanceEntry("Aigerim", At(9, 0)), new AttendanceEntry("Dias", At(9, 5)) },
            new[] { new AttendanceEntry("Madina", At(9, 10)), new AttendanceEntry("Timur", At(9, 15)) }
        };

        var previous = new List<AttendanceEntry>();
        for (int i = 0; i < runs.Count; i++)
        {
            int run = i + 1;
            var fromText = RunOnce(new TextLogStore(textPath), runs[i]);
            var fromJson = RunOnce(new JsonStore(jsonPath), runs[i]);

            Console.WriteLine($"Run {run}: {runs[i].Length} new check-ins, the text log now has {fromText.Count} entries and the JSON file has {fromJson.Count}.");
            foreach (var entry in fromText)
                Console.WriteLine("  " + Describe(entry));

            Check($"run {run}: the text log kept the earlier entries", fromText.Take(previous.Count).SequenceEqual(previous));
            Check($"run {run}: the JSON file kept the earlier entries", fromJson.Take(previous.Count).SequenceEqual(previous));
            Check($"run {run}: both stores hold exactly the same entries", fromText.SequenceEqual(fromJson));
            Check($"run {run}: no entry appears twice", fromText.Distinct().Count() == fromText.Count && fromJson.Distinct().Count() == fromJson.Count);
            Check($"run {run}: the total is {previous.Count + runs[i].Length}", fromText.Count == previous.Count + runs[i].Length);
            previous = fromText;
        }
    }
}

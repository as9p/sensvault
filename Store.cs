using System.IO;
using System.Text.Json;

namespace SensVault;

public class AppData
{
    public List<SensProfile> Profiles { get; set; } = [];
    public List<Game> CustomGames { get; set; } = [];

    /// <summary>DPI the entry form was left on, so a new session starts on your mouse.</summary>
    public double LastDpi { get; set; } = 800;

    /// <summary>Vault columns currently hidden, by key. Absent or empty shows everything,
    /// so a file written before the column picker existed opens with all six.</summary>
    public List<string> HiddenColumns { get; set; } = [];

    /// <summary>Games left out of every picker, by name. Stored as the names rather than as
    /// whole <see cref="Game"/> entries because the library itself is compiled in and gets
    /// rebuilt on each launch -- only the choice about it belongs in the file. A name here
    /// that no longer matches any game is simply ignored, so a renamed or dropped built-in
    /// costs nothing. Empty shows the whole library, which is what a file written before
    /// Settings &gt; Games existed opens as.</summary>
    public List<string> HiddenGames { get; set; } = [];

    /// <summary>Hand-ordered DPI presets. Nothing reads or writes these right now -- the
    /// picker that used them is shelved -- but the property stays so a saved list
    /// round-trips through the file untouched and survives until the feature comes back.</summary>
    public List<double> DpiPresets { get; set; } = [400, 800, 1600, 3200];

    /// <summary>Zoom on the vault side of the window, 0.7 to 2.0.</summary>
    public double VaultZoom { get; set; } = 1;

    /// <summary>Whether the left entry panel was hidden, leaving only the vault.</summary>
    public bool PanelCollapsed { get; set; }

    /// <summary>Whether the filter bar above the grid was folded away to its rail. The filter
    /// and search it holds still apply while it is down, so this travels with
    /// <see cref="LastFilter"/> rather than replacing it.</summary>
    public bool BarCollapsed { get; set; }

    /// <summary>Hand-dragged column widths, keyed the same as <see cref="HiddenColumns"/>.
    /// A column with no entry keeps whatever the XAML asked for.</summary>
    public Dictionary<string, ColumnWidth> ColumnWidths { get; set; } = [];

    /// <summary>Game the vault was filtered to at close. Empty means all games.</summary>
    public string LastFilter { get; set; } = "";

    /// <summary>Whether the scrollbars are hidden throughout. The wheel still scrolls -- this
    /// takes away the bars, not the scrolling.</summary>
    public bool HideScrollBars { get; set; }

    /// <summary>Whether the status line under the vault is folded away. It is also where
    /// warnings surface, so hiding it is a real trade and the settings page says so.</summary>
    public bool HideStatusBar { get; set; }

    /// <summary>Applied theme, by name. Stored as the name for the same reason
    /// <see cref="HiddenGames"/> is: the palettes are compiled in, and only the choice about
    /// them belongs in the file. A name that no longer matches any theme falls back to the
    /// default, so a renamed or dropped theme costs nothing. Empty is the default too, which
    /// is what a file written before Settings &gt; Themes existed opens as.</summary>
    public string Theme { get; set; } = "";
}

/// <summary>
/// A column's width plus whether it was proportional. Star and pixel widths are not
/// interchangeable -- restoring a star column as a fixed pixel width would freeze it and
/// stop it sharing the leftover space when the window resizes -- so the unit travels with
/// the number.
/// </summary>
public class ColumnWidth
{
    public double Value { get; set; }
    public bool Star { get; set; }
}

public static class Store
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Folder { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SensVault"
        );

    public static string FilePath { get; } = Path.Combine(Folder, "data.json");

    /// <summary>How many parked copies of a corrupt file are kept. Older ones go first.</summary>
    private const int KeepBad = 5;

    /// <summary>Set by <see cref="Load()"/> when the file could not be read, so the window
    /// can say so on the status line. Null when the load was clean.</summary>
    public static string? LoadProblem { get; private set; }

    public static AppData Load() => Load(FilePath);

    internal static AppData Load(string path)
    {
        LoadProblem = null;
        DeleteStaleTemps(path);
        try
        {
            if (!File.Exists(path))
                return new AppData();
            return JsonSerializer.Deserialize<AppData>(File.ReadAllText(path)) ?? new AppData();
        }
        catch (Exception)
        {
            // A corrupt file must not cost you the whole vault -- park it and start clean.
            // Each parked copy gets its own name, so a second bad start never overwrites the
            // copy from the first.
            try
            {
                var parked = ParkBad(path);
                LoadProblem =
                    $"data.json could not be read and was saved as {Path.GetFileName(parked)}. Starting empty.";
            }
            catch (Exception ex)
            {
                LoadProblem = "data.json could not be read or set aside: " + ex.Message;
            }
            return new AppData();
        }
    }

    public static void Save(AppData data) => Save(data, FilePath);

    internal static void Save(AppData data, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, JsonSerializer.Serialize(data, Options));
    }

    /// <summary>Writes to a temp file beside <paramref name="path"/> and moves it over, so a
    /// crash mid-write never leaves a half-written file.</summary>
    private static void WriteAtomic(string path, string contents)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Flush to disk before the move. Without this, a power cut right after the
            // move can leave data.json full of zero bytes on NTFS.
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch { }
            throw;
        }
    }

    /// <summary>Moves the unreadable file to a timestamped .bad name that is not taken yet,
    /// then trims the parked copies down to <see cref="KeepBad"/>. The stamp sorts by name,
    /// so the oldest copies are the first names in order. A plain data.json.bad left by an
    /// older version is never touched.</summary>
    private static string ParkBad(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var target = $"{path}.{stamp}-01.bad";
        for (var n = 2; File.Exists(target); n++)
            target = $"{path}.{stamp}-{n:D2}.bad";
        File.Move(path, target);

        try
        {
            var parked = Siblings(path, ".bad")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var f in parked.Take(Math.Max(0, parked.Count - KeepBad)))
                File.Delete(f);
        }
        catch { }

        return target;
    }

    /// <summary>A crash between writing the temp file and moving it over leaves the temp
    /// behind. The real file is still whole then, so the leftover is just clutter.</summary>
    private static void DeleteStaleTemps(string path)
    {
        try
        {
            foreach (var f in Siblings(path, ".tmp"))
                File.Delete(f);
        }
        catch { }
    }

    /// <summary>Files named <c>{path}.{something}{suffix}</c>, with something not empty.
    /// Filtered here rather than with a search pattern, because Windows wildcard rules for
    /// dots would also match names this does not mean.</summary>
    private static IEnumerable<string> Siblings(string path, string suffix)
    {
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            return [];
        var prefix = Path.GetFileName(path) + ".";
        return Directory
            .GetFiles(dir, Path.GetFileName(path) + "*")
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.Length > prefix.Length + suffix.Length
                    && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            });
    }
}

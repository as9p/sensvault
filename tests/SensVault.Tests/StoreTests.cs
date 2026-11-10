using System.IO;

namespace SensVault.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "SensVault.Tests",
        Guid.NewGuid().ToString("N")
    );

    private string DataPath => Path.Combine(_dir, "data.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var data = new AppData
        {
            LastDpi = 1600,
            LastFilter = "Valorant",
            HiddenColumns = ["Notes"],
            PanelCollapsed = true,
        };

        Store.Save(data, DataPath);
        var loaded = Store.Load(DataPath);

        Assert.Null(Store.LoadProblem);
        Assert.Equal(1600, loaded.LastDpi);
        Assert.Equal("Valorant", loaded.LastFilter);
        Assert.Equal(["Notes"], loaded.HiddenColumns);
        Assert.True(loaded.PanelCollapsed);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        Store.Save(new AppData(), DataPath);
        Store.Save(new AppData { LastDpi = 400 }, DataPath);

        Assert.Equal(["data.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_removes_a_temp_file_left_by_a_crash()
    {
        Store.Save(new AppData { LastDpi = 400 }, DataPath);
        File.WriteAllText(DataPath + ".0123456789abcdef.tmp", "{ half");

        var loaded = Store.Load(DataPath);

        Assert.Equal(400, loaded.LastDpi);
        Assert.Equal(["data.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Corrupt_file_is_parked_as_bad_and_load_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(DataPath, "{ \"LastDpi\": 16");

        var loaded = Store.Load(DataPath);

        Assert.Equal(800, loaded.LastDpi);
        Assert.False(File.Exists(DataPath));
        var bad = Assert.Single(Directory.GetFiles(_dir, "*.bad"));
        Assert.Equal("{ \"LastDpi\": 16", File.ReadAllText(bad));
        Assert.NotNull(Store.LoadProblem);
        Assert.Contains(Path.GetFileName(bad), Store.LoadProblem);
    }

    [Fact]
    public void A_second_corrupt_file_does_not_overwrite_the_first_bad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(DataPath + ".bad", "legacy");
        File.WriteAllText(DataPath, "first");
        Store.Load(DataPath);
        File.WriteAllText(DataPath, "second");
        Store.Load(DataPath);

        var contents = Directory.GetFiles(_dir, "*.bad").Select(File.ReadAllText).Order().ToArray();
        Assert.Equal(["first", "legacy", "second"], contents);
    }

    [Fact]
    public void Only_the_newest_parked_copies_are_kept()
    {
        Directory.CreateDirectory(_dir);
        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(DataPath, $"bad {i}");
            Store.Load(DataPath);
        }

        var contents = Directory.GetFiles(_dir, "*.bad").Select(File.ReadAllText).Order().ToArray();
        Assert.Equal(["bad 3", "bad 4", "bad 5", "bad 6", "bad 7"], contents);
    }
}

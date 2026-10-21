namespace ProtoFast.DocumentImport.HistoryLoader;

public sealed class HistoryLoaderOptions
{
    /// <summary>Loads at most this many new books; 0 loads the whole selection.</summary>
    public int Limit { get; set; }

    public int Workers { get; set; } = 4;

    /// <summary>Downloaded texts are kept here, so a rerun or a rebuilt database downloads nothing twice.</summary>
    public string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "protofast", "gutenberg");

    /// <summary>Each worker's pause after a download, to stay polite to Gutenberg's servers.</summary>
    public TimeSpan DownloadDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}

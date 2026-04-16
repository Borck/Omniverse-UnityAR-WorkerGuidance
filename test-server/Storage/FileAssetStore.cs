namespace GuidanceAdminServer.Storage;

/// <summary>
/// Handles file storage for uploaded GLB models and Vuforia target archives.
/// Files are stored under <c>data/assets/{version}/</c> and
/// <c>data/targets/{version}/</c> respectively.
/// </summary>
public sealed class FileAssetStore
{
    private readonly string _dataRoot;

    public FileAssetStore(string dataRoot)
    {
        _dataRoot = dataRoot;
        Directory.CreateDirectory(Path.Combine(dataRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(dataRoot, "targets"));
        Directory.CreateDirectory(Path.Combine(dataRoot, "manifests"));
    }

    /// <summary>Saves a GLB file to <c>data/assets/{version}/{fileName}</c>.</summary>
    public async Task<string> SaveGlbAsync(IFormFile file, string assetVersion)
    {
        var dir = Path.Combine(_dataRoot, "assets", assetVersion);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, file.FileName);
        await using var stream = File.Create(dest);
        await file.CopyToAsync(stream);
        return dest;
    }

    /// <summary>Saves a Vuforia target archive to <c>data/targets/{version}/{fileName}</c>.</summary>
    public async Task<string> SaveTargetAsync(IFormFile file, string targetVersion)
    {
        var dir = Path.Combine(_dataRoot, "targets", targetVersion);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, file.FileName);
        await using var stream = File.Create(dest);
        await file.CopyToAsync(stream);
        return dest;
    }

    /// <summary>Writes a manifest JSON file to <c>data/manifests/{jobId}.manifest.json</c>.</summary>
    public async Task SaveManifestAsync(string jobId, string json)
    {
        var path = Path.Combine(_dataRoot, "manifests", $"{jobId}.manifest.json");
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>Returns the full path to a GLB asset file.</summary>
    public string GetGlbPath(string assetVersion, string fileName) =>
        Path.Combine(_dataRoot, "assets", assetVersion, fileName);

    /// <summary>Returns the full path to a Vuforia target file.</summary>
    public string GetTargetPath(string targetVersion, string fileName) =>
        Path.Combine(_dataRoot, "targets", targetVersion, fileName);
}

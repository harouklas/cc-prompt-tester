using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using PromptTester.Wpf.Models;

namespace PromptTester.Wpf.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _profilesFolder;
    private readonly string _appRoot;

    public ProfileStore(string appRoot)
    {
        _appRoot = appRoot;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesFolder = Path.Combine(localAppData, "CChaliotis", "PromptTester", "Profiles");
        Directory.CreateDirectory(_profilesFolder);
        ImportProfiles(Path.Combine(appRoot, "Profiles"));
    }

    public string ProfilesFolder => _profilesFolder;

    public IReadOnlyList<string> GetProfileNames()
    {
        return Directory.EnumerateFiles(_profilesFolder, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ProfileSettings Load(string name)
    {
        var path = GetProfilePath(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Profile was not found: {name}", path);
        }

        var json = File.ReadAllText(path);
        ProfileSettings profile;
        try
        {
            profile = JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Profile file is empty: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Profile contains invalid JSON: {path}", ex);
        }

        profile.ImageFolderPath = ResolveAppRelativePath(profile.ImageFolderPath);
        profile.ExportFilePath = ResolveAppRelativePath(profile.ExportFilePath);
        return profile;
    }

    public void Save(ProfileSettings profile)
    {
        var name = NormalizeName(profile.Name);
        profile.Name = name;

        var path = GetProfilePath(name);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public bool Exists(string name)
    {
        return File.Exists(GetProfilePath(name));
    }

    public void Delete(string name)
    {
        var path = GetProfilePath(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Profile was not found: {name}", path);
        }

        File.Delete(path);
    }

    private string GetProfilePath(string name)
    {
        return Path.Combine(_profilesFolder, $"{NormalizeName(name)}.json");
    }

    private string ResolveAppRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(_appRoot, path));
    }

    private static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Enter a profile name first.");
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalidChar, '-');
        }

        trimmed = Regex.Replace(trimmed, @"\s+", " ").Trim().TrimEnd('.');
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Enter a valid profile name first.");
        }

        return trimmed.Length <= 80 ? trimmed : trimmed[..80].TrimEnd();
    }

    private void ImportProfiles(string sourceProfilesFolder)
    {
        if (!Directory.Exists(sourceProfilesFolder)
            || string.Equals(
                Path.GetFullPath(sourceProfilesFolder),
                Path.GetFullPath(_profilesFolder),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceProfilesFolder, "*.json"))
        {
            var destinationPath = Path.Combine(_profilesFolder, Path.GetFileName(sourcePath));
            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }
        }
    }
}

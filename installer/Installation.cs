using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace RhythiansInstaller;

public static class Installation
{
    public const string Version = "0.2.0-beta.4";
    public static string? InstalledVersion(string root)
    {
        var path = Inside(root, "Rhythians/installed.json");
        if (File.Exists(path)) return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))?.Version ?? "unknown version";
        return File.Exists(Inside(root, "RhythiansRaylib.dll")) ? "an earlier version" : null;
    }
    public const string GameHash = "857C0D71B8CBD0C3F07F9FCD60E007680D5CDE6522C2904FC4DE4D937BEEBD1A";
    public const string LibraryHash = "FBD590391E9BA9CB73C147018AD39CF003132E3C788D1EDAA8E55B946642EC05";
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Owned(string name) => !name.Split('/','\\').Any(part => part is ".." or "." || part.Contains(':')) && (name is "raylib_ogl.dll" or "Rhythians.Uninstall.exe" || name.StartsWith("Rhythians/", StringComparison.Ordinal));

    public static string Inside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("The package contains an invalid path.");
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("The destination contains a redirected file.");
        var parent = Path.GetDirectoryName(target);
        while (parent is not null && parent.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("The destination contains a redirected folder.");
            parent = Path.GetDirectoryName(parent);
        }
        return target;
    }

    public static void Install(string root, Stream payload, string installer)
    {
        root = Path.GetFullPath(root);
        if (Hash(Inside(root, "rhythia.exe")) != GameHash) throw new IOException("This Rhythia build is not supported yet. Your game files were not changed.");
        var library = Inside(root, "raylib_ogl.dll");
        var backup = Inside(root, "RhythiansRaylib.dll");
        if (Hash(File.Exists(backup) ? backup : library) != LibraryHash) throw new IOException("The original graphics library does not match this release.");
        var manifestPath = Inside(root, "Rhythians/installed.json");
        if (File.Exists(manifestPath))
        {
            var previous = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))!;
            if (Hash(library) != previous.Files.GetValueOrDefault("raylib_ogl.dll")) throw new IOException("Another change was made to the graphics library. Restore it before updating.");
        }
        var stage = Inside(root, "Rhythians-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var changed = new List<(string Path, string? Backup)>();
        try
        {
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, true);
            var files = new Dictionary<string, string>();
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                var name = entry.FullName.Replace('\\', '/');
                if (name.Split('/').Any(part => part is ".." or "." || part.Contains(':'))) throw new IOException("The package contains an invalid path.");
                if (name != "raylib_ogl.dll" && !name.StartsWith("Rhythians/", StringComparison.Ordinal)) throw new IOException("Unexpected file in the package.");
                var destination = Inside(stage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
                files[name] = Hash(destination);
            }
            if (!files.ContainsKey("raylib_ogl.dll") || !files.ContainsKey("Rhythians/Rhythians.Bridge.exe")) throw new IOException("The installer package is incomplete.");
            var uninstaller = Inside(stage, "Rhythians.Uninstall.exe");
            File.Copy(installer, uninstaller, true);
            files["Rhythians.Uninstall.exe"] = Hash(uninstaller);
            if (!File.Exists(backup)) File.Copy(library, backup);
            foreach (var name in files.Keys.OrderBy(name => name == "raylib_ogl.dll" ? 1 : 0))
            {
                var destination = Inside(root, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var old = File.Exists(destination) ? Inside(stage, "rollback/" + name) : null;
                if (old is not null) { Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Copy(destination, old); }
                changed.Add((destination, old));
                File.Copy(Inside(stage, name), destination, true);
            }
            var manifest = JsonSerializer.Serialize(new Manifest(Version, files));
            File.WriteAllText(manifestPath + ".tmp", manifest);
            File.Move(manifestPath + ".tmp", manifestPath, true);
        }
        catch
        {
            foreach (var change in changed.AsEnumerable().Reverse()) { if (change.Backup is not null) File.Copy(change.Backup, change.Path, true); else File.Delete(change.Path); }
            throw;
        }
        finally { Directory.Delete(stage, true); }
    }

    public static void Uninstall(string root)
    {
        root = Path.GetFullPath(root);
        var manifestPath = Inside(root, "Rhythians/installed.json");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new IOException("The installation record is missing.");
        foreach (var name in manifest.Files.Keys) { if (!Owned(name)) throw new IOException("The installation record contains an invalid file."); Inside(root, name); }
        var backup = Inside(root, "RhythiansRaylib.dll");
        var library = Inside(root, "raylib_ogl.dll");
        if (Hash(backup) != LibraryHash || Hash(library) != manifest.Files.GetValueOrDefault("raylib_ogl.dll")) throw new IOException("The graphics library changed after installation. Nothing was removed.");
        File.Copy(backup, library, true);
        foreach (var (name, hash) in manifest.Files)
        {
            if (name == "raylib_ogl.dll") continue;
            var path = Inside(root, name);
            if (File.Exists(path) && Hash(path) == hash) File.Delete(path);
        }
        File.Delete(manifestPath);
        File.Delete(backup);
    }
}

public sealed record Manifest(string Version, Dictionary<string, string> Files);

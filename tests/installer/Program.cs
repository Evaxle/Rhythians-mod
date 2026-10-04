using System.IO.Compression;
using RhythiansInstaller;

var root = Path.GetFullPath(args[0]);
var source = Path.GetFullPath(args[1]);
if (Directory.Exists(root)) throw new Exception("Use a new isolated test directory.");
Directory.CreateDirectory(root);
File.Copy(Path.Combine(source, "rhythia.exe"), Path.Combine(root, "rhythia.exe"));
File.Copy(Path.Combine(source, "RhythiansRaylib.dll"), Path.Combine(root, "raylib_ogl.dll"));
var installer = Path.Combine(root, "test-installer.exe");
File.WriteAllText(installer, "test installer");
using var payload = new MemoryStream();
using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true))
{
    foreach (var name in new[] { "raylib_ogl.dll", "Rhythians/Rhythians.Bridge.exe", "Rhythians/assets/logo.png" })
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write("test payload " + name);
    }
}
payload.Position = 0;
Check(Installation.InstalledVersion(root) is null);
Installation.Install(root, payload, installer);
Check(Installation.InstalledVersion(root) == Installation.Version);
Check(File.Exists(Path.Combine(root, "Rhythians.Uninstall.exe")));
Check(Installation.Hash(Path.Combine(root, "RhythiansRaylib.dll")) == Installation.LibraryHash);
payload.Position = 0;
Installation.Install(root, payload, installer);
File.WriteAllText(Path.Combine(root, "Rhythians", "keep.txt"), "user file");
Installation.Uninstall(root);
Check(Installation.InstalledVersion(root) is null);
Check(Installation.Hash(Path.Combine(root, "raylib_ogl.dll")) == Installation.LibraryHash);
Check(!File.Exists(Path.Combine(root, "Rhythians.Uninstall.exe")));
Check(!File.Exists(Path.Combine(root, "Rhythians", "Rhythians.Bridge.exe")));
Check(File.ReadAllText(Path.Combine(root, "Rhythians", "keep.txt")) == "user file");
try { Installation.Inside(root, "../outside.txt"); throw new Exception("Traversal accepted."); } catch (IOException) { }
using var malicious = new MemoryStream();
using (var zip = new ZipArchive(malicious, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("Rhythians/../outside.txt").Open()); writer.Write("invalid"); }
malicious.Position = 0;
try { Installation.Install(root, malicious, installer); throw new Exception("Invalid package accepted."); } catch (IOException) { }
Check(Installation.Hash(Path.Combine(root, "raylib_ogl.dll")) == Installation.LibraryHash);
Console.WriteLine("Install, update, uninstall, original-library restoration, user-file preservation, and invalid-package rejection passed.");

static void Check(bool condition) { if (!condition) throw new Exception("Installer check failed."); }

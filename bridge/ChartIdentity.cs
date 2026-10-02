using System.Security.Cryptography;

namespace Rhythians;

public static class ChartIdentity
{
    public static string Read(string folder, LocalMap map)
    {
        var root = System.IO.Path.GetFullPath(folder) + System.IO.Path.DirectorySeparatorChar;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, map.Path.TrimStart('/', '\\')));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Map path is outside the game data folder.");
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (new string(reader.ReadChars(8)) != "RHYCACHE" || reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported map cache.");
        var metadata = reader.ReadInt32();
        var count = reader.ReadInt32();
        var length = reader.ReadInt32();
        if (count <= 0 || count > 1000000 || count != map.NoteCount || metadata < 0 || length != count * 12 || 24L + metadata + length != file.Length) throw new InvalidDataException("Invalid map cache.");
        file.Position += metadata;
        var notes = new (uint Time, float X, float Y)[count];
        for (var i = 0; i < count; i++)
        {
            notes[i] = (reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle());
            if (!float.IsFinite(notes[i].X) || !float.IsFinite(notes[i].Y)) throw new InvalidDataException("Invalid note position.");
        }
        Array.Sort(notes, (a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        using var bytes = new MemoryStream(length);
        using var writer = new BinaryWriter(bytes);
        foreach (var note in notes)
        {
            writer.Write(note.Time);
            writer.Write(note.X == 0 ? 0f : note.X);
            writer.Write(note.Y == 0 ? 0f : note.Y);
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }
}

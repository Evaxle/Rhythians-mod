using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Rhythians;

public record LocalMap(long Id, long OnlineId, string Title, string Mapper, string Hash, string Path, int NoteCount);
public record GameScore(long Id, long MapId, string Hash, string Player, string CreatedAt, double Accuracy, int Misses, double Speed, bool Spin, int StartFrom, bool Passed, string Mods, string Mode);

public sealed class GameDatabase(string path)
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open();
        return connection;
    }

    public List<LocalMap> Maps()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,COALESCE(OnlineId,0),Title,MappersJson,COALESCE(MapHash,''),Path,NoteCount FROM Maps";
        using var reader = command.ExecuteReader();
        var maps = new List<LocalMap>();
        while (reader.Read()) maps.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6)));
        return maps;
    }

    public long LatestScore()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Id),0) FROM Scores";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public List<GameScore> ScoresAfter(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,COALESCE(MapId,0),COALESCE(BeatmapHash,''),COALESCE(PlayerName,''),CreatedAt,Accuracy,Misses,Speed,Spin,StartFrom,Passed,Mods,COALESCE(Mode,'') FROM Scores WHERE Id > $id ORDER BY Id LIMIT 100";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var scores = new List<GameScore>();
        while (reader.Read()) scores.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetInt32(6), reader.GetDouble(7), reader.GetBoolean(8), reader.GetInt32(9), reader.GetBoolean(10), reader.GetString(11), reader.GetString(12)));
        return scores;
    }
}

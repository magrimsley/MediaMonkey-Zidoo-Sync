using Microsoft.Data.Sqlite;
using System.IO;
namespace MediaMonkeyZidooSync.Services;
public static class PlaylistParentService {
    public sealed record ParentChoice(string Name,string Description,int Descendants);
    public static async Task<IReadOnlyList<ParentChoice>> ListAsync(string database) {
        var path=Environment.ExpandEnvironmentVariables(database);
        if(!File.Exists(path))throw new InvalidDataException("Choose your MediaMonkey database first.");
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="SELECT IDPlaylist,PlaylistName,ParentPlaylist FROM Playlists";
        await using var reader=await command.ExecuteReaderAsync();var rows=new List<(int Id,string Name,int Parent)>();
        while(await reader.ReadAsync())rows.Add((reader.GetInt32(0),reader.IsDBNull(1)?"":reader.GetString(1),reader.IsDBNull(2)?0:reader.GetInt32(2)));
        return rows.Where(row=>!string.IsNullOrWhiteSpace(row.Name)).Select(row=>{
            var visited=new HashSet<int>{row.Id};var queue=new Queue<int>();queue.Enqueue(row.Id);
            while(queue.TryDequeue(out var parent))foreach(var child in rows.Where(x=>x.Parent==parent))if(visited.Add(child.Id))queue.Enqueue(child.Id);
            var names=new List<string>{row.Name};var ancestor=row.Parent;var seen=new HashSet<int>{row.Id};
            while(seen.Add(ancestor)&&rows.FirstOrDefault(x=>x.Id==ancestor) is var up&&up.Id!=0){names.Insert(0,up.Name);ancestor=up.Parent;}
            var count=visited.Count-1;return new ParentChoice(row.Name,string.Join(" > ",names)+$" ({count} child playlists)"+(rows.Count(x=>x.Name==row.Name)>1?" — duplicate name; rename in MediaMonkey":""),count);
        }).OrderBy(row=>row.Description,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

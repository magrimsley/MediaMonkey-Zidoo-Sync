using System.Text;
namespace MediaMonkeyZidooSync.Services;
/// <summary>Matches Windows PowerShell's frozen UTF8Encoding(true) File.WriteAllLines output.</summary>
public static class FrozenM3uContent {
    private static readonly UTF8Encoding EncodingWithBom=new(true);
    public static byte[] Bytes(IEnumerable<string> lines)=>EncodingWithBom.GetPreamble()
        .Concat(EncodingWithBom.GetBytes(string.Join("\r\n",lines)+"\r\n")).ToArray();
}

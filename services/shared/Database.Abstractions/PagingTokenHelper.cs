using System.Text;
using System.Text.Json;

namespace ProtoFast.Database.Abstractions;

public sealed class PagingTokenHelper()
{
    public string Encode<T>(T pagingToken) where T : PagingTokenBase
    {
        string json = JsonSerializer.Serialize(pagingToken);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public T? Decode<T>(string? token) where T : PagingTokenBase
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        string json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        T pagingToken = JsonSerializer.Deserialize<T>(json)
                        ?? throw new InvalidOperationException("Invalid PagingToken");

        return pagingToken;
    }
}

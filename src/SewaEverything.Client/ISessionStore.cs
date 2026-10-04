namespace SewaEverything.Client;

public interface ISessionStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    Task RemoveAsync(string key);
}

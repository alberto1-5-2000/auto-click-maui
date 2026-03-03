using System.Text.Json;

namespace AutoClickMaui.Services;

public class ProfileStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ProfileStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _filePath = Path.Combine(appData, "AutoClickMaui", "profiles.json");
    }

    public async Task<List<AutoClickProfile>> ListAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new List<AutoClickProfile>();
        }

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<AutoClickProfile>();
        }

        return JsonSerializer.Deserialize<List<AutoClickProfile>>(json) ?? new List<AutoClickProfile>();
    }

    public async Task SaveAsync(AutoClickProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("El nombre del perfil es obligatorio.");
        }

        var all = await ListAsync();
        all.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        all.Add(profile);

        var json = JsonSerializer.Serialize(all, _jsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<AutoClickProfile?> LoadAsync(string name)
    {
        var all = await ListAsync();
        return all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task DeleteAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var all = await ListAsync();
        var removed = all.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(all, _jsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await File.WriteAllTextAsync(_filePath, json);
    }
}

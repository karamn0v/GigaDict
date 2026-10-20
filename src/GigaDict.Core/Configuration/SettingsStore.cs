using System.Text.Json;

namespace GigaDict.Core.Configuration;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Файл настроек пуст.");
        var errors = settings.Validate();
        return errors.Count == 0 ? settings : throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var errors = settings.Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Некорректный путь настроек.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        catch
        {
            // Do not leave a stray settings.json.tmp behind a failed save.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}


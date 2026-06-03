// -----------------------------------------------------------------------
// <copyright file="ModelSelectionReader.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Text.Json;
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Reads the "Models" and "Providers" sections of <c>netclaw.json</c> for
/// display on the Model page. Same security gate as <see cref="NetclawConfigReader"/>:
/// only the public config file is opened — never <c>secrets.json</c>.
/// </summary>
public sealed class ModelSelectionReader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly NetclawPaths _paths;

    public ModelSelectionReader(NetclawPaths paths) => _paths = paths;

    public string ConfigPath => _paths.NetclawConfigPath;

    public ModelSelectionReadResult Read()
    {
        if (!File.Exists(_paths.NetclawConfigPath))
            return ModelSelectionReadResult.Missing(_paths.NetclawConfigPath);

        try
        {
            using var stream = File.OpenRead(_paths.NetclawConfigPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            ModelSelection? selection = null;
            if (TryGetProperty(root, "Models", out var modelsElement))
                selection = modelsElement.Deserialize<ModelSelection>(ReadOptions);

            var providerKeys = Array.Empty<string>();
            if (TryGetProperty(root, "Providers", out var providersElement)
                && providersElement.ValueKind == JsonValueKind.Object)
            {
                providerKeys = providersElement.EnumerateObject().Select(p => p.Name).ToArray();
            }

            return ModelSelectionReadResult.Ok(_paths.NetclawConfigPath, selection, providerKeys);
        }
        catch (JsonException ex)
        {
            return ModelSelectionReadResult.ParseError(_paths.NetclawConfigPath, ex.Message);
        }
        catch (IOException ex)
        {
            return ModelSelectionReadResult.ParseError(_paths.NetclawConfigPath, ex.Message);
        }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

public sealed record ModelSelectionReadResult(
    ModelSelectionReadState State,
    string Path,
    ModelSelection? Selection,
    IReadOnlyList<string> ProviderKeys,
    string? ErrorDetail)
{
    public static ModelSelectionReadResult Ok(string path, ModelSelection? selection, IReadOnlyList<string> providerKeys)
        => new(ModelSelectionReadState.Ok, path, selection, providerKeys, null);

    public static ModelSelectionReadResult Missing(string path)
        => new(ModelSelectionReadState.Missing, path, null, [], null);

    public static ModelSelectionReadResult ParseError(string path, string detail)
        => new(ModelSelectionReadState.ParseError, path, null, [], detail);
}

public enum ModelSelectionReadState
{
    Ok,
    Missing,
    ParseError,
}

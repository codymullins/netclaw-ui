// -----------------------------------------------------------------------
// <copyright file="NetclawConfigReader.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Text.Json;
using Netclaw.Configuration;

namespace Netclaw.Web.Services;

/// <summary>
/// Reads <c>netclaw.json</c> for display on the Configuration page.
/// Security gate: only <see cref="NetclawPaths.NetclawConfigPath"/> is ever
/// opened — <c>secrets.json</c> and <c>devices.json</c> are off-limits to the
/// Web host's read surface to avoid leaking credentials onto a rendered page.
/// </summary>
public sealed class NetclawConfigReader
{
    private readonly NetclawPaths _paths;

    public NetclawConfigReader(NetclawPaths paths)
    {
        _paths = paths;
    }

    public string ConfigPath => _paths.NetclawConfigPath;

    public NetclawConfigReadResult Read()
    {
        if (!File.Exists(_paths.NetclawConfigPath))
            return NetclawConfigReadResult.Missing(_paths.NetclawConfigPath);

        try
        {
            var raw = File.ReadAllText(_paths.NetclawConfigPath);
            using var doc = JsonDocument.Parse(raw);
            var formatted = JsonSerializer.Serialize(
                doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            return NetclawConfigReadResult.Ok(_paths.NetclawConfigPath, formatted);
        }
        catch (JsonException ex)
        {
            return NetclawConfigReadResult.ParseError(_paths.NetclawConfigPath, ex.Message);
        }
        catch (IOException ex)
        {
            return NetclawConfigReadResult.ParseError(_paths.NetclawConfigPath, ex.Message);
        }
    }
}

public sealed record NetclawConfigReadResult(
    NetclawConfigReadState State,
    string Path,
    string? Content,
    string? ErrorDetail)
{
    public static NetclawConfigReadResult Ok(string path, string content)
        => new(NetclawConfigReadState.Ok, path, content, null);

    public static NetclawConfigReadResult Missing(string path)
        => new(NetclawConfigReadState.Missing, path, null, null);

    public static NetclawConfigReadResult ParseError(string path, string detail)
        => new(NetclawConfigReadState.ParseError, path, null, detail);
}

public enum NetclawConfigReadState
{
    Ok,
    Missing,
    ParseError
}

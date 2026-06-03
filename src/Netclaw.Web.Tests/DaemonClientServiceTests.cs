// -----------------------------------------------------------------------
// <copyright file="DaemonClientServiceTests.cs" company="Petabridge, LLC">
//      Copyright (C) 2026 - 2026 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------
using System.Net;
using System.Text;
using System.Text.Json;
using Netclaw.Configuration;
using Netclaw.Web.Services;
using Refit;
using Xunit;

namespace Netclaw.Web.Tests;

public sealed class DaemonClientServiceTests : IDisposable
{
    private const string Endpoint = "http://127.0.0.1:65535";

    private readonly string _tempHome;
    private readonly DaemonTargetStore _targets;

    public DaemonClientServiceTests()
    {
        // An endpoint override makes EffectiveEndpoint deterministic without
        // touching env vars or the shared resolver.
        _tempHome = Path.Combine(Path.GetTempPath(), "netclaw-web-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempHome);
        _targets = new DaemonTargetStore(new NetclawPaths(_tempHome), _tempHome);
        _targets.Update(new DaemonTargetOverride { Endpoint = Endpoint });
    }

    [Fact]
    public async Task Returns_unreachable_when_daemon_cannot_be_contacted()
    {
        var service = Build(new ThrowingHandler());

        var result = await service.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DaemonApiResultState.Unreachable, result.State);
        Assert.Equal(Endpoint, result.Endpoint);
    }

    [Fact]
    public async Task Returns_unauthorized_when_daemon_responds_401()
    {
        var service = Build(new StaticHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await service.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DaemonApiResultState.Unauthorized, result.State);
    }

    [Fact]
    public async Task Returns_ok_with_parsed_status()
    {
        var payload = JsonSerializer.Serialize(new
        {
            overall = "healthy",
            build = new { version = "0.0.0", commitHash = "abc", buildTimestamp = "2026-01-01" },
            process = new { pid = 1, startedAtUtc = "2026-01-01T00:00:00Z", uptimeSeconds = 60 },
            connectors = Array.Empty<object>(),
            persistence = new { provider = "Sqlite" },
            telemetry = new { enabled = false, otlpEndpoint = (string?)null, channels = Array.Empty<object>() }
        });

        var service = Build(new StaticHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        }));

        var result = await service.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DaemonApiResultState.Ok, result.State);
        Assert.NotNull(result.Value);
        Assert.Equal("healthy", result.Value!.Overall);
        Assert.Equal("0.0.0", result.Value.Build.Version);
    }

    private DaemonClientService Build(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(Endpoint) };
        var api = RestService.For<IDaemonApi>(httpClient);
        return new DaemonClientService(api, _targets);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempHome, recursive: true); } catch (IOException) { } // slopwatch-ignore: SW003 temp-dir cleanup is best-effort.
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public StaticHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _factory(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNativeSilentTests(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    [Trait("Category", "NativeSilentTransport")]
    public async Task AuthorizedHeadsetAndMicrophoneRunBoundedSilentTransport()
    {
        Assert.True(Environment.GetEnvironmentVariable("WAVESTUDIO_SILENT_START_AUTHORIZED") == "1",
            "A prova ativa exige autorização específica de Start.");
        Assert.True(Environment.GetEnvironmentVariable("WAVESTUDIO_SILENT_PREREQUISITES_CONFIRMED") == "1",
            "Confirme a validação física pertinente, o headset e a ausência de testes/chamadas em andamento.");
        var token = TestContext.Current.CancellationToken;
        var route = await Preflight("WAVESTUDIO_SILENT_CAPTURE_NAME", "WAVESTUDIO_SILENT_HEADSET_NAME", token);
        await using var session = new WasapiSilentSession(route);
        var result = await session.RunAsync(TimeSpan.FromSeconds(5), token);
        Save("wasapi-silent", "native-silent", new { CaptureFormat = route.Capture.Mix, PlaybackFormat = route.Playback.Mix, Result = result });
        Assert.True(result.TransportObserved, "A prova não confirmou transporte silencioso, movimento e liberação dos dois streams.");
    }

    [Fact(Explicit = true)]
    [Trait("Category", "NativeSilentDriftDiagnostic")]
    public async Task AuthorizedEndpointsCollectSilentDriftEvidenceAtNominalRatio()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_DRIFT_DIAGNOSTIC_START_AUTHORIZED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_DRIFT_DIAGNOSTIC_PREREQUISITES_CONFIRMED"));

        var token = TestContext.Current.CancellationToken;
        var route = await Preflight("WAVESTUDIO_DRIFT_DIAGNOSTIC_CAPTURE_NAME", "WAVESTUDIO_DRIFT_DIAGNOSTIC_HEADSET_NAME", token);
        await using var session = new WasapiSilentSession(route, nativeDriftDiagnostic: true);
        var duration = WasapiDriftObservation.MaximumDuration;
        var result = await session.RunAsync(duration, token);
        var diagnostic = session.ReadDriftReport();
        var observation = diagnostic?.Observation;
        bool checkpointsConfirmed = observation?.CheckpointCoverageConfirmed(result.Observation100ns) == true;
        bool prolongedNominalTransport = result.TransportObserved && result.Capture.CaptureTiming is { Confirmed: true } &&
            result.AlwaysSilent && !result.ResourcesRetained && result.DroppedFrames == 0 && result.UnderrunFrames == 0 &&
            checkpointsConfirmed;
        Save("wasapi-drift-diagnostic", "native-drift", new
        {
            CaptureFormat = route.Capture.Mix, PlaybackFormat = route.Playback.Mix,
            RequestedDuration100ns = duration.Ticks, ProlongedNominalTransport = prolongedNominalTransport,
            CheckpointsConfirmed = checkpointsConfirmed, Result = result, Diagnostic = diagnostic
        });

        Assert.True(prolongedNominalTransport,
            "O transporte prolongado exige SILENT, continuidade, duração, cortes coerentes, zero perdas e limpeza.");
        Assert.NotNull(diagnostic);
        Assert.False(diagnostic.CorrectionEnabled);
        Assert.Equal(0, diagnostic.TrackingUpdates);
        Assert.NotNull(observation);
        Assert.False(observation.CorrectionSeen);
    }


    [Fact(Explicit = true)]
    [Trait("Category", "NativeSilentBoundedNominal")]
    public async Task AuthorizedEndpointsRunBoundedSilentNominalTransport()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_NOMINAL_SILENT_START_AUTHORIZED"));
        Assert.Equal("1", Environment.GetEnvironmentVariable("WAVESTUDIO_NOMINAL_SILENT_PREFLIGHT_CONFIRMED"));
        var token = TestContext.Current.CancellationToken;
        var route = await Preflight("WAVESTUDIO_NOMINAL_SILENT_CAPTURE_NAME", "WAVESTUDIO_NOMINAL_SILENT_HEADSET_NAME", token);
        await using var session = WasapiNominalSession.CreateSilentProof(route);
        var result = await session.RunAsync(WasapiNominalSession.MaximumDuration, token);
        Save("wasapi-nominal-silent", "native-nominal", new { CaptureFormat = route.Capture.Mix, PlaybackFormat = route.Playback.Mix, Result = result });
        Assert.True(result.NominalObserved && result.RenderSilent && result.PacketRateConfirmed,
            "A prova nominal exige integridade de transporte, cortes frescos, saída SILENT e cleanup confirmado.");
        Assert.False(result.DriftPrecisionAvailable);
        Assert.False(result.Cleanup.TransportObserved);
        Assert.False(result.Cleanup.DriftControlEnabled);
    }

    internal static async Task<TransportRoute> Preflight(string captureName, string playbackName, CancellationToken token)
    {
        AssertNoPreviewProcess();
        var catalog = await new CoreAudioDeviceService().QueryAsync(token);
        async Task<WasapiEndpointInfo> Probe(EndpointDirection direction, string variable)
        {
            var name = Environment.GetEnvironmentVariable(variable);
            Assert.False(string.IsNullOrWhiteSpace(name), "Selecione por nome exato um dispositivo ativo da direção solicitada.");
            var matches = catalog.Devices.Where(d => d.Direction == direction && d.State == EndpointState.Active
                && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            Assert.True(matches.Length == 1, $"A seleção de {direction} deve identificar exatamente um dispositivo ativo.");
            var endpoint = new TransportEndpoint(matches[0].Id, direction, 1);
            var probe = await WasapiEndpointProbe.QueryAsync(endpoint, token);
            Assert.True(probe.Evidence == FormatEvidence.ExactCapability && probe.Error is null && probe.Mix is not null,
                $"Capacidade exata indisponível para {direction}.");
            return probe;
        }

        var capture = await Probe(EndpointDirection.Capture, captureName);
        var playback = await Probe(EndpointDirection.Playback, playbackName);
        AssertNoPreviewProcess();
        return new(capture, playback);
    }

    private void Save(string folder, string prefix, object evidence)
    {
        // O relatório registra somente formatos, evidências numéricas e rótulos fixos.
        var report = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(report);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts", folder));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}.json"), report);
    }

    internal static void AssertNoPreviewProcess()
    {
        var processes = Process.GetProcessesByName("WaveStudio");
        try { Assert.True(processes.Length == 0, "Feche o Wave Studio normalmente antes da prova."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}

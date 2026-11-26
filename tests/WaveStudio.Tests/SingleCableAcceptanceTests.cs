using System.Buffers.Binary;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class SingleCableAcceptanceTests
{
    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-adapter")]
    [InlineData("wrong-generation")]
    [InlineData("wrong-format")]
    public void RefusesUnconfirmedTopology(string fault)
    {
        var graph = Graph();
        var end = graph.Cable.Capture;
        var altered = fault switch
        {
            "wrong-id" => end with { Info = end.Info with { Endpoint = end.Endpoint with { Id = "CABO-RENDER" } } },
            "wrong-adapter" => end with { AdapterId = "outro" },
            "wrong-generation" => end with { Info = end.Info with { Endpoint = end.Endpoint with { Generation = 3 } } },
            _ => end with { Info = end.Info with { Mix = end.Info.Mix! with { BlockAlign = 1 } } }
        };
        var wrong = graph with { Cable = graph.Cable with { Capture = altered } };
        if (fault == "wrong-format") Assert.Throws<NotSupportedException>(wrong.Validate);
        else Assert.Throws<ArgumentException>(wrong.Validate);
        var shared = new ExternalRouteTopology(graph.Cable, graph.Cable, graph.Headset, graph.Microphone);
        Assert.Throws<ArgumentException>(shared.Validate);
    }

    [Fact]
    public async Task TransfersBothFlowsInOrderWithFreshGenerationAndConfirmedRestoration()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var graph = Graph();
        var guard = new TransportActivityGuard();
        await using var batch = new SingleCableAcceptance(graph, Original(), stop.Token, guard);
        Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Microphone, batch.MicrophoneTopology, batch.MicrophoneSelections, true, () => true));
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Browser, graph, batch.MicrophoneSelections, true, () => true));
        var browser = await RunFlow(batch, ExternalFlow.Browser, stop.Token);
        Assert.True(browser.Passed);
        Assert.Equal(1, graph.Cable.Capture.Endpoint.Generation);
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Microphone, batch.MicrophoneTopology, batch.MicrophoneSelections, true, () => true));
        Assert.Equal(ExternalRestoreState.Restore, batch.Restoration(batch.BrowserSelections).Browser);
        Assert.Equal(ExternalRestoreState.Conflict, batch.Restoration(batch.MicrophoneSelections).Consumer);
        Assert.Equal(ExternalRestoreState.AlreadyRestored, batch.Restoration(Original()).Browser);

        var quiet = await ObserveQuiet(batch, "quiet");
        Assert.True(quiet.Passed, quiet.Failure);
        Assert.Equal(2, batch.QuietTopology.Cable.Capture.Endpoint.Generation);
        Assert.Equal(3, batch.MicrophoneTopology.Cable.Render.Endpoint.Generation);
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Microphone, graph, batch.MicrophoneSelections, true, () => true));
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Microphone, batch.MicrophoneTopology, batch.BrowserSelections, true, () => true));
        var mic = await RunFlow(batch, ExternalFlow.Microphone, stop.Token);
        Assert.True(mic.Passed);
        Assert.Equal(SingleCableState.FinalRestore, batch.State);
        Assert.False(batch.FinishRestoration(batch.MicrophoneSelections).Complete);
        Assert.True(batch.FinishRestoration(Original()).Complete);
        Assert.Equal(SingleCableState.Complete, batch.State);
        await batch.DisposeAsync();
        using var released = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(released);
    }

    [Theory]
    [InlineData("residual")]
    [InlineData("producer")]
    [InlineData("no-data")]
    [InlineData("stalled-tail")]
    [InlineData("release")]
    [InlineData("stop")]
    [InlineData("cancel")]
    [InlineData("timestamp")]
    [InlineData("disconnect")]
    public async Task QuietProbeBlocksMicrophoneWhenEvidenceOrCleanupFails(string fault)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var guard = new TransportActivityGuard();
        await using var batch = new SingleCableAcceptance(Graph(), Original(), stop.Token, guard);
        Assert.True((await RunFlow(batch, ExternalFlow.Browser, stop.Token)).Passed);
        var quiet = await ObserveQuiet(batch, fault, stop);
        Assert.False(quiet.Passed);
        string expected = fault switch
        {
            "residual" => "Quiet/ResidualSignal",
            "producer" => "Quiet/ActiveProducer",
            "no-data" or "stalled-tail" => "Quiet/InsufficientEvidence",
            "release" => "Quiet/ReleaseBuffer",
            "stop" => "Quiet/Stop",
            "cancel" => "Quiet/Canceled",
            "timestamp" => "Quiet/Packet",
            _ => "Next"
        };
        Assert.Equal(expected, quiet.Failure);
        Assert.Equal(SingleCableState.Failed, batch.State);
        int opens = 0;
        Assert.ThrowsAny<Exception>(() => batch.Begin(ExternalFlow.Microphone, batch.MicrophoneTopology, batch.MicrophoneSelections, true,
            () => true, _ => { opens++; throw new Exception(); }));
        Assert.Equal(0, opens);
        await batch.DisposeAsync();
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        if (fault is "stop" or "release") Assert.Null(lease);
        else Assert.NotNull(lease);
    }

    [Fact]
    public async Task RestorationConflictAndCanceledBrowserPreventQuietAndSecondFlow()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var batch = new SingleCableAcceptance(Graph(), Original(), stop.Token, new());
        var report = await RunFlow(batch, ExternalFlow.Browser, stop.Token, stop);
        Assert.False(report.Passed);
        Assert.Equal(SingleCableState.Failed, batch.State);
        var external = Original() with { Browser = Original().Browser with { EndpointId = "externo" } };
        Assert.Equal(ExternalRestoreState.Conflict, batch.Restoration(external).Browser);
        Assert.Equal(ExternalRestoreState.Unavailable, batch.Restoration(Original(), originalBrowserPresent: false).Browser);
        await Assert.ThrowsAnyAsync<Exception>(() => batch.CheckQuietAsync(batch.QuietTopology, Original(), true, () => true));
    }

    [Fact]
    public async Task DisabledObsSourceStaysOffUntilMicrophoneAndMustBeRestoredAsDisabled()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var original = Original();
        original = original with { Consumer = new("obs/scene/source", null, true, true, Disabled: true) };
        await using var batch = new SingleCableAcceptance(Graph(), original, stop.Token, new());

        Assert.True(batch.BrowserSelections.Consumer.Disabled);
        Assert.False(batch.MicrophoneSelections.Consumer.Disabled);
        Assert.Equal(batch.MicrophoneTopology.Cable.Capture.Endpoint.Id, batch.MicrophoneSelections.Consumer.EndpointId);
        Assert.True((await RunFlow(batch, ExternalFlow.Browser, stop.Token)).Passed);

        var defaultInput = original with { Consumer = original.Consumer with { Disabled = false } };
        Assert.Equal(ExternalRestoreState.Conflict, batch.Restoration(defaultInput).Consumer);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            batch.CheckQuietAsync(batch.QuietTopology, defaultInput, true, () => true));
        Assert.True((await ObserveQuiet(batch, "quiet", original: original)).Passed);
        Assert.True((await RunFlow(batch, ExternalFlow.Microphone, stop.Token)).Passed);
        Assert.Equal(ExternalRestoreState.Restore, batch.Restoration(batch.MicrophoneSelections).Consumer);
        Assert.False(batch.FinishRestoration(defaultInput).Complete);
        Assert.True(batch.FinishRestoration(original).Complete);
        Assert.Equal(SingleCableState.Complete, batch.State);
    }

    [Theory]
    [InlineData("DEFAULT", null, false)]
    [InlineData("DISABLED", null, true)]
    [InlineData("endpoint-original", "endpoint-original", false)]
    public void SnapshotDistinguishesObsSourceStates(string snapshotValue, string? endpointId, bool disabled)
    {
        var parsed = NativeSingleCableProofTests.ParseOriginalSelection("obs/scene/source", snapshotValue, allowDisabled: true);
        Assert.Equal(endpointId, parsed.EndpointId);
        Assert.Equal(disabled, parsed.Disabled);
        if (disabled)
            Assert.Throws<InvalidOperationException>(() =>
                NativeSingleCableProofTests.ParseOriginalSelection("browser/profile", snapshotValue, allowDisabled: false));
    }

    [Fact]
    public async Task ProducerReturningAfterQuietNeverOpensMicrophone()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var batch = new SingleCableAcceptance(Graph(), Original(), stop.Token, new());
        Assert.True((await RunFlow(batch, ExternalFlow.Browser, stop.Token)).Passed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            batch.CheckQuietAsync(batch.QuietTopology, batch.BrowserSelections, true, () => true));
        Assert.True((await ObserveQuiet(batch, "quiet")).Passed);
        int opens = 0;
        Assert.Throws<InvalidOperationException>(() => batch.Begin(ExternalFlow.Microphone, batch.MicrophoneTopology,
            batch.MicrophoneSelections, true, () => false, _ => { opens++; throw new Exception(); }));
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task CancelingMicrophoneRequiresFinalRestorationAndInvalidatesTheBatch()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var guard = new TransportActivityGuard();
        await using var batch = new SingleCableAcceptance(Graph(), Original(), stop.Token, guard);
        Assert.True((await RunFlow(batch, ExternalFlow.Browser, stop.Token)).Passed);
        Assert.True((await ObserveQuiet(batch, "quiet")).Passed);
        var result = await RunFlow(batch, ExternalFlow.Microphone, stop.Token, stop);
        Assert.False(result.Passed);
        Assert.True(result.Transport.Cleanup.Completed);
        Assert.Equal(SingleCableState.Failed, batch.State);
        Assert.Equal(ExternalRestoreState.Restore, batch.Restoration(batch.MicrophoneSelections).Consumer);
        Assert.True(batch.FinishRestoration(Original()).Complete);
        Assert.Equal(SingleCableState.Failed, batch.State);
    }

    [Fact]
    public Task QuietEncoderScanRejectsNoiseAndNaNWithoutRecurringAllocation() =>
        AllocationIsolation.Run<SingleCableAcceptanceTests>(MeasureQuietScan, TestContext.Current.CancellationToken);

    private static void MeasureQuietScan()
    {
        var pcm = new byte[480 * 2];
        var floats = new byte[480 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(floats, 0x8000_0000);
        for (int i = 0; i < 500; i++)
        {
            WasapiCableQuietProbe.IsDigitalSilence(pcm, PcmEncoding.Pcm16LittleEndian);
            WasapiCableQuietProbe.IsDigitalSilence(floats, PcmEncoding.Float32LittleEndian);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool quiet = true;
        for (int i = 0; i < 1000; i++)
        {
            quiet &= WasapiCableQuietProbe.IsDigitalSilence(pcm, PcmEncoding.Pcm16LittleEndian);
            quiet &= WasapiCableQuietProbe.IsDigitalSilence(floats, PcmEncoding.Float32LittleEndian);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(quiet);
        pcm[0] = 1;
        BinaryPrimitives.WriteSingleLittleEndian(floats, float.NaN);
        Assert.False(WasapiCableQuietProbe.IsDigitalSilence(pcm, PcmEncoding.Pcm16LittleEndian));
        Assert.False(WasapiCableQuietProbe.IsDigitalSilence(floats, PcmEncoding.Float32LittleEndian));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("selection")]
    public async Task BrowserFailureCannotAdvanceToQuietOrMicrophone(string fault)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var batch = new SingleCableAcceptance(Graph(), Original(), stop.Token, new());
        var result = await RunFlow(batch, ExternalFlow.Browser, stop.Token, fault: fault);
        Assert.False(result.Passed);
        Assert.True(result.Transport.Cleanup.Completed);
        Assert.Equal(SingleCableState.Failed, batch.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => batch.CheckQuietAsync(batch.QuietTopology, Original(), true, () => true));
    }

    private static async Task<ExternalRouteReport> RunFlow(SingleCableAcceptance batch, ExternalFlow flow,
        CancellationToken token, CancellationTokenSource? cancel = null, string? fault = null)
    {
        var graph = flow == ExternalFlow.Browser ? batch.BrowserTopology : batch.MicrophoneTopology;
        var route = graph.Route(flow);
        using var rig = new WasapiDriftTests.VirtualRig(route.Capture.Mix!.SampleRate, route.Playback.Mix!.SampleRate,
            0, 0, assigned: route);
        double bypass = 0, effect = 0;
        int block = 0;
        rig.Calls.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags == 2) return;
            Assert.Equal(0u, flags);
            var data = rig.Calls.Output.Data.AsSpan(0, (int)frames * route.Playback.Mix.BlockAlign);
            for (int i = 0; i < data.Length; i += route.Playback.Mix.ContainerBits / 8)
            {
                double value = route.Playback.Mix.ContainerBits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(data[i..])
                    : BinaryPrimitives.ReadInt16LittleEndian(data[i..]) / 32768d;
                Assert.True(double.IsFinite(value) && Math.Abs(value) <= .05002);
                if (block is >= 25 and <= 125) bypass = Math.Max(bypass, Math.Abs(value));
                if (block is >= 175 and <= 395) effect = Math.Max(effect, Math.Abs(value));
            }
        };
        var session = batch.Begin(flow, graph, flow == ExternalFlow.Browser ? batch.BrowserSelections : batch.MicrophoneSelections,
            true, () => true, rig.Calls.Open, rig.NominalPacing());
        var run = session.RunAsync(TimeSpan.FromSeconds(4), token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!session.Running)
        {
            if (run.IsCompleted) Assert.Fail((await run).Transport.Cleanup.Failure);
            await Task.Delay(1, timeout.Token);
        }
        for (block = 1; block <= 400 && !run.IsCompleted; block++)
        {
            if (block == 150) session.SetBypass(false);
            if (block == 30 && cancel is not null) cancel.Cancel();
            if (block == 35 && fault == "get") rig.Calls.Output.FailStep = "GetRender";
            if (block == 35 && fault == "selection") session.Invalidate(graph.Generation);
            rig.Advance(block, run);
            session.Step();
        }
        var result = await run;
        await batch.FinishFlowAsync();
        if (cancel is null && fault is null)
        {
            Assert.True(result.Passed, result.Transport.Cleanup.Failure);
            Assert.InRange(effect / bypass, .24, .26);
            Assert.Equal(0, result.Transport.Cleanup.DroppedFrames);
            Assert.Equal(0, result.Transport.Cleanup.UnderrunFrames);
        }
        return result;
    }

    private static Task<CableQuietReport> ObserveQuiet(SingleCableAcceptance batch, string fault, CancellationTokenSource? cancel = null,
        ExternalSelections? original = null)
    {
        const long origin = 3_000_000_000;
        long now = origin, next = origin + 100_000, position = 0;
        var route = batch.QuietTopology.Route(ExternalFlow.Browser);
        var calls = new WasapiSilentTests.Calls(route);
        calls.Input.Capacity = 1056;
        if (fault != "residual") Array.Clear(calls.Input.Data);
        calls.Input.PacketSizeOverride = () => now >= next && fault != "no-data" &&
            (fault != "stalled-tail" || now - origin < 26_500_000) ? 480u : 0u;
        calls.Input.CaptureBufferOverride = () =>
        {
            var result = new WasapiCaptureBuffer(0, 0, 480, 0, (ulong)position, (ulong)(fault == "timestamp" ? next + 2_000_000 : next - 1));
            next += 100_000;
            position += 480;
            return result;
        };
        if (fault == "disconnect") calls.Input.FailStep = "Next";
        if (fault == "release") calls.Input.ReleaseResult = WasapiSilentTests.Invalidated;
        if (fault == "stop") calls.Input.StopResult = WasapiSilentTests.Invalidated;
        return batch.CheckQuietAsync(batch.QuietTopology, original ?? Original(), true,
            () => fault != "producer" || now - origin < 5_000_000, calls.Open, () => now, _ =>
            {
                now += 50_000;
                if (fault == "cancel") cancel!.Cancel();
            });
    }

    internal static SingleCableTopology Graph()
    {
        var baseline = WasapiContractTests.Route(inputRate: 48000, outputRate: 48000, inputChannels: 2);
        var mic = WasapiContractTests.Route(inputRate: 44100, outputRate: 48000, inputChannels: 1, outputFloat: false);
        ExternalEndpoint End(WasapiEndpointInfo info, string id, string adapter) =>
            new(info with { Endpoint = info.Endpoint with { Id = id } }, adapter);
        return new(new(End(mic.Playback, "cabo-render", "cabo"), End(baseline.Capture, "cabo-capture", "cabo")),
            End(baseline.Playback, "headset", "físico-saída"), End(mic.Capture, "microfone", "físico-entrada"));
    }

    private static ExternalSelections Original() => new(new("browser/profile", "original-browser", true, true),
        new("obs/scene/source", "original-obs", true, true));
}

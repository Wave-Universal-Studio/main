using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed partial class OwnedBusSchedulerTests
{
    [Fact]
    public void PreparedClientsSupplyTheirOwnClockBeforeTheLocalQpcCut()
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();

            Assert.True(route.Scheduler.Tick(0));
            Assert.False(route.Scheduler.ClockAdmitted);
            Assert.Equal(0, route.CapturePackets.Releases);
            Assert.Equal(0, route.RenderPackets.Releases);

            Assert.True(route.Scheduler.Tick(1));
            Assert.True(route.Scheduler.ClockAdmitted);
            Assert.Equal(["capture", "render", "qpc", "capture", "render", "qpc"], route.Order);
            Assert.Equal(1, route.CapturePackets.Releases);
            Assert.Equal(1, route.RenderPackets.Releases);
            route.Scheduler.Stop();
            Assert.True(route.CaptureClient.ClockDisposed);
            Assert.True(route.RenderClient.ClockDisposed);
            Assert.True(route.CapturePackets.Disposed);
            Assert.True(route.RenderPackets.Disposed);
            Assert.False(route.CaptureClient.ClientDisposed || route.RenderClient.ClientDisposed);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(unchecked((int)0x88890004))]
    public void UncertainOrFailedNativePositionNeverReachesBuffers(int hresult)
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();
            route.RenderClient.PositionResult = hresult;
            Assert.False(route.Scheduler.Tick(0));
            Assert.False(route.Scheduler.IsRunning);
            Assert.Equal(OwnedBusClockFailure.InvalidReading, route.Scheduler.ClockFailure);
            Assert.Equal(NativeClockReason.InaccurateReading, route.Scheduler.NativeClockFailure);
            Assert.Equal(0, route.CapturePackets.Releases);
            Assert.Equal(0, route.RenderPackets.Releases);
            Assert.True(route.RenderClient.ClockDisposed);
        });
    }

    [Fact]
    public void NativeSchedulerRejectsAnInjectedCut()
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();
            Assert.False(route.Scheduler.Tick(0, 900_000, ClockReadings(900_000, route.Input, route.Output)));
            Assert.False(route.Scheduler.IsRunning);
            Assert.Empty(route.Order);
            Assert.Equal(0, route.RenderPackets.Releases);
        });
    }

    [Fact]
    public void FailedClockCallStopsTheSchedulerAndClosesBothClocks()
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();
            route.RenderClient.ThrowRead = true;
            Assert.Throws<InvalidOperationException>(() => route.Scheduler.Tick(0));
            Assert.False(route.Scheduler.IsRunning);
            Assert.True(route.CaptureClient.ClockDisposed && route.RenderClient.ClockDisposed);
            Assert.Equal(0, route.RenderPackets.Releases);
        });
    }

    [Fact]
    public void CaptureBindingChecksThePreparedClientBeforeOpeningServices()
    {
        RunMta(() =>
        {
            var source = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [source]);
            using var adapter = new WasapiCaptureBlockAdapter(bridge, source.Endpoint, Mono, Pcm16());
            var mistaken = new PreparedClient(source.Endpoint with { Generation = 2 }, "capture", []);
            Assert.Throws<ArgumentException>(() => WasapiBusCaptureOwnerStep.Attach(mistaken, adapter, source.Endpoint, Mono));
            Assert.Equal(0, mistaken.ServiceOpens);
        });
    }

    [Fact]
    public void PacketServiceFailureReleasesTheClockAlreadyAcquiredOnThatClient()
    {
        RunMta(() =>
        {
            var source = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [source]);
            using var adapter = new WasapiCaptureBlockAdapter(bridge, source.Endpoint, Mono, Pcm16());
            var client = new PreparedClient(source.Endpoint, "capture", []) { ThrowOpenPacket = true };
            Assert.Throws<InvalidOperationException>(() => WasapiBusCaptureOwnerStep.Attach(client, adapter, source.Endpoint, Mono));
            Assert.True(client.ClockDisposed);
            Assert.False(client.ClientDisposed);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsDifferentPreparedEncodingBeforeOpeningServices(bool render)
    {
        RunMta(() =>
        {
            var source = Source("game");
            var output = OutputEndpoint("out");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [source]);
            using var captureAdapter = new WasapiCaptureBlockAdapter(bridge, source.Endpoint, Mono, Pcm16());
            using var renderAdapter = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var client = new PreparedClient(render ? output : source.Endpoint, "incompatible", [])
            {
                PreparedFormat = new(3, 48000, 1, 32, 32, 4, 192000, 0, WasapiPcm.FloatSubFormat, null)
            };
            if (render)
                Assert.Throws<ArgumentException>(() => WasapiBusRenderOwnerStep.Attach(client, renderAdapter, output, Mono, Pcm16()));
            else
                Assert.Throws<ArgumentException>(() => WasapiBusCaptureOwnerStep.Attach(client, captureAdapter, source.Endpoint, Mono));

            Assert.Equal(0, client.ServiceOpens);
        });
    }

    [Fact]
    public void UncertainNativeBufferReleaseRetainsClockAndGuard()
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();
            Assert.True(route.Scheduler.Tick(0));
            route.RenderPackets.ThrowOnRelease = true;
            Assert.Throws<InvalidOperationException>(() => route.Scheduler.Tick(1));
            Assert.True(route.Scheduler.ResourcesRetained);
            Assert.True(route.CaptureClient.ClockDisposed);
            Assert.False(route.RenderClient.ClockDisposed);
            Assert.False(route.RenderClient.ClientDisposed);
            Assert.Null(route.Guard.TryAcquireOwnedBus(2));
        });
    }

    [Fact]
    public void FailedAttachKeepsGuardWhenRollbackCannotReleaseClock()
    {
        RunMta(() =>
        {
            var group = Source("game");
            var target = OutputEndpoint("out");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [group]);
            using var input = new WasapiCaptureBlockAdapter(bridge, group.Endpoint, Mono, Pcm16());
            using var output = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var capture = new PreparedClient(group.Endpoint, "capture", []) { ThrowOpenPacket = true, ThrowClockDispose = true };
            var render = new PreparedClient(target, "render", []);
            var guard = new TransportActivityGuard();

            Assert.Throws<WasapiBusBindingCleanupException>(() => OwnedBusScheduler.CreatePrepared(1, Mono, bridge,
                [new(group, capture, input)], [new(target, MixBus.Personal, render, output)], guard, TestContext.Current.CancellationToken));
            Assert.True(capture.Borrowed);
            Assert.Throws<InvalidOperationException>(capture.Dispose);
            Assert.Null(guard.TryAcquireOwnedBus(2));
            Assert.Equal(0, render.ServiceOpens);
        });
    }

    [Fact]
    public void StopKeepsGuardButContinuesDisposingIndependentOwners()
    {
        RunMta(() =>
        {
            using var route = new PreparedRoute();
            route.CaptureClient.ThrowClockDispose = true;
            Assert.Throws<WasapiBusBindingCleanupException>(route.Scheduler.Stop);
            Assert.True(route.Scheduler.ResourcesRetained);
            Assert.True(route.CaptureClient.Borrowed);
            Assert.True(route.RenderClient.ClockDisposed);
            Assert.True(route.RenderPackets.Disposed);
            Assert.Null(route.Guard.TryAcquireOwnedBus(2));
        });
    }

    [Fact]
    public void OrdinaryOwnerRejectsNativePacketServiceWithoutTheClockBinding()
    {
        RunMta(() =>
        {
            var group = Source("game");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [group]);
            using var adapter = new WasapiCaptureBlockAdapter(bridge, group.Endpoint, Mono, Pcm16());
            using var service = new NativeMarkedCapture();
            Assert.Throws<ArgumentException>(() => new WasapiBusCaptureOwnerStep(service, adapter, group.Endpoint, Mono));
        });
    }

    private sealed class NativeMarkedCapture : IWasapiCapturePackets, IWasapiNativePacketService
    {
        public uint NextPacketSize() => throw new NotSupportedException();
        public WasapiCaptureBuffer GetBuffer() => throw new NotSupportedException();
        public int ReleaseBuffer(uint framesRead) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Fact]
    public void HardwareClientRejectsAnInjectedQpcReferenceBeforeAttaching()
    {
        RunMta(() =>
        {
            var group = Source("game");
            var target = OutputEndpoint("out");
            using var bridge = new BoundedBusPcmTransport(1, Mono, [group]);
            using var input = new WasapiCaptureBlockAdapter(bridge, group.Endpoint, Mono, Pcm16());
            using var output = new WasapiRenderBlockAdapter(bridge, Mono, Pcm16());
            var capture = new HardwareClient(group.Endpoint);
            var render = new PreparedClient(target, "render", []);
            var guard = new TransportActivityGuard();

            Assert.Throws<ArgumentException>(() => OwnedBusScheduler.CreatePrepared(1, Mono, bridge,
                [new(group, capture, input)], [new(target, MixBus.Personal, render, output)], guard,
                TestContext.Current.CancellationToken, readReferenceTime: () => 900_000));
            Assert.Equal(0, capture.ServiceOpens);
            using var lease = guard.TryAcquireOwnedBus(1);
            Assert.NotNull(lease);
        });
    }

    private sealed class HardwareClient(TransportEndpoint endpoint)
        : PreparedClient(endpoint, "hardware", []), IWasapiNativeBoundStreamClient { }

    private sealed class PreparedRoute : IDisposable
    {
        private readonly BoundedBusPcmTransport bridge;
        private readonly WasapiCaptureBlockAdapter captureAdapter;
        private readonly WasapiRenderBlockAdapter renderAdapter;
        private long now = 800_000;

        public readonly TransportEndpoint Input = Source("game").Endpoint;
        public readonly TransportEndpoint Output = OutputEndpoint("out");
        public readonly List<string> Order = [];
        public readonly CapturePackets CapturePackets = new(8192);
        public readonly RenderPackets RenderPackets = new();
        public readonly PreparedClient CaptureClient, RenderClient;
        public readonly OwnedBusScheduler Scheduler;
        public readonly TransportActivityGuard Guard = new();

        public PreparedRoute()
        {
            var group = Source(Input.Id);
            bridge = new(1, Mono, [group]);
            captureAdapter = new(bridge, Input, Mono, Pcm16());
            renderAdapter = new(bridge, Mono, Pcm16());
            CaptureClient = new(Input, "capture", Order) { Capture = CapturePackets };
            RenderClient = new(Output, "render", Order) { Render = RenderPackets };
            Scheduler = OwnedBusScheduler.CreatePrepared(1, Mono, bridge,
                [new(group, CaptureClient, captureAdapter)], [new(Output, MixBus.Personal, RenderClient, renderAdapter)],
                Guard, readReferenceTime: () =>
                {
                    Order.Add("qpc");
                    return now += 100_000;
                });
        }

        public void Dispose()
        {
            Scheduler.Dispose();
            // O descarte de fakes libera somente a memória do ensaio.
            CapturePackets.Dispose();
            RenderPackets.Dispose();
            if (!CaptureClient.Borrowed) CaptureClient.Dispose();
            if (!RenderClient.Borrowed) RenderClient.Dispose();
            captureAdapter.Dispose();
            renderAdapter.Dispose();
            bridge.Dispose();
        }
    }

    private class PreparedClient(TransportEndpoint endpoint, string name, List<string> order) : IWasapiBoundStreamClient
    {
        internal int ServiceOpens;
        internal bool ClockDisposed, ClientDisposed, ThrowRead, ThrowOpenPacket, ThrowClockDispose, Borrowed;
        internal int PositionResult;
        internal IWasapiCapturePackets? Capture;
        internal IWasapiRenderPackets? Render;
        public TransportEndpoint Endpoint => endpoint;
        public WasapiPcm PreparedFormat { get; init; } = Pcm16();

        private void LogRead() => order.Add(name);

        public IWasapiPreparationClock OpenClock()
        {
            ServiceOpens++;
            return new Reading(this);
        }

        private sealed class Reading(PreparedClient client) : IWasapiPreparationClock
        {
            private ulong qpc = 799_000, deviceUnits = 192_000;

            public WasapiClockReading Read()
            {
                client.LogRead();
                if (client.ThrowRead) throw new InvalidOperationException("Falha simulada no clock.");
                return new(0, 192_000, client.PositionResult, deviceUnits += 1920, qpc += 100_000);
            }

            public void Dispose()
            {
                if (client.ThrowClockDispose) throw new InvalidOperationException("Falha simulada ao liberar o clock.");
                client.ClockDisposed = true;
            }
        }

        public IWasapiCapturePackets OpenCapture() => ThrowOpenPacket ? throw new InvalidOperationException("Falha simulada ao obter captura.") : Capture!;
        public IWasapiRenderPackets OpenRender() => ThrowOpenPacket ? throw new InvalidOperationException("Falha simulada ao obter render.") : Render!;
        public IDisposable BorrowBusServices()
        {
            if (Borrowed) throw new InvalidOperationException("Cliente já vinculado.");
            Borrowed = true;
            return new Borrow(this);
        }

        private sealed class Borrow(PreparedClient owner) : IDisposable
        {
            public void Dispose() => owner.Borrowed = false;
        }

        public void Dispose()
        {
            if (Borrowed) throw new InvalidOperationException("Cliente ainda possui serviços vinculados.");
            ClientDisposed = true;
        }
        public int Start() => throw new NotSupportedException();
        public int Stop() => throw new NotSupportedException();
        public void Initialize() => throw new NotSupportedException();
        public void RequireExactSupport() => throw new NotSupportedException();
        public void RevalidateFormat(WasapiPcm expected) => throw new NotSupportedException();
        public (uint, long) ReadBufferInfo() => throw new NotSupportedException();
        public (long, long) ReadPeriods() => throw new NotSupportedException();
        public uint ReadPadding() => throw new NotSupportedException();
        public WasapiPcm ReadMixFormat() => throw new NotSupportedException();
    }
}

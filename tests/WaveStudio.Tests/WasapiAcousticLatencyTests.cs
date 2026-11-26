using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiAcousticLatencyTests
{
    [Theory]
    [InlineData(44100, 0, 2871, .008f)]
    [InlineData(48000, 1, 4213, -.002f)]
    [InlineData(48000, 2, 3839, .001f)]
    public void DetectsQuietOrInvertedMarkerAtNonAlignedOffsets(int rate, int marker, int offset, float amplitude)
    {
        var probe = WasapiAcousticSignal.Create(rate, marker);
        var samples = Noise(rate / 5);
        for (int i = 0; i < probe.Length; i++)
            samples[offset + i] += probe[i] * amplitude / .04f + .001f;

        var match = WasapiAcousticSignal.Find(probe, samples, 0, samples.Length - probe.Length, rate);

        Assert.NotNull(match);
        Assert.InRange(match.Value.Frame, offset - 1, offset + 1);
        Assert.InRange(match.Value.Correlation, .90, 1);
    }

    [Fact]
    public void RejectsSilenceUnrelatedAudioAndAmbiguousCopies()
    {
        const int rate = 48000;
        var probe = WasapiAcousticSignal.Create(rate, 0);
        var samples = Noise(rate / 4);
        Assert.Null(WasapiAcousticSignal.Find(probe, new float[samples.Length], 0, 8000, rate));
        Assert.Null(WasapiAcousticSignal.Find(probe, samples, 0, 8000, rate));

        for (int i = 0; i < probe.Length; i++)
        {
            samples[2207 + i] += probe[i] * .1f;
            samples[5251 + i] += probe[i] * .1f;
        }
        Assert.Null(WasapiAcousticSignal.Find(probe, samples, 0, 8000, rate));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void EveryStimulusHasFixedPeakAndDuration(int rate)
    {
        for (int i = 0; i < 3; i++)
        {
            var probe = WasapiAcousticSignal.Create(rate, i);
            Assert.InRange(probe.Length, rate * .017, rate * .019);
            Assert.InRange(probe.Max(MathF.Abs), .035f, .04f);
            Assert.Equal(0f, probe[0]);
            Assert.Equal(0f, probe[^1]);
        }
    }

    [Fact]
    public void ReportsSubmissionAndAvailabilityBoundsOnTheSameClock()
    {
        // Início do marcador 5 ms dentro do pacote. A entrega foi observada 16 ms depois.
        var result = WasapiAcousticTiming.Measure(240, 48000, new(1_000_000, 1_000_100),
            new(0, 480, 1_140_000, 1_180_000, 1_350_000));
        Assert.NotNull(result);
        Assert.Equal(18.995, result.Value.FrameTimestampMs, 3);
        Assert.Equal(18.99, result.Value.FrameTimestampLowerMs, 3);
        Assert.Equal(19.0, result.Value.FrameTimestampUpperMs, 3);
        Assert.Equal(34.995, result.Value.ReadObservedMs, 3);
        Assert.Equal(17.99, result.Value.AvailabilityLowerMs, 3);
        Assert.Equal(35.0, result.Value.AvailabilityUpperMs, 3);
    }

    [Fact]
    public void RejectsCaptureFromBeforeSubmissionOrOutsidePacket()
    {
        var packet = new WasapiAcousticPacket(0, 480, 900_000, 950_000, 1_000_000);
        Assert.Null(WasapiAcousticTiming.Measure(240, 48000, new(1_000_000, 1_000_100), packet));
        Assert.Null(WasapiAcousticTiming.Measure(480, 48000, new(850_000, 850_100), packet));
    }

    private static float[] Noise(int length)
    {
        var random = new Random(8314);
        return Enumerable.Range(0, length).Select(_ => (float)(random.NextDouble() - .5) * .0002f).ToArray();
    }
}

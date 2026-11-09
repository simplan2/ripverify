namespace RipVerify.Tests.Infrastructure.Cue;

using FluentAssertions;
using RipVerify.Core.Models;
using RipVerify.Core.Services.Classification;
using RipVerify.Infrastructure.Cue;
using Xunit;

public class CueToolsOutputParserTests
{
    private static readonly CueSheet Cue = new() { FilePath = @"C:\albums\disc.cue" };

    [Fact]
    public void ParsesAccurateRipSectionWithMixedMatches()
    {
        const string output = """
            [CUETools log; Date: 01/11/2026 09:20:56 p. m.; Version: 2.2.6]
            [CTDB TOCID: NxhxEtD3FYobTBshU7QrGkSs14I-] found.
            Track | CTDB Status
              1   | ( 44/173) Accurately ripped
              2   | ( 42/173) Differs in 58 samples @00:02:32-00:02:33
            [AccurateRip ID: 00142e60-00bd0bfe-950acb0c] found.
            Track   [  CRC   |   V2   ] Status
             01     [9b8d54e9|1f84bc1c] (03+05/46) Accurately ripped
             02     [42b6e59c|325cca65] (00+00/47) No match
            Offsetted by 398:
             01     [d8a5359f] (00/46) No match (V2 was not tested)
            """;

        var result = Parse(output);

        result.AccurateRipStatus.Should().Be(AccurateRipStatus.Inaccurate);
        result.ConfidenceScore.Should().Be(8);
        result.CtdbStatus.Should().Be(CtdbStatus.Inaccurate);
        result.CtdbParityAvailable.Should().BeTrue();
        result.ParserWarnings.Should().Contain(w => w.Contains("Differs", StringComparison.OrdinalIgnoreCase));
        result.ParserWarnings.Should().Contain(w => w.Contains("No match", StringComparison.OrdinalIgnoreCase));
        result.Status.Should().Be(VerificationStatus.FakeLossless);
    }

    [Fact]
    public void ParsesMissingDatabasesAsNotFound()
    {
        const string output = """
            [CUETools log; Date: 01/11/2026 09:22:20 p. m.; Version: 2.2.6]
            [CTDB TOCID: IRCoVz1lDJfRcnxC1FXj0hMw5H8-] disk not present in database.
            [AccurateRip ID: 000f6d24-006fb126-860a2c09] disk not present in database.
            """;

        var result = Parse(output);

        result.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        result.CtdbStatus.Should().Be(CtdbStatus.NotFound);
        result.CtdbParityAvailable.Should().BeFalse();
        result.ConfidenceScore.Should().Be(0);
        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
    }

    [Fact]
    public void ReportsMissingAccurateRipSeparatelyFromCtdb()
    {
        const string output = """
            [CUETools log; Date: 01/11/2026 09:22:34 p. m.; Version: 2.2.6]
            [CTDB TOCID: G9tNJEI3jqe0oFOBTPMr3AQ4Yh0-] found.
                    [370bbe90] (5/5) Accurately ripped
            Track | CTDB Status
              1   | (5/5) Accurately ripped
              2   | (5/5) Accurately ripped
            [AccurateRip ID: 00140008-00ac52df-890b570b] disk not present in database.
            """;

        var result = Parse(output);

        result.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        result.CtdbStatus.Should().Be(CtdbStatus.AccuratelyRipped);
        result.CtdbConfidenceScore.Should().Be(5);
        result.CtdbMatches.Should().Be(2);
        result.CtdbParityAvailable.Should().BeTrue();
        result.ConfidenceScore.Should().Be(5);
        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);

        var finalResult = new VerificationClassifier().Classify(
            result,
            SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } },
            OriginSignals.Empty);

        finalResult.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        finalResult.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        finalResult.Diagnostics.Should().Contain(d => d.Contains("CTDB accurate"));
    }

    [Fact]
    public void PreservesCueOrigin()
    {
        const string output = """
            [CTDB TOCID: IRCoVz1lDJfRcnxC1FXj0hMw5H8-] disk not present in database.
            [AccurateRip ID: 000f6d24-006fb126-860a2c09] disk not present in database.
            """;

        var result = Parse(output, Cue with { Origin = CueOrigin.GeneratedFromMetadata });

        result.CueOrigin.Should().Be(CueOrigin.GeneratedFromMetadata);
    }

    private static VerificationResult Parse(string output, CueSheet? cue = null)
    {
        var run = new CueToolsRunResult
        {
            Process = new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = output
            }
        };

        return CueToolsOutputParser.Parse(run, cue ?? Cue, new AppSettings());
    }
}

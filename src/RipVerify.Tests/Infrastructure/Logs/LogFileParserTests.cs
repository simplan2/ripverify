using FluentAssertions;
using RipVerify.Infrastructure.Logs;

namespace RipVerify.Tests.Infrastructure.Logs;

public class LogFileParserTests
{
    [Fact]
    public void ParsesSpanishEacLogAndVirtualDrive()
    {
        const string content = """
            Exact Audio Copy V1.8 from 15. July 2024

            Usar unidad  : DiscSoftVirtual   Adaptador: 1  ID: 0
            Velocidad de lectura : 8.6 X

            Pista  1
                 Nombre de Archivo: 01 - Track One.wav
                 Copiar CRC 11111111
                 Probar CRC 22222222

            Pista  2
                 Nombre de Archivo: 02 - Track Two.wav
                 Copiar CRC AABBCCDD
            """;

        var document = LogFileParser.Parse(content, @"C:\music\album.log");

        document.Creator.Should().Be("Exact Audio Copy");
        document.DriveName.Should().Contain("DiscSoftVirtual");
        document.IsVirtualDrive.Should().BeTrue();
        document.Tracks.Should().HaveCount(2);
        document.Tracks[0].Number.Should().Be(1);
        document.Tracks[0].FileName.Should().Be("01 - Track One.wav");
        document.Tracks[0].CopyCrc.Should().Be("11111111");
        document.Tracks[0].TestCrc.Should().Be("22222222");
        document.Tracks[1].CopyCrc.Should().Be("AABBCCDD");
    }

    [Fact]
    public void ParsesEnglishEacLogAndPhysicalDrive()
    {
        const string content = """
            Exact Audio Copy V1.5 from 20. February 2020

            Used drive  : HL-DT-STBD-RE WH14NS40   Adapter: 1  ID: 0

            Track  1
                 Filename D:\Music\01 - Track One.wav
                 Copy CRC 23795CBC
                 Test CRC A185F15F
            """;

        var document = LogFileParser.Parse(content, @"D:\Music\album.log");

        document.DriveName.Should().Be("HL-DT-STBD-RE WH14NS40");
        document.IsVirtualDrive.Should().BeFalse();
        document.Tracks.Should().ContainSingle();
        document.Tracks[0].CopyCrc.Should().Be("23795CBC");
        document.Tracks[0].TestCrc.Should().Be("A185F15F");
    }

    [Fact]
    public void IgnoresTracksWithoutCrc()
    {
        const string content = """
            Track  1
                 Filename first.wav
            Track  2
                 Filename second.wav
                 Copy CRC 12345678
            """;

        var document = LogFileParser.Parse(content, "album.log");

        document.Tracks.Should().ContainSingle();
        document.Tracks[0].Number.Should().Be(2);
    }
}

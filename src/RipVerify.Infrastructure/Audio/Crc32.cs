namespace RipVerify.Infrastructure.Audio;

public static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
        => Finish(Append(Begin(), data));

    public static uint Begin() => uint.MaxValue;

    public static uint Append(uint state, ReadOnlySpan<byte> data)
    {
        var crc = state;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    public static uint Finish(uint state) => state ^ uint.MaxValue;

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xEDB88320u ^ (value >> 1)
                    : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}

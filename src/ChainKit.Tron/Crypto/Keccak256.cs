namespace ChainKit.Tron.Crypto;

/// <summary>
/// Keccak-256 hash function (pre-NIST variant, padding byte 0x01).
/// Used for Ethereum/Tron address generation and ABI function selectors.
/// This is NOT SHA3-256, which uses padding byte 0x06.
/// </summary>
public static class Keccak256
{
    private const int Rate = 136; // bytes (1088 bits) for 256-bit output
    private const int HashSize = 32; // 256-bit output
    private const int Rounds = 24;
    private const int StateSize = 25; // 5x5 lanes of 64 bits each

    private static readonly ulong[] RoundConstants = new ulong[24]
    {
        0x0000000000000001UL, 0x0000000000008082UL,
        0x800000000000808AUL, 0x8000000080008000UL,
        0x000000000000808BUL, 0x0000000080000001UL,
        0x8000000080008081UL, 0x8000000000008009UL,
        0x000000000000008AUL, 0x0000000000000088UL,
        0x0000000080008009UL, 0x000000008000000AUL,
        0x000000008000808BUL, 0x800000000000008BUL,
        0x8000000000008089UL, 0x8000000000008003UL,
        0x8000000000008002UL, 0x8000000000000080UL,
        0x000000000000800AUL, 0x800000008000000AUL,
        0x8000000080008081UL, 0x8000000000008080UL,
        0x0000000080000001UL, 0x8000000080008008UL,
    };

    private static readonly int[] RotationOffsets = new int[25]
    {
         0,  1, 62, 28, 27,
        36, 44,  6, 55, 20,
         3, 10, 43, 25, 39,
        41, 45, 15, 21,  8,
        18,  2, 61, 56, 14,
    };

    public static byte[] Hash(byte[] input)
    {
        var state = new ulong[StateSize];

        // Absorb phase: pad and process blocks
        int blockCount = input.Length / Rate;
        int remainder = input.Length % Rate;

        // Process complete blocks
        for (int i = 0; i < blockCount; i++)
        {
            AbsorbBlock(state, input, i * Rate, Rate);
        }

        // Pad the final block: Keccak uses multi-rate padding (pad10*1)
        // with domain separation byte 0x01 (NOT 0x06 for SHA3)
        var lastBlock = new byte[Rate];
        Buffer.BlockCopy(input, blockCount * Rate, lastBlock, 0, remainder);
        lastBlock[remainder] = 0x01;
        lastBlock[Rate - 1] |= 0x80;

        AbsorbBlock(state, lastBlock, 0, Rate);

        // Squeeze phase: extract 32 bytes
        var output = new byte[HashSize];
        for (int i = 0; i < HashSize / 8; i++)
        {
            BitConverter.TryWriteBytes(output.AsSpan(i * 8), state[i]);
        }

        return output;
    }

    private static void AbsorbBlock(ulong[] state, byte[] data, int offset, int length)
    {
        int laneCount = length / 8;
        for (int i = 0; i < laneCount; i++)
        {
            state[i] ^= BitConverter.ToUInt64(data, offset + i * 8);
        }

        KeccakF1600(state);
    }

    private static void KeccakF1600(ulong[] state)
    {
        var c = new ulong[5];
        var d = new ulong[5];
        var b = new ulong[25];

        for (int round = 0; round < Rounds; round++)
        {
            // Theta step
            for (int x = 0; x < 5; x++)
            {
                c[x] = state[x] ^ state[x + 5] ^ state[x + 10] ^ state[x + 15] ^ state[x + 20];
            }

            for (int x = 0; x < 5; x++)
            {
                d[x] = c[(x + 4) % 5] ^ RotateLeft(c[(x + 1) % 5], 1);
            }

            for (int i = 0; i < 25; i++)
            {
                state[i] ^= d[i % 5];
            }

            // Rho and Pi steps (combined)
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    int sourceIndex = x + 5 * y;
                    int destX = y;
                    int destY = (2 * x + 3 * y) % 5;
                    int destIndex = destX + 5 * destY;
                    b[destIndex] = RotateLeft(state[sourceIndex], RotationOffsets[sourceIndex]);
                }
            }

            // Chi step
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    int index = x + 5 * y;
                    state[index] = b[index] ^ (~b[(x + 1) % 5 + 5 * y] & b[(x + 2) % 5 + 5 * y]);
                }
            }

            // Iota step
            state[0] ^= RoundConstants[round];
        }
    }

    private static ulong RotateLeft(ulong value, int offset)
    {
        return (value << offset) | (value >> (64 - offset));
    }
}

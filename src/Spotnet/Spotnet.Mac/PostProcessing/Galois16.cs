using System;
using System.Numerics;

namespace Spotnet.Mac.PostProcessing;

/// <summary>
/// Arithmetic in GF(2^16) as par2 defines it: generator polynomial 0x1100B with 2
/// as the primitive element.
///
/// Multiplication goes through log/antilog tables — 64 K entries each, built once —
/// which is what makes repairing a multi-gigabyte set practical at all.
/// </summary>
public static class Galois16
{
    public const int Bits = 16;
    public const int Count = 1 << Bits;      // 65536
    public const int Limit = Count - 1;      // 65535
    public const int GeneratorPolynomial = 0x1100B;

    private static readonly ushort[] Log = new ushort[Count];
    private static readonly ushort[] AntiLog = new ushort[Count];

    static Galois16()
    {
        uint b = 1;
        for (uint l = 0; l < Limit; l++)
        {
            Log[b] = (ushort)l;
            AntiLog[l] = (ushort)b;
            b <<= 1;
            if ((b & Count) != 0) b ^= GeneratorPolynomial;
        }
        Log[0] = Limit;
        AntiLog[Limit] = 0;
    }

    /// <summary>Addition in a binary field is XOR.</summary>
    public static ushort Add(ushort a, ushort b) => (ushort)(a ^ b);

    public static ushort Multiply(ushort a, ushort b)
    {
        if (a == 0 || b == 0) return 0;
        int sum = Log[a] + Log[b];
        if (sum >= Limit) sum -= Limit;
        return AntiLog[sum];
    }

    public static ushort Divide(ushort a, ushort b)
    {
        if (a == 0) return 0;
        if (b == 0) throw new DivideByZeroException("Galois16 division by zero");
        int diff = Log[a] - Log[b];
        if (diff < 0) diff += Limit;
        return AntiLog[diff];
    }

    public static ushort Reciprocal(ushort a)
    {
        if (a == 0) throw new DivideByZeroException("Galois16 has no reciprocal of zero");
        return AntiLog[a == 1 ? 0 : Limit - Log[a]];
    }

    public static ushort Pow(ushort a, uint exponent)
    {
        if (exponent == 0) return 1;
        if (a == 0) return 0;
        long sum = (long)Log[a] * exponent % Limit;
        return AntiLog[sum];
    }

    /// <summary>
    /// The base constants par2 assigns to the input slices.
    ///
    /// par2 walks the exponents 1, 2, 3, … keeping only those coprime with 65535 —
    /// so nothing divisible by 3, 5, 17 or 257 — because only those yield a
    /// Vandermonde matrix guaranteed to be invertible. The constant is 2 raised to
    /// that exponent. Reproduced from par2cmdline's ReedSolomon::SetInput.
    /// </summary>
    public static ushort[] BaseConstants(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var bases = new ushort[count];
        int logbase = 0;
        for (int i = 0; i < count; i++)
        {
            do { logbase++; } while (Gcd(Limit, logbase) != 1);
            if (logbase >= Limit)
                throw new NotSupportedException("Te veel invoerblokken voor de Reed-Solomon-matrix");
            bases[i] = Pow(2, (uint)logbase);
        }
        return bases;
    }

    /// <summary>The base constant for one input slice index.</summary>
    public static ushort BaseConstant(int index) => BaseConstants(index + 1)[index];

    private static int Gcd(int a, int b) => (int)BigInteger.GreatestCommonDivisor(a, b);
}

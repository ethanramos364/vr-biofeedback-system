using System;
using System.Numerics;

/// <summary>
/// FFT2D
/// 
/// Minimal but correct implementation of 2D Cooley-Tukey radix-2 FFT.
/// Used for one-time precomputation of magnitude/phase from source image.
/// 
/// Performance: O(N^2 * log N) for NxN real image -> complex spectrum.
/// For production, consider GPU FFT libraries or offline pre-baking in Python.
/// </summary>
public static class FFT2D
{
    /// <summary>
    /// Forward 2D FFT from real image to complex spectrum.
    /// 
    /// Input: float[] of length N*N in row-major order (real-valued only)
    /// Output: Complex[] of length N*N in row-major order (complex spectrum)
    /// 
    /// Assumes N is a power of 2 (e.g., 256, 512).
    /// </summary>
    public static Complex[] ForwardRealToComplex(float[] real, int N)
    {
        if ((N & (N - 1)) != 0) 
            throw new ArgumentException("N must be a power of 2");

        Complex[] data = new Complex[N * N];

        // Load real values
        for (int i = 0; i < N * N; i++)
            data[i] = new Complex(real[i], 0);

        // FFT rows
        Complex[] row = new Complex[N];
        for (int y = 0; y < N; y++)
        {
            int off = y * N;
            for (int x = 0; x < N; x++) 
                row[x] = data[off + x];
            
            FFT1D(row, inverse: false);
            
            for (int x = 0; x < N; x++) 
                data[off + x] = row[x];
        }

        // FFT columns
        Complex[] col = new Complex[N];
        for (int x = 0; x < N; x++)
        {
            for (int y = 0; y < N; y++) 
                col[y] = data[y * N + x];
            
            FFT1D(col, inverse: false);
            
            for (int y = 0; y < N; y++) 
                data[y * N + x] = col[y];
        }

        return data;
    }

    /// <summary>
    /// In-place radix-2 Cooley-Tukey FFT (forward or inverse).
    /// 
    /// Modifies array in place. Length must be power of 2.
    /// If inverse=true, produces the IFFT (not normalized).
    /// </summary>
    public static void FFT1D(Complex[] a, bool inverse)
    {
        int n = a.Length;
        int j = 0;

        // Bit-reversal permutation
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) 
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                var tmp = a[i];
                a[i] = a[j];
                a[j] = tmp;
            }
        }

        // Iterative Cooley-Tukey butterfly stages
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));

            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    Complex u = a[i + k];
                    Complex v = a[i + k + len / 2] * w;
                    a[i + k] = u + v;
                    a[i + k + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        // For forward DFT, no normalization needed here (applied elsewhere if needed).
        // For IFFT, you'd typically divide by n here, but in our compute shader
        // we handle the normalization there.
        if (inverse)
        {
            for (int i = 0; i < n; i++) 
                a[i] /= n;
        }
    }
}

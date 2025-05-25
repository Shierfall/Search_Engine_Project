using System.Collections;
using System.Text;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

public class BloomFilter : IBloomFilter  
{  
    private readonly BitArray _bits;  
    private readonly int _hashCount;  

    public BloomFilter(int expectedItems, double falsePositiveRate)  
    {  
        if (expectedItems <= 0) throw new ArgumentOutOfRangeException(nameof(expectedItems));
        if (falsePositiveRate <= 0 || falsePositiveRate >= 1)
            throw new ArgumentOutOfRangeException(nameof(falsePositiveRate));

        // compute bit-array size (m) and hash count (k)
        // m↑ -> decreases false positive rate but increases memory usage
        // optimal m given by m = -(n * ln(p)) / (ln(2)^2)
        var m = (int)Math.Ceiling(-expectedItems * Math.Log(falsePositiveRate)
                                  / (Math.Log(2) * Math.Log(2)));
        // k↑ -> decreases false positive rate (to an optimum then ↑) but increases computation time
        // optimal k given by k = (m / n) * ln(2)
        var k = (int)Math.Round((m / (double)expectedItems) * Math.Log(2));

        _bits = new BitArray(m);
        _hashCount = k;
    }  

    public void Add(string term)  
    {  
        var bytes = Encoding.UTF8.GetBytes(term);  
        for (uint i = 0; i < (uint)_hashCount; i++)  
        {  
            ulong hash = MurmurHash3_x64_64(bytes, i);  
            _bits[(int)(hash % (ulong)_bits.Length)] = true;  
        }  
    }  

    public bool MightContain(string term)  
    {  
        var bytes = Encoding.UTF8.GetBytes(term);  
        for (uint i = 0; i < (uint)_hashCount; i++)  
        {  
            ulong hash = MurmurHash3_x64_64(bytes, i);  
            if (!_bits[(int)(hash % (ulong)_bits.Length)]) return false;  
        }  
        return true;  
    }
    
    // MurmurHash3_x64_64 implementation
    private static ulong MurmurHash3_x64_64(byte[] data, uint seed)
    {
        const ulong c1 = 0x87c37b91114253d5UL;
        const ulong c2 = 0x4cf5ad432745937fUL;

        ulong h1 = seed;
        ulong h2 = seed;

        int len = data.Length;
        int nblocks = len / 16;

        // body
        for (int i = 0; i < nblocks; i++)
        {
            int idx = i * 16;
            ulong k1 = BitConverter.ToUInt64(data, idx);
            ulong k2 = BitConverter.ToUInt64(data, idx + 8);

            k1 *= c1; k1 = Rotl64(k1, 31); k1 *= c2; h1 ^= k1;
            h1 = Rotl64(h1, 27); h1 += h2; h1 = h1 * 5 + 0x52dce729;

            k2 *= c2; k2 = Rotl64(k2, 33); k2 *= c1; h2 ^= k2;
            h2 = Rotl64(h2, 31); h2 += h1; h2 = h2 * 5 + 0x38495ab5;
        }

        // tail
        ulong k1_tail = 0, k2_tail = 0;
        int tailIdx = nblocks * 16;
        int tailLen = len & 15;
        if (tailLen > 0)
        {
            for (int i = 0; i < tailLen; i++)
            {
                byte b = data[tailIdx + i];
                if (i < 8)
                    k1_tail |= ((ulong)b) << (8 * i);
                else
                    k2_tail |= ((ulong)b) << (8 * (i - 8));
            }
            if (k1_tail != 0)
            {
                k1_tail *= c1; k1_tail = Rotl64(k1_tail, 31); k1_tail *= c2; h1 ^= k1_tail;
            }
            if (k2_tail != 0)
            {
                k2_tail *= c2; k2_tail = Rotl64(k2_tail, 33); k2_tail *= c1; h2 ^= k2_tail;
            }
        }

        // finalization
        h1 ^= (ulong)len;
        h2 ^= (ulong)len;

        h1 += h2;
        h2 += h1;

        h1 = FMix64(h1);
        h2 = FMix64(h2);

        h1 += h2;
        // h2 += h1; // not needed if you only want 64 bits

        return h1;
    }

    private static ulong Rotl64(ulong x, int r) => (x << r) | (x >> (64 - r));

    private static ulong FMix64(ulong k)
    {
        k ^= k >> 33;
        k *= 0xff51afd7ed558ccdUL;
        k ^= k >> 33;
        k *= 0xc4ceb9fe1a85ec53UL;
        k ^= k >> 33;
        return k;
    } 
}
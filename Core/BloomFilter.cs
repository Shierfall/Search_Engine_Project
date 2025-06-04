using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SearchEngine.Core.Interfaces;

namespace SearchEngine.Core;

/// <summary>
/// Bloom filter implementation optimized for high-performance.
/// A Bloom filter is a space-efficient probabilistic data structure
/// used to test whether an element is a member of a set.
/// </summary>
public class BloomFilter : IBloomFilter
{
    // Bloom filter parameters
    private readonly ulong[] _filter;
    private readonly int _numHashes;
    private readonly ulong _filterSize;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
    
    // Performance optimization constants
    private const int PARALLEL_THRESHOLD = 1000; // Number of items before using parallel processing
    private const int BITS_PER_ELEMENT = 64; // Size of ulong in bits
    
    /// <summary>
    /// Expected size of the bloom filter in MB
    /// </summary>
    public double SizeInMB => (_filter.Length * sizeof(ulong)) / (1024.0 * 1024.0);

    /// <summary>
    /// Constructor with recommended parameters for balancing memory usage and false positive rate
    /// </summary>
    /// <param name="expectedItems">Expected number of unique items</param>
    /// <param name="falsePositiveRate">Target false positive rate (e.g., 0.01 for 1%)</param>
    public BloomFilter(int expectedItems = 1000000, double falsePositiveRate = 0.01)
    {
        // Calculate optimal filter size and number of hash functions
        double bitsPerItem = -Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2));
        _filterSize = (ulong)Math.Ceiling(expectedItems * bitsPerItem);
        _numHashes = Math.Max(1, (int)Math.Round(Math.Log(2) * _filterSize / expectedItems));
        
        // Allocate memory for the filter (rounded up to the nearest multiple of 64 bits)
        int arraySize = (int)Math.Ceiling(_filterSize / (double)BITS_PER_ELEMENT);
        _filter = new ulong[arraySize];
    }
    
    /// <summary>
    /// Add an item to the Bloom filter
    /// </summary>
    public void Add(string item)
    {
        if (string.IsNullOrEmpty(item))
            return;
            
        // Generate hash values
        var hash1 = MurmurHash3.Hash(item, 0);
        var hash2 = MurmurHash3.Hash(item, hash1);
        
        _lock.EnterWriteLock();
        try
        {
            // Set bits using double hashing technique
            for (int i = 0; i < _numHashes; i++)
            {
                var combinedHash = (hash1 + (ulong)i * hash2) % _filterSize;
                SetBit(combinedHash);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Add multiple items to the Bloom filter in a single batch operation
    /// </summary>
    public void AddBatch(IEnumerable<string> items)
    {
        if (items == null)
            return;
            
        var itemsList = items as ICollection<string> ?? new List<string>(items);
        if (itemsList.Count == 0)
            return;
            
        // Small batches: use direct locking for less overhead
        if (itemsList.Count < PARALLEL_THRESHOLD)
        {
            _lock.EnterWriteLock();
            try
            {
                foreach (var item in itemsList)
                {
                    if (string.IsNullOrEmpty(item))
                        continue;
                        
                    var hash1 = MurmurHash3.Hash(item, 0);
                    var hash2 = MurmurHash3.Hash(item, hash1);
                    
                    for (int i = 0; i < _numHashes; i++)
                    {
                        var combinedHash = (hash1 + (ulong)i * hash2) % _filterSize;
                        SetBitUnsafe(combinedHash);
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            return;
        }
        
        // Large batches: use optimized approach with thread-local bit arrays
        // 1. Calculate all bit positions in parallel without modifying the filter
        var bitPositions = new List<ulong>[itemsList.Count];
        
        Parallel.For(0, itemsList.Count, i =>
        {
            var item = itemsList is IList<string> list ? list[i] : itemsList.ElementAtOrDefault(i);
            if (string.IsNullOrEmpty(item))
                return;
                
            var localPositions = new List<ulong>(_numHashes);
            var hash1 = MurmurHash3.Hash(item, 0);
            var hash2 = MurmurHash3.Hash(item, hash1);
            
            for (int h = 0; h < _numHashes; h++)
            {
                var combinedHash = (hash1 + (ulong)h * hash2) % _filterSize;
                localPositions.Add(combinedHash);
            }
            
            bitPositions[i] = localPositions;
        });
        
        // 2. Acquire the write lock once and update the filter
        _lock.EnterWriteLock();
        try
        {
            // Set all bits at once while holding the lock
            for (int i = 0; i < bitPositions.Length; i++)
            {
                var positions = bitPositions[i];
                if (positions == null) continue;
                
                foreach (var pos in positions)
                {
                    SetBitUnsafe(pos);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    /// <summary>
    /// Check if an item might be in the set
    /// </summary>
    /// <returns>True if the item might be in the set, false if it's definitely not</returns>
    public bool MightContain(string item)
    {
        if (string.IsNullOrEmpty(item))
            return false;
            
        var hash1 = MurmurHash3.Hash(item, 0);
        var hash2 = MurmurHash3.Hash(item, hash1);
        
        _lock.EnterReadLock();
        try
        {
            for (int i = 0; i < _numHashes; i++)
            {
                var combinedHash = (hash1 + (ulong)i * hash2) % _filterSize;
                if (!GetBit(combinedHash))
                    return false;
            }
            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    /// <summary>
    /// Check if multiple items might be in the set
    /// </summary>
    /// <returns>Dictionary of items mapped to their containment status</returns>
    public Dictionary<string, bool> MightContainBatch(IEnumerable<string> items)
    {
        if (items == null)
            return new Dictionary<string, bool>();
            
        var results = new Dictionary<string, bool>();
        var itemsList = items as IList<string> ?? items.ToList();
        
        if (itemsList.Count == 0)
            return results;
        
        // For small batches, just check each item individually
        if (itemsList.Count < PARALLEL_THRESHOLD)
        {
            _lock.EnterReadLock();
            try
            {
                foreach (var item in itemsList)
                {
                    if (string.IsNullOrEmpty(item))
                    {
                        results[item ?? string.Empty] = false;
                        continue;
                    }
                    
                    var hash1 = MurmurHash3.Hash(item, 0);
                    var hash2 = MurmurHash3.Hash(item, hash1);
                    bool mightContain = true;
                    
                    for (int i = 0; i < _numHashes && mightContain; i++)
                    {
                        var combinedHash = (hash1 + (ulong)i * hash2) % _filterSize;
                        if (!GetBit(combinedHash))
                            mightContain = false;
                    }
                    
                    results[item] = mightContain;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
            return results;
        }
        
        // For large batches, use parallel processing
        var localResults = new Dictionary<string, bool>[itemsList.Count];
        
        Parallel.For(0, itemsList.Count, i =>
        {
            var item = itemsList[i];
            if (string.IsNullOrEmpty(item))
            {
                localResults[i] = new Dictionary<string, bool> { { item ?? string.Empty, false } };
                return;
            }
            
            // Calculate hashes outside the lock
            var hash1 = MurmurHash3.Hash(item, 0);
            var hash2 = MurmurHash3.Hash(item, hash1);
            
            _lock.EnterReadLock();
            bool mightContain = true;
            try
            {
                for (int h = 0; h < _numHashes && mightContain; h++)
                {
                    var combinedHash = (hash1 + (ulong)h * hash2) % _filterSize;
                    if (!GetBit(combinedHash))
                        mightContain = false;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
            
            localResults[i] = new Dictionary<string, bool> { { item, mightContain } };
        });
        
        // Merge all results
        for (int i = 0; i < localResults.Length; i++)
        {
            if (localResults[i] != null)
            {
                foreach (var kvp in localResults[i])
                {
                    results[kvp.Key] = kvp.Value;
                }
            }
        }
        
        return results;
    }
    
    /// <summary>
    /// Clear the filter
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            Array.Clear(_filter, 0, _filter.Length);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(ulong bitPosition)
    {
        var arrayIndex = bitPosition / BITS_PER_ELEMENT;
        var bitIndex = bitPosition % BITS_PER_ELEMENT;
        _filter[arrayIndex] |= 1UL << (int)bitIndex;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBitUnsafe(ulong bitPosition)
    {
        var arrayIndex = bitPosition / BITS_PER_ELEMENT;
        var bitIndex = bitPosition % BITS_PER_ELEMENT;
        _filter[arrayIndex] |= 1UL << (int)bitIndex;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetBit(ulong bitPosition)
    {
        var arrayIndex = bitPosition / BITS_PER_ELEMENT;
        var bitIndex = bitPosition % BITS_PER_ELEMENT;
        return (_filter[arrayIndex] & (1UL << (int)bitIndex)) != 0;
    }
}

/// <summary>
/// MurmurHash3 implementation for string hashing
/// Provides fast, high-quality hash functions
/// </summary>
internal static class MurmurHash3
{
    private const uint Seed = 0xc58f1a7b;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(string data, ulong seed)
    {
        const ulong c1 = 0x87c37b91114253d5;
        const ulong c2 = 0x4cf5ad432745937f;
        
        int length = data.Length;
        ulong h1 = seed;
        ulong h2 = seed;
        
        ReadOnlySpan<char> chars = data;
        
        // Body
        int i = 0;
        while (i + 4 <= length)
        {
            ulong k1 = chars[i] | ((ulong)chars[i + 1] << 16) | ((ulong)chars[i + 2] << 32) | ((ulong)chars[i + 3] << 48);
            k1 *= c1;
            k1 = RotateLeft(k1, 31);
            k1 *= c2;
            h1 ^= k1;
            h1 = RotateLeft(h1, 27);
            h1 += h2;
            h1 = h1 * 5 + 0x52dce729;
            i += 4;
        }
        
        // Tail
        ulong k = 0;
        switch (length - i)
        {
            case 3:
                k ^= (ulong)chars[i + 2] << 32;
                goto case 2;
            case 2:
                k ^= (ulong)chars[i + 1] << 16;
                goto case 1;
            case 1:
                k ^= chars[i];
                k *= c1;
                k = RotateLeft(k, 31);
                k *= c2;
                h1 ^= k;
                break;
        }
        
        // Finalization
        h1 ^= (ulong)length;
        h1 ^= h1 >> 33;
        h1 *= 0xff51afd7ed558ccd;
        h1 ^= h1 >> 33;
        h1 *= 0xc4ceb9fe1a85ec53;
        h1 ^= h1 >> 33;
        
        return h1;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotateLeft(ulong x, int r)
    {
        return (x << r) | (x >> (64 - r));
    }
}
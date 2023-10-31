namespace MinHashSharp {
    public class MinHash {
        private readonly int _numPerm;
        private readonly int _seed;
        private readonly Func<string, uint> _hashFunc;
        private readonly uint[] _hashValues;
        private (ulong[] a, ulong[] b) _perms;

        /// <summary>
        /// MinHash is a probabilistic data structure for computing Jaccard similarity between sets.
        /// </summary>
        /// <param name="numPerm">Number of random permutation functions.</param>
        /// <param name="seed">The random seed for generating the permutation functions for this MinHash. MinHash's generated with different seeds aren't comparable.</param>
        /// <param name="hashFunc">The hash function to use. Defaults to Google's Farmhash</param>
        public MinHash(int numPerm, int seed = 1111, Func<string, uint>? hashFunc = null) {
            _numPerm = numPerm;
            _seed = seed;
            _hashFunc = hashFunc ?? Farmhash.Sharp.Farmhash.Hash32;
            _perms = InitPermutations(numPerm, seed);
            // Initialize the hash values with maximum hash value (so min always updates on the first go)
            _hashValues = Enumerable.Repeat(uint.MaxValue, numPerm).ToArray();
        }

        /// <summary>
        /// Update this MinHash with more values from the current set that will be hashed.
        /// The value will be hashed using the hash function specified by the hashFunc parameter specified in the constructor.
        /// </summary>
        /// <param name="values">The values from the set to hash</param>
        /// <returns></returns>
        public MinHash Update(params string[] values) {
            // For each value, we calculate N hashes like this:
            // ((hash(val) * ai + bi) % MersennePrime)
            // Take the minimum for each one
            for (int i = 0; i < _numPerm; i++) {
                ulong a = _perms.a[i];
                ulong b = _perms.b[i];

                for (int iValue = 0; iValue < values.Length; iValue++) {
                    uint hash = (uint)((_hashFunc(values[iValue]) * a + b) % MersennePrime);
                    if (hash < _hashValues[i])
                        _hashValues[i] = hash;
                }// next value
            }

            return this; // to allow chaining
        }


        /// <summary>
        /// Estimate the Jaccard similarity between two sets represented by their MinHash object. 
        /// </summary>
        /// <param name="other">The MinHash representing the comparison set</param>
        /// <returns>The estimated Jaccard similarity, between 0.0 and 1.0</returns>
        /// <exception cref="ArgumentException">Comparison MinHash must have the same seed and number of permutations</exception>
        public double Jaccard(MinHash other) {
            // Validation:
            if (other._seed != _seed)
                throw new ArgumentException("Cannot compute Jaccard given MinHash with different seeds");
            if (other._numPerm != _numPerm)
                throw new ArgumentException("Cannot compute Jaccard given MinHash with different number of permutation functions");

            // Jaccard similarity is defined as the number of intersect / count. 
            int c = 0;
            for (int i = 0; i < _numPerm; i++) {
                if (other._hashValues[i] == _hashValues[i])
                    c++;
            }

            return c / (double)_numPerm;
        }

        /// <summary>
        /// Return a subset of the hash values in a specific range. Used by the MinHashLSH to index into buckets.
        /// </summary>
        /// <param name="start">Index of first hash value</param>
        /// <param name="end">Index of last hash value (exclusive upper-bound)</param>
        /// <returns></returns>
        public uint[] HashValues(int start, int end) => _hashValues[start..Math.Min(end, _hashValues.Length)];

        /// <summary>
        /// The number of permutations in this MinHash
        /// </summary>
        public int Length => _numPerm;


        // http://en.wikipedia.org/wiki/Mersenne_prime
        private static readonly ulong MersennePrime = (1L << 61) - 1;
        // Store a cache with the most recently initialized permutations, since often the use-case will involve
        // creating many hashes from the same permutations, and no reason to recalculate it multiple times.
        // For the future, may be worth using a LRU dictionary or perhaps storing the cache in another 
        // structure which will manage the cache.
        private static (int perm, int seed, ulong[] a, ulong[] b) _cachedPermutation = (0, 0, Array.Empty<ulong>(), Array.Empty<ulong>());
        
        private static (ulong[] a, ulong[] b) InitPermutations(int numPerm, int seed) {
            // In case there's an update to the cache object from a different thread, just store the instance
            // locally, so the check will be safe. 
            var cached = _cachedPermutation;
            if (cached.perm == numPerm && cached.seed == seed)
                return (cached.a, cached.b);

            // Create parameters for a random bijective permutation function
            // that maps a 32-bit hash value to another 32-bit hash value.
            // http://en.wikipedia.org/wiki/Universal_hashing

            var r = new Random(seed);

            // Calculate the permtuations!
            var a = new ulong[numPerm];
            var b = new ulong[numPerm];
            for (int i = 0; i < numPerm; i++) {
                a[i] = (ulong)r.NextInt64(1, (long)MersennePrime);
                b[i] = (ulong)r.NextInt64(0, (long)MersennePrime);
            }// next perm

            // Cache the result and return it
            _cachedPermutation = (numPerm, seed, a, b);
            return (a, b);
        }
    }
}
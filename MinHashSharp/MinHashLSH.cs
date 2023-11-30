using MathNet.Numerics.Integration;
using System.Runtime.InteropServices;
using TqdmSharp;

namespace MinHashSharp {
    public class MinHashLSH {
        private readonly int _numPerm;
        private readonly int _numBuckets;
        private readonly int _bucketRange;
        private readonly Dictionary<string, HashSet<string>>[] _hashTables;
        private readonly (int start, int end)[] _hashRanges;
        private readonly HashSet<string> _keys;
        private bool _frozen;

        /// <summary>
        /// The `MinHash_LSH` index. 
        /// Supporting for big-data fast querying using an approximate `Jaccard similarity` threshold 
        /// </summary>
        /// <param name="threshold">The Jaccard similarity threshold between 0.0 and 1.0. The initialized MinHash LSH will optimize the bucket count and ranges to minimize the false positives and false negatives.</param>
        /// <param name="numPerm">The number of permutation functions used by the MinHash to be indexed.</param>
        public MinHashLSH(double threshold, int numPerm) : this(threshold, numPerm, (0.5, 0.5)) {
        }

        /// <summary>
        /// The `MinHash_LSH` index. 
        /// Supporting for big-data fast querying using an approximate `Jaccard similarity` threshold 
        /// </summary>
        /// <param name="threshold">The Jaccard similarity threshold between 0.0 and 1.0. The initialized MinHash LSH will optimize the bucket count and ranges to minimize the false positives and false negatives.</param>
        /// <param name="numPerm">The number of permutation functions used by the MinHash to be indexed.</param>
        /// <param name="weights">The weights to apply to the bucket optimization for fp (false-positive) and fn (false-negative) matches. Defaults to (0.5, 0.5)</param>
        public MinHashLSH(double threshold, int numPerm, (double fp, double fn) weights) : this(numPerm, CalculateOptimalBucketParams(threshold, numPerm, weights)) {
        }

        private MinHashLSH(int numPerm, (int numBuckets, int bucketRange) param) {
            if (numPerm < 2)
                throw new ArgumentOutOfRangeException(nameof(numPerm), "Must be >=2");
            if (param.numBuckets * param.bucketRange > numPerm)
                throw new ArgumentOutOfRangeException(nameof(param), "Product of bucket * range must be less than numPerm");
            if (param.numBuckets < 2)
                throw new ArgumentOutOfRangeException(nameof(param.numBuckets), "Must be >=2");

            _numPerm = numPerm;
            _numBuckets = param.numBuckets;
            _bucketRange = param.bucketRange;
            _keys = new HashSet<string>();
            _hashTables = new Dictionary<string, HashSet<string>>[_numBuckets];
            _hashRanges = new (int start, int end)[_numBuckets];
            for (int i = 0; i < _numBuckets; i++) {
                _hashTables[i] = new();
                _hashRanges[i] = (i * _bucketRange, (i + 1) * _bucketRange);
            }
        }

        /// <summary>
        /// Insert a key to the index, together with a MinHash of the set referenced by a unique key.
        /// A list of keys will be the return value when querying for matches. 
        /// </summary>
        /// <param name="key">A unique key representing the hash</param>
        /// <param name="mh">The MinHash object</param>
        /// <exception cref="ArgumentException"></exception>
        public void Insert(string key, MinHash mh) {
            if (_frozen)
                throw new MethodAccessException("Cannot insert new hashes into a frozen index.");
            if (mh.Length != _numPerm)
                throw new ArgumentException("Permutation length of minhash doesn't match expected.", nameof(mh));
            if (_keys.Contains(key))
                throw new ArgumentException("Key already exists", nameof(key));

            _keys.Add(key);
            // Calculate the representation of each hash range and index them
            for (int i = 0; i < _numBuckets; i++) {
                var h = CreateRepresentationOfHashValues(mh.HashValues(_hashRanges[i].start, _hashRanges[i].end));

                if (!_hashTables[i].ContainsKey(h))
                    _hashTables[i].Add(h, new());
                _hashTables[i][h].Add(key);
            }// next bucket
        }

        /// <summary>
        /// Giving the MinHash of the query set, retrieve the keys that reference sets with
        /// Jaccard similarities likely greater than the threshold.
        /// Results are based on minhash segment collision and are thus approximate.
        /// For more accurate results, filter again with: `MinHash.jaccard`.
        /// </summary>
        /// <param name="mh">The MinHash object to find approximate matches for</param>
        /// <returns>An enumerable of `keys` that reference MinHash objects that have a Jaccard similarity >= threshold (approximate)</returns>
        /// <exception cref="ArgumentException"></exception>
        public IEnumerable<string> Query(MinHash mh) {
            if (mh.Length != _numPerm)
                throw new ArgumentException("Permutation length of minhash doesn't match expected.", nameof(mh));

            // Calculate the representation of each hash range and check for matches in our table
            for (int i = 0; i < _numBuckets; i++) {
                var h = CreateRepresentationOfHashValues(mh.HashValues(_hashRanges[i].start, _hashRanges[i].end));

                if (_hashTables[i].ContainsKey(h)) {
                    foreach (string key in _hashTables[i][h])
                        yield return key;
                }
            }// next bucket
        }

        /// <summary>
        /// Freeze the LSH so that no more keys can be added, and by doing so we can clear out the keys array and save memory.
        /// </summary>
        public void Freeze() {
            _frozen = true;
            // Clear out the keys set, no need to store them anymore
            _keys.Clear();
        }

        public int Count => _keys.Count;

        public bool IsFrozen => _frozen;

        /// <summary>
        /// Serialize the LSH index to a binary file.
        /// </summary>
        /// <param name="path">Path to save the index</param>
        public void Serialize(string path) {
            using var stream = File.OpenWrite(path);
            using var bw = new BinaryWriter(stream);

            // Write out the 3 parameters
            bw.Write(_frozen);
            bw.Write(_numPerm);
            bw.Write(_numBuckets);
            bw.Write(_bucketRange);
            // The _hashRanges is generated automatically from these parameters, so no need to serialize it
            // Save all the keys 
            bw.Write(_keys.Count);
            foreach (string key in _keys)
                bw.Write(key);
            // Save the _hashTables - count is _numBuckets
            for (int i = 0; i < _numBuckets; i++) {
                bw.Write(_hashTables[i].Count);
                foreach (var kvp in _hashTables[i]) {
                    bw.Write(kvp.Key);
                    bw.Write(kvp.Value.Count);
                    foreach (var v in kvp.Value)
                        bw.Write(v);
                }
            }
        }
        /// <summary>
        /// Deserialize an LSH index from a binary file saved using <see cref="MinHashLSH.Serialize(string)"/>.
        /// </summary>
        /// <param name="path">Path to the saved LSH index</param>
        /// <param name="asFrozen">Whether to treat the index as frozen, and not load in the key array</param>
        /// <param name="verbose">Print out progress updates to the console.</param>
        /// <returns>The loaded LSH index</returns>
        public static MinHashLSH Deserialize(string path, bool asFrozen = false, bool verbose = false) {
            if (verbose)
                Console.WriteLine($"Deserializing LSH from {path}...");
            using var stream = File.OpenRead(path);
            using var br = new BinaryReader(stream);

            // Read the first 3 parameters
            bool frozen = br.ReadBoolean() || asFrozen;
            int numPerm = br.ReadInt32();
            int numBuckets = br.ReadInt32();
            int bucketRange = br.ReadInt32();
            // Create our LSH object
            var lsh = new MinHashLSH(numPerm, (numBuckets, bucketRange));

            if (verbose)
                Console.WriteLine("Loading in keys set:");
            // Read in the keys
            int count = br.ReadInt32();
            var tqdm = verbose ? new Tqdm.ProgressBar(total: count) : null;
            while (count-- > 0) {
                string key = br.ReadString();
                if (!asFrozen)
                    lsh._keys.Add(key);
                tqdm?.Step();
            }
            if (count > 0)
                tqdm?.Finish();
            tqdm?.Reset();

            // Read in the _hashTables
            if (verbose)
                Console.WriteLine($"Reading in {numBuckets} buckets:");
            // Save the _hashTables - count is _numBuckets
            for (int i = 0; i < numBuckets; i++) {
                count = br.ReadInt32();
                tqdm?.Reset();
                tqdm?.SetLabel($"Bucket #{i + 1}");
                tqdm?.Progress(0, count); // reset our total for the tqdm progress bar

                while (count-- > 0) {
                    // Key: string, value: HashSet<string>
                    string key = br.ReadString();
                    var value = new HashSet<string>();
                    int valueCount = br.ReadInt32();
                    while (valueCount-- > 0)
                        value.Add(br.ReadString());
                    // Add it in to our hashTables object
                    lsh._hashTables[i][key] = value;

                    tqdm?.Step();
                }
            }
            tqdm?.Finish();

            if (asFrozen)
                lsh.Freeze();

            return lsh;
        }


        private static string CreateRepresentationOfHashValues(uint[] vals) {
            // Convert the uints to chars, and then convert to string
            // This isn't perfect, but we tested 6 different ways to index the hash values,
            // and this was the fastest by a large factor. 
            return new string(MemoryMarshal.Cast<uint, char>(vals));
        }

        private static (int b, int r) CalculateOptimalBucketParams(double threshold, int numPerm, (double fp, double fn) weights) {
            // Validations:
            if (threshold is > 1 or < 0)
                throw new ArgumentOutOfRangeException(nameof(threshold), "Must be in [0.0, 1.0]");
            if (weights.fn is < 0 or > 1 || weights.fp is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(weights), "Must be in [0.0, 1.0]");
            if (Math.Round(weights.fn + weights.fp, 1) != 1.0)
                throw new ArgumentOutOfRangeException(nameof(weights), "Must sum to 1.0");

            double minError = double.MaxValue;
            var opt = (0, 0);// optimal value to return, initialize to zeros

            // Figure out the optimal way to choose the number buckets and the bucket range
            // Go through and calculate the error for each permutation
            for (int b = 1; b <= numPerm; b++) {
                int maxR = numPerm / b;
                for (int r = 1; r <= maxR; r++) {
                    // Calculate the false positive and negative probabilities by integrating over the
                    // error function for each, and choose the set that produces the smallest error.
                    // fp = f(x) = 1 - ((1 - x^r)^b), integrated from 0 to thresh
                    var fpProb = GaussLegendreRule.Integrate(x => 1 - Math.Pow(1 - Math.Pow(x, r), b), invervalBegin: 0, invervalEnd: threshold, order: 5);
                    // fn = f(x) = 1 - fp(x), integrated from thresh to 1
                    var fnProb = GaussLegendreRule.Integrate(x => Math.Pow(1 - Math.Pow(x, r), b), invervalBegin: threshold, invervalEnd: 1, order: 5);

                    // Combine the probabilities using the weights that were given (e.g., one can prioritize
                    // having fewer false positives at the cost of false negatives)
                    var error = fpProb * weights.fp + fnProb * weights.fn;
                    if (error < minError) {
                        opt = (b, r);
                        minError = error;
                    }
                }// next range size
            }// next bucket size

            return opt;
        }
    }
}
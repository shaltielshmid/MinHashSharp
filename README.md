# MinHashSharp - A simple lightweight library for similarity estimation

`MinHashSharp` offers a simple lightweight data structure for indexing and estimating Jaccard similarity between sets. 

Was tested with 60GB of data and tens of millions of documents, and ran smoothly and efficiently. 

The library currently offers two classes:

`MinHash`: A probabilistic data structure for computing Jaccard similarity between sets. 

`MinHashLSH`: A class for supporting big-data fast querying using an approximate `Jaccard similarity` threshold.

## Sample usage

```cs
string s1 = "The quick brown fox jumps over the lazy dog and proceeded to run towards the other room";
string s2 = "The slow purple elephant runs towards the happy fox and proceeded to run towards the other room";
string s3 = "The quick brown fox jumps over the angry dog and proceeded to run towards the other room";

var m1 = new MinHash(numPerm: 128).Update(s1.Split());
var m2 = new MinHash(numPerm: 128).Update(s2.Split());
var m3 = new MinHash(numPerm: 128).Update(s3.Split());

Console.WriteLine(m1.Jaccard(m2));// 0.51

var lsh = new MinHashLSH(threshold: 0.8, numPerm: 128);

lsh.Insert("s1", m1);
lsh.Insert("s2", m2);

Console.WriteLine(string.Join(", ", lsh.Query(m3))); // s1
```

## Multi-threading

The library is entirely thread-safe except for the `MinHashLSH.Insert` function (and the custom injected hash function, if relevant). Therefore, you can create `MinHash` objects on multiple threads and query the same `MinHashLSH` object freely. If you are indexing sets on multiple threads, then just make sure to gain exclusive access to the LSH around every `Insert` call:

```cs
lock (lsh) {
    lsh.Insert("s3", m3);
}
```

## Custom hash function

By default, the library uses the [Farmhash function](https://opensource.googleblog.com/2014/03/introducing-farmhash.html) introduced by Google for efficiency. For more accurate hashes, one can inject a custom hash function into the `MinHash` object.

For example, if you want to use the C# default string hash function:

```cs
static uint StringHash(string s) => (uint)s.GetHashCode();

var m = new MinHash(numPerm: 128, hashFunc: StringHash).Update(s1.Split());
```

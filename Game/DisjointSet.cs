namespace JigsawPuzzle.Game;

/// <summary>Union-find over integer indices with path halving and union by size.</summary>
public sealed class DisjointSet
{
    private readonly int[] _parent;
    private readonly int[] _size;

    public DisjointSet(int count)
    {
        _parent = new int[count];
        _size = new int[count];
        for (var i = 0; i < count; i++)
        {
            _parent[i] = i;
            _size[i] = 1;
        }
        SetCount = count;
    }

    /// <summary>Number of disjoint sets remaining; 1 means everything is connected.</summary>
    public int SetCount { get; private set; }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]];
            x = _parent[x];
        }
        return x;
    }

    /// <returns>true if the elements were in different sets and are now merged.</returns>
    public bool Union(int a, int b)
    {
        a = Find(a);
        b = Find(b);
        if (a == b)
            return false;
        if (_size[a] < _size[b])
            (a, b) = (b, a);
        _parent[b] = a;
        _size[a] += _size[b];
        SetCount--;
        return true;
    }
}

using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Builds Minimum Spanning Tree using Kruskal's algorithm.
/// Creates optimal tree structure connecting all locations with minimum total distance.
/// </summary>
public class MinimumSpanningTree
{
    private record Edge(SourceLocation From, SourceLocation To, double Distance);

    /// <summary>
    /// Builds MST and returns adjacency list
    /// </summary>
    public Dictionary<SourceLocation, List<SourceLocation>> BuildMST(List<SourceLocation> locations)
    {
        if (locations.Count <= 1)
            return locations.ToDictionary(l => l, l => new List<SourceLocation>());

        // Step 1: Create all possible edges
        var allEdges = new List<Edge>();
        for (var i = 0; i < locations.Count; i++)
        {
            for (var j = i + 1; j < locations.Count; j++)
            {
                var distance = HaversineDistance(locations[i], locations[j]);
                allEdges.Add(new Edge(locations[i], locations[j], distance));
            }
        }

        // Step 2: Sort edges by distance (shortest first)
        allEdges.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        // Step 3: Kruskal's - add shortest edges that don't create cycles
        var unionFind = new UnionFind(locations);
        var mstEdges = new List<Edge>();

        foreach (var edge in allEdges)
        {
            if (!unionFind.IsConnected(edge.From, edge.To))
            {
                unionFind.Union(edge.From, edge.To);
                mstEdges.Add(edge);

                // MST complete when we have n-1 edges
                if (mstEdges.Count == locations.Count - 1)
                    break;
            }
        }

        // Step 4: Build adjacency list
        var adjacency = locations.ToDictionary(l => l, l => new List<SourceLocation>());
        foreach (var edge in mstEdges)
        {
            adjacency[edge.From].Add(edge.To);
            adjacency[edge.To].Add(edge.From);
        }

        return adjacency;
    }

    /// <summary>
    /// Convert MST graph to traversable path with DFS (creates out-and-back for branches)
    /// </summary>
    public List<SourceLocation> TreeToPath(Dictionary<SourceLocation, List<SourceLocation>> tree)
    {
        if (tree.Count == 0)
            return new List<SourceLocation>();

        // Start from a leaf node (location with only 1 neighbor) for natural path
        var start = tree
            .Where(kvp => kvp.Value.Count == 1)
            .Select(kvp => kvp.Key)
            .FirstOrDefault() ?? tree.First().Key;

        var path = new List<SourceLocation>();
        var visited = new HashSet<SourceLocation>();

        DfsTraversal(start, tree, visited, path);

        return path;
    }

    private void DfsTraversal(SourceLocation current, Dictionary<SourceLocation, List<SourceLocation>> tree,
        HashSet<SourceLocation> visited, List<SourceLocation> path)
    {
        visited.Add(current);
        path.Add(current);

        foreach (var neighbor in tree[current])
        {
            if (!visited.Contains(neighbor))
            {
                DfsTraversal(neighbor, tree, visited, path);
                path.Add(current); // Backtrack
            }
        }
    }

    private double HaversineDistance(SourceLocation a, SourceLocation b)
    {
        const double R = 6371.0;

        var lat1 = ToRadians(a.Lat);
        var lat2 = ToRadians(b.Lat);
        var dLat = ToRadians(b.Lat - a.Lat);
        var dLon = ToRadians(b.Lon - a.Lon);

        var x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x));

        return R * c;
    }

    private double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private class UnionFind
    {
        private readonly Dictionary<SourceLocation, SourceLocation> _parent;
        private readonly Dictionary<SourceLocation, int> _rank;

        public UnionFind(List<SourceLocation> locations)
        {
            _parent = locations.ToDictionary(l => l, l => l);
            _rank = locations.ToDictionary(l => l, l => 0);
        }

        public SourceLocation Find(SourceLocation location)
        {
            if (_parent[location] != location)
                _parent[location] = Find(_parent[location]);

            return _parent[location];
        }

        public bool IsConnected(SourceLocation a, SourceLocation b)
        {
            return Find(a) == Find(b);
        }

        public void Union(SourceLocation a, SourceLocation b)
        {
            var rootA = Find(a);
            var rootB = Find(b);

            if (rootA == rootB)
                return;

            if (_rank[rootA] < _rank[rootB])
            {
                _parent[rootA] = rootB;
            }
            else if (_rank[rootA] > _rank[rootB])
            {
                _parent[rootB] = rootA;
            }
            else
            {
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
        }
    }
}

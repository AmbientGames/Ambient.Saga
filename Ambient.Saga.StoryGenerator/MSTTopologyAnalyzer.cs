using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Analyzes MST topology to identify narrative structure:
/// - Main thread (longest path)
/// - Branch threads (side stories)
/// - Convergence points (where branches meet)
/// </summary>
public class MSTTopologyAnalyzer
{
    /// <summary>
    /// Analyzes MST edges to extract story threads
    /// </summary>
    public List<StoryThread> AnalyzeThreads(List<(SourceLocation from, SourceLocation to)> mstEdges, List<SourceLocation> allLocations)
    {
        var threads = new List<StoryThread>();

        if (mstEdges.Count == 0)
        {
            // Single location - treat as standalone optional content
            if (allLocations.Count > 0)
            {
                threads.Add(new StoryThread
                {
                    RefName = "THREAD_STANDALONE",
                    DisplayName = "Standalone Location",
                    Type = StoryThreadType.Optional,
                    Locations = allLocations,
                    BranchDepth = 0
                });
            }
            return threads;
        }

        // Build adjacency list for graph traversal
        var graph = BuildGraph(mstEdges);

        // Find leaf nodes (degree 1) for path endpoints
        var leafNodes = graph.Where(kvp => kvp.Value.Count == 1).Select(kvp => kvp.Key).ToList();

        // Find longest path (main thread)
        var (mainPath, maxLength) = FindLongestPath(graph, leafNodes);

        if (mainPath.Count > 0)
        {
            threads.Add(new StoryThread
            {
                RefName = "THREAD_MAIN",
                DisplayName = "Main Quest",
                Type = StoryThreadType.Main,
                Locations = mainPath,
                BranchDepth = 0
            });
        }

        // Find branches off the main path
        var mainPathSet = new HashSet<SourceLocation>(mainPath);
        var visitedBranches = new HashSet<SourceLocation>(mainPath);
        var branchIndex = 1;

        foreach (var mainLocation in mainPath)
        {
            var branches = FindBranches(graph, mainLocation, mainPathSet, visitedBranches);
            foreach (var branch in branches)
            {
                threads.Add(new StoryThread
                {
                    RefName = $"THREAD_BRANCH_{branchIndex:D2}",
                    DisplayName = $"Side Quest {branchIndex}",
                    Type = StoryThreadType.Branch,
                    Locations = branch,
                    BranchDepth = 1
                });
                branchIndex++;
            }
        }

        return threads;
    }

    /// <summary>
    /// Build adjacency list from MST edges
    /// </summary>
    private Dictionary<SourceLocation, List<SourceLocation>> BuildGraph(List<(SourceLocation from, SourceLocation to)> edges)
    {
        var graph = new Dictionary<SourceLocation, List<SourceLocation>>();

        foreach (var (from, to) in edges)
        {
            if (!graph.ContainsKey(from))
                graph[from] = new List<SourceLocation>();
            if (!graph.ContainsKey(to))
                graph[to] = new List<SourceLocation>();

            graph[from].Add(to);
            graph[to].Add(from); // Undirected graph
        }

        return graph;
    }

    /// <summary>
    /// Find longest path in tree (DFS from each leaf)
    /// </summary>
    private (List<SourceLocation> path, int length) FindLongestPath(
        Dictionary<SourceLocation, List<SourceLocation>> graph,
        List<SourceLocation> leafNodes)
    {
        var longestPath = new List<SourceLocation>();
        var maxLength = 0;

        foreach (var leaf in leafNodes)
        {
            var (path, length) = DFS(graph, leaf, null, new HashSet<SourceLocation>());
            if (length > maxLength)
            {
                maxLength = length;
                longestPath = path;
            }
        }

        return (longestPath, maxLength);
    }

    /// <summary>
    /// DFS to find longest path from a node
    /// </summary>
    private (List<SourceLocation> path, int length) DFS(
        Dictionary<SourceLocation, List<SourceLocation>> graph,
        SourceLocation current,
        SourceLocation? parent,
        HashSet<SourceLocation> visited)
    {
        visited.Add(current);
        var longestPath = new List<SourceLocation> { current };
        var maxLength = 0;

        foreach (var neighbor in graph[current])
        {
            if (neighbor != parent && !visited.Contains(neighbor))
            {
                var (subPath, subLength) = DFS(graph, neighbor, current, visited);
                if (subLength > maxLength)
                {
                    maxLength = subLength;
                    longestPath = new List<SourceLocation> { current };
                    longestPath.AddRange(subPath);
                }
            }
        }

        return (longestPath, maxLength + 1);
    }

    /// <summary>
    /// Find all branches emanating from a main path location
    /// </summary>
    private List<List<SourceLocation>> FindBranches(
        Dictionary<SourceLocation, List<SourceLocation>> graph,
        SourceLocation branchPoint,
        HashSet<SourceLocation> mainPath,
        HashSet<SourceLocation> visited)
    {
        var branches = new List<List<SourceLocation>>();

        foreach (var neighbor in graph[branchPoint])
        {
            if (!mainPath.Contains(neighbor) && !visited.Contains(neighbor))
            {
                var branch = TraceBranch(graph, neighbor, branchPoint, visited);
                if (branch.Count > 0)
                {
                    branches.Add(branch);
                }
            }
        }

        return branches;
    }

    /// <summary>
    /// Trace a branch path from a starting point
    /// </summary>
    private List<SourceLocation> TraceBranch(
        Dictionary<SourceLocation, List<SourceLocation>> graph,
        SourceLocation start,
        SourceLocation parent,
        HashSet<SourceLocation> visited)
    {
        var branch = new List<SourceLocation> { start };
        visited.Add(start);
        var current = start;
        var prev = parent;

        // Follow branch until we hit a leaf or reconnect to visited
        while (true)
        {
            var unvisitedNeighbors = graph[current].Where(n => n != prev && !visited.Contains(n)).ToList();

            if (unvisitedNeighbors.Count == 0)
                break; // Leaf node

            // Follow the first unvisited neighbor (branches within branches handled recursively)
            var next = unvisitedNeighbors[0];
            branch.Add(next);
            visited.Add(next);
            prev = current;
            current = next;
        }

        return branch;
    }
}

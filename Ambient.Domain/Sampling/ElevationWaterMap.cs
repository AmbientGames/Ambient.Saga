using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ambient.Domain.Sampling;

/// <summary>
/// Memory-efficient storage for elevation data with derived water detection using bit-packed ushort array.
/// Bits 0-14: Elevation data (0-32767)
/// Bit 15: Water flag (1 = water, 0 = land)
/// </summary>
public class ElevationWaterMap
{
    private const ushort WaterBitMask = 0x8000; // 15th bit
    private const ushort ElevationMask = 0x7FFF; // Bits 0-14
    
    private readonly ushort[,] _data;
    
    public int Width { get; }
    public int Height { get; }
    public ushort MinElevation { get; private set; }
    public ushort MaxElevation { get; private set; }
    public ushort SeaLevel { get; private set; }

    public ElevationWaterMap(int width, int height)
    {
        Width = width;
        Height = height;
        _data = new ushort[width, height];
        MinElevation = ushort.MaxValue;
        MaxElevation = ushort.MinValue;
    }

    /// <summary>
    /// Creates an ElevationWaterMap from a height map image with water detection.
    /// </summary>
    public static ElevationWaterMap FromHeightMap(Image<L16> image, int minWaterAreaSize, bool adjustMinWaterAreaSizeByElevation)
    {
        var map = new ElevationWaterMap(image.Width, image.Height);
        
        // Extract elevation data and find min/max
        var elevationData = new ushort[image.Width, image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    var elevation = row[x].PackedValue;
                    elevationData[x, y] = elevation;
                    map.MinElevation = Math.Min(map.MinElevation, elevation);
                    map.MaxElevation = Math.Max(map.MaxElevation, elevation);
                }
            }
        });

        map.SeaLevel = map.MinElevation;

        // Detect water using flood fill algorithm
        var waterMask = DetectWater(elevationData, image.Width, image.Height, map.SeaLevel, minWaterAreaSize, map.MinElevation, map.MaxElevation, adjustMinWaterAreaSizeByElevation);

        // Pack elevation and water data into bit-packed format
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var elevation = elevationData[x, y];
                var isWater = waterMask[x, y];
                
                // Pack: elevation in bits 0-14, water flag in bit 15
                map._data[x, y] = (ushort)(elevation | (isWater ? WaterBitMask : 0));
            }
        }

        return map;
    }

    /// <summary>
    /// Gets the elevation at the specified coordinates.
    /// </summary>
    public ushort GetElevation(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return 0;
            
        return (ushort)(_data[x, y] & ElevationMask);
    }

    /// <summary>
    /// Gets whether the location is water.
    /// </summary>
    public bool IsWater(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;
            
        return (_data[x, y] & WaterBitMask) != 0;
    }

    /// <summary>
    /// Gets the raw packed data at the specified coordinates.
    /// No bounds checking for performance - caller must ensure valid coordinates.
    /// </summary>
    public ushort GetPackedData(int x, int y)
    {
        return _data[x, y];
    }

    /// <summary>
    /// Gets both elevation and water data at the specified coordinates.
    /// </summary>
    public (ushort elevation, bool isWater) GetData(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return (0, false);
            
        var packed = _data[x, y];
        return ((ushort)(packed & ElevationMask), (packed & WaterBitMask) != 0);
    }

    private static bool[,] DetectWater(ushort[,] elevationData, int width, int height, ushort seaLevel, int minWaterAreaSize, ushort minElevation, ushort maxElevation, bool adjustMinWaterAreaSizeByElevation)
    {
        var waterMask = new bool[width, height];
        var visited = new bool[width, height];
        var flatAreas = new List<List<(int x, int y)>>();

        // Find all flat areas using flood fill with elevation-based size threshold
        var elevationRange = maxElevation - minElevation;
        
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!visited[x, y])
                {
                    var area = FloodFillFlatArea(elevationData, visited, x, y, width, height);
                    if (area.Count > 2)
                    {
                        // Calculate elevation-based minimum size - smaller areas allowed at higher elevations
                        var avgElevation = area.Average(p => elevationData[p.x, p.y]);
                        var elevationFactor = elevationRange > 0 ? (double)(avgElevation - minElevation) / elevationRange : 0;
                        var adjustedMinWaterAreaSize = minWaterAreaSize;
                        if (adjustMinWaterAreaSizeByElevation)
                        {
                            adjustedMinWaterAreaSize = (int)(minWaterAreaSize * (1.0 - elevationFactor * 0.95));
                        }

                        if (adjustedMinWaterAreaSize < 2)
                        {
                            adjustedMinWaterAreaSize = 2;
                        }

                        if (area.Count >= adjustedMinWaterAreaSize)
                        {
                            flatAreas.Add(area);
                        }
                    }
                }
            }
        }

        // Mark all detected flat areas as water
        foreach (var area in flatAreas)
        {
            foreach (var (x, y) in area)
            {
                waterMask[x, y] = true;
            }
        }

        // Flow water to lower elevations
        FlowWaterToLowerElevations(elevationData, waterMask, width, height);

        return waterMask;
    }

    private static List<(int x, int y)> FloodFillFlatArea(ushort[,] elevationData, bool[,] visited, int startX, int startY, int width, int height)
    {
        var area = new List<(int x, int y)>();
        var stack = new Stack<(int x, int y)>();
        var baseElevation = elevationData[startX, startY];
        
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            
            if (x < 0 || x >= width || y < 0 || y >= height || visited[x, y])
                continue;

            var elevation = elevationData[x, y];
            if (Math.Abs(elevation - baseElevation) > 0)
                continue;

            visited[x, y] = true;
            area.Add((x, y));

            stack.Push((x, y - 1));
            stack.Push((x - 1, y));
            stack.Push((x + 1, y));
            stack.Push((x, y + 1));
        }

        return area;
    }

    private static void FlowWaterToLowerElevations(ushort[,] elevationData, bool[,] waterMask, int width, int height, int maxFlowDistance = 3)
    {
        var distanceMap = new int[width, height];
        var currentFrontier = new Queue<(int x, int y, int distance)>();
        
        // Initialize with original water sources at distance 0
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (waterMask[x, y])
                {
                    distanceMap[x, y] = 0;
                    currentFrontier.Enqueue((x, y, 0));
                }
                else
                {
                    distanceMap[x, y] = int.MaxValue;
                }
            }
        }
        
        var neighbors = new[]
        {
            (0, -1), (1, -1), (1, 0), (1, 1),
            (0, 1), (-1, 1), (-1, 0), (-1, -1)
        };
        
        while (currentFrontier.Count > 0)
        {
            var (x, y, distance) = currentFrontier.Dequeue();
            
            // Stop flowing if we've reached the maximum distance
            if (distance >= maxFlowDistance)
                continue;
                
            var currentElevation = elevationData[x, y];
            
            foreach (var (dx, dy) in neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;
                    
                if (waterMask[nx, ny])
                    continue;
                    
                var neighborElevation = elevationData[nx, ny];
                var newDistance = distance + 1;
                
                // Only flow if elevation allows and we haven't found a shorter path
                if (neighborElevation <= currentElevation && newDistance < distanceMap[nx, ny])
                {
                    waterMask[nx, ny] = true;
                    distanceMap[nx, ny] = newDistance;
                    currentFrontier.Enqueue((nx, ny, newDistance));
                }
            }
        }
    }
}
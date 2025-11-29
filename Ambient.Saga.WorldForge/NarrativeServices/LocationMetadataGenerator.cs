using Ambient.Domain;
using Ambient.Saga.WorldForge;

namespace Ambient.Saga.WorldForge.NarrativeServices;

/// <summary>
/// Generates AI metadata for locations to guide narrative coherence.
/// </summary>
public class LocationMetadataGenerator
{
    private readonly RefNameGenerator _refNameGenerator;

    public LocationMetadataGenerator(RefNameGenerator refNameGenerator)
    {
        _refNameGenerator = refNameGenerator;
    }

    public Dictionary<string, LocationAIMetadata> GenerateLocationMetadata(List<StoryThread> threads, NarrativeStructure narrative)
    {
        var metadata = new Dictionary<string, LocationAIMetadata>();
        var mainThread = threads.FirstOrDefault(t => t.Type == StoryThreadType.Main);

        if (mainThread == null)
            return metadata;

        // Assign narrative roles based on position in main thread
        for (var i = 0; i < mainThread.Locations.Count; i++)
        {
            var location = mainThread.Locations[i];
            var refName = _refNameGenerator.GetRefName(location);
            var progress = (double)i / Math.Max(1, mainThread.Locations.Count - 1);

            var meta = new LocationAIMetadata
            {
                RefName = refName,
                StoryThreadRefs = new List<string> { mainThread.RefName },
                NarrativeSequence = i,
                NarrativeRole = DetermineNarrativeRole(i, mainThread.Locations.Count, location.Type),
                Tone = DetermineTone(progress, location.Type),
                ThematicTags = DetermineThematicTags(progress, location.Type),
                NarrativeConnections = GenerateNarrativeConnections(location, mainThread.Locations, i)
            };

            metadata[refName] = meta;
        }

        // Add branch threads
        foreach (var branchThread in threads.Where(t => t.Type == StoryThreadType.Branch))
        {
            for (var i = 0; i < branchThread.Locations.Count; i++)
            {
                var location = branchThread.Locations[i];
                var refName = _refNameGenerator.GetRefName(location);

                if (metadata.ContainsKey(refName))
                {
                    // Location appears in multiple threads
                    metadata[refName].StoryThreadRefs.Add(branchThread.RefName);
                }
                else
                {
                    metadata[refName] = new LocationAIMetadata
                    {
                        RefName = refName,
                        StoryThreadRefs = new List<string> { branchThread.RefName },
                        NarrativeSequence = i,
                        NarrativeRole = NarrativeRole.SideQuest,
                        Tone = "exploratory",
                        ThematicTags = "optional,discovery,reward",
                        NarrativeConnections = new List<NarrativeConnection>()
                    };
                }
            }
        }

        return metadata;
    }

    private NarrativeRole DetermineNarrativeRole(int index, int totalCount, SourceLocationType type)
    {
        if (totalCount <= 1)
            return NarrativeRole.Optional;

        var progress = (double)index / (totalCount - 1);

        if (index == 0)
            return NarrativeRole.Introduction;
        else if (progress < 0.25)
            return NarrativeRole.IncitingIncident;
        else if (progress < 0.7)
            return NarrativeRole.RisingAction;
        else if (progress < 0.85)
            return NarrativeRole.Climax;
        else if (progress < 0.95)
            return NarrativeRole.FallingAction;
        else
            return NarrativeRole.Resolution;
    }

    private string DetermineTone(double progress, SourceLocationType type)
    {
        if (type == SourceLocationType.Structure)
            return progress > 0.7 ? "climactic" : "challenging";
        else if (progress < 0.3)
            return "welcoming";
        else if (progress < 0.7)
            return "mysterious";
        else
            return "urgent";
    }

    private string DetermineThematicTags(double progress, SourceLocationType type)
    {
        var tags = new List<string>();

        if (progress < 0.3)
            tags.Add("beginning");
        else if (progress > 0.7)
            tags.Add("endgame");

        if (type == SourceLocationType.Structure)
            tags.Add("combat");
        else if (type == SourceLocationType.QuestSignpost)
            tags.Add("quest");
        else
            tags.Add("exploration");

        return string.Join(",", tags);
    }

    private List<NarrativeConnection> GenerateNarrativeConnections(SourceLocation location, List<SourceLocation> threadLocations, int currentIndex)
    {
        var connections = new List<NarrativeConnection>();

        // Connect to previous location (caused-by)
        if (currentIndex > 0)
        {
            var prevRefName = _refNameGenerator.GetRefName(threadLocations[currentIndex - 1]);
            connections.Add(new NarrativeConnection
            {
                TargetRef = prevRefName,
                Relationship = "caused-by",
                Description = "Previous location in quest chain"
            });
        }

        // Connect to next location (leads-to)
        if (currentIndex < threadLocations.Count - 1)
        {
            var nextRefName = _refNameGenerator.GetRefName(threadLocations[currentIndex + 1]);
            connections.Add(new NarrativeConnection
            {
                TargetRef = nextRefName,
                Relationship = "leads-to",
                Description = "Next location in quest chain"
            });
        }

        // Foreshadow climax from rising action
        if (currentIndex < threadLocations.Count - 2 && location.Type != SourceLocationType.Structure)
        {
            var climaxLocation = threadLocations.Skip(currentIndex + 1).FirstOrDefault(l => l.Type == SourceLocationType.Structure);
            if (climaxLocation != null)
            {
                var climaxRefName = _refNameGenerator.GetRefName(climaxLocation);
                connections.Add(new NarrativeConnection
                {
                    TargetRef = climaxRefName,
                    Relationship = "foreshadows",
                    Description = "Hints at upcoming challenge"
                });
            }
        }

        return connections;
    }
}

using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator.NarrativeServices;

/// <summary>
/// Generates token chains for quest progression.
/// Locations in sequence require tokens from previous locations.
/// </summary>
public class TokenChainGenerator
{
    private readonly RefNameGenerator _refNameGenerator;

    public TokenChainGenerator(RefNameGenerator refNameGenerator)
    {
        _refNameGenerator = refNameGenerator;
    }

    public List<TokenChainLink> GenerateTokenChains(List<StoryThread> threads)
    {
        var chains = new List<TokenChainLink>();

        foreach (var thread in threads)
        {
            for (var i = 0; i < thread.Locations.Count; i++)
            {
                var location = thread.Locations[i];
                var refName = _refNameGenerator.GetRefName(location);
                var tokenAwarded = $"TOKEN_{refName}_COMPLETE";

                var link = new TokenChainLink
                {
                    Location = location,
                    TokenAwarded = tokenAwarded,
                    SequenceNumber = i,
                    StoryThreadRef = thread.RefName,
                    TokensRequired = new List<string>()
                };

                // First location in thread has no requirements
                if (i > 0)
                {
                    var prevLocation = thread.Locations[i - 1];
                    var prevRefName = _refNameGenerator.GetRefName(prevLocation);
                    link.TokensRequired.Add($"TOKEN_{prevRefName}_COMPLETE");
                }

                // Branch threads require the branch point token from main thread
                if (thread.Type == StoryThreadType.Branch && i == 0)
                {
                    // Find the branch point on main thread (we'll add this logic later if needed)
                    // For now, branches start unlocked
                }

                chains.Add(link);
            }
        }

        return chains;
    }
}

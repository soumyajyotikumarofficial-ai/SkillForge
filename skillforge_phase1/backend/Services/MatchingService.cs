using SkillForge.API.Models;

namespace SkillForge.API.Services;

public class MatchingService
{
    public IEnumerable<Match> GetMatches() => new[] { new Match { Id = 1, CandidateId = 1, Score = 0.9 } };
}

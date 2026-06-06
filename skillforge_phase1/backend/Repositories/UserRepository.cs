using SkillForge.API.Models;
using System.Collections.Concurrent;

namespace SkillForge.API.Repositories;

public class UserRepository
{
    private readonly ConcurrentDictionary<int, User> _store = new();
    private int _next = 1;
    public IEnumerable<User> GetAll() => _store.Values;
    public User Add(User u) { u.Id = _next++; _store[u.Id]=u; return u; }
}

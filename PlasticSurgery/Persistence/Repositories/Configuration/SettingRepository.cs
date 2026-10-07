using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Configuration;

namespace PlasticSurgery.Persistence.Repositories.Configuration;

public class SettingRepository : ISettingRepository
{
    private readonly ApplicationDbContext _db;

    public SettingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ConfigSetting>> ListReadOnlyAsync(CancellationToken ct = default) =>
        await _db.ConfigSettings.AsNoTracking().OrderBy(s => s.Section).ThenBy(s => s.Key).ToListAsync(ct);

    public Task<ConfigSetting?> GetForUpdateAsync(string section, string key, CancellationToken ct = default) =>
        _db.ConfigSettings.FirstOrDefaultAsync(s => s.Section == section && s.Key == key, ct);

    public void Add(ConfigSetting setting) => _db.ConfigSettings.Add(setting);

    public void Remove(ConfigSetting setting) => _db.ConfigSettings.Remove(setting);
}

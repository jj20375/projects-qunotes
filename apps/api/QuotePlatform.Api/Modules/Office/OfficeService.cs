using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>部門辦公室列表的一列。</summary>
/// <param name="DepartmentId">部門識別碼，同時是辦公室房間識別碼。</param>
/// <param name="ParentId">上層部門識別碼；頂層部門為 null。</param>
/// <param name="Name">部門名稱。</param>
/// <param name="SortOrder">部門排序值。</param>
/// <param name="MemberCount">部門的啟用成員數（即座位數）。</param>
/// <param name="OnlineCount">歸屬此部門且目前上線或閒置的成員數。</param>
/// <param name="OccupantCount">目前實際在此辦公室內的人數（含來訪者）。</param>
public sealed record OfficeRoomSummary(Guid DepartmentId, Guid? ParentId, string Name, int SortOrder, int MemberCount, int OnlineCount, int OccupantCount);

/// <summary>已載入的辦公室（快取 30 秒）。</summary>
/// <param name="DepartmentId">部門識別碼。</param>
/// <param name="Name">部門名稱。</param>
/// <param name="Map">依部門成員產生的地圖。</param>
public sealed record LoadedOffice(Guid DepartmentId, string Name, OfficeMap Map);

/// <summary>設定部門辦公室主題。</summary>
/// <param name="Theme">主題代碼，必須是 <see cref="OfficeRules.Themes"/> 其中之一。</param>
public sealed record UpdateRoomThemeRequest(string? Theme);

/// <summary>虛擬辦公室的資料庫存取；走動、聊天等即時資料不經過這裡。</summary>
public sealed class OfficeService(QuoteDbContext db, CompanyContext companyContext, IMemoryCache cache, PresenceTracker tracker)
{
    private static readonly TimeSpan MapCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>hub 與背景巡檢沒有可靠的 HttpContext，由呼叫端以 JWT（或連線身分）的公司明確指定範圍。</summary>
    /// <param name="companyId">後續查詢與寫入所屬的公司。</param>
    public void UseCompany(Guid companyId) => companyContext.UseCompany(companyId);

    /// <summary>公司內所有啟用中的部門；部門樹是完整載入的小型資料，不分頁。</summary>
    /// <param name="companyId">目前公司，用於取得該公司的上線狀態快照。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>依排序值與名稱排列的辦公室摘要。</returns>
    public async Task<IReadOnlyList<OfficeRoomSummary>> RoomsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var departments = await db.Departments.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder).ThenBy(value => value.Name)
            .Select(value => new { value.Id, value.ParentId, value.Name, value.SortOrder })
            .ToListAsync(cancellationToken);
        var memberCounts = await ActiveMembers()
            .Where(value => value.DepartmentId != null)
            .GroupBy(value => value.DepartmentId!.Value)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.Key, value => value.Count, cancellationToken);
        var presence = tracker.Snapshot(companyId);
        return departments
            .Select(value => new OfficeRoomSummary(
                value.Id, value.ParentId, value.Name, value.SortOrder,
                memberCounts.GetValueOrDefault(value.Id),
                presence.Count(entry => entry.HomeDepartmentId == value.Id),
                presence.Count(entry => entry.OfficeDepartmentId == value.Id)))
            .ToList();
    }

    /// <summary>載入部門辦公室；部門不存在、停用或不屬於目前公司時回傳 null。成員異動最多 30 秒後反映在地圖上。</summary>
    /// <param name="companyId">目前公司，用於組成快取鍵。</param>
    /// <param name="departmentId">要載入的部門。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>辦公室與地圖；找不到時為 null。</returns>
    public async Task<LoadedOffice?> LoadAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken)
    {
        var key = MapCacheKey(companyId, departmentId);
        if (cache.TryGetValue(key, out LoadedOffice? cached)) return cached;
        var department = await db.Departments.AsNoTracking()
            .Where(value => value.Id == departmentId && value.IsActive)
            .Select(value => new { value.Id, value.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (department is null) return null;
        var theme = await db.OfficeRooms.AsNoTracking().Where(value => value.DepartmentId == departmentId).Select(value => value.Theme).SingleOrDefaultAsync(cancellationToken) ?? "wood";
        var owners = await ActiveMembers()
            .Where(value => value.DepartmentId == departmentId)
            .OrderBy(value => value.CreatedAt).ThenBy(value => value.Id)
            .Select(value => new OfficeSeatOwner(
                value.Id,
                value.User.DisplayName,
                db.OfficeProfiles.Where(profile => profile.MembershipId == value.Id).Select(profile => profile.LastSeenAt).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        var loaded = new LoadedOffice(department.Id, department.Name, OfficeMapGenerator.Generate(owners, theme));
        cache.Set(key, loaded, MapCacheDuration);
        return loaded;
    }

    /// <summary>連線時讀取姓名與個人設定；沒有設定時用預設角色與 available。</summary>
    /// <param name="membershipId">成員資格識別碼。</param>
    /// <param name="userId">使用者識別碼，用於決定預設角色。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>個人設定與顯示名稱。</returns>
    public async Task<(MemberProfile Profile, string Name)> ProfileAsync(Guid membershipId, Guid userId, CancellationToken cancellationToken)
    {
        var name = await db.CompanyMemberships.AsNoTracking().Where(value => value.Id == membershipId).Select(value => value.User.DisplayName).SingleAsync(cancellationToken);
        var profile = await db.OfficeProfiles.AsNoTracking().SingleOrDefaultAsync(value => value.MembershipId == membershipId, cancellationToken);
        return (new MemberProfile(profile?.AvatarKey ?? OfficeRules.DefaultAvatar(userId), profile?.Status ?? "available", profile?.StatusText), name);
    }

    /// <summary>儲存成員的狀態與狀態文字；沒有個人設定時自動建立。</summary>
    /// <param name="membershipId">成員資格識別碼。</param>
    /// <param name="status">狀態代碼。</param>
    /// <param name="statusText">狀態文字；可為 null。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    public async Task SaveStatusAsync(Guid membershipId, string status, string? statusText, CancellationToken cancellationToken)
    {
        var profile = await EditableProfileAsync(membershipId, cancellationToken);
        profile.Status = status;
        profile.StatusText = statusText;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>儲存成員選擇的角色造型；沒有個人設定時自動建立。</summary>
    /// <param name="membershipId">成員資格識別碼。</param>
    /// <param name="avatarKey">角色造型代碼。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    public async Task SaveAvatarAsync(Guid membershipId, string avatarKey, CancellationToken cancellationToken)
    {
        var profile = await EditableProfileAsync(membershipId, cancellationToken);
        profile.AvatarKey = avatarKey;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>同一家公司一批轉為離線的成員；呼叫前必須 <see cref="UseCompany"/>。</summary>
    /// <param name="items">成員資格識別碼與其最後上線時間。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    public async Task WriteLastSeenAsync(IReadOnlyList<(Guid MembershipId, DateTimeOffset At)> items, CancellationToken cancellationToken)
    {
        foreach (var (membershipId, at) in items)
        {
            var profile = await EditableProfileAsync(membershipId, cancellationToken);
            profile.LastSeenAt = at;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>設定部門辦公室主題，並清除該辦公室的地圖快取。</summary>
    /// <param name="companyId">目前公司，用於清除快取。</param>
    /// <param name="departmentId">要設定的部門。</param>
    /// <param name="theme">主題代碼，必須在 <see cref="OfficeRules.Themes"/> 內。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>成功為 <see cref="Empty"/>；主題不正確回 400，部門不存在回 404。</returns>
    public async Task<ServiceResult<Empty>> UpdateThemeAsync(Guid companyId, Guid departmentId, string? theme, CancellationToken cancellationToken)
    {
        if (theme is null || !OfficeRules.Themes.Contains(theme)) return ServiceResult<Empty>.BadRequest("主題不正確。");
        if (!await db.Departments.AnyAsync(value => value.Id == departmentId, cancellationToken)) return ServiceResult<Empty>.NotFound("找不到部門。");
        var room = await db.OfficeRooms.SingleOrDefaultAsync(value => value.DepartmentId == departmentId, cancellationToken);
        if (room is null)
        {
            room = new OfficeRoom { DepartmentId = departmentId };
            db.OfficeRooms.Add(room);
        }
        room.Theme = theme;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(MapCacheKey(companyId, departmentId));
        return ServiceResult<Empty>.Ok(Empty.Value);
    }

    private async Task<OfficeProfile> EditableProfileAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        // 同一批寫入可能重複出現同一成員，先查追蹤中的實體避免重複 Add。
        var profile = db.OfficeProfiles.Local.FirstOrDefault(value => value.MembershipId == membershipId)
            ?? await db.OfficeProfiles.SingleOrDefaultAsync(value => value.MembershipId == membershipId, cancellationToken);
        if (profile is not null) return profile;
        profile = new OfficeProfile { MembershipId = membershipId };
        db.OfficeProfiles.Add(profile);
        return profile;
    }

    private IQueryable<CompanyMembership> ActiveMembers() =>
        db.CompanyMemberships.AsNoTracking().Where(value => value.IsActive && value.User.Status == "active");

    private static string MapCacheKey(Guid companyId, Guid departmentId) => $"office-map:{companyId}:{departmentId}";
}

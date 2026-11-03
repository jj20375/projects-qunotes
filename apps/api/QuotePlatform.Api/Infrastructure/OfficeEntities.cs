namespace QuotePlatform.Api.Infrastructure;

/// <summary>部門虛擬辦公室的房間設定；管理員第一次設定主題時建立，沒有資料列時主題視為 wood。</summary>
public sealed class OfficeRoom : ICompanyOwned
{
    /// <summary>房間設定唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬部門識別碼；一個部門一間辦公室。</summary>
    public Guid DepartmentId { get; set; }

    /// <summary>地圖主題：wood（木地板）／carpet（地毯）／dark（深色）。</summary>
    public string Theme { get; set; } = "wood";

    /// <summary>建立時間戳記（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>成員在虛擬辦公室的個人設定；第一次修改狀態、角色或轉為離線時建立。</summary>
public sealed class OfficeProfile : ICompanyOwned
{
    /// <summary>個人設定唯一識別碼。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所屬公司識別碼。</summary>
    public Guid CompanyId { get; set; }

    /// <summary>所屬成員身分識別碼。</summary>
    public Guid MembershipId { get; set; }

    /// <summary>角色外觀鍵；null 表示依 userId 雜湊選出的預設角色。</summary>
    public string? AvatarKey { get; set; }

    /// <summary>手動狀態：available／busy／meeting／break／away。</summary>
    public string Status { get; set; } = "available";

    /// <summary>自訂狀態文字，最多 40 字。</summary>
    public string? StatusText { get; set; }

    /// <summary>最後在線時間（轉為離線時寫入，UTC）。</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>最後更新時間戳記（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

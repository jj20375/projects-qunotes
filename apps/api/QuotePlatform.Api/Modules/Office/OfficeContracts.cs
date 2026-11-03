namespace QuotePlatform.Api.Modules.Office;

/// <summary>進入辦公室時回傳的快照。</summary>
/// <param name="Map">辦公室地圖（牆、桌椅、座位）。</param>
/// <param name="DepartmentName">辦公室所屬部門名稱。</param>
/// <param name="Me">自己在辦公室內的位置與外觀。</param>
/// <param name="Occupants">目前在辦公室內的所有人，含自己。</param>
public sealed record OfficeSnapshot(OfficeMap Map, string DepartmentName, OfficeOccupant Me, IReadOnlyList<OfficeOccupant> Occupants);

/// <summary>靠近聊天的一則訊息；<see cref="Recipients"/> 由後端計算，不含發送者。</summary>
/// <param name="FromMembershipId">發話者的 membership。</param>
/// <param name="FromName">發話者顯示名稱。</param>
/// <param name="Text">訊息內容（已去除前後空白）。</param>
/// <param name="Recipients">依位置算出的收訊對象 membership，不含發送者。</param>
/// <param name="SentAt">伺服器端的發送時間。</param>
public sealed record OfficeChatMessage(Guid FromMembershipId, string FromName, string Text, IReadOnlyList<Guid> Recipients, DateTimeOffset SentAt);

/// <summary>敲門通知。</summary>
/// <param name="FromMembershipId">敲門者的 membership。</param>
/// <param name="FromName">敲門者顯示名稱。</param>
/// <param name="DepartmentId">敲門者目前所在辦公室的部門。</param>
/// <param name="DepartmentName">敲門者所在辦公室的部門名稱。</param>
/// <param name="Message">敲門附言；未填為 null。</param>
public sealed record OfficeKnock(Guid FromMembershipId, string FromName, Guid DepartmentId, string DepartmentName, string? Message);

/// <summary>伺服器推給瀏覽器的事件；方法名稱即前端 <c>hub.on</c> 的事件名稱。</summary>
public interface IOfficeClient
{
    /// <summary>某位成員的上線或手動狀態改變（公司群組廣播）。</summary>
    /// <param name="entry">最新的上線狀態。</param>
    /// <param name="lastSeenAt">轉為離線時的最後在線時間；其他情況為 null。</param>
    Task PresenceChanged(PresenceEntry entry, DateTimeOffset? lastSeenAt);

    /// <summary>有人進入辦公室（辦公室群組廣播，不含本人）。</summary>
    /// <param name="occupant">新進入者的位置與外觀。</param>
    Task OccupantJoined(OfficeOccupant occupant);

    /// <summary>有人離開辦公室、離線或被新分頁取代。</summary>
    /// <param name="membershipId">離開者的 membership。</param>
    Task OccupantLeft(Guid membershipId);

    /// <summary>有人移動到新位置。</summary>
    /// <param name="membershipId">移動者的 membership。</param>
    /// <param name="x">新的 X 格座標。</param>
    /// <param name="y">新的 Y 格座標。</param>
    /// <param name="facing">面向（up／down／left／right）。</param>
    Task Moved(Guid membershipId, int x, int y, string facing);

    /// <summary>自己的移動不合法時，只回給本人的校正位置。</summary>
    /// <param name="x">伺服器認定的 X 格座標。</param>
    /// <param name="y">伺服器認定的 Y 格座標。</param>
    /// <param name="facing">伺服器認定的面向。</param>
    Task PositionCorrected(int x, int y, string facing);

    /// <summary>有人送出表情。</summary>
    /// <param name="membershipId">送出表情者的 membership。</param>
    /// <param name="key">表情代碼。</param>
    Task Emoted(Guid membershipId, string key);

    /// <summary>有人更換角色外觀。</summary>
    /// <param name="membershipId">更換者的 membership。</param>
    /// <param name="avatarKey">新的角色代碼。</param>
    Task AvatarChanged(Guid membershipId, string avatarKey);

    /// <summary>收到附近同事的聊天訊息（發送者自己也會收到）。</summary>
    /// <param name="message">聊天訊息與收訊對象。</param>
    Task ChatReceived(OfficeChatMessage message);

    /// <summary>有人敲門（送到被敲者的所有分頁）。</summary>
    /// <param name="knock">敲門資訊。</param>
    Task Knocked(OfficeKnock knock);

    /// <summary>敲門的回應（送到敲門者的所有分頁）。</summary>
    /// <param name="membershipId">回應者的 membership。</param>
    /// <param name="name">回應者顯示名稱。</param>
    /// <param name="accepted">是否接受。</param>
    /// <param name="message">回覆附言；未填為 null。</param>
    Task KnockAnswered(Guid membershipId, string name, bool accepted, string? message);

    /// <summary>同一人在別的分頁進入了辦公室，這個分頁的辦公室席位被取代。</summary>
    Task OfficeSessionReplaced();

    /// <summary>連線即將被伺服器斷開（例如權限異動或帳號停用）。</summary>
    /// <param name="reason">給使用者看的繁體中文原因。</param>
    Task ForceDisconnect(string reason);
}

/// <summary>SignalR 群組名稱；公司群組收上線狀態，辦公室群組收走動與表情。</summary>
public static class OfficeGroups
{
    /// <summary>公司群組：同公司的所有連線，接收上線狀態。</summary>
    /// <param name="companyId">公司識別碼。</param>
    /// <returns>群組名稱。</returns>
    public static string Company(Guid companyId) => $"company:{companyId}";

    /// <summary>辦公室群組：同一間部門辦公室內的連線，接收走動、表情與頭像變更。</summary>
    /// <param name="companyId">公司識別碼。</param>
    /// <param name="departmentId">部門識別碼。</param>
    /// <returns>群組名稱。</returns>
    public static string Office(Guid companyId, Guid departmentId) => $"office:{companyId}:{departmentId}";
}

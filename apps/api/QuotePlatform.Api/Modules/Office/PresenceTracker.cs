namespace QuotePlatform.Api.Modules.Office;

/// <summary>hub 連線時從 JWT 與資料庫取得的身分；AuthVersion 供背景巡檢比對。</summary>
/// <param name="CompanyId">所屬公司。</param>
/// <param name="MembershipId">公司成員代碼；上線狀態以此為單位。</param>
/// <param name="UserId">使用者代碼。</param>
/// <param name="DepartmentId">所屬部門；沒有部門時為 null。</param>
/// <param name="Name">顯示名稱。</param>
/// <param name="AuthVersion">連線當下的 <c>users.AuthVersion</c>。</param>
public sealed record ConnectionIdentity(Guid CompanyId, Guid MembershipId, Guid UserId, Guid? DepartmentId, string Name, int AuthVersion);

/// <summary>會跟著人走的個人設定（角色外觀與手動狀態）。</summary>
/// <param name="AvatarKey">角色外觀代碼。</param>
/// <param name="Status">手動狀態代碼。</param>
/// <param name="StatusText">狀態補充文字。</param>
public sealed record MemberProfile(string AvatarKey, string Status, string? StatusText);

/// <summary>給全公司看的上線狀態。</summary>
/// <param name="MembershipId">公司成員代碼。</param>
/// <param name="Presence">連線狀態：<c>online</c>、<c>idle</c> 或 <c>offline</c>。</param>
/// <param name="Status">手動狀態代碼。</param>
/// <param name="StatusText">狀態補充文字。</param>
/// <param name="HomeDepartmentId">所屬部門。</param>
/// <param name="OfficeDepartmentId">目前所在的辦公室部門；不在辦公室時為 null。</param>
public sealed record PresenceEntry(Guid MembershipId, string Presence, string Status, string? StatusText, Guid? HomeDepartmentId, Guid? OfficeDepartmentId);

/// <summary>辦公室裡的一個角色。</summary>
/// <param name="MembershipId">公司成員代碼。</param>
/// <param name="Name">顯示名稱。</param>
/// <param name="AvatarKey">角色外觀代碼。</param>
/// <param name="X">格子 X 座標。</param>
/// <param name="Y">格子 Y 座標。</param>
/// <param name="Facing">面向（up、down、left、right）。</param>
/// <param name="Status">手動狀態代碼。</param>
/// <param name="StatusText">狀態補充文字。</param>
/// <param name="HomeDepartmentId">所屬部門；與所在辦公室不同時代表訪客。</param>
public sealed record OfficeOccupant(Guid MembershipId, string Name, string AvatarKey, int X, int Y, string Facing, string Status, string? StatusText, Guid? HomeDepartmentId);

/// <summary>進入辦公室的結果；<see cref="ReplacedConnectionId"/> 為被取代的舊分頁。</summary>
/// <param name="Me">自己在辦公室中的角色。</param>
/// <param name="PreviousDepartmentId">進入前所在的辦公室；原本不在任何辦公室時為 null。</param>
/// <param name="ReplacedConnectionId">被取代的舊辦公室分頁連線；沒有時為 null。</param>
/// <param name="AlreadyPresent">是否本來就在這間辦公室（寬限期內重連或另一個分頁）。</param>
/// <param name="Occupants">辦公室內所有人（含自己）。</param>
public sealed record OfficeJoin(OfficeOccupant Me, Guid? PreviousDepartmentId, string? ReplacedConnectionId, bool AlreadyPresent, IReadOnlyList<OfficeOccupant> Occupants);

/// <summary>移動的結果；不合法時 <see cref="Occupant"/> 為原位置，供 PositionCorrected 使用。</summary>
/// <param name="Accepted">移動是否被接受。</param>
/// <param name="DepartmentId">所在辦公室的部門。</param>
/// <param name="Occupant">移動後（或被拒絕時的原）角色狀態。</param>
public sealed record OfficeMove(bool Accepted, Guid DepartmentId, OfficeOccupant Occupant);

/// <summary>某人所在辦公室與其中所有人的位置（聊天、敲門用）。</summary>
/// <param name="DepartmentId">辦公室所屬部門。</param>
/// <param name="Map">辦公室地圖。</param>
/// <param name="Positions">辦公室內每個成員的位置，以 membership 為鍵。</param>
public sealed record OfficePresence(Guid DepartmentId, OfficeMap Map, IReadOnlyDictionary<Guid, OfficePoint> Positions);

/// <summary>需要廣播的狀態變化。</summary>
/// <param name="CompanyId">事件所屬公司，廣播範圍以此決定。</param>
public abstract record PresenceEvent(Guid CompanyId);

/// <summary>上線、閒置、離線或手動狀態變化；離線時帶最後在線時間。</summary>
/// <param name="CompanyId">事件所屬公司。</param>
/// <param name="Entry">變化後的上線狀態。</param>
/// <param name="LastSeenAt">離線時的最後在線時間；其他狀態為 null。</param>
public sealed record PresenceChangedEvent(Guid CompanyId, PresenceEntry Entry, DateTimeOffset? LastSeenAt) : PresenceEvent(CompanyId);

/// <summary>有人離開辦公室（寬限期過後才發出）。</summary>
/// <param name="CompanyId">事件所屬公司。</param>
/// <param name="DepartmentId">離開的辦公室所屬部門。</param>
/// <param name="MembershipId">離開的成員。</param>
public sealed record OccupantLeftEvent(Guid CompanyId, Guid DepartmentId, Guid MembershipId) : PresenceEvent(CompanyId);

/// <summary>
/// 記憶體中的上線狀態與辦公室位置；以 membership 為單位，多個分頁算同一人。
/// </summary>
/// <remarks>
/// 單一 API 實例時有效；改為多實例時需換成 SignalR Redis backplane 與共用儲存。
/// 斷線後有 <see cref="Grace"/> 寬限期，重新整理頁面不會讓狀態或辦公室位置閃爍。
/// 全部操作以同一把鎖保護；人數在數百以內，O(n) 掃描足夠。
/// </remarks>
/// <param name="clock">時間來源；測試時換成 <see cref="ManualClock"/>。</param>
public sealed class PresenceTracker(TimeProvider clock)
{
    /// <summary>沒有操作多久後轉為閒置。</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>斷線後保留狀態與辦公室位置的寬限期。</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    /// <summary>敲門紀錄的有效期限。</summary>
    public static readonly TimeSpan KnockExpiry = TimeSpan.FromMinutes(5);

    private sealed class Member(ConnectionIdentity identity, MemberProfile profile, DateTimeOffset now)
    {
        public ConnectionIdentity Identity { get; set; } = identity;
        public MemberProfile Profile { get; set; } = profile;
        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);
        public DateTimeOffset LastActivity { get; set; } = now;
        public DateTimeOffset? DisconnectedAt { get; set; }
        public bool Idle { get; set; }
        public Guid? OfficeDepartmentId { get; set; }
        public OfficeMap? Map { get; set; }
        public string? OfficeConnectionId { get; set; }
        public DateTimeOffset? OfficeLeftAt { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Facing { get; set; } = "down";
    }

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Member> _members = [];
    private readonly Dictionary<string, Guid> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid From, Guid To), DateTimeOffset> _knocks = [];

    /// <summary>新連線；第一次上線或從閒置恢復時回傳要廣播的事件。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <param name="identity">連線身分。</param>
    /// <param name="profile">個人設定（僅在成員第一次出現時採用）。</param>
    /// <returns>需要廣播的事件；多開分頁或寬限期內重連時為 null。</returns>
    public PresenceChangedEvent? Connect(string connectionId, ConnectionIdentity identity, MemberProfile profile)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var isNew = !_members.TryGetValue(identity.MembershipId, out var member);
            if (member is null)
            {
                member = new Member(identity, profile, now);
                _members[identity.MembershipId] = member;
            }
            var wasIdle = member.Idle;
            member.Identity = identity;
            member.Connections.Add(connectionId);
            member.DisconnectedAt = null;
            member.LastActivity = now;
            member.Idle = false;
            _connections[connectionId] = identity.MembershipId;
            return isNew || wasIdle ? Changed(member) : null;
        }
    }

    /// <summary>斷線只記下時間；離線與離開辦公室由 <see cref="Sweep"/> 在寬限期後處理。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    public void Disconnect(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var membershipId) || !_members.TryGetValue(membershipId, out var member)) return;
            var now = clock.GetUtcNow();
            member.Connections.Remove(connectionId);
            if (member.OfficeConnectionId == connectionId)
            {
                member.OfficeConnectionId = null;
                member.OfficeLeftAt = now;
            }
            if (member.Connections.Count == 0) member.DisconnectedAt = now;
        }
    }

    /// <summary>前端回報有操作；從閒置恢復時回傳事件。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <returns>從閒置恢復上線時的事件，否則為 null。</returns>
    public PresenceChangedEvent? Touch(string connectionId)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member) return null;
            member.LastActivity = clock.GetUtcNow();
            if (!member.Idle) return null;
            member.Idle = false;
            return Changed(member);
        }
    }

    /// <summary>背景巡檢：寬限期過後離開辦公室、轉離線，以及 5 分鐘沒操作轉閒置。</summary>
    /// <returns>需要廣播的事件清單。</returns>
    public IReadOnlyList<PresenceEvent> Sweep()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            var events = new List<PresenceEvent>();
            foreach (var member in _members.Values.ToList())
            {
                if (member.OfficeDepartmentId is Guid departmentId && member.OfficeConnectionId is null && now - member.OfficeLeftAt >= Grace)
                {
                    events.Add(new OccupantLeftEvent(member.Identity.CompanyId, departmentId, member.Identity.MembershipId));
                    ClearOffice(member);
                }
                if (member.Connections.Count == 0 && now - member.DisconnectedAt >= Grace)
                {
                    _members.Remove(member.Identity.MembershipId);
                    events.Add(new PresenceChangedEvent(member.Identity.CompanyId, Entry(member) with { Presence = "offline", OfficeDepartmentId = null }, member.DisconnectedAt));
                }
                else if (member.Connections.Count > 0 && !member.Idle && now - member.LastActivity >= IdleAfter)
                {
                    member.Idle = true;
                    events.Add(Changed(member));
                }
            }
            foreach (var key in _knocks.Where(pair => now - pair.Value >= KnockExpiry).Select(pair => pair.Key).ToList()) _knocks.Remove(key);
            return events;
        }
    }

    /// <summary>取得連線的身分。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <returns>連線身分；連線不存在時為 null。</returns>
    public ConnectionIdentity? Identity(string connectionId)
    {
        lock (_gate) return Find(connectionId)?.Identity;
    }

    /// <summary>目前至少有一條連線的成員；寬限期中的人不算在線（不能被敲門）。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <returns>成員身分；不在線時為 null。</returns>
    public ConnectionIdentity? OnlineMember(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) && member.Connections.Count > 0 ? member.Identity : null;
    }

    /// <summary>取得成員目前所有連線。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <returns>連線代碼清單；不在線時為空。</returns>
    public IReadOnlyList<string> ConnectionsOf(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) ? member.Connections.ToList() : [];
    }

    /// <summary>正在顯示辦公室的那個分頁；聊天只送到這個分頁。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <returns>辦公室分頁的連線代碼；不在辦公室時為 null。</returns>
    public string? OfficeConnectionOf(Guid membershipId)
    {
        lock (_gate) return _members.TryGetValue(membershipId, out var member) ? member.OfficeConnectionId : null;
    }

    /// <summary>取得公司內目前在線（含閒置）的成員狀態。</summary>
    /// <param name="companyId">公司代碼。</param>
    /// <returns>上線狀態清單；離線者不在其中。</returns>
    public IReadOnlyList<PresenceEntry> Snapshot(Guid companyId)
    {
        lock (_gate) return _members.Values.Where(member => member.Identity.CompanyId == companyId).Select(Entry).ToList();
    }

    /// <summary>所有連線與其身分，供 AuthVersion 巡檢。</summary>
    /// <returns>以連線代碼為鍵的身分對照。</returns>
    public IReadOnlyDictionary<string, ConnectionIdentity> AllConnections()
    {
        lock (_gate) return _connections.ToDictionary(pair => pair.Key, pair => _members[pair.Value].Identity, StringComparer.Ordinal);
    }

    /// <summary>
    /// 進入部門辦公室；同一人已在這間（寬限期或另一個分頁）時保留位置，否則從入口旁的空格出現。
    /// 由目前這個分頁接手辦公室畫面，舊分頁回傳於 <see cref="OfficeJoin.ReplacedConnectionId"/>。
    /// </summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <param name="departmentId">要進入的辦公室所屬部門。</param>
    /// <param name="map">該辦公室的地圖。</param>
    /// <returns>進入結果；連線不存在時為 null。</returns>
    public OfficeJoin? JoinOffice(string connectionId, Guid departmentId, OfficeMap map)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member) return null;
            var previous = member.OfficeDepartmentId;
            var replaced = member.OfficeConnectionId is string old && old != connectionId ? old : null;
            var alreadyPresent = previous == departmentId;
            if (!alreadyPresent || !map.IsWalkable(member.X, member.Y))
            {
                var spot = OfficeRules.NearestFree(map, map.Spawn, Occupied(member, departmentId));
                (member.X, member.Y, member.Facing) = (spot.X, spot.Y, "down");
            }
            member.OfficeDepartmentId = departmentId;
            member.Map = map;
            member.OfficeConnectionId = connectionId;
            member.OfficeLeftAt = null;
            var occupants = InOffice(member.Identity.CompanyId, departmentId).Select(Occupant).ToList();
            return new OfficeJoin(Occupant(member), previous, replaced, alreadyPresent, occupants);
        }
    }

    /// <summary>主動離開（換頁）；只有目前的辦公室分頁可以離開。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <returns>離場事件；不是目前辦公室分頁時為 null。</returns>
    public OccupantLeftEvent? LeaveOffice(string connectionId)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member || member.OfficeConnectionId != connectionId || member.OfficeDepartmentId is not Guid departmentId) return null;
            ClearOffice(member);
            return new OccupantLeftEvent(member.Identity.CompanyId, departmentId, member.Identity.MembershipId);
        }
    }

    /// <summary>走一格或原地轉向；不是目前的辦公室分頁時回傳 null。</summary>
    /// <param name="connectionId">SignalR 連線代碼。</param>
    /// <param name="x">目標格 X。</param>
    /// <param name="y">目標格 Y。</param>
    /// <param name="facing">移動後的面向。</param>
    /// <returns>移動結果；被拒絕時 <see cref="OfficeMove.Occupant"/> 為原位置。</returns>
    public OfficeMove? TryMove(string connectionId, int x, int y, string facing)
    {
        lock (_gate)
        {
            if (Find(connectionId) is not { } member || member.OfficeConnectionId != connectionId || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var accepted = (x == member.X && y == member.Y) || OfficeRules.IsValidStep(map, new OfficePoint(member.X, member.Y), new OfficePoint(x, y));
            if (accepted) (member.X, member.Y, member.Facing) = (x, y, facing);
            return new OfficeMove(accepted, departmentId, Occupant(member));
        }
    }

    /// <summary>瞬移到目標附近的空格（回座位、接受敲門）。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <param name="target">希望到達的格子。</param>
    /// <param name="facing">到達後的面向。</param>
    /// <returns>放置後的角色；成員不在辦公室時為 null。</returns>
    public OfficeOccupant? Place(Guid membershipId, OfficePoint target, string facing)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member) || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var spot = OfficeRules.NearestFree(map, target, Occupied(member, departmentId));
            (member.X, member.Y, member.Facing) = (spot.X, spot.Y, facing);
            return Occupant(member);
        }
    }

    /// <summary>取得成員所在辦公室與其中所有人的位置。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <returns>辦公室位置資訊；不在辦公室時為 null。</returns>
    public OfficePresence? OfficeOf(Guid membershipId)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member) || member is not { OfficeDepartmentId: Guid departmentId, Map: { } map }) return null;
            var positions = InOffice(member.Identity.CompanyId, departmentId).ToDictionary(value => value.Identity.MembershipId, value => new OfficePoint(value.X, value.Y));
            return new OfficePresence(departmentId, map, positions);
        }
    }

    /// <summary>更新手動狀態。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <param name="status">新的狀態代碼。</param>
    /// <param name="statusText">狀態補充文字。</param>
    /// <returns>要廣播的事件；成員不在線時為 null。</returns>
    public PresenceChangedEvent? SetStatus(Guid membershipId, string status, string? statusText)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member)) return null;
            member.Profile = member.Profile with { Status = status, StatusText = statusText };
            return Changed(member);
        }
    }

    /// <summary>更新角色外觀。</summary>
    /// <param name="membershipId">公司成員代碼。</param>
    /// <param name="avatarKey">新的角色外觀代碼。</param>
    /// <returns>是否更新成功；成員不在線時為 false。</returns>
    public bool SetAvatar(Guid membershipId, string avatarKey)
    {
        lock (_gate)
        {
            if (!_members.TryGetValue(membershipId, out var member)) return false;
            member.Profile = member.Profile with { AvatarKey = avatarKey };
            return true;
        }
    }

    /// <summary>記錄一次敲門。</summary>
    /// <param name="from">敲門的人。</param>
    /// <param name="to">被敲門的人。</param>
    public void RecordKnock(Guid from, Guid to)
    {
        lock (_gate) _knocks[(from, to)] = clock.GetUtcNow();
    }

    /// <summary>回應敲門前必須有 <see cref="KnockExpiry"/> 內的敲門紀錄；每次敲門只能回應一次。</summary>
    /// <param name="from">敲門的人。</param>
    /// <param name="to">被敲門的人。</param>
    /// <returns>是否有有效的敲門紀錄。</returns>
    public bool ConsumeKnock(Guid from, Guid to)
    {
        lock (_gate) return _knocks.Remove((from, to), out var at) && clock.GetUtcNow() - at < KnockExpiry;
    }

    private Member? Find(string connectionId) =>
        _connections.TryGetValue(connectionId, out var membershipId) ? _members.GetValueOrDefault(membershipId) : null;

    private IEnumerable<Member> InOffice(Guid companyId, Guid departmentId) =>
        _members.Values.Where(member => member.Identity.CompanyId == companyId && member.OfficeDepartmentId == departmentId);

    private HashSet<OfficePoint> Occupied(Member self, Guid departmentId) =>
        InOffice(self.Identity.CompanyId, departmentId).Where(member => member != self).Select(member => new OfficePoint(member.X, member.Y)).ToHashSet();

    private static void ClearOffice(Member member)
    {
        member.OfficeDepartmentId = null;
        member.Map = null;
        member.OfficeConnectionId = null;
        member.OfficeLeftAt = null;
    }

    private static PresenceEntry Entry(Member member) =>
        new(member.Identity.MembershipId, member.Idle ? "idle" : "online", member.Profile.Status, member.Profile.StatusText, member.Identity.DepartmentId, member.OfficeDepartmentId);

    private static PresenceChangedEvent Changed(Member member) => new(member.Identity.CompanyId, Entry(member), null);

    private static OfficeOccupant Occupant(Member member) =>
        new(member.Identity.MembershipId, member.Identity.Name, member.Profile.AvatarKey, member.X, member.Y, member.Facing, member.Profile.Status, member.Profile.StatusText, member.Identity.DepartmentId);

    /// <summary>供 <c>--self-test</c> 驗證上線、閒置、寬限期、辦公室進出與敲門。</summary>
    public static void AssertExamples()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tracker = new PresenceTracker(clock);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var department = Guid.NewGuid();
        ConnectionIdentity Identity(Guid companyId, string name) => new(companyId, Guid.NewGuid(), Guid.NewGuid(), department, name, 1);
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"上線狀態：{message}");
        }
        var profile = new MemberProfile("a1", "available", null);
        var alice = Identity(companyA, "Alice");
        var bob = Identity(companyA, "Bob");
        var carol = Identity(companyB, "Carol");

        Check(tracker.Connect("a1", alice, profile)?.Entry.Presence == "online", "第一條連線要廣播上線");
        Check(tracker.Connect("a2", alice, profile) is null, "第二個分頁不重複廣播");
        tracker.Disconnect("a1");
        Check(tracker.Snapshot(companyA).Single().Presence == "online", "還有分頁時維持上線");
        tracker.Disconnect("a2");
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(tracker.Sweep().Count == 0, "寬限期內不轉離線");
        Check(tracker.Connect("a3", alice, profile) is null, "寬限期內重連不廣播");
        tracker.Disconnect("a3");
        clock.Advance(TimeSpan.FromSeconds(16));
        var offline = tracker.Sweep().OfType<PresenceChangedEvent>().Single();
        Check(offline.Entry.Presence == "offline" && offline.LastSeenAt is not null, "超過寬限期轉離線並帶最後在線時間");
        Check(tracker.Snapshot(companyA).Count == 0, "離線後不在快照中");

        tracker.Connect("b1", bob, profile);
        tracker.Connect("c1", carol, profile);
        Check(tracker.Snapshot(companyA).All(entry => entry.MembershipId == bob.MembershipId), "快照不含其他公司");
        clock.Advance(PresenceTracker.IdleAfter);
        Check(tracker.Sweep().OfType<PresenceChangedEvent>().Count(change => change.Entry.Presence == "idle") == 2, "5 分鐘沒操作轉閒置");
        Check(tracker.Touch("b1")?.Entry.Presence == "online", "有操作就恢復上線");

        var map = OfficeMapGenerator.Generate([new OfficeSeatOwner(bob.MembershipId, "Bob", null)], "wood");
        var join = tracker.JoinOffice("b1", department, map)!;
        Check(join.Me.X == map.Spawn.X && join.Me.Y == map.Spawn.Y && !join.AlreadyPresent, "進入辦公室出現在入口");
        Check(tracker.TryMove("b1", map.Spawn.X + 1, map.Spawn.Y, "right")!.Accepted, "相鄰格可以走");
        Check(!tracker.TryMove("b1", map.Spawn.X + 2, map.Spawn.Y + 1, "right")!.Accepted, "斜走或跳格要被拒絕");
        tracker.Connect("b2", bob, profile);
        Check(tracker.JoinOffice("b2", department, map)!.ReplacedConnectionId == "b1", "另一個分頁進入時取代原分頁");
        Check(tracker.TryMove("b1", map.Spawn.X, map.Spawn.Y, "left") is null, "被取代的分頁不能再移動");
        tracker.Disconnect("b2");
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(!tracker.Sweep().OfType<OccupantLeftEvent>().Any(), "辦公室分頁寬限期內不離場");
        tracker.Connect("b3", bob, profile);
        var rejoin = tracker.JoinOffice("b3", department, map)!;
        Check(rejoin.AlreadyPresent && rejoin.Me.X == map.Spawn.X + 1, "寬限期內重回辦公室保留位置");
        var seat = map.Seats.Single();
        Check(tracker.Place(bob.MembershipId, new OfficePoint(seat.X, seat.Y), seat.Facing) is { } placed && placed.X == seat.X && placed.Y == seat.Y, "回座位要放到自己的座位");
        tracker.Disconnect("b3");
        clock.Advance(TimeSpan.FromSeconds(16));
        Check(tracker.Sweep().OfType<OccupantLeftEvent>().Count() == 1, "超過寬限期離開辦公室");

        tracker.RecordKnock(alice.MembershipId, bob.MembershipId);
        Check(tracker.ConsumeKnock(alice.MembershipId, bob.MembershipId), "敲門後可以回應");
        Check(!tracker.ConsumeKnock(alice.MembershipId, bob.MembershipId), "同一次敲門只能回應一次");
    }
}

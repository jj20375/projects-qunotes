using System.Collections.Concurrent;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>虛擬辦公室的合法值與判斷規則；hub 與前端提示共用同一套定義（前端在 office-grid.ts 有對應實作）。</summary>
public static class OfficeRules
{
    /// <summary>成員可選的狀態清單；清單外的狀態字串一律拒收。</summary>
    public static readonly IReadOnlyList<string> Statuses = ["available", "busy", "meeting", "break", "away"];

    /// <summary>可送出的表情動作清單。</summary>
    public static readonly IReadOnlyList<string> Emotes = ["wave", "coffee", "thumbsup", "party", "question"];

    /// <summary>可選的角色外觀清單；未自選時由 <see cref="DefaultAvatar"/> 依使用者 Id 固定指派。</summary>
    public static readonly IReadOnlyList<string> Avatars = ["a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8"];

    /// <summary>地圖主題清單。</summary>
    public static readonly IReadOnlyList<string> Themes = ["wood", "carpet", "dark", "park", "space"];

    /// <summary>座位朝向清單。</summary>
    public static readonly IReadOnlyList<string> Facings = ["up", "down", "left", "right"];

    /// <summary>自訂狀態文字的最大長度（字元數）。</summary>
    public const int StatusTextMaxLength = 40;

    /// <summary>聊天訊息的最大長度（字元數）。</summary>
    public const int ChatMaxLength = 500;

    /// <summary>敲門訊息的最大長度（字元數）。</summary>
    public const int KnockMessageMaxLength = 100;

    /// <summary>一般區域的聊天收訊半徑（格數，以棋盤距離計算）。</summary>
    public const int ChatRange = 2;

    /// <summary>依使用者 Id 固定指派預設角色外觀，同一人每次結果相同。</summary>
    /// <param name="userId">使用者識別碼。</param>
    /// <returns>位於 <see cref="Avatars"/> 內的外觀代號。</returns>
    public static string DefaultAvatar(Guid userId) =>
        Avatars[(int)(BitConverter.ToUInt32(userId.ToByteArray(), 0) % (uint)Avatars.Count)];

    /// <summary>判斷角色能否從 <paramref name="from"/> 單步移到 <paramref name="to"/>。</summary>
    /// <param name="map">目前的辦公室地圖。</param>
    /// <param name="from">目前所在格子。</param>
    /// <param name="to">要移動到的格子。</param>
    /// <returns>只能上下左右走一格，且目的地可通行時為 <c>true</c>。</returns>
    public static bool IsValidStep(OfficeMap map, OfficePoint from, OfficePoint to) =>
        Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) == 1 && map.IsWalkable(to.X, to.Y);

    /// <summary>計算一則聊天訊息的收訊對象。</summary>
    /// <param name="map">目前的辦公室地圖。</param>
    /// <param name="sender">發送者的使用者 Id。</param>
    /// <param name="positions">所有在線成員目前的位置。</param>
    /// <returns>依 Id 排序的收訊者清單，不含發送者本人；發送者不在 <paramref name="positions"/> 內時為空。</returns>
    public static IReadOnlyList<Guid> ChatRecipients(OfficeMap map, Guid sender, IReadOnlyDictionary<Guid, OfficePoint> positions)
    {
        if (!positions.TryGetValue(sender, out var origin)) return [];
        var room = RoomAt(map, origin);
        return positions
            .Where(pair => pair.Key != sender && (room is not null
                ? room.Contains(pair.Value.X, pair.Value.Y)
                : RoomAt(map, pair.Value) is null && Math.Max(Math.Abs(pair.Value.X - origin.X), Math.Abs(pair.Value.Y - origin.Y)) <= ChatRange))
            .Select(pair => pair.Key)
            .Order()
            .ToList();
    }

    /// <summary>找出離目標最近、可通行且沒人站的格子，供成員進入或重新定位時使用。</summary>
    /// <param name="map">目前的辦公室地圖。</param>
    /// <param name="target">期望的位置。</param>
    /// <param name="occupied">已被占用的格子。</param>
    /// <returns>找到的空格；整張地圖都無空位時回傳入口。</returns>
    public static OfficePoint NearestFree(OfficeMap map, OfficePoint target, IReadOnlySet<OfficePoint> occupied)
    {
        var visited = new HashSet<OfficePoint>();
        var queue = new Queue<OfficePoint>();
        IEnumerable<OfficePoint> starts = map.IsWalkable(target.X, target.Y) ? new[] { target } : OfficeMap.Neighbors(target);
        foreach (var point in starts.Where(point => map.IsWalkable(point.X, point.Y) && visited.Add(point))) queue.Enqueue(point);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!occupied.Contains(point)) return point;
            foreach (var next in OfficeMap.Neighbors(point).Where(next => map.IsWalkable(next.X, next.Y) && visited.Add(next))) queue.Enqueue(next);
        }
        return map.Spawn;
    }

    /// <summary>取得格子所屬的會議室或茶水間；其他區塊與走道都不算房間。</summary>
    /// <param name="map">目前的辦公室地圖。</param>
    /// <param name="point">要查詢的格子。</param>
    /// <returns>房間區塊；不在房間內時為 <c>null</c>。</returns>
    private static OfficeZone? RoomAt(OfficeMap map, OfficePoint point) =>
        map.ZoneAt(point.X, point.Y) is { Type: "meeting" or "pantry" } zone ? zone : null;

    /// <summary>供 <c>--self-test</c> 驗證移動、聊天範圍、空位與頻率限制。</summary>
    /// <exception cref="InvalidOperationException">任一驗證條件不成立時拋出。</exception>
    public static void AssertExamples()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"虛擬辦公室規則：{message}");
        }
        Guid Id(int value) => new(value, 0, 0, new byte[8]);

        // 0 人的地圖：入口 (3,12)，會議室 x 8..15、y 1..6，冰箱在 (2,1)。
        var map = OfficeMapGenerator.Generate([], "wood");
        var spawn = map.Spawn;
        Check(IsValidStep(map, spawn, new(spawn.X + 1, spawn.Y)), "相鄰可走的格子被拒");
        Check(!IsValidStep(map, spawn, new(spawn.X + 1, spawn.Y + 1)), "斜走沒有被擋下");
        Check(!IsValidStep(map, spawn, new(spawn.X + 2, spawn.Y)), "跳格沒有被擋下");
        Check(!IsValidStep(map, new(1, 2), new(0, 2)), "走進外牆沒有被擋下");
        Check(!IsValidStep(map, new(2, 2), new(2, 1)), "走進家具沒有被擋下");

        var near = new Dictionary<Guid, OfficePoint> { [Id(1)] = spawn, [Id(2)] = new(spawn.X + 2, spawn.Y), [Id(3)] = new(spawn.X + 3, spawn.Y) };
        Check(ChatRecipients(map, Id(1), near).SequenceEqual([Id(2)]), "一般區域只有 2 格內收得到聊天");

        var room = new Dictionary<Guid, OfficePoint> { [Id(1)] = new(9, 2), [Id(2)] = new(14, 5), [Id(3)] = new(9, 8), [Id(4)] = new(9, 6) };
        Check(ChatRecipients(map, Id(1), room).SequenceEqual([Id(2), Id(4)]), "會議室內的人都要收到、室外收不到");
        Check(ChatRecipients(map, Id(3), room).Count == 0, "室外的人不能收到會議室內的人");

        var free = NearestFree(map, spawn, new HashSet<OfficePoint> { spawn });
        Check(free != spawn && Math.Abs(free.X - spawn.X) + Math.Abs(free.Y - spawn.Y) == 1 && map.IsWalkable(free.X, free.Y), "入口被占時要放到旁邊的空格");
        var besideFridge = NearestFree(map, new(2, 1), new HashSet<OfficePoint>());
        Check(besideFridge == new OfficePoint(2, 2), "目標是家具時要放到旁邊可走的格子");

        var avatar = DefaultAvatar(Id(7));
        Check(Avatars.Contains(avatar) && avatar == DefaultAvatar(Id(7)), "預設角色必須固定且在清單內");

        var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var gate = new RateGate(clock);
        Check(gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)) && gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "限制內要放行");
        Check(!gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "超過限制要擋下");
        clock.Advance(TimeSpan.FromSeconds(1));
        Check(gate.TryPass("c:move", 2, TimeSpan.FromSeconds(1)), "時間窗過後要恢復");
        gate.Forget("c:");
        Check(gate.TryPass("c:move", 1, TimeSpan.FromSeconds(1)), "Forget 後重新計算");
    }
}

/// <summary>
/// 以滑動時間窗計數的頻率限制；key 慣例為 <c>{connectionId}:{動作}</c>，斷線時以 <see cref="Forget"/> 清除。
/// </summary>
/// <param name="clock">計算時間窗使用的時鐘，測試時以 <see cref="ManualClock"/> 替換。</param>
public sealed class RateGate(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);

    /// <summary>嘗試記錄一次動作；時間窗內已達上限時拒絕且不計入。</summary>
    /// <param name="key">限制的鍵值，通常為連線識別碼加動作名稱。</param>
    /// <param name="limit">時間窗內允許的最大次數。</param>
    /// <param name="window">滑動時間窗長度。</param>
    /// <returns>放行時為 <c>true</c>；超過限制時為 <c>false</c>。</returns>
    public bool TryPass(string key, int limit, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        var hits = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() >= window) hits.Dequeue();
            if (hits.Count >= limit) return false;
            hits.Enqueue(now);
            return true;
        }
    }

    /// <summary>清除所有鍵值以指定前綴開頭的紀錄，通常於連線中斷時呼叫。</summary>
    /// <param name="keyPrefix">要清除的鍵值前綴。</param>
    public void Forget(string keyPrefix)
    {
        foreach (var key in _hits.Keys.Where(key => key.StartsWith(keyPrefix, StringComparison.Ordinal))) _hits.TryRemove(key, out _);
    }
}

/// <summary>self-test 用的可控時鐘。</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <summary>回傳目前模擬的 UTC 時間。</summary>
    /// <returns>模擬時鐘的目前時間。</returns>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>將模擬時間往前推進指定長度。</summary>
    /// <param name="by">推進的時間長度。</param>
    public void Advance(TimeSpan by) => _now += by;
}

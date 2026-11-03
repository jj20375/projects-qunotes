using System.Text.Json;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>地圖上的格子座標。</summary>
/// <param name="X">橫向格座標，由左至右遞增。</param>
/// <param name="Y">縱向格座標，由上至下遞增。</param>
public sealed record OfficePoint(int X, int Y);

/// <summary>地圖區塊；會議室與茶水間是「同區塊即聊」的房間。</summary>
/// <param name="Id">區塊識別碼，例如 <c>pantry</c>、<c>meeting</c>。</param>
/// <param name="Type">區塊類型：<c>desk</c>、<c>meeting</c>、<c>pantry</c>、<c>entrance</c>。</param>
/// <param name="X">區塊左上角的橫向格座標。</param>
/// <param name="Y">區塊左上角的縱向格座標。</param>
/// <param name="W">區塊寬度（格數）。</param>
/// <param name="H">區塊高度（格數）。</param>
public sealed record OfficeZone(string Id, string Type, int X, int Y, int W, int H)
{
    /// <summary>判斷指定格子是否落在此區塊內（左閉右開）。</summary>
    /// <param name="x">要判斷的橫向格座標。</param>
    /// <param name="y">要判斷的縱向格座標。</param>
    /// <returns>格子位於區塊內時為 <c>true</c>。</returns>
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>家具；只有 chair、rug、whiteboard 不擋路（whiteboard 掛在牆上）。</summary>
/// <param name="Kind">家具種類：<c>desk</c>、<c>chair</c>、<c>meeting-table</c>、<c>coffee</c>、<c>fridge</c>、<c>counter</c>、<c>table</c>、<c>plant</c>、<c>whiteboard</c>、<c>rug</c>。</param>
/// <param name="X">橫向格座標。</param>
/// <param name="Y">縱向格座標。</param>
public sealed record OfficeFurniture(string Kind, int X, int Y);

/// <summary>部門成員的專屬座位。</summary>
/// <param name="MembershipId">成員的部門身分（membership）識別碼。</param>
/// <param name="Name">成員顯示名稱。</param>
/// <param name="X">座位橫向格座標。</param>
/// <param name="Y">座位縱向格座標。</param>
/// <param name="Facing">座位朝向：<c>up</c>、<c>down</c>、<c>left</c>、<c>right</c>。</param>
/// <param name="LastSeenAt">成員最後上線時間；從未上線時為 <c>null</c>。</param>
public sealed record OfficeSeat(Guid MembershipId, string Name, int X, int Y, string Facing, DateTimeOffset? LastSeenAt);

/// <summary>產生地圖的輸入：依加入部門的先後排序。</summary>
/// <param name="MembershipId">成員的部門身分（membership）識別碼。</param>
/// <param name="Name">成員顯示名稱。</param>
/// <param name="LastSeenAt">成員最後上線時間；從未上線時為 <c>null</c>。</param>
public sealed record OfficeSeatOwner(Guid MembershipId, string Name, DateTimeOffset? LastSeenAt);

/// <summary>
/// 一間部門辦公室；<see cref="Grid"/> 每列一個字串：<c>#</c> 牆、<c>.</c> 可走、<c>f</c> 家具。
/// </summary>
/// <param name="Width">地圖寬度（格數）。</param>
/// <param name="Height">地圖高度（格數）。</param>
/// <param name="TileSize">每格的像素尺寸。</param>
/// <param name="Theme">主題名稱，由呼叫端決定，地圖本身不解讀。</param>
/// <param name="Grid">每列一個字串的格子資料，長度與 <paramref name="Width"/>、<paramref name="Height"/> 一致。</param>
/// <param name="Furniture">家具清單。</param>
/// <param name="Zones">區塊清單。</param>
/// <param name="Seats">成員座位清單，順序與輸入成員順序相同。</param>
/// <param name="Spawn">入口（新進成員出現的位置）。</param>
public sealed record OfficeMap(
    int Width,
    int Height,
    int TileSize,
    string Theme,
    IReadOnlyList<string> Grid,
    IReadOnlyList<OfficeFurniture> Furniture,
    IReadOnlyList<OfficeZone> Zones,
    IReadOnlyList<OfficeSeat> Seats,
    OfficePoint Spawn)
{
    /// <summary>判斷指定格子是否可走（超出邊界或非 <c>.</c> 皆不可走）。</summary>
    /// <param name="x">橫向格座標。</param>
    /// <param name="y">縱向格座標。</param>
    /// <returns>格子可走時為 <c>true</c>。</returns>
    public bool IsWalkable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Grid[y][x] == '.';

    /// <summary>取得指定格子所屬的區塊；不在任何區塊內時回傳 <c>null</c>。</summary>
    /// <param name="x">橫向格座標。</param>
    /// <param name="y">縱向格座標。</param>
    /// <returns>所屬區塊；沒有時為 <c>null</c>。</returns>
    public OfficeZone? ZoneAt(int x, int y) => Zones.FirstOrDefault(zone => zone.Contains(x, y));

    /// <summary>從起點出發的四方向步數；起點不可走時回傳空集合。</summary>
    /// <param name="from">起點格子。</param>
    /// <returns>每個可達格子到起點的最短步數。</returns>
    public IReadOnlyDictionary<OfficePoint, int> Distances(OfficePoint from)
    {
        var distances = new Dictionary<OfficePoint, int>();
        if (!IsWalkable(from.X, from.Y)) return distances;
        distances[from] = 0;
        var queue = new Queue<OfficePoint>([from]);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            foreach (var next in Neighbors(point))
            {
                if (!IsWalkable(next.X, next.Y) || distances.ContainsKey(next)) continue;
                distances[next] = distances[point] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }

    /// <summary>上、右、下、左四個鄰格。</summary>
    /// <param name="point">中心格子。</param>
    /// <returns>四個鄰格，不檢查是否可走或超出邊界。</returns>
    public static IEnumerable<OfficePoint> Neighbors(OfficePoint point) =>
        [new(point.X, point.Y - 1), new(point.X + 1, point.Y), new(point.X, point.Y + 1), new(point.X - 1, point.Y)];
}

/// <summary>依部門成員自動產生辦公室；同樣的輸入必定產生同樣的地圖，不另存地圖檔。</summary>
public static class OfficeMapGenerator
{
    /// <summary>每格的像素尺寸。</summary>
    public const int TileSize = 16;

    private const int ClustersPerBand = 3;
    private const int SeatsPerCluster = 4;
    private const int SeatsPerBand = ClustersPerBand * SeatsPerCluster;
    private const int PantryWidth = 6;
    private const int RoomHeight = 6;
    private const int EntranceWidth = 5;
    private const int BandHeight = 5;
    // 上牆(0)、茶水間與會議室(1..6)、隔牆(7)、走道(8) 之後才是第一排桌位。
    private const int FirstBandY = RoomHeight + 3;

    /// <summary>
    /// 依成員產生辦公室地圖。
    /// </summary>
    /// <remarks>
    /// 版面：上方左茶水間、右會議室（≤8 人寬 8 格，否則 12 格），中間一道有門的隔牆；
    /// 下方左側入口，右側桌位以 4 人一組（兩兩對坐）排列，一排固定 3 組，人多往下加排。
    /// 每排組數固定，所以會議室變寬或加排時，原有成員的座位不會移動。
    /// </remarks>
    /// <param name="owners">依加入部門先後排序的成員清單。</param>
    /// <param name="theme">主題名稱，原樣寫入地圖。</param>
    /// <returns>含牆、家具、區塊、座位與入口的地圖。</returns>
    public static OfficeMap Generate(IReadOnlyList<OfficeSeatOwner> owners, string theme)
    {
        var meetingWidth = owners.Count <= 8 ? 8 : 12;
        var width = PantryWidth + meetingWidth + 3;
        var bands = Math.Max(1, (owners.Count + SeatsPerBand - 1) / SeatsPerBand);
        var height = FirstBandY + bands * BandHeight + 1;
        var meetingX = PantryWidth + 2;
        var dividerY = RoomHeight + 1;

        var grid = new char[height][];
        for (var y = 0; y < height; y++)
        {
            grid[y] = new char[width];
            for (var x = 0; x < width; x++) grid[y][x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : '.';
        }
        for (var x = 1; x < width - 1; x++) grid[dividerY][x] = '#';
        for (var y = 1; y < dividerY; y++) grid[y][PantryWidth + 1] = '#';
        grid[dividerY][3] = '.';
        grid[dividerY][meetingX + meetingWidth / 2] = '.';

        var furniture = new List<OfficeFurniture>();
        void Place(string kind, int x, int y, bool blocking = true)
        {
            furniture.Add(new OfficeFurniture(kind, x, y));
            if (blocking) grid[y][x] = 'f';
        }

        Place("coffee", 1, 1);
        Place("fridge", 2, 1);
        Place("counter", 3, 1);
        Place("counter", 4, 1);
        Place("plant", PantryWidth, 1);
        Place("table", 3, 4);
        Place("table", 4, 4);

        Place("whiteboard", meetingX + meetingWidth / 2, 0, blocking: false);
        Place("plant", meetingX + meetingWidth - 1, 1);
        for (var x = meetingX + 2; x <= meetingX + meetingWidth - 3; x++)
        {
            Place("meeting-table", x, 3);
            Place("meeting-table", x, 4);
        }

        var spawn = new OfficePoint(3, height - 3);
        Place("rug", spawn.X, spawn.Y, blocking: false);
        Place("plant", 1, height - 2);

        var usedClusters = (owners.Count + SeatsPerCluster - 1) / SeatsPerCluster;
        for (var cluster = 0; cluster < usedClusters; cluster++)
        {
            var (left, top) = ClusterOrigin(cluster);
            for (var column = 0; column < 2; column++)
            {
                Place("desk", left + column, top + 1);
                Place("desk", left + column, top + 2);
            }
        }

        var seats = new List<OfficeSeat>(owners.Count);
        for (var index = 0; index < owners.Count; index++)
        {
            var (left, top) = ClusterOrigin(index / SeatsPerCluster);
            var slot = index % SeatsPerCluster;
            var upper = slot < 2;
            var x = left + slot % 2;
            var y = upper ? top : top + 3;
            Place("chair", x, y, blocking: false);
            seats.Add(new OfficeSeat(owners[index].MembershipId, owners[index].Name, x, y, upper ? "down" : "up", owners[index].LastSeenAt));
        }

        var bottomHeight = height - dividerY - 2;
        OfficeZone[] zones =
        [
            new("pantry", "pantry", 1, 1, PantryWidth, RoomHeight),
            new("meeting", "meeting", meetingX, 1, meetingWidth, RoomHeight),
            new("entrance", "entrance", 1, dividerY + 1, EntranceWidth, bottomHeight),
            new("desk", "desk", EntranceWidth + 1, dividerY + 1, width - EntranceWidth - 2, bottomHeight),
        ];
        return new OfficeMap(width, height, TileSize, theme, grid.Select(row => new string(row)).ToArray(), furniture, zones, seats, spawn);

        static (int Left, int Top) ClusterOrigin(int cluster) =>
            (EntranceWidth + 2 + cluster % ClustersPerBand * 3, FirstBandY + cluster / ClustersPerBand * BandHeight);
    }

    /// <summary>供 <c>--self-test</c> 驗證地圖產生規則。</summary>
    /// <exception cref="InvalidOperationException">任一驗證條件不成立時拋出。</exception>
    public static void AssertExamples()
    {
        var owners = Enumerable.Range(1, 30).Select(index => new OfficeSeatOwner(new Guid(index, 0, 0, new byte[8]), $"成員{index}", null)).ToArray();
        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException($"虛擬辦公室地圖：{message}");
        }

        Check(JsonSerializer.Serialize(Generate(owners[..5], "wood")) == JsonSerializer.Serialize(Generate(owners[..5], "wood")), "同樣的成員必須產生同樣的地圖");
        foreach (var count in new[] { 0, 1, 8, 9, 12, 13, 30 })
        {
            var map = Generate(owners[..count], "wood");
            Check(map.Grid.Count == map.Height && map.Grid.All(row => row.Length == map.Width), $"{count} 人：地圖尺寸不一致");
            Check(map.Seats.Count == count, $"{count} 人：座位數與成員數不符");
            Check(map.Seats.Select(seat => (seat.X, seat.Y)).Distinct().Count() == count, $"{count} 人：座位重複");
            var reachable = map.Distances(map.Spawn);
            Check(map.Seats.All(seat => reachable.ContainsKey(new OfficePoint(seat.X, seat.Y))), $"{count} 人：有座位從入口走不到");
            Check(map.Zones.All(zone => reachable.Keys.Any(point => zone.Contains(point.X, point.Y))), $"{count} 人：有區塊從入口走不到");
        }
        foreach (var (before, after) in new[] { (8, 9), (12, 13) })
        {
            var earlier = Generate(owners[..before], "wood").Seats;
            var later = Generate(owners[..after], "wood").Seats;
            Check(earlier.Select((seat, index) => seat.X == later[index].X && seat.Y == later[index].Y).All(same => same), $"{before}→{after} 人：新增成員後原有座位被移動");
        }
    }
}

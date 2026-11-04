using System.Text.Json;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>地圖上的格子座標。</summary>
/// <param name="X">橫向格座標，由左至右遞增。</param>
/// <param name="Y">縱向格座標，由上至下遞增。</param>
public sealed record OfficePoint(int X, int Y);

/// <summary>
/// 地圖區塊；會議室與茶水間是「同區塊即聊」的房間。
/// 交誼廳（<c>lounge</c>）刻意沿用 <c>pantry</c> 類型，所以同樣是同區塊即聊的房間，
/// 前端地板也用茶水間貼圖；<c>break</c> 狀態只會走到第一個 <c>pantry</c>（茶水間）。
/// </summary>
/// <param name="Id">區塊識別碼，例如 <c>pantry</c>、<c>meeting</c>、<c>lounge</c>。</param>
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
/// <param name="Kind">家具種類：<c>desk</c>、<c>desk-side</c>、<c>chair</c>、<c>meeting-table</c>、<c>coffee</c>、<c>fridge</c>、<c>counter</c>、<c>water</c>、<c>table</c>、<c>sofa</c>、<c>bookshelf</c>、<c>plant</c>、<c>whiteboard</c>、<c>rug</c>、<c>cabinet</c>。</param>
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
    private const int MeetingWidth = 12;
    private const int LoungeWidth = 8;
    private const int RoomHeight = 6;
    private const int EntranceWidth = 6;
    private const int BandHeight = 5;
    // 同一組內兩個直欄人位（左右各一台桌＋側桌，各佔 2 格）的起點間距；
    // 小於 3 時兩人的姓名牌會互相重疊，椅子座標也必須用同一個間距才會落在桌前。
    private const int DeskPitchX = 3;
    // 一組寬 5 格（兩欄人位＋中間植栽／收納櫃）＋2 格通道，通道讓叢集之間與右側植栽旁都走得過去。
    private const int ClusterPitchX = DeskPitchX + 2 + 2;
    // 上牆(0)、茶水間／會議室／交誼廳(1..6)、隔牆(7)、主走道(8) 之後才是第一排桌位。
    private const int FirstBandY = RoomHeight + 3;

    /// <summary>
    /// 依成員產生寬螢幕現代辦公室地圖。
    /// </summary>
    /// <remarks>
    /// 版面：上方左茶水吧台間、中大型會議室、右沙發交誼廳，中間一道有雙開門的隔牆；
    /// 下方左側迎賓接待區，右側工作站以 4 人一組（每人 2 格寬 L 型電腦桌、兩兩對坐、中間隔以植栽與收納櫃）排列，
    /// 一排固定 3 組，最少 2 排以填滿寬螢幕比例，人多往下加排。
    /// 每排組數與上方房間寬度固定，所以新增成員時原有成員的座位不會移動。
    /// </remarks>
    /// <param name="owners">依加入部門先後排序的成員清單。</param>
    /// <param name="theme">主題名稱，原樣寫入地圖。</param>
    /// <returns>含牆、家具、區塊、座位與入口的地圖。</returns>
    public static OfficeMap Generate(IReadOnlyList<OfficeSeatOwner> owners, string theme)
    {
        var width = PantryWidth + MeetingWidth + LoungeWidth + 4;
        var bands = Math.Max(2, (owners.Count + SeatsPerBand - 1) / SeatsPerBand);
        var height = FirstBandY + bands * BandHeight + 1;
        var meetingX = PantryWidth + 2;
        var loungeX = meetingX + MeetingWidth + 1;
        var dividerY = RoomHeight + 1;

        var grid = new char[height][];
        for (var y = 0; y < height; y++)
        {
            grid[y] = new char[width];
            for (var x = 0; x < width; x++) grid[y][x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : '.';
        }
        for (var x = 1; x < width - 1; x++) grid[dividerY][x] = '#';
        for (var y = 1; y < dividerY; y++)
        {
            grid[y][PantryWidth + 1] = '#';
            grid[y][loungeX - 1] = '#';
        }
        // 門寬 2 格：單格門會被站在門口的人整個堵住。
        grid[dividerY][3] = '.';
        grid[dividerY][4] = '.';
        grid[dividerY][meetingX + MeetingWidth / 2 - 1] = '.';
        grid[dividerY][meetingX + MeetingWidth / 2] = '.';
        grid[dividerY][loungeX + LoungeWidth / 2 - 1] = '.';
        grid[dividerY][loungeX + LoungeWidth / 2] = '.';

        var furniture = new List<OfficeFurniture>();
        void Place(string kind, int x, int y, bool blocking = true)
        {
            furniture.Add(new OfficeFurniture(kind, x, y));
            if (blocking) grid[y][x] = 'f';
        }

        // 左上：茶水吧台間。(2,1) 必須是家具、(1,2)(2,2) 必須可走：
        // OfficeRules.AssertExamples 站在這幾格驗證「走進牆／家具被拒」與「目標是家具時改放旁邊空格」。
        Place("coffee", 1, 1);
        Place("fridge", 2, 1);
        Place("counter", 3, 1);
        Place("counter", 4, 1);
        Place("water", 5, 1);
        Place("plant", PantryWidth, 1);
        Place("table", 3, 4);
        Place("table", 4, 4);

        // 中上：大型簡報會議室。(9,2)(14,5)(9,6) 必須可走且位於會議室內：
        // OfficeRules.AssertExamples 讓角色站在這幾格驗證會議室內即聊的收訊範圍。
        Place("whiteboard", meetingX + MeetingWidth / 2 - 1, 0, blocking: false);
        Place("whiteboard", meetingX + MeetingWidth / 2, 0, blocking: false);
        Place("plant", meetingX, 1);
        Place("plant", meetingX + MeetingWidth - 1, 1);
        for (var x = meetingX + 3; x <= meetingX + MeetingWidth - 4; x++)
        {
            Place("chair", x, 2, blocking: false);
            Place("meeting-table", x, 3);
            Place("meeting-table", x, 4);
            Place("chair", x, 5, blocking: false);
        }

        // 右上：沙發交誼廳與圖書角
        Place("bookshelf", loungeX + 1, 1);
        Place("bookshelf", loungeX + 2, 1);
        Place("bookshelf", loungeX + 3, 1);
        Place("cabinet", loungeX + 5, 1);
        Place("plant", loungeX + LoungeWidth - 1, 1);
        Place("sofa", loungeX + 2, 3);
        Place("sofa", loungeX + 4, 3);
        Place("rug", loungeX + 2, 4, blocking: false);
        Place("table", loungeX + 3, 4);
        Place("rug", loungeX + 4, 4, blocking: false);

        // 左下：迎賓接待大廳與入口
        Place("plant", 1, dividerY + 2);
        Place("sofa", 2, dividerY + 4);
        Place("table", 3, dividerY + 4);
        Place("bookshelf", 5, dividerY + 2);
        var spawn = new OfficePoint(3, height - 3);
        Place("rug", spawn.X, spawn.Y, blocking: false);
        Place("rug", spawn.X + 1, spawn.Y, blocking: false);
        Place("plant", 1, height - 2);
        Place("water", 5, height - 2);

        // 右下：工作站島區，每組 4 人。
        var totalClusters = bands * ClustersPerBand;
        for (var cluster = 0; cluster < totalClusters; cluster++)
        {
            var (left, top) = ClusterOrigin(cluster);
            for (var offset = 0; offset < 2 * DeskPitchX; offset += DeskPitchX)
            {
                Place("desk", left + offset, top + 1);
                Place("desk-side", left + offset + 1, top + 1);
                Place("desk", left + offset, top + 2);
                Place("desk-side", left + offset + 1, top + 2);
            }
            Place("plant", left + 2, top + 1);
            Place("cabinet", left + 2, top + 2);
        }

        for (var band = 0; band < bands; band++)
        {
            Place("plant", width - 2, FirstBandY + band * BandHeight + 1);
        }

        var seats = new List<OfficeSeat>(owners.Count);
        for (var index = 0; index < owners.Count; index++)
        {
            var (left, top) = ClusterOrigin(index / SeatsPerCluster);
            var slot = index % SeatsPerCluster;
            var upper = slot < 2;
            var x = left + (slot % 2) * DeskPitchX;
            var y = upper ? top : top + 3;
            Place("chair", x, y, blocking: false);
            seats.Add(new OfficeSeat(owners[index].MembershipId, owners[index].Name, x, y, upper ? "down" : "up", owners[index].LastSeenAt));
        }

        var bottomHeight = height - dividerY - 2;
        OfficeZone[] zones =
        [
            new("pantry", "pantry", 1, 1, PantryWidth, RoomHeight),
            new("meeting", "meeting", meetingX, 1, MeetingWidth, RoomHeight),
            new("lounge", "pantry", loungeX, 1, LoungeWidth, RoomHeight),
            new("entrance", "entrance", 1, dividerY + 1, EntranceWidth, bottomHeight),
            new("desk", "desk", EntranceWidth + 1, dividerY + 1, width - EntranceWidth - 2, bottomHeight),
        ];
        return new OfficeMap(width, height, TileSize, theme, grid.Select(row => new string(row)).ToArray(), furniture, zones, seats, spawn);

        // +2：入口區（寬 EntranceWidth）之後再空出一格縱向走道，第一組桌位才開始；
        // 座位只由 cluster 編號決定，與人數、排數無關，這是「新增成員不移動既有座位」的前提。
        static (int Left, int Top) ClusterOrigin(int cluster) =>
            (EntranceWidth + 2 + cluster % ClustersPerBand * ClusterPitchX, FirstBandY + cluster / ClustersPerBand * BandHeight);
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
        foreach (var count in new[] { 0, 1, 4, 12, 13, 24, 25, 30 })
        {
            var map = Generate(owners[..count], "wood");
            Check(map.Grid.Count == map.Height && map.Grid.All(row => row.Length == map.Width), $"{count} 人：地圖尺寸不一致");
            var expectedBands = Math.Max(2, (count + SeatsPerBand - 1) / SeatsPerBand);
            Check(map.Height == FirstBandY + expectedBands * BandHeight + 1, $"{count} 人：桌位排數應為 {expectedBands} 排");
            Check(map.Seats.Count == count, $"{count} 人：座位數與成員數不符");
            Check(map.Seats.Select(seat => (seat.X, seat.Y)).Distinct().Count() == count, $"{count} 人：座位重複");
            Check(map.IsWalkable(map.Spawn.X, map.Spawn.Y) && map.ZoneAt(map.Spawn.X, map.Spawn.Y)?.Type == "entrance", $"{count} 人：入口必須是入口區內的可走格");
            var reachable = map.Distances(map.Spawn);
            Check(map.Seats.All(seat => reachable.ContainsKey(new OfficePoint(seat.X, seat.Y))), $"{count} 人：有座位從入口走不到");
            Check(map.Zones.All(zone => reachable.Keys.Any(point => zone.Contains(point.X, point.Y))), $"{count} 人：有區塊從入口走不到");
        }

        // 24 人剛好填滿 2 排；第 25 人才新增第 3 排，地圖只往下加高一排。
        // 入口（Spawn）刻意不比對：它貼著底部，會隨 Height 下移，規則只保證座位不動。
        var twoBands = Generate(owners[..24], "wood");
        var threeBands = Generate(owners[..25], "wood");
        Check(Generate([], "wood").Height == twoBands.Height, "0 人與 24 人都應是最少的 2 排桌位");
        Check(threeBands.Height == twoBands.Height + BandHeight, "第 25 人應新增第 3 排桌位，地圖高度增加一排");
        Check(threeBands.Width == twoBands.Width, "新增桌位排不應改變地圖寬度");

        // 座位穩定：4→5 跨叢集、12→13 跨到第 2 排、24→25 與 24→30 跨到第 3 排（排數增加）。
        foreach (var (before, after) in new[] { (4, 5), (12, 13), (24, 25), (24, 30) })
        {
            var earlier = Generate(owners[..before], "wood").Seats;
            var later = Generate(owners[..after], "wood").Seats;
            Check(earlier.Select((seat, index) => seat.X == later[index].X && seat.Y == later[index].Y && seat.Facing == later[index].Facing).All(same => same), $"{before}→{after} 人：新增成員後原有座位被移動");
        }
    }
}

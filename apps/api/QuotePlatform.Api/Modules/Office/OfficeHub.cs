using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using QuotePlatform.Api.Infrastructure;

namespace QuotePlatform.Api.Modules.Office;

/// <summary>
/// 虛擬辦公室的即時通道：上線狀態、走動、表情、靠近聊天與敲門。
/// </summary>
/// <remarks>
/// 前端只送意圖，座標、對象與文字一律在這裡驗證後才轉發；錯誤以 <see cref="HubException"/> 回傳繁體中文訊息。
/// 長連線不會在每個請求重新驗證 JWT，帳號停用與權限異動由 <see cref="OfficeSweeper"/> 定期比對 AuthVersion 後斷線。
/// hub 沒有可靠的 HttpContext，存取資料庫前以 <see cref="OfficeService.UseCompany"/> 明確指定連線身分的公司。
/// </remarks>
[Authorize]
public sealed class OfficeHub(PresenceTracker tracker, RateGate gate, OfficeService service, OfficeConnections connections, TimeProvider clock) : Hub<IOfficeClient>
{
    /// <summary>hub 的對外路徑；JWT 的 query access_token 只在這個路徑被接受。</summary>
    public const string Path = "/api/hubs/office";

    /// <summary>只接受公司範圍、有 membership、非平台代入且具備 office:read 的連線。</summary>
    /// <returns>連線建立完成的工作。</returns>
    public override async Task OnConnectedAsync()
    {
        if (Context.User is null
            || !RequestScope.TryRead(Context.User, out var scope)
            || !scope.IsCompany
            || scope.MembershipId is not Guid membershipId
            || scope.IsImpersonating
            || !scope.Has("office", "read"))
        {
            Context.Abort();
            return;
        }
        var companyId = scope.CompanyId!.Value;
        try
        {
            service.UseCompany(companyId);
            var (profile, name) = await service.ProfileAsync(membershipId, scope.UserId, Context.ConnectionAborted);
            connections.Register(Context);
            await Groups.AddToGroupAsync(Context.ConnectionId, OfficeGroups.Company(companyId));
            var changed = tracker.Connect(Context.ConnectionId, new ConnectionIdentity(companyId, membershipId, scope.UserId, scope.DepartmentId, name, scope.AuthVersion), profile);
            if (changed is not null) await Clients.Group(OfficeGroups.Company(companyId)).PresenceChanged(changed.Entry, null);
            await base.OnConnectedAsync();
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            // 用戶端剛握手就關閉：視為正常結束，不留錯誤紀錄。
            Cleanup();
        }
        catch
        {
            // OnConnectedAsync 拋例外時 SignalR 不會再呼叫 OnDisconnectedAsync，不清掉的話這個人會永遠顯示在線。
            Cleanup();
            throw;
        }
    }

    /// <summary>連線中斷時清除追蹤狀態與頻率限制計數；離線廣播由 <see cref="OfficeSweeper"/> 在寬限期後處理。</summary>
    /// <param name="exception">中斷原因；正常關閉為 null。</param>
    /// <returns>清理完成的工作。</returns>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Cleanup();
        await base.OnDisconnectedAsync(exception);
    }

    private void Cleanup()
    {
        tracker.Disconnect(Context.ConnectionId);
        connections.Unregister(Context.ConnectionId);
        gate.Forget(Context.ConnectionId + ":");
    }

    /// <summary>前端偵測到操作時最多每 60 秒送一次，用來維持「在線」而非「閒置」。</summary>
    /// <returns>狀態有變時廣播完成的工作。</returns>
    public async Task Activity()
    {
        if (!Allow("activity", 5, TimeSpan.FromMinutes(1))) return;
        if (tracker.Touch(Context.ConnectionId) is { } changed) await Clients.Group(OfficeGroups.Company(changed.CompanyId)).PresenceChanged(changed.Entry, null);
    }

    /// <summary>進入某部門的辦公室；同一人同時只會有一個分頁在辦公室內，舊分頁會收到 OfficeSessionReplaced。</summary>
    /// <param name="departmentId">要進入的部門。</param>
    /// <returns>辦公室地圖、自己的位置與目前在場的人。</returns>
    /// <exception cref="HubException">找不到辦公室、連線已失效或操作太頻繁。</exception>
    public async Task<OfficeSnapshot> JoinOffice(Guid departmentId)
    {
        var me = Me();
        Limit("join", 10, TimeSpan.FromMinutes(1));
        service.UseCompany(me.CompanyId);
        var office = await service.LoadAsync(me.CompanyId, departmentId, Context.ConnectionAborted) ?? throw new HubException("找不到此部門的辦公室。");
        var join = tracker.JoinOffice(Context.ConnectionId, departmentId, office.Map) ?? throw new HubException("連線已失效，請重新整理頁面。");
        if (join.PreviousDepartmentId is Guid previous)
        {
            if (join.ReplacedConnectionId is string replaced)
            {
                await Groups.RemoveFromGroupAsync(replaced, OfficeGroups.Office(me.CompanyId, previous));
                await Clients.Client(replaced).OfficeSessionReplaced();
            }
            if (previous != departmentId)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, OfficeGroups.Office(me.CompanyId, previous));
                await Clients.Group(OfficeGroups.Office(me.CompanyId, previous)).OccupantLeft(me.MembershipId);
            }
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, OfficeGroups.Office(me.CompanyId, departmentId));
        if (!join.AlreadyPresent) await Clients.OthersInGroup(OfficeGroups.Office(me.CompanyId, departmentId)).OccupantJoined(join.Me);
        return new OfficeSnapshot(office.Map, office.Name, join.Me, join.Occupants);
    }

    /// <summary>離開目前所在的辦公室，但保留上線狀態。</summary>
    /// <returns>廣播離開完成的工作；本來就不在辦公室則什麼都不做。</returns>
    public async Task LeaveOffice()
    {
        if (tracker.LeaveOffice(Context.ConnectionId) is not { } left) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, OfficeGroups.Office(left.CompanyId, left.DepartmentId));
        await Clients.Group(OfficeGroups.Office(left.CompanyId, left.DepartmentId)).OccupantLeft(left.MembershipId);
    }

    /// <summary>走一格；不合法時只回給自己 PositionCorrected，超量的訊息直接丟棄。</summary>
    /// <param name="x">目標 X 格座標。</param>
    /// <param name="y">目標 Y 格座標。</param>
    /// <param name="facing">面向（up／down／left／right）。</param>
    /// <returns>移動廣播完成的工作。</returns>
    public async Task Move(int x, int y, string facing)
    {
        if (!OfficeRules.Facings.Contains(facing) || !Allow("move", 10, TimeSpan.FromSeconds(1))) return;
        if (tracker.TryMove(Context.ConnectionId, x, y, facing) is not { } move) return;
        var occupant = move.Occupant;
        if (!move.Accepted)
        {
            CountRejection();
            await Clients.Caller.PositionCorrected(occupant.X, occupant.Y, occupant.Facing);
            return;
        }
        await Clients.OthersInGroup(OfficeGroups.Office(Me().CompanyId, move.DepartmentId)).Moved(occupant.MembershipId, x, y, facing);
    }

    /// <summary>瞬間移動回自己的座位。</summary>
    /// <returns>廣播新位置完成的工作。</returns>
    /// <exception cref="HubException">尚未進入辦公室、沒有座位或操作太頻繁。</exception>
    public async Task GoToSeat()
    {
        var me = Me();
        Limit("seat", 5, TimeSpan.FromSeconds(10));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        var seat = office.Map.Seats.FirstOrDefault(value => value.MembershipId == me.MembershipId) ?? throw new HubException("你在這間辦公室沒有座位。");
        var placed = tracker.Place(me.MembershipId, new OfficePoint(seat.X, seat.Y), seat.Facing) ?? throw new HubException("請先進入辦公室。");
        await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).Moved(placed.MembershipId, placed.X, placed.Y, placed.Facing);
    }

    /// <summary>設定手動狀態並持久化到 office_profiles。</summary>
    /// <param name="status">狀態代碼，須在 <see cref="OfficeRules.Statuses"/> 內。</param>
    /// <param name="statusText">狀態文字；空白視為清除。</param>
    /// <returns>儲存並廣播完成的工作。</returns>
    /// <exception cref="HubException">狀態不正確、文字過長或操作太頻繁。</exception>
    public async Task SetStatus(string status, string? statusText)
    {
        var me = Me();
        if (!OfficeRules.Statuses.Contains(status)) throw new HubException("狀態不正確。");
        statusText = string.IsNullOrWhiteSpace(statusText) ? null : statusText.Trim();
        if (statusText?.Length > OfficeRules.StatusTextMaxLength) throw new HubException($"狀態文字最多 {OfficeRules.StatusTextMaxLength} 字。");
        Limit("status", 5, TimeSpan.FromSeconds(10));
        service.UseCompany(me.CompanyId);
        await service.SaveStatusAsync(me.MembershipId, status, statusText, Context.ConnectionAborted);
        if (tracker.SetStatus(me.MembershipId, status, statusText) is { } changed) await Clients.Group(OfficeGroups.Company(me.CompanyId)).PresenceChanged(changed.Entry, null);
    }

    /// <summary>更換角色外觀並持久化到 office_profiles。</summary>
    /// <param name="avatarKey">角色代碼，須在 <see cref="OfficeRules.Avatars"/> 內。</param>
    /// <returns>儲存並廣播完成的工作。</returns>
    /// <exception cref="HubException">角色不正確或操作太頻繁。</exception>
    public async Task SetAvatar(string avatarKey)
    {
        var me = Me();
        if (!OfficeRules.Avatars.Contains(avatarKey)) throw new HubException("角色不正確。");
        Limit("avatar", 5, TimeSpan.FromSeconds(10));
        service.UseCompany(me.CompanyId);
        await service.SaveAvatarAsync(me.MembershipId, avatarKey, Context.ConnectionAborted);
        if (tracker.SetAvatar(me.MembershipId, avatarKey) && tracker.OfficeOf(me.MembershipId) is { } office)
        {
            await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).AvatarChanged(me.MembershipId, avatarKey);
        }
    }

    /// <summary>在辦公室內送出表情。</summary>
    /// <param name="key">表情代碼，須在 <see cref="OfficeRules.Emotes"/> 內。</param>
    /// <returns>廣播完成的工作。</returns>
    /// <exception cref="HubException">表情不正確、尚未進入辦公室或操作太頻繁。</exception>
    public async Task Emote(string key)
    {
        var me = Me();
        if (!OfficeRules.Emotes.Contains(key)) throw new HubException("表情不正確。");
        Limit("emote", 1, TimeSpan.FromSeconds(1));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        await Clients.Group(OfficeGroups.Office(me.CompanyId, office.DepartmentId)).Emoted(me.MembershipId, key);
    }

    /// <summary>收訊對象由後端依位置計算；只送到對方正在顯示辦公室的分頁，不存資料庫。</summary>
    /// <param name="text">訊息內容；空白訊息直接忽略。</param>
    /// <returns>送達完成的工作。</returns>
    /// <exception cref="HubException">訊息過長、附近沒有人、尚未進入辦公室或操作太頻繁。</exception>
    public async Task SendChat(string text)
    {
        var me = Me();
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return;
        if (text.Length > OfficeRules.ChatMaxLength) throw new HubException($"訊息最多 {OfficeRules.ChatMaxLength} 字。");
        Limit("chat", 2, TimeSpan.FromSeconds(1));
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室。");
        var recipients = OfficeRules.ChatRecipients(office.Map, me.MembershipId, office.Positions);
        if (recipients.Count == 0) throw new HubException("附近沒有人，走近同事再聊天。");
        var message = new OfficeChatMessage(me.MembershipId, me.Name, text, recipients, clock.GetUtcNow());
        var targets = recipients.Select(tracker.OfficeConnectionOf).OfType<string>().Append(Context.ConnectionId).Distinct().ToList();
        await Clients.Clients(targets).ChatReceived(message);
    }

    /// <summary>敲門送到對方所有分頁；不同公司、不存在與離線回同一句話，避免透露其他公司的成員。</summary>
    /// <param name="targetMembershipId">被敲門者的 membership。</param>
    /// <param name="message">附言；空白視為沒有。</param>
    /// <returns>通知送出完成的工作。</returns>
    /// <exception cref="HubException">對象不存在或離線、附言過長、尚未進入辦公室或操作太頻繁。</exception>
    public async Task Knock(Guid targetMembershipId, string? message)
    {
        var me = Me();
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > OfficeRules.KnockMessageMaxLength) throw new HubException($"附言最多 {OfficeRules.KnockMessageMaxLength} 字。");
        if (targetMembershipId == me.MembershipId) throw new HubException("不能敲自己的門。");
        Limit("knock", 5, TimeSpan.FromMinutes(1));
        if (tracker.OnlineMember(targetMembershipId) is not { } target || target.CompanyId != me.CompanyId) throw new HubException("找不到此成員或對方不在線上。");
        var office = tracker.OfficeOf(me.MembershipId) ?? throw new HubException("請先進入辦公室再敲門。");
        if (!gate.TryPass($"knock:{me.MembershipId}:{targetMembershipId}", 1, TimeSpan.FromSeconds(30))) throw new HubException("請稍候 30 秒再敲門。");
        service.UseCompany(me.CompanyId);
        var room = await service.LoadAsync(me.CompanyId, office.DepartmentId, Context.ConnectionAborted);
        tracker.RecordKnock(me.MembershipId, targetMembershipId);
        await Clients.Clients(tracker.ConnectionsOf(targetMembershipId)).Knocked(new OfficeKnock(me.MembershipId, me.Name, office.DepartmentId, room?.Name ?? string.Empty, message));
    }

    /// <summary>接受時把自己放到敲門者旁邊；必須已進入對方所在的辦公室（前端會先 JoinOffice）。</summary>
    /// <param name="fromMembershipId">敲門者的 membership。</param>
    /// <param name="accept">是否接受。</param>
    /// <param name="message">回覆附言；空白視為沒有。</param>
    /// <returns>回應送出完成的工作。</returns>
    /// <exception cref="HubException">沒有待回應的敲門、對方已離開、尚未進入對方的辦公室、附言過長或操作太頻繁。</exception>
    public async Task AnswerKnock(Guid fromMembershipId, bool accept, string? message)
    {
        var me = Me();
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message?.Length > OfficeRules.KnockMessageMaxLength) throw new HubException($"附言最多 {OfficeRules.KnockMessageMaxLength} 字。");
        Limit("answer", 10, TimeSpan.FromMinutes(1));
        if (!tracker.ConsumeKnock(fromMembershipId, me.MembershipId)) throw new HubException("沒有待回應的敲門，可能已經逾時。");
        if (accept)
        {
            var theirs = tracker.OfficeOf(fromMembershipId) ?? throw new HubException("對方已離開辦公室。");
            if (tracker.OfficeOf(me.MembershipId)?.DepartmentId != theirs.DepartmentId) throw new HubException("請先進入對方所在的辦公室。");
            var placed = tracker.Place(me.MembershipId, theirs.Positions[fromMembershipId], "down") ?? throw new HubException("請先進入對方所在的辦公室。");
            await Clients.Group(OfficeGroups.Office(me.CompanyId, theirs.DepartmentId)).Moved(placed.MembershipId, placed.X, placed.Y, placed.Facing);
        }
        await Clients.Clients(tracker.ConnectionsOf(fromMembershipId)).KnockAnswered(me.MembershipId, me.Name, accept, message);
    }

    private ConnectionIdentity Me() =>
        tracker.Identity(Context.ConnectionId) ?? throw new HubException("連線已失效，請重新整理頁面。");

    // 超過頻率回傳 false 並累計；同一連線 10 秒內被擋 50 次即斷開。
    private bool Allow(string action, int limit, TimeSpan window)
    {
        if (gate.TryPass($"{Context.ConnectionId}:{action}", limit, window)) return true;
        CountRejection();
        return false;
    }

    private void Limit(string action, int limit, TimeSpan window)
    {
        if (!Allow(action, limit, window)) throw new HubException("操作太頻繁，請稍候再試。");
    }

    private void CountRejection()
    {
        if (!gate.TryPass($"{Context.ConnectionId}:rejected", 50, TimeSpan.FromSeconds(10))) Context.Abort();
    }
}

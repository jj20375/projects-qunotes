using Microsoft.Extensions.Caching.Memory;

namespace QuotePlatform.Api.Modules.Identity;

/// <summary>
/// 登入端點的 IP 層級節流：與帳號層級鎖定（<c>users.LockedUntil</c>）互補，
/// 防止同一來源改用大量不同帳號分散嘗試以規避單一帳號的鎖定門檻。
/// 以 <see cref="IMemoryCache"/> 保存，重啟服務或多實例部署下不會同步，這裡刻意接受此限制——
/// 目前只有單一 API 容器，行為與既有 AssistantService 的問答節流一致。
/// </summary>
public sealed class LoginAttemptGuard(IMemoryCache cache)
{
    private const int MaxFailuresPerWindow = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    /// <summary>此來源 IP 是否因短時間內登入失敗次數過多而被暫時封鎖。</summary>
    public bool IsBlocked(string clientIp) => cache.TryGetValue(BlockKey(clientIp), out _);

    /// <summary>登記一次登入失敗；達到門檻時封鎖此 IP 一段時間。</summary>
    public void RegisterFailure(string clientIp)
    {
        var counter = cache.GetOrCreate(CountKey(clientIp), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new FailureCounter();
        })!;

        int count;
        lock (counter) { count = ++counter.Value; }
        if (count >= MaxFailuresPerWindow) cache.Set(BlockKey(clientIp), true, Window);
    }

    private static string CountKey(string clientIp) => $"login-ip-fail:{clientIp}";
    private static string BlockKey(string clientIp) => $"login-ip-block:{clientIp}";

    private sealed class FailureCounter
    {
        public int Value;
    }
}

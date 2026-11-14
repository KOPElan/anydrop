using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AnyDrop.Hubs;

/// <summary>
/// S0-07（SEC-08）：Hub 显式要求认证；删除客户端可调用的广播方法。
/// 服务端广播仍走 <c>IHubContext&lt;ShareHub&gt;</c>（ShareService / 清理服务），不受影响。
/// 此前客户端可直接调用 <c>SendTopicsUpdatedAsync</c> 向所有人广播任意主题列表，
/// 等于把服务端广播能力交给了任何一个已登录客户端。
/// </summary>
[Authorize]
public sealed class ShareHub : Hub
{
}

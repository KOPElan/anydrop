using AnyDrop.Data;
using AnyDrop.Hubs;
using AnyDrop.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AnyDrop.Tests.Unit.TestDoubles;

/// <summary>
/// 构造 <see cref="ShareService"/> 的测试工厂。
///
/// 它有 10 个构造参数，此前每个用到它的测试文件都各写一遍 mock 组合；
/// 集中到这里可以避免新增测试时重复劳动，也避免各文件之间逐渐漂移。
/// </summary>
internal static class ShareServiceFactory
{
    public static ShareService Create(
        AnyDropDbContext dbContext,
        IFileStorageService? fileStorageService = null,
        TimeProvider? timeProvider = null)
    {
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.All).Returns(new Mock<IClientProxy>().Object);

        var hubContext = new Mock<IHubContext<ShareHub>>();
        hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var linkMetadata = new LinkMetadataService(
            httpClientFactory.Object, NullLogger<LinkMetadataService>.Instance);

        return new ShareService(
            dbContext,
            hubContext.Object,
            new Mock<ITopicService>().Object,
            fileStorageService ?? new Mock<IFileStorageService>().Object,
            new Mock<IThumbnailService>().Object,
            linkMetadata,
            new Mock<ISystemSettingsService>().Object,
            new Mock<IServiceScopeFactory>().Object,
            timeProvider ?? TimeProvider.System,
            NullLogger<ShareService>.Instance);
    }
}

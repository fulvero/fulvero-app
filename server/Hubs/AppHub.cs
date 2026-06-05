using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Fulvero.Api.Hubs;

[Authorize]
public class AppHub : Hub
{
    public static string CompanyGroup(Guid companyId) => $"company:{companyId}";

    public override async Task OnConnectedAsync()
    {
        var companyIdClaim = Context.User?.FindFirstValue("company_id");
        if (Guid.TryParse(companyIdClaim, out var companyId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
        }

        await base.OnConnectedAsync();
    }
}

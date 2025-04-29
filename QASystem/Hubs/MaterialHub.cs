using Microsoft.AspNetCore.SignalR;

namespace QASystem.Hubs
{
    public class MaterialHub : Hub
    {
        public async Task SendMaterialUpdate(string action, int materialId)
        {
            await Clients.All.SendAsync("ReceiveMaterialUpdate", action, materialId);
        }
    }
}

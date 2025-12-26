// để chạy realtime
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace PosSystem.Main.Server.Hubs
{
    public class PosHub : Hub
    {
        // Hub này dùng để định tuyến.
        // Mobile sẽ lắng nghe sự kiện: "TableUpdated"
        // PC sẽ lắng nghe sự kiện: "NewOrder"
        public async Task SendUpdate(string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", message);
        }
    }
}
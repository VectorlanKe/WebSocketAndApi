using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.WebSockets;
using SysNetWebSockets = System.Net.WebSockets;

namespace WebSocket.NetApi.Controllers
{
    public class WebSocketController : ApiController
    {
        private ConcurrentDictionary<string, SysNetWebSockets.WebSocket> _sockets = new ConcurrentDictionary<string, SysNetWebSockets.WebSocket>();
        [Route("core/websocket")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetAsync()
        {
            HttpContext.Current.AcceptWebSocketRequest(async context =>
            {
                var webSocket = context.WebSocket;
                await OnconnectedAsync(webSocket);
                await ReceiveAsync(webSocket, (websocket, result, buffer) =>
                {
                    string id = GetSokcetId(websocket);
                    string data = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    SendMessageToAllAsync(data);
                });
            });
            return await Task.FromResult(Request.CreateResponse(HttpStatusCode.SwitchingProtocols));
        }
        private string GetSokcetId(SysNetWebSockets.WebSocket webSocket) => _sockets.FirstOrDefault(p => p.Value == webSocket).Key;
        private async Task OnconnectedAsync(SysNetWebSockets.WebSocket webSocket) => await Task.Run(() => _sockets.TryAdd(Guid.NewGuid().ToString("N"), webSocket));
        private async Task OnDisconnectedAsync(SysNetWebSockets.WebSocket webSocket)
        {
            string key = GetSokcetId(webSocket);
            _sockets.TryRemove(key, out webSocket);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the ConnectionManager", CancellationToken.None);
        }
        private async Task ReceiveAsync(SysNetWebSockets.WebSocket webSocket, Action<SysNetWebSockets.WebSocket, WebSocketReceiveResult, byte[]> action)
        {
            var buffer = new byte[1021 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await OnDisconnectedAsync(webSocket);
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                    action(webSocket, result, buffer);
            }
        }
        private async Task SendMessageToAllAsync(string message)
        {
            foreach (var pair in _sockets)
            {
                await Task.Yield();
                if (pair.Value.State == WebSocketState.Open)
                {
                    await pair.Value.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message), 0, message.Length),
                        messageType: WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: CancellationToken.None);
                    return;
                }
                await OnDisconnectedAsync(pair.Value);
            }
        }
    }
}

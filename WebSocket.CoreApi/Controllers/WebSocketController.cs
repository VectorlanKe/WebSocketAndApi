using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SysNetWebSockets = System.Net.WebSockets;

namespace WebSocket.CoreApi.Controllers
{
    [Route("core/websocket")]
    [ApiController]
    public class WebSocketController : ControllerBase
    {
        private readonly ILogger<WebSocketController> _logger;
        public WebSocketController(ILogger<WebSocketController> logger)
        {
            _logger = logger;
        }
        private ConcurrentDictionary<string, SysNetWebSockets.WebSocket> _sockets = new ConcurrentDictionary<string, SysNetWebSockets.WebSocket>();
        [HttpGet]
        public async ValueTask Get()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return;
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await OnconnectedAsync(webSocket);
            await ReceiveAsync(webSocket, async (websocket, result, buffer) =>
            {
                string id = GetSokcetId(websocket);
                string data = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogDebug($"id:{id} , data:{data}");
                await SendMessageToAllAsync(data);
            });
        }
        private string GetSokcetId(SysNetWebSockets.WebSocket webSocket) => _sockets.FirstOrDefault(p => p.Value == webSocket).Key;
        private async ValueTask OnconnectedAsync(SysNetWebSockets.WebSocket webSocket) => await Task.Run(() => _sockets.TryAdd(Guid.NewGuid().ToString("N"), webSocket));
        private async ValueTask OnDisconnectedAsync(SysNetWebSockets.WebSocket webSocket)
        {
            string key = GetSokcetId(webSocket);
            _sockets.Remove(key, out webSocket);
            await webSocket.CloseAsync(SysNetWebSockets.WebSocketCloseStatus.NormalClosure, "Closed by the ConnectionManager", CancellationToken.None);
        }
        private async ValueTask ReceiveAsync(SysNetWebSockets.WebSocket webSocket, Action<SysNetWebSockets.WebSocket, SysNetWebSockets.WebSocketReceiveResult, byte[]> action)
        {
            var buffer = new byte[1021 * 4];
            while (webSocket.State == SysNetWebSockets.WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == SysNetWebSockets.WebSocketMessageType.Close)
                {
                    await OnDisconnectedAsync(webSocket);
                    return;
                }
                if (result.MessageType == SysNetWebSockets.WebSocketMessageType.Text)
                    action(webSocket, result, buffer);
            }
        }
        private async ValueTask SendMessageToAllAsync(string message)
        {
            foreach (var pair in _sockets)
            {
                await Task.Yield();
                if (pair.Value.State == SysNetWebSockets.WebSocketState.Open)
                {
                    await pair.Value.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message), 0, message.Length),
                        messageType: SysNetWebSockets.WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: CancellationToken.None);
                    return;
                }
                await OnDisconnectedAsync(pair.Value);
            }
        }
    }
}

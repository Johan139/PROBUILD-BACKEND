// File: Services/WebSocketManager.cs
using Microsoft.AspNetCore.WebSockets;
using ProbuildBackend.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class WebSocketManager
{
    private readonly ApplicationDbContext _context;
    private readonly ConcurrentDictionary<string, WebSocket> _connectedClients = new();
    private readonly ConcurrentDictionary<string, string> _userWebSocketMap = new(); 

    public WebSocketManager(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task HandleWebSocketAsync(WebSocket webSocket, string userId)
    {
        var buffer = new byte[1024 * 4];
        var webSocketId = GetWebSocketId(webSocket);

        _connectedClients[webSocketId] = webSocket;
        _userWebSocketMap[webSocketId] = userId;

        try
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!result.CloseStatus.HasValue)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Received WebSocket message: {message}");

                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket error: {ex.Message}");
        }
        finally
        {
            _connectedClients.TryRemove(webSocketId, out _);
            _userWebSocketMap.TryRemove(webSocketId, out _);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
        }
    }


    public async Task BroadcastMessageAsync(string message, List<string> recipients)
    {
        var messageBuffer = Encoding.UTF8.GetBytes(message);

        foreach (var client in _connectedClients)
        {
            var webSocketId = client.Key;
            var webSocket = client.Value;

            var userId = GetUserIdFromClient(webSocketId);

            if (recipients.Contains(userId) && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.SendAsync(new ArraySegment<byte>(messageBuffer, 0, messageBuffer.Length),
                        WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending WebSocket message to {userId}: {ex.Message}");
                }
            }
        }
    }


    private string GetUserIdFromClient(string clientId)
    {
        return _userWebSocketMap.ContainsKey(clientId) ? _userWebSocketMap[clientId] : null;
    }

    private async Task HandleNotificationMessageAsync(string message)
    {
        var notificationData = message.Substring("notification:".Length);
        var notification = new NotificationModel
        {
            Message = notificationData,
            Timestamp = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        await BroadcastMessageAsync($"Notification: {notification.Message}", new List<string>());
    }

    private string GetWebSocketId(WebSocket webSocket)
    {
        return webSocket.GetHashCode().ToString();
    }
}

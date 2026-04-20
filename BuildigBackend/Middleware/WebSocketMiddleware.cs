public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly WebSocketManager _webSocketManager;

    public WebSocketMiddleware(RequestDelegate next, WebSocketManager webSocketManager)
    {
        _next = next;
        _webSocketManager = webSocketManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            var userId = context.Request.Query["userId"].ToString();

            using (var webSocket = await context.WebSockets.AcceptWebSocketAsync())
            {
                await _webSocketManager.HandleWebSocketAsync(webSocket, userId);
            }
        }
        else
        {
            await _next(context);
        }
    }
}

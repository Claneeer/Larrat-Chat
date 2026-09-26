using Larrat.Core.Entities;
using Larrat.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Larrat.Web.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IMessageService _messageService;
    private readonly IEncryptionService _encryptionService;
    private readonly IConfiguration _configuration;

    // Mapeamento userId -> connectionId (em memória - em produção usar Redis)
    private static readonly Dictionary<string, HashSet<string>> _userConnections = new();
    private static readonly object _lockObj = new();

    public ChatHub(IMessageService messageService, IEncryptionService encryptionService, IConfiguration configuration)
    {
        _messageService = messageService;
        _encryptionService = encryptionService;
        _configuration = configuration;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        lock (_lockObj)
        {
            if (!_userConnections.ContainsKey(userId))
                _userConnections[userId] = new HashSet<string>();
            _userConnections[userId].Add(Context.ConnectionId);
        }

        // Notifica contatos que o usuário ficou online
        await Clients.Others.SendAsync("UserStatusChanged", userId, "Online");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        lock (_lockObj)
        {
            if (_userConnections.ContainsKey(userId))
            {
                _userConnections[userId].Remove(Context.ConnectionId);
                if (_userConnections[userId].Count == 0)
                    _userConnections.Remove(userId);
            }
        }

        // Se não tem mais conexões, está offline
        bool isOffline;
        lock (_lockObj) { isOffline = !_userConnections.ContainsKey(userId); }
        if (isOffline)
            await Clients.Others.SendAsync("UserStatusChanged", userId, "Offline");

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Entra em um canal de grupo (grupo SignalR).
    /// </summary>
    public async Task JoinChannel(string channelId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"channel-{channelId}");
    }

    public async Task LeaveChannel(string channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel-{channelId}");
    }

    /// <summary>
    /// Envia mensagem para um canal de grupo.
    /// A mensagem já vem criptografada do cliente (AES-256-GCM).
    /// </summary>
    public async Task SendChannelMessage(
        string channelId,
        string encryptedContent,
        string iv,
        string authTag,
        string messageType = "Text",
        string? attachmentId = null,
        string? replyToId = null)
    {
        var userId = GetUserId();
        var type = Enum.TryParse<MessageType>(messageType, out var mt) ? mt : MessageType.Text;

        var message = await _messageService.SendChannelMessageAsync(
            userId,
            Guid.Parse(channelId),
            encryptedContent, iv, authTag,
            type,
            attachmentId != null ? Guid.Parse(attachmentId) : null,
            replyToId != null ? Guid.Parse(replyToId) : null
        );

        // Envia para todos no canal
        await Clients.Group($"channel-{channelId}").SendAsync("ReceiveChannelMessage", new
        {
            id = message.Id,
            channelId,
            senderId = message.SenderId,
            senderName = message.Sender?.DisplayName,
            senderAvatar = message.Sender?.AvatarUrl,
            encryptedContent,
            iv,
            authTag,
            type = message.Type.ToString(),
            attachment = message.Attachment != null ? new
            {
                id = message.Attachment.Id,
                fileName = message.Attachment.OriginalFileName,
                contentType = message.Attachment.ContentType,
                sizeBytes = message.Attachment.FileSizeBytes,
                category = message.Attachment.Category.ToString()
            } : null,
            replyToId,
            sentAt = message.SentAt
        });
    }

    /// <summary>
    /// Envia mensagem direta para outro usuário.
    /// </summary>
    public async Task SendDirectMessage(
        string conversationId,
        string targetUserId,
        string encryptedContent,
        string iv,
        string authTag,
        string messageType = "Text",
        string? attachmentId = null)
    {
        var userId = GetUserId();
        var type = Enum.TryParse<MessageType>(messageType, out var mt) ? mt : MessageType.Text;

        var message = await _messageService.SendDirectMessageAsync(
            userId,
            Guid.Parse(conversationId),
            encryptedContent, iv, authTag,
            type,
            attachmentId != null ? Guid.Parse(attachmentId) : null
        );

        var payload = new
        {
            id = message.Id,
            conversationId,
            senderId = userId,
            senderName = message.Sender?.DisplayName,
            senderAvatar = message.Sender?.AvatarUrl,
            encryptedContent,
            iv,
            authTag,
            type = message.Type.ToString(),
            sentAt = message.SentAt
        };

        // Entrega para o destinatário (todas as conexões dele)
        await DeliverToUserAsync(targetUserId, "ReceiveDirectMessage", payload);
        // Confirma para o remetente
        await Clients.Caller.SendAsync("ReceiveDirectMessage", payload);
    }

    /// <summary>
    /// Notifica que o usuário está digitando.
    /// </summary>
    public async Task SendTyping(string channelId, bool isTyping)
    {
        var userId = GetUserId();
        await Clients.OthersInGroup($"channel-{channelId}").SendAsync("UserTyping", channelId, userId, isTyping);
    }

    /// <summary>
    /// Adiciona reação a uma mensagem.
    /// </summary>
    public async Task AddReaction(string messageId, string emoji, string channelId)
    {
        var userId = GetUserId();
        await _messageService.AddReactionAsync(Guid.Parse(messageId), userId, emoji);
        await Clients.Group($"channel-{channelId}").SendAsync("ReactionAdded", messageId, userId, emoji);
    }

    public async Task RemoveReaction(string messageId, string emoji, string channelId)
    {
        var userId = GetUserId();
        await _messageService.RemoveReactionAsync(Guid.Parse(messageId), userId, emoji);
        await Clients.Group($"channel-{channelId}").SendAsync("ReactionRemoved", messageId, userId, emoji);
    }

    // Helper: obtém userId do ClaimsPrincipal
    private string GetUserId()
        => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new HubException("Usuário não autenticado.");

    private async Task DeliverToUserAsync(string targetUserId, string method, object payload)
    {
        List<string> connectionIds;
        lock (_lockObj)
        {
            connectionIds = _userConnections.TryGetValue(targetUserId, out var ids)
                ? ids.ToList()
                : new List<string>();
        }

        foreach (var connId in connectionIds)
            await Clients.Client(connId).SendAsync(method, payload);
    }
}

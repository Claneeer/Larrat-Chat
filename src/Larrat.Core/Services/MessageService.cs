using Larrat.Core.Data;
using Larrat.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Larrat.Core.Services;

public interface IMessageService
{
    Task<Message> SendChannelMessageAsync(string senderId, Guid channelId, string encryptedContent, string iv, string authTag, MessageType type = MessageType.Text, Guid? attachmentId = null, Guid? replyToId = null);
    Task<Message> SendDirectMessageAsync(string senderId, Guid conversationId, string encryptedContent, string iv, string authTag, MessageType type = MessageType.Text, Guid? attachmentId = null);
    Task<List<Message>> GetChannelMessagesAsync(Guid channelId, int page = 1, int pageSize = 50);
    Task<List<Message>> GetDirectMessagesAsync(Guid conversationId, int page = 1, int pageSize = 50);
    Task<DirectConversation> GetOrCreateDirectConversationAsync(string userId1, string userId2);
    Task<bool> MarkMessagesAsReadAsync(string userId, Guid conversationId);
    Task AddReactionAsync(Guid messageId, string userId, string emoji);
    Task RemoveReactionAsync(Guid messageId, string userId, string emoji);
    Task<bool> DeleteMessageAsync(Guid messageId, string requestingUserId);
}

public class MessageService : IMessageService
{
    private readonly LarratDbContext _db;

    public MessageService(LarratDbContext db)
    {
        _db = db;
    }

    public async Task<Message> SendChannelMessageAsync(
        string senderId, Guid channelId, string encryptedContent, string iv, string authTag,
        MessageType type = MessageType.Text, Guid? attachmentId = null, Guid? replyToId = null)
    {
        var message = new Message
        {
            SenderId = senderId,
            ChannelId = channelId,
            EncryptedContent = encryptedContent,
            IV = iv,
            AuthTag = authTag,
            Type = type,
            AttachmentId = attachmentId,
            ReplyToMessageId = replyToId,
            Status = MessageStatus.Sent
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        return await _db.Messages
            .Include(m => m.Sender)
            .Include(m => m.Attachment)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
            .FirstAsync(m => m.Id == message.Id);
    }

    public async Task<Message> SendDirectMessageAsync(
        string senderId, Guid conversationId, string encryptedContent, string iv, string authTag,
        MessageType type = MessageType.Text, Guid? attachmentId = null)
    {
        var message = new Message
        {
            SenderId = senderId,
            DirectConversationId = conversationId,
            EncryptedContent = encryptedContent,
            IV = iv,
            AuthTag = authTag,
            Type = type,
            AttachmentId = attachmentId,
            Status = MessageStatus.Delivered
        };

        _db.Messages.Add(message);

        // Atualiza LastMessageAt na conversa
        var conversation = await _db.DirectConversations.FindAsync(conversationId);
        if (conversation != null)
            conversation.LastMessageAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await _db.Messages
            .Include(m => m.Sender)
            .Include(m => m.Attachment)
            .FirstAsync(m => m.Id == message.Id);
    }

    public async Task<List<Message>> GetChannelMessagesAsync(Guid channelId, int page = 1, int pageSize = 50)
    {
        return await _db.Messages
            .Where(m => m.ChannelId == channelId && !m.IsDeleted)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(m => m.Sender)
            .Include(m => m.Attachment)
            .Include(m => m.Reactions).ThenInclude(r => r.User)
            .Include(m => m.ReplyToMessage).ThenInclude(r => r!.Sender)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }

    public async Task<List<Message>> GetDirectMessagesAsync(Guid conversationId, int page = 1, int pageSize = 50)
    {
        return await _db.Messages
            .Where(m => m.DirectConversationId == conversationId && !m.IsDeleted)
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(m => m.Sender)
            .Include(m => m.Attachment)
            .Include(m => m.Reactions).ThenInclude(r => r.User)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }

    public async Task<DirectConversation> GetOrCreateDirectConversationAsync(string userId1, string userId2)
    {
        // Busca conversa existente entre os dois usuários
        var existing = await _db.DirectConversations
            .Where(dc => dc.Participants.Any(p => p.UserId == userId1)
                      && dc.Participants.Any(p => p.UserId == userId2))
            .Include(dc => dc.Participants)
            .FirstOrDefaultAsync();

        if (existing != null)
            return existing;

        // Cria nova conversa direta
        var conversation = new DirectConversation();
        conversation.Participants.Add(new DirectConversationParticipant { UserId = userId1 });
        conversation.Participants.Add(new DirectConversationParticipant { UserId = userId2 });

        _db.DirectConversations.Add(conversation);
        await _db.SaveChangesAsync();
        return conversation;
    }

    public async Task<bool> MarkMessagesAsReadAsync(string userId, Guid conversationId)
    {
        var participant = await _db.DirectConversationParticipants
            .FirstOrDefaultAsync(p => p.DirectConversationId == conversationId && p.UserId == userId);

        if (participant == null) return false;

        participant.LastReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task AddReactionAsync(Guid messageId, string userId, string emoji)
    {
        if (!await _db.MessageReactions.AnyAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji))
        {
            _db.MessageReactions.Add(new MessageReaction
            {
                MessageId = messageId,
                UserId = userId,
                Emoji = emoji
            });
            await _db.SaveChangesAsync();
        }
    }

    public async Task RemoveReactionAsync(Guid messageId, string userId, string emoji)
    {
        var reaction = await _db.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == userId && r.Emoji == emoji);
        if (reaction != null)
        {
            _db.MessageReactions.Remove(reaction);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> DeleteMessageAsync(Guid messageId, string requestingUserId)
    {
        var message = await _db.Messages.FindAsync(messageId);
        if (message == null || message.SenderId != requestingUserId) return false;

        message.IsDeleted = true;
        message.EncryptedContent = null;
        message.IV = null;
        message.AuthTag = null;
        await _db.SaveChangesAsync();
        return true;
    }
}

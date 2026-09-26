namespace Larrat.Core.Entities;

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SenderId { get; set; } = string.Empty;
    public ApplicationUser? Sender { get; set; }

    // Destino: pode ser canal de grupo OU conversa direta
    public Guid? ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public Guid? DirectConversationId { get; set; }
    public DirectConversation? DirectConversation { get; set; }

    // Conteúdo criptografado (AES-256-GCM)
    public string? EncryptedContent { get; set; }
    public string? IV { get; set; }        // Initialization Vector para AES-GCM
    public string? AuthTag { get; set; }   // Tag de autenticação GCM

    public MessageType Type { get; set; } = MessageType.Text;
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    // Arquivo anexo (opcional)
    public Guid? AttachmentId { get; set; }
    public FileAttachment? Attachment { get; set; }

    // Resposta a outra mensagem (thread)
    public Guid? ReplyToMessageId { get; set; }
    public Message? ReplyToMessage { get; set; }

    public bool IsEdited { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    // Reações
    public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
}

public class MessageReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Message? Message { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Conversa direta entre dois usuários
/// </summary>
public class DirectConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastMessageAt { get; set; }

    public ICollection<DirectConversationParticipant> Participants { get; set; } = new List<DirectConversationParticipant>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public class DirectConversationParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DirectConversationId { get; set; }
    public DirectConversation? DirectConversation { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public DateTime? LastReadAt { get; set; }
}

public enum MessageType
{
    Text,
    Audio,
    Image,
    Video,
    File,
    System // mensagens do sistema como "entrou no grupo"
}

public enum MessageStatus
{
    Sent,
    Delivered,
    Read
}

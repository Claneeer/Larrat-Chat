using Microsoft.AspNetCore.Identity;

namespace Larrat.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? StatusMessage { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Offline;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public string? PublicKeyBase64 { get; set; } // Chave pública para criptografia E2E

    // Navegação
    public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<DirectConversationParticipant> DirectConversations { get; set; } = new List<DirectConversationParticipant>();
}

public enum UserStatus
{
    Online,
    Away,
    DoNotDisturb,
    Offline
}
